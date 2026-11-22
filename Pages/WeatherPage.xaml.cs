using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        _ctx.Weather.Changed += () => { if (IsVisible) Refresh(); };
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
        Body.Visibility = d is null ? Visibility.Collapsed : Visibility.Visible;
        Status.Visibility = d is null ? Visibility.Visible : Visibility.Collapsed;
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
        Unit.ToolTip = d.TempUnit == "F" ? "Show °C" : "Show °F";
        Condition.Text = WeatherService.Describe(d.Now.Code);
        var today = d.Days.FirstOrDefault();
        Detail.Text = $"Feels {Math.Round(d.Now.Feels)}°" +
                      (today is null ? "" : $" · H {Math.Round(today.Max)}° L {Math.Round(today.Min)}°") +
                      $" · {Math.Round(d.Now.Wind)} {d.WindUnit}";
        PlaceText.Text = d.Place.Name;
        PlaceBtn.ToolTip = $"{d.Place.Label} — click to change";
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

    FrameworkElement Column(string label, string kind, bool night, string main, string? sub, int rain, string tip)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent, ToolTip = rain > 0 ? $"{tip} · {rain}% chance of rain" : tip };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = (Brush)FindResource("Sub"), HorizontalAlignment = HorizontalAlignment.Center });
        // Rain chance sits beside the temperatures (only when 20%+), keeping each forecast row compact.
        var mid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
        mid.Children.Add(new WeatherIcon { Kind = kind, IsNight = night, Width = 18, Height = 18 });
        sp.Children.Add(mid);
        var temps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        temps.Children.Add(new TextBlock { Text = main, FontSize = 12, FontWeight = FontWeights.SemiBold });
        if (sub is not null) temps.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = (Brush)FindResource("Sub"), Margin = new Thickness(5, 0, 0, 0) });
        if (rain >= 20) temps.Children.Add(new TextBlock { Text = $" {rain}%", FontSize = 10, Foreground = (Brush)FindResource("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 1, 0, 0) });
        sp.Children.Add(temps);
        return sp;
    }

    async void Unit_Click(object sender, MouseButtonEventArgs e)
    {
        Updated.Text = "Updating…";
        await _ctx.Weather.SetUnitAsync(_ctx.Weather.Unit == "F" ? "C" : "F");
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Updated.Text = "Updating…";
        await _ctx.Weather.RefreshAsync();
    }

    // ------------------------------------------------------------ place picker

    void Place_Click(object sender, RoutedEventArgs e) => OpenPicker();

    void OpenPicker()
    {
        Picker.Visibility = Visibility.Visible;
        PlaceSearch.Text = "";
        Results.Children.Clear();
        PickerHint.Text = "Type a city name.";
        Dispatcher.InvokeAsync(() => PlaceSearch.Focus(), DispatcherPriority.Input);
    }

    void PickerClose_Click(object sender, RoutedEventArgs e) => Picker.Visibility = Visibility.Collapsed;

    void PlaceSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    void PlaceSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Results.Children.OfType<Button>().FirstOrDefault() is { } first)
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    async Task Search()
    {
        int version = ++_searchVersion;
        string q = PlaceSearch.Text.Trim();
        Results.Children.Clear();
        if (q.Length < 2) { PickerHint.Text = "Type a city name."; return; }
        PickerHint.Text = "Searching…";
        List<WeatherPlace> hits;
        try { hits = await WeatherService.SearchAsync(q); }
        catch { if (version == _searchVersion) PickerHint.Text = "Couldn't search right now. Check your connection."; return; }
        if (version != _searchVersion) return;

        PickerHint.Text = hits.Count == 0 ? "No places found." : "";
        foreach (var p in hits.Take(4))
        {
            var b = new Button
            {
                Style = (Style)FindResource("TextBtn"), HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4, 8, 4),
                Content = new TextBlock { Text = p.Label, FontSize = 13, Foreground = (Brush)FindResource("Fg"), TextTrimming = TextTrimming.CharacterEllipsis },
            };
            b.Click += async (_, _) =>
            {
                Picker.Visibility = Visibility.Collapsed;
                Status.Text = "Loading weather…";
                await _ctx.Weather.SetPlaceAsync(p);
            };
            Results.Children.Add(b);
        }
    }

    /// <summary>Esc closes the place picker before it closes the notch.</summary>
    public bool HandleEscape()
    {
        if (Picker.Visibility != Visibility.Visible) return false;
        Picker.Visibility = Visibility.Collapsed;
        return true;
    }
}
