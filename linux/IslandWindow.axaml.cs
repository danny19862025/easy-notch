using Avalonia.Threading;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EasyNotch.Controls;
using EasyNotch.Pages;
using EasyNotch.Services;

namespace EasyNotch;

public partial class IslandWindow : Window
{
    const double CompactH = 32, ExpandedW = 600, ExpandedH = 232, TuckW = 120, TuckH = 4;

    readonly Settings _settings;
    readonly MediaService _media = new();
    readonly FocusTimer _focus;
    readonly ClipboardHistory _clips = new();
    readonly NotesStore _notes = new();
    readonly WeatherService _weather;
    readonly UpdateChecker _updates;
    readonly SettingsPage _settingsPage;
    SettingsWindow? _settingsWindow;
    readonly Dictionary<string, (Control View, IPage Page)> _pages = [];
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TranslateTransform _pageSlide = new();
    readonly ImageBrush _compactArt = new() { Stretch = Stretch.UniformToFill };
    static readonly Cursor Hand = new(StandardCursorType.Hand);

    List<string> _order = [];
    string _current = "clock";
    bool _expanded, _tucked, _hovering;

    public IslandWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _focus = new FocusTimer(settings);
        _weather = new WeatherService(settings);
        _updates = new UpdateChecker(settings);
        Icon = App.AppIcon;
        PageHost.RenderTransform = _pageSlide;
        CompactArtBox.Background = _compactArt;
        foreach (var bar in new[] { Eq1, Eq2, Eq3 }) bar.RenderTransform = new ScaleTransform(1, 0.5);

        var ctx = new PageContext
        {
            Settings = settings, Media = _media, Focus = _focus, Clips = _clips, Notes = _notes, Weather = _weather,
            Updates = _updates, GoTo = id => GoTo(id),
        };
        Add("clock", new ClockPage(ctx));
        Add("weather", new WeatherPage(ctx));
        Add("media", new MediaPage(ctx));
        Add("focus", new FocusPage(ctx));
        Add("notes", new NotesPage(ctx));
        Add("clipboard", new ClipboardPage(ctx));
        Add("mixer", new MixerPage(ctx));
        Add("about", new AboutPage(ctx));
        // Settings lives in its own window, not in the notch.
        _settingsPage = new SettingsPage(ctx);
        _settingsPage.PagesChanged += () =>
        {
            BuildOrder();
            if (!_order.Contains(_current)) _current = _order.FirstOrDefault() ?? "about";
            BuildDots();
            UpdateCompact();
        };

