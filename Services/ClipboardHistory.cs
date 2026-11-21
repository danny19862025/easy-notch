using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace EasyNotch.Services;

public sealed class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Text { get; set; }
    /// <summary>File name of a PNG under the clips folder, for copied images.</summary>
    public string? Image { get; set; }
    /// <summary>Paths, for copied files (Explorer copy).</summary>
    public List<string>? Files { get; set; }
    public bool Pinned { get; set; }
    public DateTime CopiedAt { get; set; } = DateTime.Now;

    public string Kind => Image is not null ? "image" : Files is { Count: > 0 } ? "files" : IsLink ? "link" : "text";

    bool IsLink => Text is { } t && !t.Contains('\n') && Uri.TryCreate(t.Trim(), UriKind.Absolute, out var u)
                   && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>Card heading: a link's site, a file's name, or nothing for plain text.</summary>
    public string Title => Kind switch
    {
        "link" => new Uri(Text!.Trim()).Host.Replace("www.", ""),
        "files" => Files!.Count == 1 ? Path.GetFileName(Files[0]) : $"{Files.Count} files",
        _ => "",
    };

    public string Preview => Kind switch
    {
        "image" => "",
        "files" => Files!.Count == 1 ? Path.GetDirectoryName(Files[0]) ?? "" : string.Join(", ", Files.Select(Path.GetFileName)),
        "link" => "",
        _ => Text!.Length > 400 ? Text[..400] + "…" : Text,
    };

    public string When => TimeAgo.Of(CopiedAt);
    public BitmapImage? Thumb => Image is null ? null : ClipboardHistory.LoadThumb(Image);
    public string SearchText => Text ?? (Files is null ? "" : string.Join(" ", Files));
}

/// <summary>
/// Keeps what you copy (text and images) on this PC. Skips anything an app marks as
/// private, e.g. passwords copied from a password manager.
/// </summary>
public sealed class ClipboardHistory
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    const int MaxUnpinned = 60;

    static readonly string Dir = Path.Combine(Settings.Dir, "clips");
    static string IndexPath => Path.Combine(Settings.Dir, "clipboard.json");

    public ObservableCollection<ClipItem> Items { get; } = [];
    /// <summary>While paused, nothing new is recorded.</summary>
    public bool Paused { get; set; }
    string? _ignoreText;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")]
    static extern bool IsClipboardFormatAvailable(uint format);

    static readonly uint ExcludeFormat = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    static readonly uint HistoryFormat = RegisterClipboardFormat("CanIncludeInClipboardHistory");

    public void Attach(Window w)
    {
        Load();
        var src = HwndSource.FromHwnd(new WindowInteropHelper(w).Handle);
        src.AddHook(Hook);
        AddClipboardFormatListener(src.Handle);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            Application.Current.Dispatcher.InvokeAsync(Capture, System.Windows.Threading.DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    void Capture()
    {
        try
        {
            if (Paused || IsClipboardFormatAvailable(ExcludeFormat)) return;
            // Apps set CanIncludeInClipboardHistory = 0 for sensitive data; respect it.
            if (IsClipboardFormatAvailable(HistoryFormat) && Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream ms
                && ms.Length >= 4 && BitConverter.ToInt32(ms.ToArray(), 0) == 0) return;

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (files.Count == 0) return;
                var existing = Items.FirstOrDefault(i => i.Files is { } f && f.SequenceEqual(files));
                if (existing is not null) Items.Remove(existing);
                Insert(new ClipItem { Files = files, Pinned = existing?.Pinned ?? false });
            }
            else if (Clipboard.ContainsText())
            {
                string t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t) || t == _ignoreText) { _ignoreText = null; return; }
                var existing = Items.FirstOrDefault(i => i.Text == t);
                if (existing is not null) Items.Remove(existing);
                Insert(new ClipItem { Text = t, Pinned = existing?.Pinned ?? false });
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                Directory.CreateDirectory(Dir);
                var item = new ClipItem { Image = $"{Guid.NewGuid():N}.png" };
                using (var f = File.Create(Path.Combine(Dir, item.Image)))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(img));
                    enc.Save(f);
                }
                Insert(item);
            }
        }
        catch (COMException) { /* clipboard busy; the next copy will be caught */ }
        catch (ExternalException) { }
    }

    void Insert(ClipItem item)
    {
        // Pinned items stay at the top; new items go right below them.
        int at = Items.TakeWhile(i => i.Pinned).Count();
        if (item.Pinned) at = 0;
        Items.Insert(at, item);
        var unpinned = Items.Where(i => !i.Pinned).ToList();
        foreach (var old in unpinned.Skip(MaxUnpinned)) Remove(old, save: false);
        Save();
    }

    public void CopyBack(ClipItem item)
    {
        try
        {
            if (item.Text is not null)
            {
                _ignoreText = item.Text;
                Clipboard.SetText(item.Text);
            }
            else if (item.Image is not null && File.Exists(Path.Combine(Dir, item.Image)))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(Path.Combine(Dir, item.Image));
                bmp.EndInit();
                Clipboard.SetImage(bmp);
            }
            else if (item.Files is { Count: > 0 } files)
            {
                var sc = new System.Collections.Specialized.StringCollection();
                sc.AddRange(files.Where(File.Exists).ToArray());
                if (sc.Count > 0) Clipboard.SetFileDropList(sc);
            }
        }
        catch (COMException) { }
    }

    public void TogglePin(ClipItem item)
    {
        item.Pinned = !item.Pinned;
        Items.Remove(item);
        Insert(item);
    }

    public void Remove(ClipItem item, bool save = true)
    {
        Items.Remove(item);
        if (item.Image is not null) { try { File.Delete(Path.Combine(Dir, item.Image)); } catch { } }
        if (save) Save();
    }

    public void ClearUnpinned()
    {
        foreach (var i in Items.Where(i => !i.Pinned).ToList()) Remove(i, save: false);
        Save();
    }

    void Load()
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(IndexPath)) ?? [];
            foreach (var i in list.Where(i => i.Text is not null || i.Files is { Count: > 0 } || (i.Image is not null && File.Exists(Path.Combine(Dir, i.Image)))))
                Items.Add(i);
        }
        catch { }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(Items.ToList()));
        }
        catch { }
    }

    internal static BitmapImage? LoadThumb(string file)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = 240;
            bmp.UriSource = new Uri(Path.Combine(Dir, file));
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}

