using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EasyNotch.Services;

namespace EasyNotch;

public partial class IslandWindow : Window
{
    const double CompactH = 36, ExpandedW = 440, ExpandedH = 340, TuckW = 110, TuckH = 6;

    static readonly Dictionary<string, (string Glyph, string Name)> WidgetInfo = new()
    {
        ["media"] = ("", "Now Playing"),
        ["focus"] = ("", "Focus Timer"),
        ["clipboard"] = ("", "Clipboard"),
        ["notes"] = ("", "Notes"),
        ["clock"] = ("", "Clock"),
        ["mixer"] = ("", "Volume Mixer"),
    };

    readonly Settings _settings;
    readonly MediaService _media;
    readonly FocusTimer _focus;
    readonly ClipboardHistory _clips = new();
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _notesSave = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly ICollectionView _clipView;
    readonly BindingList<CityRow> _cities = [];

    bool _expanded, _tucked, _hovering, _seeking, _loadingNotes;
    string _tab = "media";

    public IslandWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _media = new MediaService(Dispatcher);
        _focus = new FocusTimer(settings);

        _clipView = CollectionViewSource.GetDefaultView(_clips.Items);
        _clipView.Filter = o => o is ClipItem c && (ClipSearch.Text.Length == 0 ||
            (c.Text?.Contains(ClipSearch.Text, StringComparison.CurrentCultureIgnoreCase) ?? false));
        ClipList.ItemsSource = _clipView;
        _clips.Items.CollectionChanged += (_, _) => ClipEmpty.Visibility = _clips.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CityList.ItemsSource = _cities;

        SourceInitialized += (_, _) => OnSourceInitialized();
        Loaded += async (_, _) =>
        {
            Reposition();
            _clips.Attach(this);
            ClipEmpty.Visibility = _clips.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            await _media.StartAsync();
        };
        Deactivated += (_, _) => { if (_expanded) Collapse(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _expanded) { Collapse(); e.Handled = true; } };
        SystemParameters.StaticPropertyChanged += (_, e) => { if (e.PropertyName == nameof(SystemParameters.WorkArea)) Reposition(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(Reposition);

        _media.Changed += () => { UpdateMedia(); UpdateCompact(); };
        _focus.Changed += () => { UpdateFocus(); UpdateCompact(); };
        _focus.PhaseEnded += _ => { if (!_expanded && IsOn("focus")) Expand("focus"); };
        _tick.Tick += (_, _) => OnTick();
        _notesSave.Tick += (_, _) => { _notesSave.Stop(); NotesStore.Save(NotesBox.Text); NotesStatus.Text = "Saved"; };

        BuildNoiseChoices();
        BuildTabs();
        BuildWidgetSettings();
        LoadCities();
        _loadingNotes = true;
        NotesBox.Text = NotesStore.Load();
        _loadingNotes = false;
        StartupToggle.IsChecked = StartWithWindows.IsOn;
        TuckToggle.IsChecked = _settings.TuckOverFullScreen;

        UpdateFocus();
        UpdateMedia();
        UpdateCompact();
        _tick.Start();
    }

    // ---------------------------------------------------------------- window plumbing

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    void OnSourceInitialized()
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
        if (_expanded)
        {
            if (_tab == "clock") UpdateClock();
            if (_tab == "media") UpdateMediaPosition();
            if (_tab == "mixer") RefreshMixer();
        }
        else if (!_focus.IsRunning) UpdateCompact(); // keeps the compact clock current
    }

    // ---------------------------------------------------------------- expand / collapse

    static Duration AnimTime => SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(260) : TimeSpan.Zero;

