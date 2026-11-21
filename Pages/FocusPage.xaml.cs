using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class FocusPage : UserControl, IPage
{
    readonly PageContext _ctx;

    public FocusPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _ctx.Focus.Changed += () => { if (IsVisible) Refresh(); };
    }

    public void OnShown() => Refresh();
    public void OnTick() { }

    void Refresh()
    {
        var f = _ctx.Focus;
        var s = _ctx.Settings;
        bool idle = f.Phase == FocusPhase.Idle;

        NoiseText.Text = char.ToUpper(s.Noise[0]) + s.Noise[1..];
        Minutes.Text = s.FocusMinutes.ToString();
        Picker.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        Running.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        Countdown.Text = Fmt.Time(f.Remaining);
        Phase.Text = f.Phase == FocusPhase.Break ? "Break" : "Focus";
        if (f.IsPaused) Phase.Text += " · paused";
        Phase.Foreground = (Brush)FindResource(f.Phase == FocusPhase.Break ? "Rest" : "Focus");

        bool zen = s.FocusMode == "zen";
        PomodoroBtn.Foreground = (Brush)FindResource(zen ? "Sub" : "Fg");
        PomodoroBtn.FontWeight = zen ? FontWeights.Normal : FontWeights.Bold;
        ZenBtn.Foreground = (Brush)FindResource(zen ? "Fg" : "Sub");
        ZenBtn.FontWeight = zen ? FontWeights.Bold : FontWeights.Normal;

        PlayBtn.Content = f.IsRunning ? "" : "";
        PlayBtn.ToolTip = idle ? "Start" : f.IsPaused ? "Resume" : "Pause";
        StopBtn.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
    }

    void Noise_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = NoiseBtn, Placement = PlacementMode.Bottom };
        foreach (var kind in Noise.Kinds)
        {
            var item = new MenuItem
            {
                Header = kind == "off" ? "Off" : $"{char.ToUpper(kind[0])}{kind[1..]} noise",
                IsChecked = _ctx.Settings.Noise == kind,
            };
            item.Click += (_, _) =>
            {
                _ctx.Settings.Noise = kind;
                _ctx.Settings.Save();
                _ctx.Focus.ApplyNoiseSetting();
                Refresh();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void Minus_Click(object sender, RoutedEventArgs e) => Adjust(-5);
    void Plus_Click(object sender, RoutedEventArgs e) => Adjust(+5);

    void Adjust(int delta)
    {
        var s = _ctx.Settings;
        // 1, 5, 10 … : small steps near zero so very short sessions are possible.
        int m = s.FocusMinutes;
        m = delta < 0 ? (m <= 5 ? Math.Max(1, m - 1) : m - 5) : (m < 5 ? m + 1 : m + 5);
        s.FocusMinutes = Math.Clamp(m, 1, 180);
        s.Save();
        Refresh();
    }

    void Pomodoro_Click(object sender, RoutedEventArgs e) => SetMode("pomodoro");
    void Zen_Click(object sender, RoutedEventArgs e) => SetMode("zen");

    void SetMode(string mode)
    {
        _ctx.Settings.FocusMode = mode;
        _ctx.Settings.Save();
        Refresh();
    }

    void Play_Click(object sender, RoutedEventArgs e) => _ctx.Focus.TogglePause();
    void Stop_Click(object sender, RoutedEventArgs e) => _ctx.Focus.Stop();
    void Settings_Click(object sender, RoutedEventArgs e) => _ctx.GoTo("settings");
}
