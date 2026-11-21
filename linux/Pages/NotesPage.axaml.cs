using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using EasyNotch.Services;

namespace EasyNotch.Pages;

public partial class NotesPage : UserControl, IPage
{
    const double CardW = 168, CardH = 118;

    readonly PageContext _ctx;
    readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(500) };
    Note? _editing;
    bool _loading;

    public NotesPage(PageContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _save.Tick += (_, _) => { _save.Stop(); SaveEditor(); };
    }

    public void OnShown()
    {
        if (_editing is null) BuildCards();
    }

    public void OnTick() { }

    /// <summary>Saves any pending edit; called when the notch closes.</summary>
    public void Flush()
    {
        if (_save.IsEnabled) { _save.Stop(); SaveEditor(); }
    }

    // ------------------------------------------------------------ list

    void BuildCards()
    {
        Cards.Children.Clear();
        string q = (Search.Text ?? "").Trim();
        var notes = _ctx.Notes.Items.Where(n => q.Length == 0
            || n.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || n.Body.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        foreach (var n in notes) Cards.Children.Add(MakeCard(n));
        Empty.IsVisible = _ctx.Notes.Items.Count == 0;
        Scroller.IsVisible = notes.Count > 0;
    }

    Button MakeCard(Note n)
    {
        var card = new Button { Width = CardW, Height = CardH, Margin = new Thickness(0, 0, 10, 0) };
        card.Classes.Add("card");
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        root.Children.Add(new TextBlock { Text = n.DisplayTitle, FontSize = 15, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, n.Pinned ? 26 : 0, 0) });

        // Body preview: up to three lines, checklist lines as tickable boxes.
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetRow(body, 1);
        var lines = n.Lines.Select((l, i) => (l, i)).Where(x => x.l.Trim().Length > 0).ToList();
        if (n.Title.Length == 0 && lines.Count > 0) lines.RemoveAt(0); // first line is already the title
        foreach (var (line, index) in lines.Take(3))
        {
            if (Note.IsCheckLine(line))
            {
                bool on = Note.IsChecked(line);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
                row.Children.Add(new TextBlock { Text = on ? Glyph.Checked : Glyph.Unchecked, FontFamily = Res.Icons, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Foreground = Res.Brush(on ? "Sub" : "Fg") });
                row.Children.Add(new TextBlock { Text = Note.CheckText(line), FontSize = 13, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = Res.Brush(on ? "Sub" : "Fg") });
                int li = index;
                row.PointerPressed += (_, e) => e.Handled = true; // don't open the editor
                row.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton != MouseButton.Left) return;
                    _ctx.Notes.ToggleCheck(n, li);
                    BuildCards();
                    e.Handled = true;
                };
                body.Children.Add(row);
            }
            else
                body.Children.Add(new TextBlock { Text = line.Trim(), FontSize = 13, Foreground = Res.Brush("Sub"), TextTrimming = TextTrimming.CharacterEllipsis });
        }
        root.Children.Add(body);

        var foot = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetRow(foot, 2);
        if (n.Progress is double p)
        {
            var track = new Grid { Width = 40, Height = 4, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { Background = Res.Brush("Line"), CornerRadius = new CornerRadius(2) });
            track.Children.Add(new Border { Background = Res.Brush("Fg"), CornerRadius = new CornerRadius(2), Width = 40 * p, HorizontalAlignment = HorizontalAlignment.Left });
            foot.Children.Add(track);
        }
        foot.Children.Add(new TextBlock { Text = TimeAgo.Of(n.Updated), FontSize = 12, Foreground = Res.Brush("Sub") });
        root.Children.Add(foot);

        if (n.Pinned)
            root.Children.Add(PinBadge(new Thickness(0, -4, -4, 0)));

        card.Content = root;
        card.Click += (_, _) => Open(n);
        card.ContextMenu = Menu(n);
        return card;
    }

    internal static Border PinBadge(Thickness margin) => new()
    {
        Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Res.Brush("IslandBg"),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = margin,
        Child = new TextBlock { Text = Glyph.Pin, FontFamily = Res.Icons, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Res.Brush("Fg") },
    };

    ContextMenu Menu(Note n)
    {
        var m = new ContextMenu();
        var pin = new MenuItem { Header = n.Pinned ? "Unpin" : "Pin" };
        pin.Click += (_, _) => { _ctx.Notes.TogglePin(n); BuildCards(); };
        var del = new MenuItem { Header = "Delete" };
        del.Click += (_, _) => { _ctx.Notes.Remove(n); BuildCards(); };
        m.Items.Add(pin);
        m.Items.Add(del);
        return m;
    }

    void Search_Changed(object? sender, TextChangedEventArgs e) => BuildCards();

    void New_Click(object? sender, RoutedEventArgs e) => Open(_ctx.Notes.Add());

    // ------------------------------------------------------------ editor

    void Open(Note n)
    {
        _editing = n;
        _loading = true;
        TitleBox.Text = n.Title;
        BodyBox.Text = n.Body;
        _loading = false;
        UpdatePin();
        ListPanel.IsVisible = false;
        Editor.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            var target = n.Title.Length == 0 && n.Body.Length == 0 ? TitleBox : BodyBox;
            target.Focus();
            target.CaretIndex = target.Text?.Length ?? 0;
        }, DispatcherPriority.Input);
    }

    void Close()
    {
        Flush();
        if (_editing is not null) _ctx.Notes.Prune(_editing);
        _editing = null;
        Editor.IsVisible = false;
        ListPanel.IsVisible = true;
        BuildCards();
    }

    void UpdatePin()
    {
        if (_editing is null) return;
        ToolTip.SetTip(PinBtn, _editing.Pinned ? "Unpin" : "Pin");
        PinBtn.Foreground = Res.Brush(_editing.Pinned ? "Accent" : "Fg");
    }

    void Editor_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _save.Stop();
        _save.Start();
    }

    void SaveEditor()
    {
        if (_editing is not null) _ctx.Notes.Update(_editing, (TitleBox.Text ?? "").Trim(), BodyBox.Text ?? "");
    }

    void Check_Click(object? sender, RoutedEventArgs e)
    {
        string text = BodyBox.Text ?? "";
        string prefix = text.Length == 0 || text.EndsWith('\n') ? "" : "\n";
        BodyBox.Text = text + prefix + "[ ] ";
        BodyBox.Focus();
        BodyBox.CaretIndex = BodyBox.Text.Length;
    }

    void Back_Click(object? sender, RoutedEventArgs e) => Close();

    void Pin_Click(object? sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        _ctx.Notes.TogglePin(_editing);
        UpdatePin();
    }

    void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        _save.Stop();
        _ctx.Notes.Remove(_editing);
        _editing = null;
        Editor.IsVisible = false;
        ListPanel.IsVisible = true;
        BuildCards();
    }

    /// <summary>Esc inside the editor goes back to the list instead of closing the notch.</summary>
    public bool HandleEscape()
    {
        if (_editing is null) return false;
        Close();
        return true;
    }
}
