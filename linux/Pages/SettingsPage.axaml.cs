using Avalonia.Threading;
using System.Net.Http;
using Avalonia.Interactivity;
using Avalonia.Layout;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class SettingsPage : UserControl, IPage
{
    public static readonly Dictionary<string, string> PageNames = new()
    {
        ["clock"] = "Clock",
        ["weather"] = "Weather",
        ["media"] = "Now Playing",
        ["focus"] = "Focus",
        ["notes"] = "Notes",
        ["clipboard"] = "Clipboard",
        ["mixer"] = "Volume Mixer",
        ["about"] = "About",
    };

    readonly PageContext _ctx;
    readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    int _searchVersion;

    /// <summary>Raised when pages are turned on/off or reordered.</summary>
    public event Action? PagesChanged;

    public SettingsPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _searchDelay.Tick += async (_, _) => { _searchDelay.Stop(); await SearchPlaces(); };
        DataNote.Text = $"Notes, clipboard and settings stay on this computer, in {Settings.Dir.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~")}.";
    }

    public void OnShown()
    {
        BuildWidgets();
        BuildPlaces();
        Startup.IsChecked = LinuxDesktop.StartsAtLogin;
        Tuck.IsChecked = _ctx.Settings.TuckOverFullScreen;
        AutoUpdate.IsChecked = _ctx.Settings.CheckForUpdates;
    }

    public void OnTick() { }

    void AutoUpdate_Click(object? sender, RoutedEventArgs e)
    {
        _ctx.Settings.CheckForUpdates = AutoUpdate.IsChecked == true;
        _ctx.Settings.Save();
    }

    void BuildWidgets()
    {
        Widgets.Children.Clear();
        var list = _ctx.Settings.Widgets;
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            int index = i;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var down = IconButton(Glyph.Down, "Move down", i < list.Count - 1, () => Move(index, +1));
            var up = IconButton(Glyph.Up, "Move up", i > 0, () => Move(index, -1));
            DockPanel.SetDock(down, Dock.Right);
            DockPanel.SetDock(up, Dock.Right);
            var check = new CheckBox { Content = PageNames[w.Id], IsChecked = w.On, VerticalAlignment = VerticalAlignment.Center };
            check.Click += (_, _) =>
            {
                w.On = check.IsChecked == true;
                _ctx.Settings.Save();
                PagesChanged?.Invoke();
            };
            row.Children.Add(down);
            row.Children.Add(up);
            row.Children.Add(check);
            Widgets.Children.Add(row);
        }
    }

    static Button IconButton(string glyph, string tip, bool enabled, Action click)
    {
        var b = new Button { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), FontSize = 16, Content = glyph, IsEnabled = enabled };
        b.Classes.Add("icon");
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    void Move(int i, int delta)
    {
        var list = _ctx.Settings.Widgets;
        int j = i + delta;
        if (j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        _ctx.Settings.Save();
        BuildWidgets();
        PagesChanged?.Invoke();
    }

    void BuildPlaces()
    {
        Places.Children.Clear();
        foreach (var place in _ctx.Settings.Places.ToList())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var remove = IconButton(Glyph.Close, "Remove", true, () =>
            {
                _ctx.Settings.Places.Remove(place);
                if (_ctx.Settings.ClockZone == place.Key) _ctx.Settings.ClockZone = null;
                _ctx.Settings.Save();
                BuildPlaces();
            });
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            string offset = place.TimeZone is { } z ? $"  ·  {Fmt.Offset(z, DateTime.UtcNow)}" : "  ·  unknown time zone";
            row.Children.Add(new TextBlock { Text = place.Name + offset, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            Places.Children.Add(row);
        }
    }

    void PlaceSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        _searchDelay.Stop();
        PlaceHits.Children.Clear();
        if ((PlaceSearch.Text ?? "").Trim().Length < 2) return;
        _searchDelay.Start();
    }

    /// <summary>
    /// Any city worldwide, via the same lookup the weather page uses (it returns each
    /// city's time zone). Falls back to the system's own time-zone names when offline.
    /// </summary>
    async Task SearchPlaces()
    {
        int version = ++_searchVersion;
        string q = (PlaceSearch.Text ?? "").Trim();
        var hits = new List<(string Label, ClockPlace Place)>();
        try
        {
            foreach (var p in await WeatherService.SearchAsync(q))
            {
                if (p.TimeZone is null) continue;
                var place = new ClockPlace(p.Name, p.TimeZone);
                if (place.TimeZone is null || _ctx.Settings.Places.Any(x => x.Key == place.Key)) continue;
                hits.Add((p.Label, place));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) { /* offline: fall through to system zones */ }

        if (hits.Count == 0)
            foreach (var z in TimeZoneInfo.GetSystemTimeZones()
                         .Where(z => z.Id.Replace('_', ' ').Contains(q, StringComparison.CurrentCultureIgnoreCase) || z.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                         .Take(4))
                hits.Add(($"{Fmt.City(z)} ({z.Id})", new ClockPlace(Fmt.City(z), z.Id)));

        if (version != _searchVersion) return;
        PlaceHits.Children.Clear();
        if (hits.Count == 0)
        {
            PlaceHits.Children.Add(new TextBlock { Text = "No places found.", FontSize = 12, Foreground = Res.Brush("Sub"), Margin = new Thickness(8, 4, 0, 0) });
            return;
        }
        foreach (var (label, place) in hits.Take(5))
        {
            var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Padding = new Thickness(8, 3, 8, 3) };
            b.Classes.Add("text");
            b.Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Res.Brush("Fg") };
            b.Click += (_, _) =>
            {
                _ctx.Settings.Places.Add(place);
                _ctx.Settings.Save();
                PlaceSearch.Text = "";
                BuildPlaces();
            };
            PlaceHits.Children.Add(b);
        }
    }

    void Startup_Click(object? sender, RoutedEventArgs e)
    {
        try { LinuxDesktop.SetStartAtLogin(Startup.IsChecked == true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Startup.IsChecked = LinuxDesktop.StartsAtLogin;
    }

    void Tuck_Click(object? sender, RoutedEventArgs e)
    {
        _ctx.Settings.TuckOverFullScreen = Tuck.IsChecked == true;
        _ctx.Settings.Save();
    }

    void Data_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Settings.Dir);
        LinuxDesktop.Open(Settings.Dir);
    }

    void Quit_Click(object? sender, RoutedEventArgs e) => App.Quit();
}
