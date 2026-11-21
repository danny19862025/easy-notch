using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Layout;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class ClipboardPage : UserControl, IPage
{
    const double CardW = 160, CardH = 112;

    readonly PageContext _ctx;
    int _toast;

    public ClipboardPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _ctx.Clips.Paused = ctx.Settings.ClipboardPaused;
        _ctx.Clips.Items.CollectionChanged += (_, _) => { if (IsVisible && Parent is not null) Build(); };
    }

    public void OnShown() => Build();
    public void OnTick() { }

    void Build()
    {
        bool paused = _ctx.Clips.Paused;
        PauseBtn.Content = paused ? Glyph.Play : Glyph.Pause;
        ToolTip.SetTip(PauseBtn, paused ? "Resume saving what you copy" : "Pause saving what you copy");
        PauseBtn.Foreground = Res.Brush(paused ? "Focus" : "Fg");

        Cards.Children.Clear();
        string q = (Search.Text ?? "").Trim();
        var items = _ctx.Clips.Items.Where(i => q.Length == 0 || i.SearchText.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        foreach (var item in items) Cards.Children.Add(MakeCard(item));

        Empty.Text = _ctx.Clips.Items.Count == 0
            ? (paused ? "Paused. Press ▶ to start saving what you copy." : "Copy text, a link, an image or files and it'll be kept here.")
            : "Nothing matches.";
        Empty.IsVisible = items.Count == 0;
        Scroller.IsVisible = items.Count > 0;
    }

    Button MakeCard(ClipItem item)
    {
        var card = new Button { Width = CardW, Height = CardH, Margin = new Thickness(0, 0, 8, 8) };
        card.Classes.Add("card");
        var root = new Grid();

        switch (item.Kind)
        {
            case "image":
                card.Padding = new Thickness(0);
                root.Children.Add(new Image { Source = item.Thumb, Stretch = Stretch.UniformToFill });
                root.Children.Add(new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom, Height = 34, Padding = new Thickness(10, 0, 10, 6),
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(200, 0, 0, 0), 1) },
                    },
                    Child = new TextBlock { Text = item.When, FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brushes.White },
                });
                break;

            case "link":
            case "files":
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = item.Title, FontSize = 14, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, item.Pinned ? 24 : 0, 0) });
                if (item.Preview.Length > 0)
                    sp.Children.Add(new TextBlock { Text = item.Preview, FontSize = 12, Foreground = Res.Brush("Sub"), TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
                root.Children.Add(sp);
                var meta = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
                meta.Children.Add(new TextBlock { Text = item.Kind == "link" ? Glyph.Link : Glyph.File, FontFamily = Res.Icons, FontSize = 15, Foreground = Res.Brush("Sub"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                meta.Children.Add(new TextBlock { Text = item.When, FontSize = 12, Foreground = Res.Brush("Sub"), VerticalAlignment = VerticalAlignment.Center });
                root.Children.Add(meta);
                break;

            default:
                root.Children.Add(new TextBlock
                {
                    Text = item.Preview, FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, item.Pinned ? 22 : 0, 18),
                });
                root.Children.Add(new TextBlock { Text = item.When, FontSize = 12, Foreground = Res.Brush("Sub"), VerticalAlignment = VerticalAlignment.Bottom });
                break;
        }

        if (item.Pinned)
            root.Children.Add(NotesPage.PinBadge(item.Kind == "image" ? new Thickness(0, 6, 6, 0) : new Thickness(0, -4, -4, 0)));

        card.Content = root;
        if (item.Kind == "text" && item.Text!.Length > 120) ToolTip.SetTip(card, item.Text[..Math.Min(item.Text.Length, 600)]);
        card.Click += (_, _) => { _ctx.Clips.CopyBack(item); ShowToast(); };
        card.ContextMenu = Menu(item);
        return card;
    }

    ContextMenu Menu(ClipItem item)
    {
        var m = new ContextMenu();
        void Add(string header, Action a) { var mi = new MenuItem { Header = header }; mi.Click += (_, _) => a(); m.Items.Add(mi); }

        Add("Copy", () => { _ctx.Clips.CopyBack(item); ShowToast(); });
        if (item.Kind == "link") Add("Open link", () => LinuxDesktop.Open(item.Text!.Trim()));
        if (item.Kind == "files" && item.Files!.FirstOrDefault(File.Exists) is { } f)
            Add("Show in folder", () => LinuxDesktop.ShowInFolder(f));
        Add(item.Pinned ? "Unpin" : "Pin", () => _ctx.Clips.TogglePin(item));
        Add("Delete", () => _ctx.Clips.Remove(item));
        return m;
    }

    void ShowToast()
    {
        int version = ++_toast;
        Anim.To(Toast, "opacity", Toast.Opacity, 1, 120, v => Toast.Opacity = v, () =>
            DispatcherTimer.RunOnce(() =>
            {
                if (version == _toast) Anim.To(Toast, "opacity", 1, 0, 300, v => Toast.Opacity = v);
            }, TimeSpan.FromMilliseconds(880)));
    }

    void Search_Changed(object? sender, TextChangedEventArgs e) => Build();

    void Pause_Click(object? sender, RoutedEventArgs e)
    {
        _ctx.Clips.Paused = !_ctx.Clips.Paused;
        _ctx.Settings.ClipboardPaused = _ctx.Clips.Paused;
        _ctx.Settings.Save();
        Build();
    }

    void Clear_Click(object? sender, RoutedEventArgs e) => _ctx.Clips.ClearUnpinned();
    void Settings_Click(object? sender, RoutedEventArgs e) => _ctx.GoTo("settings");
}
