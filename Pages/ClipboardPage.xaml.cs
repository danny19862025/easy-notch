using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class ClipboardPage : UserControl, IPage
{
    const double CardW = 160, CardH = 112;

    readonly PageContext _ctx;

    public ClipboardPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _ctx.Clips.Paused = ctx.Settings.ClipboardPaused;
        _ctx.Clips.Items.CollectionChanged += (_, _) => { if (IsVisible) Build(); };
    }

    public void OnShown() => Build();
    public void OnTick() { }

    void Build()
    {
        bool paused = _ctx.Clips.Paused;
        PauseBtn.Content = paused ? "" : "";
        PauseBtn.ToolTip = paused ? "Resume saving what you copy" : "Pause saving what you copy";
        PauseBtn.Foreground = (Brush)FindResource(paused ? "Focus" : "Fg");

        Cards.Children.Clear();
        string q = Search.Text.Trim();
        var items = _ctx.Clips.Items.Where(i => q.Length == 0 || i.SearchText.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        foreach (var item in items) Cards.Children.Add(MakeCard(item));

        Empty.Text = _ctx.Clips.Items.Count == 0
            ? (paused ? "Paused. Press ▶ to start saving what you copy." : "Copy text, a link, an image or files and it'll be kept here.")
            : "Nothing matches.";
        Empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Scroller.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    Button MakeCard(ClipItem item)
    {
        var card = new Button { Style = (Style)FindResource("Card"), Width = CardW, Height = CardH, Margin = new Thickness(0, 0, 8, 8) };
        var root = new Grid();

        switch (item.Kind)
        {
            case "image":
                card.Padding = new Thickness(0);
                root.Children.Add(new Image { Source = item.Thumb, Stretch = Stretch.UniformToFill });
                var shade = new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom, Height = 34, Padding = new Thickness(10, 0, 10, 6),
                    Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(200, 0, 0, 0), 90),
                    Child = new TextBlock { Text = item.When, FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brushes.White },
                };
                root.Children.Add(shade);
                break;

            case "link":
            case "files":
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = item.Title, FontSize = 14, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, item.Pinned ? 24 : 0, 0) });
                if (item.Preview.Length > 0)
                    sp.Children.Add(new TextBlock { Text = item.Preview, FontSize = 12, Foreground = (Brush)FindResource("Sub"), TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
                root.Children.Add(sp);
                var meta = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
                meta.Children.Add(new TextBlock { Text = item.Kind == "link" ? "" : "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Foreground = (Brush)FindResource("Sub"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                meta.Children.Add(new TextBlock { Text = item.When, FontSize = 12, Foreground = (Brush)FindResource("Sub") });
                root.Children.Add(meta);
                break;

            default:
                root.Children.Add(new TextBlock
                {
                    Text = item.Preview, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, item.Pinned ? 22 : 0, 18),
                });
                root.Children.Add(new TextBlock { Text = item.When, FontSize = 12, Foreground = (Brush)FindResource("Sub"), VerticalAlignment = VerticalAlignment.Bottom });
                break;
        }

        if (item.Pinned)
            root.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = (Brush)FindResource("IslandBg"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = item.Kind == "image" ? new Thickness(0, 6, 6, 0) : new Thickness(0, -4, -4, 0),
                Child = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });

        card.Content = root;
        card.ToolTip = item.Kind == "text" && item.Text!.Length > 120 ? item.Text[..Math.Min(item.Text.Length, 600)] : null;
        card.Click += (_, _) => { _ctx.Clips.CopyBack(item); ShowToast(); };
        card.ContextMenu = Menu(item);
        return card;
    }

    ContextMenu Menu(ClipItem item)
    {
        var m = new ContextMenu();
        void Add(string header, Action a) { var mi = new MenuItem { Header = header }; mi.Click += (_, _) => a(); m.Items.Add(mi); }

        Add("Copy", () => { _ctx.Clips.CopyBack(item); ShowToast(); });
        if (item.Kind == "link") Add("Open link", () => Shell(item.Text!.Trim()));
        if (item.Kind == "files" && item.Files!.FirstOrDefault(File.Exists) is { } f)
            Add("Show in folder", () => Process.Start("explorer.exe", $"/select,\"{f}\""));
        Add(item.Pinned ? "Unpin" : "Pin", () => _ctx.Clips.TogglePin(item));
        Add("Delete", () => _ctx.Clips.Remove(item));
        return m;
    }

    static void Shell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    void ShowToast()
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1300) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300))));
        Toast.BeginAnimation(OpacityProperty, a);
    }

    void Search_Changed(object sender, TextChangedEventArgs e) => Build();

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        _ctx.Clips.Paused = !_ctx.Clips.Paused;
        _ctx.Settings.ClipboardPaused = _ctx.Clips.Paused;
        _ctx.Settings.Save();
        Build();
    }

    void Clear_Click(object sender, RoutedEventArgs e) => _ctx.Clips.ClearUnpinned();
    void Settings_Click(object sender, RoutedEventArgs e) => _ctx.GoTo("settings");
}
