using Avalonia.Interactivity;

namespace EasyNotch.Pages;

public partial class MediaPage : UserControl, IPage
{
    readonly PageContext _ctx;
    readonly ImageBrush _art = new() { Stretch = Stretch.UniformToFill };

    public MediaPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        ArtBox.Background = _art;
        _ctx.Media.Changed += () => { if (IsVisible && Parent is not null) Refresh(); };
        Seek.Committed += v => _ctx.Media.Seek(TimeSpan.FromSeconds(v));
    }

    public void OnShown() => Refresh();
    public void OnTick() => UpdatePosition();

    void Refresh()
    {
        var m = _ctx.Media.State;
        bool has = m is not null && (m.Title.Length > 0 || m.Artist.Length > 0);
        Empty.IsVisible = !has;
        Body.IsVisible = has;
        if (!has) return;

        TitleText.Text = m!.Title;
        ArtistText.Text = m.Artist.Length > 0 ? m.Artist : m.App;
        _art.Source = m.Art;
        ArtBox.IsVisible = m.Art is not null;
        PlayBtn.Content = m.IsPlaying ? Glyph.Pause : Glyph.Play;
        ToolTip.SetTip(PlayBtn, m.IsPlaying ? "Pause" : "Play");
        Seek.IsEnabled = m.CanSeek;
        Seek.Maximum = Math.Max(1, m.Duration.TotalSeconds);

        ShuffleBtn.IsEnabled = m.CanShuffle;
        ShuffleBtn.Foreground = Res.Brush(m.Shuffle ? "Accent" : "Sub");
        RepeatBtn.IsEnabled = m.CanRepeat;
        RepeatBtn.Content = m.Repeat == "Track" ? Glyph.RepeatOne : Glyph.Repeat;
        RepeatBtn.Foreground = Res.Brush(m.Repeat == "None" ? "Sub" : "Accent");
        ToolTip.SetTip(RepeatBtn, m.Repeat switch
        {
            "Playlist" => "Repeating all",
            "Track" => "Repeating this one",
            _ => "Repeat",
        });
        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (_ctx.Media.State is not { } m) return;
        var p = _ctx.Media.LivePosition;
        bool timed = m.Duration > TimeSpan.Zero;
        Pos.Text = timed ? Fmt.Time(p) : "";
        LeftText.Text = timed ? "−" + Fmt.Time(m.Duration - p) : "";
        if (!Seek.IsSeeking) Seek.Value = p.TotalSeconds;
    }

    void Play_Click(object? sender, RoutedEventArgs e) => _ctx.Media.PlayPause();
    void Prev_Click(object? sender, RoutedEventArgs e) => _ctx.Media.Previous();
    void Next_Click(object? sender, RoutedEventArgs e) => _ctx.Media.Next();
    void Shuffle_Click(object? sender, RoutedEventArgs e) => _ctx.Media.ToggleShuffle();
    void Repeat_Click(object? sender, RoutedEventArgs e) => _ctx.Media.CycleRepeat();
}
