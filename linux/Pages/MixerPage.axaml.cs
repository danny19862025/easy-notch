using Avalonia.Interactivity;
using Avalonia.Layout;
using EasyNotch.Controls;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class MixerPage : UserControl, IPage
{
    sealed class Strip
    {
        public required VolumeChannel Channel;
        public required Fader Fader;
        public required Button Icon;
        public required Grid Column;
        /// <summary>After a change here, ignore the sound server's (older) value for a moment.</summary>
        public DateTime HoldUntil;
    }

    readonly PageContext _ctx;
    readonly List<Strip> _strips = [];
    VolumeChannel? _out, _mic;
    DateTime _masterHold, _micHold;
    bool _masterUpdating, _reading;

    public MixerPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        Master.UserChanged += v =>
        {
            if (_masterUpdating || _out is null) return;
            if (_out.Muted && v > 0) _out.Muted = false;
            _out.Volume = (float)v;
            _masterHold = DateTime.UtcNow.AddSeconds(1.5);
            ShowMaster();
        };
    }

    public void OnShown() => Refresh();
    public void OnTick() => Refresh();

    async void Refresh()
    {
        if (_reading) return;
        _reading = true;
        MixerSnapshot snap;
        try { snap = await Task.Run(AudioMixer.Read); }
        finally { _reading = false; }
        Apply(snap);
    }

    void Apply(MixerSnapshot snap)
    {
        var now = DateTime.UtcNow;
        if (now >= _masterHold || _out is null || snap.Output?.Key != _out.Key) _out = snap.Output;
        if (now >= _micHold || _mic is null) _mic = snap.Microphone;

        DeviceName.Text = _out?.DeviceName ?? "No output device";
        Master.IsEnabled = _out is not null;
        if (!Master.IsDragging) ShowMaster();

        MicMute.IsVisible = _mic is not null;
        if (_mic is not null)
        {
            bool mm = _mic.Muted;
            MicMute.Content = mm ? Glyph.MicOff : Glyph.Mic;
            MicMute.Foreground = Res.Brush(mm ? "Focus" : "Sub");
            ToolTip.SetTip(MicMute, mm ? "Microphone is muted — click to unmute" : "Mute microphone");
        }

        var apps = snap.Apps;
        if (!apps.Select(a => a.Key).SequenceEqual(_strips.Select(f => f.Channel.Key))) Build(apps);
        else
            for (int i = 0; i < apps.Count; i++)
            {
                var s = _strips[i];
                if (s.Fader.IsDragging || now < s.HoldUntil) continue;
                s.Channel = apps[i];
                ShowStrip(s);
            }
    }

    void ShowMaster()
    {
        if (_out is null) { MasterValue.Text = "–"; MasterIcon.Text = Glyph.VolOff; return; }
        bool muted = _out.Muted;
        int v = (int)Math.Round(_out.Volume * 100);
        _masterUpdating = true;
        Master.Value = muted ? 0 : v / 100.0;
        _masterUpdating = false;
        MasterValue.Text = muted ? "Muted" : v.ToString();
        MasterIcon.Text = VolumeGlyph(v, muted);
        SpeakerMute.Content = muted ? Glyph.VolOff : Glyph.VolUp;
        SpeakerMute.Foreground = Res.Brush(muted ? "Focus" : "Sub");
        ToolTip.SetTip(SpeakerMute, muted ? "Sound is muted — click to unmute" : "Mute sound");
    }

    static string VolumeGlyph(int v, bool muted) =>
        muted ? Glyph.VolOff : v == 0 ? Glyph.VolMute : v < 50 ? Glyph.VolDown : Glyph.VolUp;

    void Build(List<VolumeChannel> apps)
    {
        Faders.Children.Clear();
        _strips.Clear();
        NoApps.IsVisible = apps.Count == 0;
        AppScroller.IsVisible = apps.Count > 0;

        foreach (var ch in apps)
        {
            var col = new Grid { Width = 50, RowDefinitions = new RowDefinitions("*,Auto"), Background = Brushes.Transparent };
            var fader = new Fader { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 6) };
            var icon = new Button { Width = 34, Height = 34, CornerRadius = new CornerRadius(17) };
            icon.Classes.Add("icon");
            icon.Content = ch.Icon is not null
                ? new Image { Source = ch.Icon, Width = 24, Height = 24 }
                : new TextBlock { Text = Glyph.Apps, FontFamily = Res.Icons, FontSize = 22 };
            Grid.SetRow(icon, 1);
            col.Children.Add(fader);
            col.Children.Add(icon);

            var s = new Strip { Channel = ch, Fader = fader, Icon = icon, Column = col };
            fader.UserChanged += v =>
            {
                // Unmute first: the new level then replaces whatever unmuting restores.
                if (v > 0 && s.Channel.Muted) s.Channel.Muted = false;
                s.Channel.Volume = (float)(v / 100);
                s.HoldUntil = DateTime.UtcNow.AddSeconds(1.5);
                ShowStrip(s, keepFader: true);
            };
            icon.Click += (_, _) =>
            {
                s.Channel.Muted = !s.Channel.Muted;
                s.HoldUntil = DateTime.UtcNow.AddSeconds(1.5);
                ShowStrip(s);
            };

            _strips.Add(s);
            Faders.Children.Add(col);
            ShowStrip(s);
        }
    }

    void ShowStrip(Strip s, bool keepFader = false)
    {
        bool muted = s.Channel.Muted;
        int v = (int)Math.Round(s.Channel.Volume * 100);
        if (!keepFader) s.Fader.Value = muted ? 0 : v;
        s.Icon.Opacity = muted ? 0.35 : 1;
        ToolTip.SetTip(s.Column, $"{s.Channel.Name} · {(muted ? "muted" : $"{v}%")}");
    }

    void SpeakerMute_Click(object? sender, RoutedEventArgs e)
    {
        if (_out is null) return;
        _out.Muted = !_out.Muted;
        _masterHold = DateTime.UtcNow.AddSeconds(1.5);
        ShowMaster();
    }

    void MicMute_Click(object? sender, RoutedEventArgs e)
    {
        if (_mic is null) return;
        _mic.Muted = !_mic.Muted;
        _micHold = DateTime.UtcNow.AddSeconds(1.5);
        Apply(new MixerSnapshot(_out, _mic, _strips.Select(x => x.Channel).ToList()));
    }
}
