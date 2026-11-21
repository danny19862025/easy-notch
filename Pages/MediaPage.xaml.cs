using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media;

namespace EasyNotch.Pages;

public partial class MediaPage : UserControl, IPage
{
    readonly PageContext _ctx;
    bool _seeking;

    public MediaPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _ctx.Media.Changed += () => { if (IsVisible) Refresh(); };
    }

    public void OnShown() => Refresh();
    public void OnTick() => UpdatePosition();

    void Refresh()
    {
        var m = _ctx.Media.State;
        bool has = m is not null && (m.Title.Length > 0 || m.Artist.Length > 0);
        Empty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        Body.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has) return;

        Title.Text = m!.Title;
        Artist.Text = m.Artist.Length > 0 ? m.Artist : m.App;
        Art.ImageSource = m.Art;
        PlayBtn.Content = m.IsPlaying ? "" : "";
        PlayBtn.ToolTip = m.IsPlaying ? "Pause" : "Play";
        Seek.IsEnabled = m.CanSeek;
        Seek.Maximum = Math.Max(1, m.Duration.TotalSeconds);

        ShuffleBtn.IsEnabled = m.CanShuffle;
        ShuffleBtn.Foreground = (Brush)FindResource(m.Shuffle ? "Accent" : "Sub");
        RepeatBtn.IsEnabled = m.CanRepeat;
        RepeatBtn.Content = m.Repeat == MediaPlaybackAutoRepeatMode.Track ? "" : "";
        RepeatBtn.Foreground = (Brush)FindResource(m.Repeat == MediaPlaybackAutoRepeatMode.None ? "Sub" : "Accent");
        RepeatBtn.ToolTip = m.Repeat switch
        {
            MediaPlaybackAutoRepeatMode.List => "Repeating all",
            MediaPlaybackAutoRepeatMode.Track => "Repeating this one",
            _ => "Repeat",
        };
        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (_ctx.Media.State is not { } m) return;
        var p = _ctx.Media.LivePosition;
        bool timed = m.Duration > TimeSpan.Zero;
        Pos.Text = timed ? Fmt.Time(p) : "";
        Left.Text = timed ? "−" + Fmt.Time(m.Duration - p) : "";
        if (!_seeking) Seek.Value = p.TotalSeconds;
    }

    void Seek_Down(object sender, MouseButtonEventArgs e) => _seeking = true;

    void Seek_Up(object sender, MouseButtonEventArgs e) =>
        Dispatcher.InvokeAsync(() => { _ctx.Media.Seek(TimeSpan.FromSeconds(Seek.Value)); _seeking = false; }, DispatcherPriority.Background);

    void Play_Click(object sender, RoutedEventArgs e) => _ctx.Media.PlayPause();
    void Prev_Click(object sender, RoutedEventArgs e) => _ctx.Media.Previous();
    void Next_Click(object sender, RoutedEventArgs e) => _ctx.Media.Next();
    void Shuffle_Click(object sender, RoutedEventArgs e) => _ctx.Media.ToggleShuffle();
    void Repeat_Click(object sender, RoutedEventArgs e) => _ctx.Media.CycleRepeat();
}
