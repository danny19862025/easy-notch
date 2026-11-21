using Avalonia.Threading;
using System.Net.Http;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using EasyNotch.Controls;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class WeatherPage : UserControl, IPage
{
    readonly PageContext _ctx;
    readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    int _searchVersion;

    public WeatherPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _ctx.Weather.Changed += () => { if (IsVisible && Parent is not null) Refresh(); };
        _searchDelay.Tick += async (_, _) => { _searchDelay.Stop(); await Search(); };
    }

    public async void OnShown()
    {
        Refresh();
        await _ctx.Weather.RefreshIfStaleAsync();
    }

    public void OnTick() { }

    void Refresh()
    {
        var w = _ctx.Weather;
        var d = w.Data;
        Body.IsVisible = d is not null;
        Status.IsVisible = d is null;
        if (d is null)
        {
            Status.Text = w.Error ?? "Loading weather…";
            if (w.Error is not null && _ctx.Settings.WeatherPlace is null) OpenPicker();
            return;
        }

        NowIcon.Kind = WeatherService.IconKind(d.Now.Code);
        NowIcon.IsNight = !d.Now.IsDay;
        NowTemp.Text = $"{Math.Round(d.Now.Temp)}°";
        Unit.Text = d.TempUnit;
        Condition.Text = WeatherService.Describe(d.Now.Code);
        var today = d.Days.FirstOrDefault();
        Detail.Text = $"Feels {Math.Round(d.Now.Feels)}°" +
                      (today is null ? "" : $" · H {Math.Round(today.Max)}° L {Math.Round(today.Min)}°") +
                      $" · {Math.Round(d.Now.Wind)} {d.WindUnit}";
        PlaceText.Text = d.Place.Name;
        ToolTip.SetTip(PlaceBtn, $"{d.Place.Label} — click to change");
        Updated.Text = w.Error ?? $"Updated {TimeAgo.Of(d.Fetched)}";

        Hourly.Children.Clear();
        foreach (var h in d.Hours.Take(7))
            Hourly.Children.Add(Column(h.Time.ToString("h tt").ToLowerInvariant(), WeatherService.IconKind(h.Code), !h.IsDay,
                $"{Math.Round(h.Temp)}°", null, h.RainChance, WeatherService.Describe(h.Code)));

        Daily.Children.Clear();
        foreach (var (day, i) in d.Days.Select((x, i) => (x, i)))
            Daily.Children.Add(Column(i == 0 ? "Today" : i == 1 ? "Tmrw" : day.Date.ToString("ddd"), WeatherService.IconKind(day.Code), false,
                $"{Math.Round(day.Max)}°", $"{Math.Round(day.Min)}°", day.RainChance,
                $"{day.Date:dddd}: {WeatherService.Describe(day.Code)}, high {Math.Round(day.Max)}°, low {Math.Round(day.Min)}°"));
    }

    static Control Column(string label, string kind, bool night, string main, string? sub, int rain, string tip)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent };
        ToolTip.SetTip(sp, rain > 0 ? $"{tip} · {rain}% chance of rain" : tip);
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Res.Brush("Sub"), HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new WeatherIcon { Kind = kind, IsNight = night, Width = 18, Height = 18, Margin = new Thickness(0, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        // Rain chance sits beside the temperatures (only when 20%+), keeping each forecast row compact.
        var temps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        temps.Children.Add(new TextBlock { Text = main, FontSize = 12, FontWeight = FontWeight.SemiBold });
        if (sub is not null) temps.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = Res.Brush("Sub"), Margin = new Thickness(5, 0, 0, 0) });
        if (rain >= 20) temps.Children.Add(new TextBlock { Text = $" {rain}%", FontSize = 10, Foreground = Res.Brush("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 1, 0, 0) });
        sp.Children.Add(temps);
        return sp;
    }

    async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        Updated.Text = "Updating…";
        await _ctx.Weather.RefreshAsync();
    }

    // ------------------------------------------------------------ place picker

    void Place_Click(object? sender, RoutedEventArgs e) => OpenPicker();

    void OpenPicker()
    {
        Picker.IsVisible = true;
        PlaceSearch.Text = "";
        Results.Children.Clear();
        PickerHint.Text = "Type a city name.";
        Dispatcher.UIThread.Post(() => PlaceSearch.Focus(), DispatcherPriority.Input);
    }

    void PickerClose_Click(object? sender, RoutedEventArgs e) => Picker.IsVisible = false;

    void PlaceSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    void PlaceSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Results.Children.OfType<Button>().FirstOrDefault() is { } first)
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    async Task Search()
    {
        int version = ++_searchVersion;
        string q = (PlaceSearch.Text ?? "").Trim();
        Results.Children.Clear();
        if (q.Length < 2) { PickerHint.Text = "Type a city name."; return; }
        PickerHint.Text = "Searching…";
        List<WeatherPlace> hits;
        try { hits = await WeatherService.SearchAsync(q); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            if (version == _searchVersion) PickerHint.Text = "Couldn't search right now. Check your connection.";
            return;
        }
        if (version != _searchVersion) return;

        PickerHint.Text = hits.Count == 0 ? "No places found." : "";
        foreach (var p in hits.Take(4))
        {
            var b = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 4, 8, 4),
                Content = new TextBlock { Text = p.Label, FontSize = 13, Foreground = Res.Brush("Fg"), TextTrimming = TextTrimming.CharacterEllipsis },
            };
            b.Classes.Add("text");
            b.Click += async (_, _) =>
            {
                Picker.IsVisible = false;
                Status.Text = "Loading weather…";
                await _ctx.Weather.SetPlaceAsync(p);
            };
            Results.Children.Add(b);
        }
    }

    /// <summary>Esc closes the place picker before it closes the notch.</summary>
    public bool HandleEscape()
    {
        if (!Picker.IsVisible) return false;
        Picker.IsVisible = false;
        return true;
    }
}
