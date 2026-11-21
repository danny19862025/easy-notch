using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class MixerPage : UserControl, IPage
{
    sealed class Fader
    {
        public required VolumeChannel Channel;
        public required Slider Slider;
        public required Button Icon;
        public bool Updating;
    }

    readonly PageContext _ctx;
    readonly List<Fader> _faders = [];
    VolumeChannel? _out, _mic;
    bool _masterUpdating;

    public MixerPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        Master.UserChanged += v =>
        {
            if (_masterUpdating || _out is null) return;
            if (_out.Muted && v > 0) _out.Muted = false;
            _out.Volume = (float)v;
            ShowMaster();
        };
    }

    public void OnShown() => Refresh();
    public void OnTick() => Refresh();

    void Refresh()
    {
        _out = AudioMixer.Output();
        _mic = AudioMixer.Microphone();

        DeviceName.Text = _out?.DeviceName ?? "No output device";
        Master.IsEnabled = _out is not null;
        if (!Master.IsMouseCaptured) ShowMaster();

        MicMute.Visibility = _mic is null ? Visibility.Collapsed : Visibility.Visible;
        if (_mic is not null)
        {
            bool mm = _mic.Muted;
            MicMute.Content = mm ? "" : "";
            MicMute.Foreground = (Brush)FindResource(mm ? "Focus" : "Sub");
            MicMute.ToolTip = mm ? "Microphone is muted — click to unmute" : "Mute microphone";
        }

        var apps = AudioMixer.Apps();
        if (!apps.Select(a => a.Key).SequenceEqual(_faders.Select(f => f.Channel.Key))) Build(apps);
        else
            for (int i = 0; i < apps.Count; i++)
            {
                _faders[i].Channel = apps[i];
                if (!_faders[i].Slider.IsMouseCaptureWithin) ShowFader(_faders[i]);
            }
    }

    void ShowMaster()
    {
        if (_out is null) { MasterValue.Text = "–"; MasterIcon.Text = ""; return; }
        bool muted = _out.Muted;
        int v = (int)Math.Round(_out.Volume * 100);
        _masterUpdating = true;
        Master.Value = muted ? 0 : v / 100.0;
        _masterUpdating = false;
        MasterValue.Text = muted ? "Muted" : v.ToString();
        MasterIcon.Text = VolumeGlyph(v, muted);
        SpeakerMute.Content = muted ? "" : "";
        SpeakerMute.Foreground = (Brush)FindResource(muted ? "Focus" : "Sub");
        SpeakerMute.ToolTip = muted ? "Sound is muted — click to unmute" : "Mute sound";
    }

    static string VolumeGlyph(int v, bool muted) =>
        muted ? "" : v == 0 ? "" : v < 34 ? "" : v < 67 ? "" : "";

    void Build(List<VolumeChannel> apps)
    {
        Faders.Children.Clear();
        _faders.Clear();
        NoApps.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppScroller.Visibility = apps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (var ch in apps)
        {
            var col = new Grid { Width = 50, ToolTip = ch.Name };
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var slider = new Slider { Style = (Style)FindResource("Fader"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 6) };
            var icon = new Button { Style = (Style)FindResource("IconBtn"), Width = 34, Height = 34 };
            icon.Content = ch.Icon is not null
                ? new Image { Source = ch.Icon, Width = 24, Height = 24 }
                : new TextBlock { Text = ch.Key == "system" ? "" : "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 18 };
            Grid.SetRow(icon, 1);
            col.Children.Add(slider);
            col.Children.Add(icon);

            var f = new Fader { Channel = ch, Slider = slider, Icon = icon };
            slider.ValueChanged += (_, e) =>
            {
                if (f.Updating) return;
                // Unmute first: unmuting may restore an old level, which the new value then replaces.
                if (e.NewValue > 0 && f.Channel.Muted) f.Channel.Muted = false;
                f.Channel.Volume = (float)(e.NewValue / 100);
                ShowFader(f, keepSlider: true);
            };
            col.PreviewMouseWheel += (_, e) => { slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? 3 : -3), 0, 100); e.Handled = true; };
            icon.Click += (_, _) => { f.Channel.Muted = !f.Channel.Muted; ShowFader(f); };

            _faders.Add(f);
            Faders.Children.Add(col);
            ShowFader(f);
        }
    }

    void ShowFader(Fader f, bool keepSlider = false)
    {
        bool muted = f.Channel.Muted;
        int v = (int)Math.Round(f.Channel.Volume * 100);
        if (!keepSlider)
        {
            f.Updating = true;
            f.Slider.Value = muted ? 0 : v;
            f.Updating = false;
        }
        f.Icon.Opacity = muted ? 0.35 : 1;
        ((FrameworkElement)f.Slider.Parent).ToolTip = $"{f.Channel.Name} · {(muted ? "muted" : $"{v}%")}";
    }

    void SpeakerMute_Click(object sender, RoutedEventArgs e)
    {
        if (_out is null) return;
        _out.Muted = !_out.Muted;
        ShowMaster();
    }

    void MicMute_Click(object sender, RoutedEventArgs e)
    {
        if (_mic is null) return;
        _mic.Muted = !_mic.Muted;
        Refresh();
    }
}