    void AnimateSize(double w, double h)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, AnimTime) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, AnimTime) { EasingFunction = ease });
    }

    static void Fade(UIElement el, double to, bool collapseAtEnd = false)
    {
        if (to > 0) el.Visibility = Visibility.Visible;
        var a = new DoubleAnimation(to, SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero);
        if (collapseAtEnd) a.Completed += (_, _) => { if (el.Opacity == 0) el.Visibility = Visibility.Collapsed; };
        el.BeginAnimation(OpacityProperty, a);
    }

    void Expand(string? tab = null)
    {
        if (tab is not null) SelectTab(tab);
        _expanded = true;
        Island.CornerRadius = new CornerRadius(26);
        Island.Cursor = null;
        Fade(Compact, 0, collapseAtEnd: true);
        Fade(Expanded, 1);
        Eq.Visibility = Visibility.Collapsed;
        StopEq();
        AnimateSize(ExpandedW, ExpandedH);
        RefreshTab();
        Activate();
    }

    void Collapse()
    {
        _expanded = false;
        CitySuggestBox.Visibility = Visibility.Collapsed;
        Island.CornerRadius = new CornerRadius(18);
        Fade(Expanded, 0, collapseAtEnd: true);
        Fade(Compact, 1);
        UpdateCompact(force: true);
    }

    void Island_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_expanded) Expand();
    }

    void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        if (_tucked) UpdateCompact(force: true);
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
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
            AnimateSize(TuckW, TuckH);
            Island.CornerRadius = new CornerRadius(3);
            return;
        }
        Compact.BeginAnimation(OpacityProperty, null);
        Compact.Opacity = 1;
        Compact.Visibility = Visibility.Visible;
        Island.CornerRadius = new CornerRadius(18);
        Island.Cursor = Cursors.Hand;

        double w;
        var m = _media.State;
        if (IsOn("focus") && _focus.Phase != FocusPhase.Idle)
        {
            ShowGlyph("", _focus.Phase == FocusPhase.Focus ? "Focus" : "Rest");
            CompactText.Text = (_focus.Phase == FocusPhase.Focus ? "Focus" : "Break") + (_focus.IsPaused ? " · paused" : "");
            CompactRight.Text = Fmt(_focus.Remaining);
            CompactRight.Visibility = Visibility.Visible;
            Eq.Visibility = Visibility.Collapsed;
            StopEq();
            w = 210;
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
            w = 320;
        }
        else
        {
            ShowGlyph("", "Sub");
            var now = DateTime.Now;
            CompactText.Text = now.ToString("t");
            CompactRight.Text = now.ToString("ddd d");
            CompactRight.Visibility = Visibility.Visible;
            Eq.Visibility = Visibility.Collapsed;
            StopEq();
            w = 170;
        }

        if (force || w != _compactW || Island.Height != CompactH)
        {
            _compactW = w;
            AnimateSize(w, CompactH);
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

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    // ---------------------------------------------------------------- tabs

    bool IsOn(string id) => _settings.Widgets.Any(w => w.Id == id && w.On);

    void BuildTabs()
    {
        Tabs.Children.Clear();
        foreach (var w in _settings.Widgets.Where(w => w.On))
        {
            var (glyph, name) = WidgetInfo[w.Id];
            var rb = new RadioButton
            {
                Style = (Style)FindResource("TabBtn"), GroupName = "tabs", Content = glyph, ToolTip = name, Tag = w.Id,
            };
            rb.Checked += Tab_Checked;
            Tabs.Children.Add(rb);
        }
        SelectTab(_tab == "settings" || IsOn(_tab) ? _tab : _settings.Widgets.FirstOrDefault(w => w.On)?.Id ?? "settings");
    }

    void SelectTab(string id)
    {
        var rb = id == "settings" ? TabSettings : Tabs.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.Tag == id);
        if (rb is null) return;
        if (rb.IsChecked == true) ShowPanel(id);
        else rb.IsChecked = true;
    }

    void Tab_Checked(object sender, RoutedEventArgs e) => ShowPanel((string)((FrameworkElement)sender).Tag);

    void ShowPanel(string id)
    {
        _tab = id;
        PanelMedia.Visibility = id == "media" ? Visibility.Visible : Visibility.Collapsed;
        PanelFocus.Visibility = id == "focus" ? Visibility.Visible : Visibility.Collapsed;
        PanelClipboard.Visibility = id == "clipboard" ? Visibility.Visible : Visibility.Collapsed;
        PanelNotes.Visibility = id == "notes" ? Visibility.Visible : Visibility.Collapsed;
        PanelClock.Visibility = id == "clock" ? Visibility.Visible : Visibility.Collapsed;
        PanelMixer.Visibility = id == "mixer" ? Visibility.Visible : Visibility.Collapsed;
        PanelSettings.Visibility = id == "settings" ? Visibility.Visible : Visibility.Collapsed;
        RefreshTab();
    }

    void RefreshTab()
    {
        switch (_tab)
        {
            case "media": UpdateMedia(); break;
            case "focus": UpdateFocus(); break;
            case "clock": UpdateClock(); break;
            case "mixer": RefreshMixer(); break;
            case "notes" when _expanded:
                Dispatcher.InvokeAsync(() => { NotesBox.Focus(); NotesBox.CaretIndex = NotesBox.Text.Length; }, DispatcherPriority.Input);
                break;
            case "clipboard" when _expanded:
                Dispatcher.InvokeAsync(() => ClipSearch.Focus(), DispatcherPriority.Input);
                break;
        }
    }

    // ---------------------------------------------------------------- now playing

    void UpdateMedia()
    {
        var m = _media.State;
        bool has = m is not null && (m.Title.Length > 0 || m.Artist.Length > 0);
        MediaEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        MediaBody.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has) return;
        MediaTitle.Text = m!.Title;
        MediaArtist.Text = m.Artist;
        MediaApp.Text = $"Playing in {m.App}";
        MediaArt.ImageSource = m.Art;
        PlayPause.Content = m.IsPlaying ? "" : "";
        PlayPause.ToolTip = m.IsPlaying ? "Pause" : "Play";
        MediaSeek.IsEnabled = m.CanSeek;
        MediaSeek.Maximum = Math.Max(1, m.Duration.TotalSeconds);
        MediaDur.Text = m.Duration > TimeSpan.Zero ? Fmt(m.Duration) : "";
        UpdateMediaPosition();
    }

    void UpdateMediaPosition()
    {
        if (_media.State is not { } m) return;
        var p = _media.LivePosition;
        MediaPos.Text = m.Duration > TimeSpan.Zero ? Fmt(p) : "";
        if (!_seeking) MediaSeek.Value = p.TotalSeconds;
    }

    void Seek_Down(object sender, MouseButtonEventArgs e) => _seeking = true;

    void Seek_Up(object sender, MouseButtonEventArgs e)
    {
        // Let the slider finish moving the thumb first.
        Dispatcher.InvokeAsync(() =>
        {
            _media.Seek(TimeSpan.FromSeconds(MediaSeek.Value));
            _seeking = false;
        }, DispatcherPriority.Background);
    }

    void PlayPause_Click(object sender, RoutedEventArgs e) => _media.PlayPause();
    void Prev_Click(object sender, RoutedEventArgs e) => _media.Previous();
    void Next_Click(object sender, RoutedEventArgs e) => _media.Next();

    // ---------------------------------------------------------------- focus

    void BuildNoiseChoices()
    {
        foreach (var kind in Noise.Kinds)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("SegBtn"), GroupName = "noise",
                Content = char.ToUpper(kind[0]) + kind[1..], IsChecked = _settings.Noise == kind,
                ToolTip = kind == "off" ? "Silence" : $"{char.ToUpper(kind[0]) + kind[1..]} noise",
            };
            rb.Checked += (_, _) => { _settings.Noise = kind; _settings.Save(); _focus.ApplyNoiseSetting(); };
            NoiseChoices.Children.Add(rb);
        }
    }

    void UpdateFocus()
    {
        var phase = _focus.Phase;
        FocusClock.Text = Fmt(_focus.Remaining);
        FocusPhaseText.Text = phase switch
        {
            FocusPhase.Focus => _focus.IsPaused ? "Focus · paused" : "Focusing",
            FocusPhase.Break => _focus.IsPaused ? "Break · paused" : "On a break",
            _ => _focus.CompletedFocusSessions > 0 ? $"{_focus.CompletedFocusSessions} session{(_focus.CompletedFocusSessions == 1 ? "" : "s")} done today" : "Ready when you are",
        };
        FocusBar.Foreground = (Brush)FindResource(phase == FocusPhase.Break ? "Rest" : "Focus");
        FocusBar.Value = phase == FocusPhase.Idle || _focus.Total <= TimeSpan.Zero ? 0
            : 1 - _focus.Remaining.TotalSeconds / _focus.Total.TotalSeconds;
        FocusStart.Content = phase == FocusPhase.Idle ? "Start focus" : _focus.IsPaused ? "Resume" : "Pause";
        FocusStop.IsEnabled = phase != FocusPhase.Idle;
        FocusBreak.IsEnabled = phase != FocusPhase.Break;
        FocusMinutes.Text = $"{_settings.FocusMinutes} min";
        BreakMinutes.Text = $"{_settings.BreakMinutes} min";
    }

    void FocusStart_Click(object sender, RoutedEventArgs e) => _focus.TogglePause();
    void FocusBreak_Click(object sender, RoutedEventArgs e) => _focus.StartBreak();
    void FocusStop_Click(object sender, RoutedEventArgs e) => _focus.Stop();

    void FocusMinus_Click(object sender, RoutedEventArgs e) => AdjustMinutes(focus: true, -5);
    void FocusPlus_Click(object sender, RoutedEventArgs e) => AdjustMinutes(focus: true, +5);
    void BreakMinus_Click(object sender, RoutedEventArgs e) => AdjustMinutes(focus: false, -1);
    void BreakPlus_Click(object sender, RoutedEventArgs e) => AdjustMinutes(focus: false, +1);

    void AdjustMinutes(bool focus, int delta)
    {
        if (focus) _settings.FocusMinutes = Math.Clamp(_settings.FocusMinutes + delta, 5, 180);
        else _settings.BreakMinutes = Math.Clamp(_settings.BreakMinutes + delta, 1, 60);
        _settings.Save();
        UpdateFocus();
        UpdateCompact();
    }

    // ---------------------------------------------------------------- clipboard

    void ClipSearch_Changed(object sender, TextChangedEventArgs e) => _clipView.Refresh();

    void ClipList_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src || FindAncestor<ButtonBase>(src) is not null) return;
        if ((src as FrameworkElement ?? FindAncestor<FrameworkElement>(src))?.DataContext is not ClipItem item) return;
        _clips.CopyBack(item);
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1300) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300))));
        ClipToast.BeginAnimation(OpacityProperty, a);
    }

    void ClipPin_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ClipItem i) _clips.TogglePin(i);
    }

    void ClipRemove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ClipItem i) _clips.Remove(i);
    }

    void ClipClear_Click(object sender, RoutedEventArgs e) => _clips.ClearUnpinned();

    static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    // ---------------------------------------------------------------- notes

    void Notes_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingNotes) return;
        NotesStatus.Text = "Saving…";
        _notesSave.Stop();
        _notesSave.Start();
    }

    // ---------------------------------------------------------------- clock

    sealed class CityRow : INotifyPropertyChanged
    {
        public required TimeZoneInfo Zone { get; init; }
        public string Name => ShortName(Zone);
        public string Time { get; private set; } = "";
        public string Offset { get; private set; } = "";
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Update(DateTime utcNow)
        {
            var local = DateTime.Now;
            var there = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Zone);
            double hrs = Math.Round((there - local).TotalHours * 2) / 2;
            string day = there.Date > local.Date ? "Tomorrow" : there.Date < local.Date ? "Yesterday" : "Today";
            string diff = hrs == 0 ? "same time" : $"{(hrs > 0 ? "+" : "")}{hrs:0.#} hr{(Math.Abs(hrs) == 1 ? "" : "s")}";
            Time = there.ToString("t");
            Offset = $"{day}, {diff}";
            PropertyChanged?.Invoke(this, new(nameof(Time)));
            PropertyChanged?.Invoke(this, new(nameof(Offset)));
        }
    }

    /// <summary>"(UTC+09:00) Osaka, Sapporo, Tokyo" → "Osaka, Sapporo, Tokyo".</summary>
    static string ShortName(TimeZoneInfo z)
    {
        string n = z.DisplayName;
        int close = n.IndexOf(") ", StringComparison.Ordinal);
        return n.StartsWith("(UTC") && close > 0 ? n[(close + 2)..] : n;
    }

    void LoadCities()
    {
        _cities.Clear();
        foreach (var id in _settings.Cities)
            try { _cities.Add(new CityRow { Zone = TimeZoneInfo.FindSystemTimeZoneById(id) }); } catch { }
        UpdateClock();
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        ClockBig.Text = now.ToString("T");
        ClockDate.Text = now.ToString("D");
        var utc = DateTime.UtcNow;
        foreach (var c in _cities) c.Update(utc);
    }

    void CitySearch_Changed(object sender, TextChangedEventArgs e)
    {
        string q = CitySearch.Text.Trim();
        if (q.Length == 0) { CitySuggestBox.Visibility = Visibility.Collapsed; return; }
        var hits = TimeZoneInfo.GetSystemTimeZones()
            .Where(z => z.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase) || z.Id.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .Where(z => !_settings.Cities.Contains(z.Id))
            .Take(6).ToList();
        CitySuggest.ItemsSource = hits;
        CitySuggestBox.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void CitySuggest_Click(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not TimeZoneInfo z) return;
        _settings.Cities.Add(z.Id);
        _settings.Save();
        CitySearch.Text = "";
        CitySuggestBox.Visibility = Visibility.Collapsed;
        LoadCities();
    }

    void CityRemove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not CityRow row) return;
        _settings.Cities.Remove(row.Zone.Id);
        _settings.Save();
        LoadCities();
    }

    // ---------------------------------------------------------------- volume mixer

    sealed class MixerRow
    {
        public required VolumeChannel Channel;
        public required Slider Slider;
        public required TextBlock Percent;
        public required Button Mute;
        public required FrameworkElement Body;
        public bool Updating;
    }

    readonly List<MixerRow> _mixerRows = [];

    /// <summary>Re-reads the devices and app sessions; rebuilds rows only when the set of apps changed.</summary>
    void RefreshMixer()
    {
        var channels = new List<VolumeChannel>();
        if (AudioMixer.Output() is { } o) channels.Add(o);
        if (AudioMixer.Microphone() is { } m) channels.Add(m);
        channels.AddRange(AudioMixer.Apps());

        if (!channels.Select(c => c.Key).SequenceEqual(_mixerRows.Select(r => r.Channel.Key)))
        {
            BuildMixerRows(channels);
            return;
        }
        for (int i = 0; i < channels.Count; i++)
        {
            var row = _mixerRows[i];
            row.Channel = channels[i];
            if (!row.Slider.IsMouseCaptureWithin) ShowRowState(row);
        }
    }

    void BuildMixerRows(List<VolumeChannel> channels)
    {
        MixerRows.Children.Clear();
        _mixerRows.Clear();

        if (channels.Count == 0)
        {
            MixerRows.Children.Add(new TextBlock
            {
                Text = "No audio devices found.", Foreground = (Brush)FindResource("Sub"), FontSize = 13,
                Margin = new Thickness(0, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        bool appsHeader = false;
        MixerRows.Children.Add(Header("Devices", top: 0));
        foreach (var ch in channels)
        {
            if (!ch.IsDevice && !appsHeader) { MixerRows.Children.Add(Header("Apps", top: 10)); appsHeader = true; }
            var row = MakeRow(ch);
            _mixerRows.Add(row);
            MixerRows.Children.Add(row.Body);
            ShowRowState(row);
        }
        if (!appsHeader)
        {
            MixerRows.Children.Add(Header("Apps", top: 10));
            MixerRows.Children.Add(new TextBlock
            {
                Text = "Apps appear here while they're playing sound.", FontSize = 12,
                Foreground = (Brush)FindResource("Faint"), Margin = new Thickness(0, 2, 0, 0),
            });
        }
    }

    TextBlock Header(string text, double top) => new()
    {
        Text = text, FontSize = 12, Foreground = (Brush)FindResource("Sub"), Margin = new Thickness(0, top, 0, 4),
    };

    MixerRow MakeRow(VolumeChannel ch)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var mute = new Button { Style = (Style)FindResource("IconBtn"), Width = 36, Height = 36, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        if (ch.Icon is not null)
            mute.Content = new Image { Source = ch.Icon, Width = 20, Height = 20 };

        var name = new TextBlock { Text = ch.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 0, 0) };
        var slider = new Slider { Style = (Style)FindResource("Seek"), Minimum = 0, Maximum = 100, Margin = new Thickness(6, 2, 4, 0) };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(name);
        stack.Children.Add(slider);
        Grid.SetColumn(stack, 1);

        var pct = new TextBlock
        {
            FontSize = 12, Foreground = (Brush)FindResource("Sub"), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(pct, FontNumeralAlignment.Tabular);
        Grid.SetColumn(pct, 2);

        grid.Children.Add(mute);
        grid.Children.Add(stack);
        grid.Children.Add(pct);

        var row = new MixerRow { Channel = ch, Slider = slider, Percent = pct, Mute = mute, Body = grid };
        slider.ValueChanged += (_, e) =>
        {
            if (row.Updating) return;
            // Unmute first: unmuting may restore an old level, which the new slider value then replaces.
            if (e.NewValue > 0 && row.Channel.Muted) row.Channel.Muted = false;
            row.Channel.Volume = (float)(e.NewValue / 100);
            ShowRowState(row, keepSlider: true);
        };
        // Scrolling over a row nudges its volume, like the Windows tray flyout.
        grid.PreviewMouseWheel += (_, e) => { slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? 2 : -2), 0, 100); e.Handled = true; };
        mute.Click += (_, _) => { row.Channel.Muted = !row.Channel.Muted; ShowRowState(row); };
        return row;
    }

    void ShowRowState(MixerRow row, bool keepSlider = false)
    {
        var ch = row.Channel;
        int v = (int)Math.Round(ch.Volume * 100);
        bool muted = ch.Muted;
        if (!keepSlider)
        {
            row.Updating = true;
            row.Slider.Value = v;
            row.Updating = false;
        }
        row.Percent.Text = muted ? "Muted" : $"{v}%";
        row.Slider.Opacity = muted ? 0.4 : 1;
        row.Mute.ToolTip = muted ? "Unmute" : "Mute";
        if (ch.Icon is null)
            row.Mute.Content = ch.IsMic ? (muted ? "" : "")
                : muted ? "" : v == 0 ? "" : v < 34 ? "" : v < 67 ? "" : "";
        else
            ((UIElement)row.Mute.Content).Opacity = muted ? 0.35 : 1;
    }

    // ---------------------------------------------------------------- settings

    void BuildWidgetSettings()
    {
        WidgetSettings.Children.Clear();
        for (int i = 0; i < _settings.Widgets.Count; i++)
        {
            var w = _settings.Widgets[i];
            int index = i;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var down = new Button { Style = (Style)FindResource("IconBtn"), Width = 26, Height = 26, FontSize = 10, Content = "", ToolTip = "Move down", IsEnabled = i < _settings.Widgets.Count - 1 };
            var up = new Button { Style = (Style)FindResource("IconBtn"), Width = 26, Height = 26, FontSize = 10, Content = "", ToolTip = "Move up", IsEnabled = i > 0 };
            DockPanel.SetDock(down, Dock.Right);
            DockPanel.SetDock(up, Dock.Right);
            up.Click += (_, _) => MoveWidget(index, -1);
            down.Click += (_, _) => MoveWidget(index, +1);
            var check = new CheckBox { Content = WidgetInfo[w.Id].Name, IsChecked = w.On, VerticalAlignment = VerticalAlignment.Center };
            check.Click += (_, _) => { w.On = check.IsChecked == true; _settings.Save(); BuildTabs(); UpdateCompact(); };
            row.Children.Add(down);
            row.Children.Add(up);
            row.Children.Add(check);
            WidgetSettings.Children.Add(row);
        }
    }

    void MoveWidget(int i, int delta)
    {
        int j = i + delta;
        if (j < 0 || j >= _settings.Widgets.Count) return;
        (_settings.Widgets[i], _settings.Widgets[j]) = (_settings.Widgets[j], _settings.Widgets[i]);
        _settings.Save();
        BuildWidgetSettings();
        BuildTabs();
        TabSettings.IsChecked = true;
    }

    void Startup_Click(object sender, RoutedEventArgs e)
    {
        try { StartWithWindows.Set(StartupToggle.IsChecked == true); }
        catch { StartupToggle.IsChecked = StartWithWindows.IsOn; }
    }

    void Tuck_Click(object sender, RoutedEventArgs e)
    {
        _settings.TuckOverFullScreen = TuckToggle.IsChecked == true;
        _settings.Save();
    }

    void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(Settings.Dir);
        Process.Start(new ProcessStartInfo(Settings.Dir) { UseShellExecute = true });
    }

    void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ---------------------------------------------------------------- used by the tray icon

    public void OpenFromTray(string? tab = null) => Expand(tab);

    public void FlushNotes()
    {
        if (_notesSave.IsEnabled) { _notesSave.Stop(); NotesStore.Save(NotesBox.Text); }
    }
}