        Opened += async (_, _) =>
        {
            Reposition();
            LinuxDesktop.ApplyNotchHints(this);
            // A new window is still being managed when it first appears, so place it again shortly after.
            DispatcherTimer.RunOnce(() => { Reposition(); LinuxDesktop.ApplyNotchHints(this); UpdateInputRegion(); }, TimeSpan.FromMilliseconds(500));
            UpdateInputRegion();
            _clips.Attach(this);
            if (IsOn("weather")) _weather.Start();
            _ = _updates.CheckIfDueAsync();
            await _media.StartAsync();
        };
        Deactivated += (_, _) => { if (_expanded) Collapse(); };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Notch.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Bubble, handledEventsToo: true);
        Notch.PointerReleased += Notch_Click;
        Notch.PointerEntered += (_, _) => { _hovering = true; if (_tucked) UpdateCompact(force: true); };
        Notch.PointerExited += (_, _) => { _hovering = false; if (_tucked) UpdateCompact(force: true); };
        Handle.PointerReleased += (_, e) => { Collapse(); e.Handled = true; };
        Notch.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) UpdateInputRegion(); };
        Screens.Changed += (_, _) => Dispatcher.UIThread.Post(Reposition);
        ScalingChanged += (_, _) => { Reposition(); UpdateInputRegion(); };

        _media.Changed += () => UpdateCompact();
        _focus.Changed += () => UpdateCompact();
        _weather.Changed += () => UpdateCompact();
        _focus.PhaseEnded += _ => { if (!_expanded && IsOn("focus")) Expand("focus"); };
        _tick.Tick += (_, _) => OnTick();

        BuildOrder();
        _current = _order.FirstOrDefault() ?? "about";
        BuildDots();
        UpdateCompact(force: true);
        _tick.Start();
    }

    void Add(string id, UserControl page) => _pages[id] = (page, (IPage)page);

    // ---------------------------------------------------------------- window plumbing

    /// <summary>Top centre of the primary screen.</summary>
    void Reposition()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var b = screen.Bounds;
        double scale = screen.Scaling > 0 ? screen.Scaling : 1;
        Position = new PixelPoint(b.X + (int)Math.Round((b.Width - Width * scale) / 2), b.Y);
    }

    /// <summary>Only the notch takes the pointer; the transparent rest of the window passes clicks through.</summary>
    void UpdateInputRegion()
    {
        double s = RenderScaling;
        var r = Notch.Bounds;
        int height = (int)Math.Ceiling(Math.Max(r.Height, 6) * s); // a tucked notch is a thin line; keep it easy to hover
        LinuxDesktop.SetInputRegion(this, new PixelRect((int)Math.Floor(r.X * s), (int)Math.Floor(r.Y * s), (int)Math.Ceiling(r.Width * s), height));
    }

    void OnTick()
    {
        CheckFullScreen();
        if (_expanded) _pages[_current].Page.OnTick();
        else if (!_focus.IsRunning) UpdateCompact(); // keeps the compact clock current
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_expanded) return;
        if (_current == "notes" && _pages["notes"].Page is NotesPage np && np.HandleEscape()) { e.Handled = true; return; }
        if (_current == "weather" && _pages["weather"].Page is WeatherPage wp && wp.HandleEscape()) { e.Handled = true; return; }
        Collapse();
        e.Handled = true;
    }

    // ---------------------------------------------------------------- pages

    bool IsOn(string id) => _settings.Widgets.Any(w => w.Id == id && w.On);

    void BuildOrder() => _order = _settings.Widgets.Where(w => w.On).Select(w => w.Id).ToList();

    void BuildDots()
    {
        Dots.Children.Clear();
        foreach (var id in _order)
        {
            bool on = id == _current;
            var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 4, 0, 4), Fill = Res.Brush(on ? "Fg" : "Faint") };
            // A larger invisible hit target around each dot.
            var hit = new Border { Background = Brushes.Transparent, Padding = new Thickness(5, 0, 5, 0), Child = dot, Cursor = Hand };
            ToolTip.SetTip(hit, SettingsPage.PageNames[id]);
            string target = id;
            hit.PointerReleased += (_, e) => { GoTo(target); e.Handled = true; };
            Dots.Children.Add(hit);
        }
    }

    public void GoTo(string id, int direction = 0)
    {
        if (id == "settings") { OpenSettings(); return; }
        if (!_pages.ContainsKey(id)) return;
        if (!_expanded) { Expand(id); return; }
        bool changed = id != _current || PageHost.Content is null;
        _current = id;
        var (view, page) = _pages[id];
        if (!ReferenceEquals(PageHost.Content, view)) PageHost.Content = view;
        page.OnShown();
        if (Math.Abs(Notch.BodyHeight - ExpandedH) > 0.5 || changed) AnimateSize(ExpandedW, ExpandedH, 40);
        BuildDots();
        if (changed && Anim.Enabled)
        {
            double from = direction == 0 ? 10 : direction * 18;
            Anim.To(_pageSlide, "y", from, 0, 220, v => _pageSlide.Y = v);
            Anim.To(PageHost, "opacity", 0, 1, 180, v => PageHost.Opacity = v);
        }
    }

    double _wheelAccum;
    DateTime _lastFlip;

    /// <summary>
    /// Scrolling over the notch flips pages, except over things that scroll themselves
    /// (lists, the knob, faders) or sideways card strips, which scroll horizontally.
    /// </summary>
    void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!_expanded) return;
        for (var v = e.Source as Visual; v is not null && v != Notch; v = v.GetVisualParent())
        {
            if (v is Knob or Fader or SeekBar) return;
            if (v is ScrollViewer sv)
            {
                bool vertical = sv.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                if (vertical && sv.Extent.Height > sv.Viewport.Height + 0.5) return;
                if (sv.Extent.Width > sv.Viewport.Width + 0.5)
                {
                    // Sideways strips (notes, mixer): the wheel scrolls them.
                    double step = (e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X) * 60;
                    sv.Offset = sv.Offset.WithX(Math.Clamp(sv.Offset.X - step, 0, sv.Extent.Width - sv.Viewport.Width));
                    e.Handled = true;
                    return;
                }
            }
            if (v is TextPresenter) return; // the note editor scrolls its own text
        }
        e.Handled = true;
        if (_order.Count < 2) return;
        _wheelAccum += e.Delta.Y;
        if (Math.Abs(_wheelAccum) < 1 || (DateTime.Now - _lastFlip).TotalMilliseconds < 280) return;
        int dir = _wheelAccum < 0 ? 1 : -1;
        _wheelAccum = 0;
        _lastFlip = DateTime.Now;
        int i = _order.IndexOf(_current);
        int next = Math.Clamp(i + dir, 0, _order.Count - 1);
        if (next != i) GoTo(_order[next], dir);
    }

    // ---------------------------------------------------------------- expand / collapse

    void AnimateSize(double w, double h, double radius)
    {
        double ms = 280;
        Anim.To(Notch, "w", Notch.BodyWidth, w, ms, v => Notch.BodyWidth = v);
        Anim.To(Notch, "h", Notch.BodyHeight, h, ms, v => Notch.BodyHeight = v);
        Anim.To(Notch, "r", Notch.BottomRadius, radius, ms, v => Notch.BottomRadius = v);
    }

    void Expand(string? page = null)
    {
        _expanded = true;
        Notch.Cursor = null;
        Anim.Fade(Compact, 0, hideAtEnd: true);
        Anim.Fade(Expanded, 1);
        StopEq();
        AnimateSize(ExpandedW, ExpandedH, 40);
        if (page is not null && !_pages.ContainsKey(page)) page = null;
        GoTo(page ?? (_order.Contains(_current) ? _current : _order.FirstOrDefault() ?? "about"));
        Activate();
        LinuxDesktop.Activate(this);
    }

    /// <summary>Opens (or brings forward) the Settings window and tucks the notch away.</summary>
    public void OpenSettings()
    {
        if (_expanded) Collapse();
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            LinuxDesktop.Activate(_settingsWindow);
            return;
        }
        _settingsWindow = new SettingsWindow(_settingsPage);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
        var w = _settingsWindow;
        DispatcherTimer.RunOnce(() => { if (w.IsVisible) LinuxDesktop.Activate(w); }, TimeSpan.FromMilliseconds(300));
    }

    void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        if (_pages["notes"].Page is NotesPage np) np.Flush();
        Anim.Fade(Expanded, 0, hideAtEnd: true);
        Anim.Fade(Compact, 1);
        UpdateCompact(force: true);
    }

    void Notch_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && !_expanded) Expand();
    }

    // ---------------------------------------------------------------- full-screen tuck

    void CheckFullScreen()
    {
        bool full = _settings.TuckOverFullScreen && !_expanded && LinuxDesktop.ActiveWindowIsFullScreen(this);
        if (full != _tucked) { _tucked = full; UpdateCompact(force: true); }
    }

    // ---------------------------------------------------------------- compact view

    double _compactW;

    void UpdateCompact(bool force = false)
    {
        if (_expanded) return;

        if (_tucked && !_hovering)
        {
            Anim.Stop(Compact, "opacity");
            Compact.Opacity = 0;
            StopEq();
            _compactW = TuckW;
            AnimateSize(TuckW, TuckH, 2);
            return;
        }
        Anim.Stop(Compact, "opacity");
        Compact.Opacity = 1;
        Compact.IsVisible = true;
        Notch.Cursor = Hand;

        double w;
        var m = _media.State;
        if (IsOn("focus") && _focus.Phase != FocusPhase.Idle)
        {
            ShowGlyph(Glyph.Timer, _focus.Phase == FocusPhase.Focus ? "Focus" : "Rest");
            CompactText.Text = (_focus.Phase == FocusPhase.Focus ? "Focus" : "Break") + (_focus.IsPaused ? " · paused" : "");
            CompactRight.Text = Fmt.Time(_focus.Remaining);
            CompactRight.IsVisible = true;
            Eq.IsVisible = false;
            StopEq();
            w = 200;
        }
        else if (IsOn("media") && m is { IsPlaying: true })
        {
            if (m.Art is not null)
            {
                _compactArt.Source = m.Art;
                CompactArtBox.IsVisible = true;
                CompactGlyph.IsVisible = false;
            }
            else ShowGlyph(Glyph.Music, "Accent");
            CompactText.Text = string.IsNullOrEmpty(m.Artist) ? m.Title : $"{m.Title} · {m.Artist}";
            CompactRight.IsVisible = false;
            Eq.IsVisible = true;
            StartEq();
            w = 300;
        }
        else
        {
            ShowGlyph(Glyph.Clock, "Sub");
            var now = DateTime.Now;
            CompactText.Text = now.ToString("t");
            // With weather on, the right side shows the temperature instead of the date.
            CompactRight.Text = IsOn("weather") && _weather.Data is { } wd ? $"{Math.Round(wd.Now.Temp)}°" : now.ToString("ddd d");
            CompactRight.IsVisible = true;
            Eq.IsVisible = false;
            StopEq();
            w = 170;
        }

        if (force || w != _compactW || Notch.BodyHeight != CompactH)
        {
            _compactW = w;
            AnimateSize(w, CompactH, 16);
        }
    }

    void ShowGlyph(string glyph, string brushKey)
    {
        CompactArtBox.IsVisible = false;
        CompactGlyph.IsVisible = true;
        CompactGlyph.Text = glyph;
        CompactGlyph.Foreground = Res.Brush(brushKey);
    }

    // The little equalizer next to the song: three bars bouncing at different speeds.
    DispatcherTimer? _eq;
    readonly System.Diagnostics.Stopwatch _eqClock = new();

    void StartEq()
    {
        if (_eq is not null || !Anim.Enabled) return;
        _eqClock.Restart();
        double[] ms = [420, 560, 360];
        var bars = new[] { Eq1, Eq2, Eq3 };
        _eq = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            double t = _eqClock.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 3; i++)
            {
                // Up and back down (auto-reverse) with a sine ease, like the Windows version.
                double phase = t / ms[i] % 2;
                double p = phase <= 1 ? phase : 2 - phase;
                double eased = 0.5 - 0.5 * Math.Cos(Math.PI * p);
                ((ScaleTransform)bars[i].RenderTransform!).ScaleY = 0.25 + 0.75 * eased;
            }
        });
    }

    void StopEq()
    {
        _eq?.Stop();
        _eq = null;
    }

    // ---------------------------------------------------------------- tray icon / other launches

    public void OpenFromTray(string? page = null)
    {
        if (page == "settings") OpenSettings();
        else if (_expanded && page is not null) GoTo(page);
        else if (_expanded) { Activate(); LinuxDesktop.Activate(this); }
        else Expand(page);
    }

    public void FlushNotes()
    {
        if (_pages["notes"].Page is NotesPage np) np.Flush();
    }
}
