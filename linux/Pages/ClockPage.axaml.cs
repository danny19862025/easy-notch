using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace EasyNotch.Pages;

public partial class ClockPage : UserControl, IPage
{
    readonly PageContext _ctx;

    public ClockPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
    }

    /// <summary>This computer's own place first, then the saved places (up to five rows fit).</summary>
    List<(string Key, string Name, TimeZoneInfo Zone)> Places()
    {
        var list = new List<(string, string, TimeZoneInfo)> { ("", Fmt.City(TimeZoneInfo.Local), TimeZoneInfo.Local) };
        foreach (var p in _ctx.Settings.Places)
            if (p.TimeZone is { } z) list.Add((p.Key, p.Name, z));
        return list.Take(5).ToList();
    }

    (string Key, string Name, TimeZoneInfo Zone) Shown
    {
        get
        {
            var all = Places();
            return all.FirstOrDefault(p => p.Key == _ctx.Settings.ClockZone && p.Key.Length > 0) is { Name: not null } hit ? hit : all[0];
        }
    }

    public void OnShown()
    {
        BuildCities();
        OnTick();
    }

    public void OnTick()
    {
        var utc = DateTime.UtcNow;
        var shown = Shown;
        var t = TimeZoneInfo.ConvertTimeFromUtc(utc, shown.Zone);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        bool h12 = culture.DateTimeFormat.ShortTimePattern.Contains('h');
        DateText.Text = t.ToString("dddd, MMMM d", culture);
        TimeText.Text = t.ToString(h12 ? "h:mm" : "HH:mm", culture);
        AmPm.Text = h12 ? t.ToString("tt", culture) : "";
        ZoneText.Text = $"{shown.Name} · {Fmt.Offset(shown.Zone, utc)}";
    }

    void BuildCities()
    {
        Cities.Children.Clear();
        string current = Shown.Key;
        foreach (var (key, name, zone) in Places())
        {
            bool on = key == current;
            var row = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 4, 10, 4) };
            row.Classes.Add("text");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Res.Brush("Sub"), Margin = new Thickness(0, 0, 10, 0), Opacity = on ? 1 : 0, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text = name, FontSize = 15, FontWeight = on ? FontWeight.Bold : FontWeight.SemiBold,
                Foreground = Res.Brush(on ? "Fg" : "Sub"), MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            row.Content = sp;
            ToolTip.SetTip(row, $"{name} · {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone):t}");
            string k = key;
            row.Click += (_, _) =>
            {
                _ctx.Settings.ClockZone = k.Length == 0 ? null : k;
                _ctx.Settings.Save();
                OnShown();
            };
            Cities.Children.Add(row);
        }
    }

    void Settings_Click(object? sender, RoutedEventArgs e) => _ctx.GoTo("settings");
}
