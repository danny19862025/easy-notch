using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using EasyNotch.Controls;
using EasyNotch.Pages;
using EasyNotch.Services;

namespace EasyNotch;

public partial class IslandWindow : Window
{
    const double CompactH = 32, ExpandedW = 600, ExpandedH = 232, TuckW = 120, TuckH = 4;

    /// <summary>All pages share one height (kept as a hook in case a page ever needs more room).</summary>
    static double HeightFor(string page) => ExpandedH;

    readonly Settings _settings;
    readonly MediaService _media;
    readonly FocusTimer _focus;
    readonly ClipboardHistory _clips = new();
    readonly NotesStore _notes = new();
    readonly WeatherService _weather;
    readonly UpdateChecker _updates;
    readonly SettingsPage _settingsPage;
    SettingsWindow? _settingsWindow;
    readonly Dictionary<string, (FrameworkElement View, IPage Page)> _pages = [];
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    List<string> _order = [];
    string _current = "clock";
    bool _expanded, _tucked, _hovering;

    public IslandWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _media = new MediaService(Dispatcher);
        _focus = new FocusTimer(settings);
        _weather = new WeatherService(settings);
        _updates = new UpdateChecker(settings);

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

        SourceInitialized += (_, _) => MakeToolWindow();
        Loaded += async (_, _) =>
        {
            Reposition();
            _clips.Attach(this);
            if (IsOn("weather")) _weather.Start();
            _ = _updates.CheckIfDueAsync();
            await _media.StartAsync();
        };
        Deactivated += (_, _) => { if (_expanded) Collapse(); };
        PreviewKeyDown += OnKey;
        Notch.AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnWheel), handledEventsToo: true);
        SystemParameters.StaticPropertyChanged += (_, e) => { if (e.PropertyName == nameof(SystemParameters.WorkArea)) Reposition(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(Reposition);

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

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    void MakeToolWindow()
    {
        // Tool window: stays out of Alt+Tab and the taskbar.
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, -20 /* GWL_EXSTYLE */, GetWindowLong(h, -20) | 0x80 /* WS_EX_TOOLWINDOW */);
    }

    void Reposition()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    void OnTick()
    {
        CheckFullScreen();
        if (_expanded) _pages[_current].Page.OnTick();
        else if (!_focus.IsRunning) UpdateCompact(); // keeps the compact clock current
    }

    void OnKey(object sender, KeyEventArgs e)
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
            var dot = new Ellipse
            {
                Width = 6, Height = 6, Margin = new Thickness(0, 4, 0, 4),
                Fill = (Brush)FindResource(on ? "Fg" : "Faint"),
            };
            // A larger invisible hit target around each dot.
            var hit = new Border
            {
                Background = Brushes.Transparent, Padding = new Thickness(5, 0, 5, 0), Child = dot, Cursor = Cursors.Hand,
                ToolTip = SettingsPage.PageInfo[id].Name,
            };
            string target = id;
            hit.MouseLeftButtonUp += (_, e) => { GoTo(target); e.Handled = true; };
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
        if (Math.Abs(Notch.BodyHeight - HeightFor(id)) > 0.5 || changed) AnimateSize(ExpandedW, HeightFor(id), 40);
        BuildDots();
        if (changed && SystemParameters.ClientAreaAnimation)
        {
            double from = direction == 0 ? 10 : direction * 18;
            PageSlide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
    }

    double _wheelAccum;
    DateTime _lastFlip;

    /// <summary>
    /// Scrolling over the notch flips pages, except over things that scroll themselves
    /// (lists, the knob, faders) or sideways card strips, which scroll horizontally.
    /// </summary>
    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_expanded) return;
        for (DependencyObject? d = e.OriginalSource as DependencyObject; d is not null && d != Notch; d = ParentOf(d))
        {
            if (d is Knob || d is Slider) return;
            if (d is ScrollViewer sv)
            {
                if (sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && sv.ScrollableHeight > 0) return;
                if (sv.ScrollableWidth > 0)
                {
                    sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                    e.Handled = true;
                    return;
                }
            }
        }
        e.Handled = true;
        if (_order.Count < 2) return;
        _wheelAccum += e.Delta;
        if (Math.Abs(_wheelAccum) < 120 || (DateTime.Now - _lastFlip).TotalMilliseconds < 280) return;
        int dir = _wheelAccum < 0 ? 1 : -1;
        _wheelAccum = 0;
        _lastFlip = DateTime.Now;
        int i = _order.IndexOf(_current);
        int next = Math.Clamp(i + dir, 0, _order.Count - 1);
        if (next != i) GoTo(_order[next], dir);
    }

    static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    // ---------------------------------------------------------------- expand / collapse

    static Duration AnimTime => SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(280) : TimeSpan.Zero;

    void AnimateSize(double w, double h, double radius)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Notch.BeginAnimation(NotchBorder.BodyWidthProperty, new DoubleAnimation(w, AnimTime) { EasingFunction = ease });
        Notch.BeginAnimation(NotchBorder.BodyHeightProperty, new DoubleAnimation(h, AnimTime) { EasingFunction = ease });
        Notch.BeginAnimation(NotchBorder.BottomRadiusProperty, new DoubleAnimation(radius, AnimTime) { EasingFunction = ease });
    }

    static void Fade(UIElement el, double to, bool collapseAtEnd = false)
    {
        if (to > 0) el.Visibility = Visibility.Visible;
        var a = new DoubleAnimation(to, SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero);
        if (collapseAtEnd) a.Completed += (_, _) => { if (el.Opacity == 0) el.Visibility = Visibility.Collapsed; };
        el.BeginAnimation(OpacityProperty, a);
    }

    void Expand(string? page = null)
    {
        _expanded = true;
        Notch.Cursor = null;
        Fade(Compact, 0, collapseAtEnd: true);
        Fade(Expanded, 1);
        StopEq();
        AnimateSize(ExpandedW, ExpandedH, 40);
        if (page is not null && !_pages.ContainsKey(page)) page = null;
        GoTo(page ?? (_order.Contains(_current) ? _current : _order.FirstOrDefault() ?? "about"));
        Activate();
    }

    /// <summary>Opens (or brings forward) the Settings window and tucks the notch away.</summary>
    public void OpenSettings()
    {
        if (_expanded) Collapse();
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settingsPage);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    void Collapse()
    {
        _expanded = false;
        if (_pages["notes"].Page is NotesPage np) np.Flush();
        Fade(Expanded, 0, collapseAtEnd: true);
        Fade(Compact, 1);
        UpdateCompact(force: true);
    }

    void Notch_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_expanded) Expand();
    }

    void Handle_Click(object sender, MouseButtonEventArgs e)
    {
        Collapse();
        e.Handled = true;
    }

    void Notch_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        if (_tucked) UpdateCompact(force: true);
    }

    void Notch_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;
        if (_tucked) UpdateCompact(force: true);
    }

    // ---------------------------------------------------------------- full-screen tuck

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    void CheckFullScreen()
    {
        bool full = false;
        if (_settings.TuckOverFullScreen && !_expanded)
        {
            var self = new WindowInteropHelper(this).Handle;
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != self && GetWindowRect(fg, out var r))
            {
                var cls = new StringBuilder(64);
                GetClassName(fg, cls, cls.Capacity);
                bool shell = cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
                var mon = MonitorFromWindow(fg, 2);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                full = !shell && mon == MonitorFromWindow(self, 2) && GetMonitorInfo(mon, ref mi)
                       && r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B;
            }
        }
        if (full != _tucked) { _tucked = full; UpdateCompact(force: true); }
    }

    // ---------------------------------------------------------------- compact view

    double _compactW;

    void UpdateCompact(bool force = false)
    {
        if (_expanded) return;

        if (_tucked && !_hovering)
        {
            Compact.Opacity = 0;
            StopEq();
            _compactW = TuckW;
            AnimateSize(TuckW, TuckH, 2);
            return;
        }
        Compact.BeginAnimation(OpacityProperty, null);
        Compact.Opacity = 1;
        Compact.Visibility = Visibility.Visible;
        Notch.Cursor = Cursors.Hand;

        double w;
        var m = _media.State;
        if (IsOn("focus") && _focus.Phase != FocusPhase.Idle)
        {
            ShowGlyph("", _focus.Phase == FocusPhase.Focus ? "Focus" : "Rest");
            CompactText.Text = (_focus.Phase == FocusPhase.Focus ? "Focus" : "Break") + (_focus.IsPaused ? " · paused" : "");
            CompactRight.Text = Fmt.Time(_focus.Remaining);
            CompactRight.Visibility = Visibility.Visible;
            Eq.Visibility = Visibility.Collapsed;
            StopEq();
            w = 200;
        }
        else if (IsOn("media") && m is { IsPlaying: true })
        {
            if (m.Art is not null)
            {
                CompactArt.ImageSource = m.Art;
                CompactArtBox.Visibility = Visibility.Visible;
                CompactGlyph.Visibility = Visibility.Collapsed;
            }
            else ShowGlyph("", "Accent");
            CompactText.Text = string.IsNullOrEmpty(m.Artist) ? m.Title : $"{m.Title} · {m.Artist}";
            CompactRight.Visibility = Visibility.Collapsed;
            Eq.Visibility = Visibility.Visible;
            StartEq();
            w = 300;
        }
        else
        {
            ShowGlyph("", "Sub");
            var now = DateTime.Now;
            CompactText.Text = now.ToString("t");
            // With weather on, the right side shows the temperature instead of the date.
            CompactRight.Text = IsOn("weather") && _weather.Data is { } wd ? $"{Math.Round(wd.Now.Temp)}°" : now.ToString("ddd d");
            CompactRight.Visibility = Visibility.Visible;
            Eq.Visibility = Visibility.Collapsed;
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
        CompactArtBox.Visibility = Visibility.Collapsed;
        CompactGlyph.Visibility = Visibility.Visible;
        CompactGlyph.Text = glyph;
        CompactGlyph.Foreground = (Brush)FindResource(brushKey);
    }

    bool _eqRunning;

    void StartEq()
    {
        if (_eqRunning || !SystemParameters.ClientAreaAnimation) return;
        _eqRunning = true;
        double[] ms = [420, 560, 360];
        var bars = new[] { Eq1, Eq2, Eq3 };
        for (int i = 0; i < 3; i++)
            ((ScaleTransform)bars[i].RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(ms[i]))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    void StopEq()
    {
        if (!_eqRunning) return;
        _eqRunning = false;
        foreach (var b in new[] { Eq1, Eq2, Eq3 })
            ((ScaleTransform)b.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    // ---------------------------------------------------------------- used by the tray icon

    public void OpenFromTray(string? page = null)
    {
        if (page == "settings") OpenSettings();
        else if (_expanded && page is not null) GoTo(page);
        else Expand(page);
    }

    public void FlushNotes()
    {
        if (_pages["notes"].Page is NotesPage np) np.Flush();
    }
}
