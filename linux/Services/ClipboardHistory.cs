// The classic clipboard calls (formats, raw data by MIME type, DataObject) are what the X11
// backend implements fully today; the newer DataTransfer API is a wrapper over the same thing.
#pragma warning disable CS0618

using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;

namespace EasyNotch.Services;

public sealed class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Text { get; set; }
    /// <summary>File name of a PNG under the clips folder, for copied images.</summary>
    public string? Image { get; set; }
    /// <summary>Paths, for copied files (file manager copy).</summary>
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
    public Bitmap? Thumb => Image is null ? null : ClipboardHistory.LoadThumb(Image);
    public string SearchText => Text ?? (Files is null ? "" : string.Join(" ", Files));
}

/// <summary>
/// Keeps what you copy (text, links, images and files) on this computer. Skips anything a
/// password manager marks as secret (the x-kde-passwordManagerHint that KeePassXC and others set).
/// Changes are noticed through the X11 XFixes extension (XWayland mirrors the Wayland clipboard);
/// without it, the clipboard is checked every second.
/// </summary>
public sealed class ClipboardHistory
{
    const int MaxUnpinned = 60;

    static readonly string Dir = Path.Combine(Settings.Dir, "clips");
    static string IndexPath => Path.Combine(Settings.Dir, "clipboard.json");

    public ObservableCollection<ClipItem> Items { get; } = [];
    /// <summary>While paused, nothing new is recorded.</summary>
    public bool Paused { get; set; }

    IClipboard? _clipboard;
    string? _ignoreText;
    DateTime _ignoreUntil;
    string _lastSeen = "";
    bool _capturing;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public void Attach(TopLevel top)
    {
        Load();
        _clipboard = top.Clipboard;
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await CaptureAsync(); };
        if (!WatchX11())
        {
            var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            poll.Tick += async (_, _) => await CaptureAsync();
            poll.Start();
        }
    }

    void Changed() => Dispatcher.UIThread.Post(() => { _debounce.Stop(); _debounce.Start(); });

    async Task CaptureAsync()
    {
        if (_clipboard is not { } clip || _capturing) return;
        _capturing = true;
        try
        {
            if (Paused || DateTime.UtcNow < _ignoreUntil) return;
            var formats = await clip.GetFormatsAsync() ?? [];
            if (formats.Length == 0) return;

            // Password managers mark secrets; never keep those.
            if (formats.Contains("x-kde-passwordManagerHint") && Decode(await clip.GetDataAsync("x-kde-passwordManagerHint")) is "secret") return;

            if (formats.Contains("text/uri-list") && ParseFiles(Decode(await clip.GetDataAsync("text/uri-list"))) is { Count: > 0 } files)
            {
                string key = "files:" + string.Join("\n", files);
                if (key == _lastSeen) return;
                _lastSeen = key;
                var existing = Items.FirstOrDefault(i => i.Files is { } f && f.SequenceEqual(files));
                if (existing is not null) Items.Remove(existing);
                Insert(new ClipItem { Files = files, Pinned = existing?.Pinned ?? false });
            }
            else if (formats.Any(IsText) && await clip.GetTextAsync() is { } t && !string.IsNullOrWhiteSpace(t))
            {
                if (t == _ignoreText) { _ignoreText = null; _lastSeen = "text:" + t; return; }
                if ("text:" + t == _lastSeen) return;
                _lastSeen = "text:" + t;
                var existing = Items.FirstOrDefault(i => i.Text == t);
                if (existing is not null) Items.Remove(existing);
                Insert(new ClipItem { Text = t, Pinned = existing?.Pinned ?? false });
            }
            else if (formats.Contains("image/png") && await clip.GetDataAsync("image/png") is byte[] { Length: > 0 } png)
            {
                string key = "image:" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(png));
                if (key == _lastSeen) return;
                _lastSeen = key;
                using (var check = new MemoryStream(png)) using (new Bitmap(check)) { } // only keep real images
                Directory.CreateDirectory(Dir);
                var item = new ClipItem { Image = $"{Guid.NewGuid():N}.png" };
                await File.WriteAllBytesAsync(Path.Combine(Dir, item.Image), png);
                Insert(item);
            }
        }
        catch (Exception) { /* clipboard owner went away or sent something odd; the next copy will be caught */ }
        finally { _capturing = false; }
    }

    static bool IsText(string f) => f is "UTF8_STRING" or "STRING" or "TEXT" or "text/plain" or "text/plain;charset=utf-8";

    static string? Decode(object? data) => data switch
    {
        string s => s,
        byte[] b => Encoding.UTF8.GetString(b).TrimEnd('\0'),
        _ => null,
    };

    /// <summary>file:// lines of a text/uri-list (comments and other schemes skipped).</summary>
    static List<string> ParseFiles(string? list)
    {
        var files = new List<string>();
        if (list is null) return files;
        foreach (var raw in list.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out var u) && u.IsFile) files.Add(u.LocalPath);
        }
        return files;
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

    public async void CopyBack(ClipItem item)
    {
        if (_clipboard is not { } clip) return;
        try
        {
            _ignoreUntil = DateTime.UtcNow.AddSeconds(1); // our own copy isn't a new item
            if (item.Text is not null)
            {
                _ignoreText = item.Text;
                await clip.SetTextAsync(item.Text);
            }
            else if (item.Image is not null && File.Exists(Path.Combine(Dir, item.Image)))
            {
                var data = new DataObject();
                data.Set("image/png", await File.ReadAllBytesAsync(Path.Combine(Dir, item.Image)));
                await clip.SetDataObjectAsync(data);
            }
            else if (item.Files is { Count: > 0 } files)
            {
                var existing = files.Where(File.Exists).ToList();
                if (existing.Count == 0) return;
                string uris = string.Join("\r\n", existing.Select(f => new Uri(f).AbsoluteUri));
                var data = new DataObject();
                data.Set("text/uri-list", Encoding.UTF8.GetBytes(uris + "\r\n"));
                data.Set("x-special/gnome-copied-files", Encoding.UTF8.GetBytes("copy\n" + uris.Replace("\r\n", "\n")));
                data.Set(DataFormats.Text, string.Join("\n", existing));
                await clip.SetDataObjectAsync(data);
            }
        }
        catch (Exception) { }
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
        if (item.Image is not null) { try { File.Delete(Path.Combine(Dir, item.Image)); } catch (IOException) { } }
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        if (Items.FirstOrDefault(i => !i.Pinned) is { Text: { } last }) _lastSeen = "text:" + last;
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            string tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items.ToList()));
            File.Move(tmp, IndexPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static Bitmap? LoadThumb(string file)
    {
        try
        {
            using var s = File.OpenRead(Path.Combine(Dir, file));
            return Bitmap.DecodeToHeight(s, 240);
        }
        catch (Exception) { return null; }
    }

    // ------------------------------------------------------------ X11 change notification

    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] static extern int XNextEvent(IntPtr display, IntPtr eventReturn);
    [DllImport("libXfixes.so.3")] static extern bool XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport("libXfixes.so.3")] static extern void XFixesSelectSelectionInput(IntPtr display, IntPtr window, IntPtr selection, IntPtr eventMask);

    /// <summary>
    /// A second X connection, owned by one background thread, waits for "the clipboard has a new owner"
    /// (which is what copying does) and nudges the UI thread to read it.
    /// </summary>
    bool WatchX11()
    {
        try
        {
            IntPtr d = XOpenDisplay(IntPtr.Zero);
            if (d == IntPtr.Zero || !XFixesQueryExtension(d, out int eventBase, out _)) return false;
            XFixesSelectSelectionInput(d, XDefaultRootWindow(d), XInternAtom(d, "CLIPBOARD", false), new IntPtr(1) /* SetSelectionOwnerNotifyMask */);
            new Thread(() =>
            {
                IntPtr ev = Marshal.AllocHGlobal(192); // sizeof(XEvent)
                try
                {
                    while (true)
                    {
                        XNextEvent(d, ev);
                        if (Marshal.ReadInt32(ev) == eventBase /* XFixesSelectionNotify */) Changed();
                    }
                }
                finally { Marshal.FreeHGlobal(ev); }
            }) { IsBackground = true, Name = "Clipboard watcher" }.Start();
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}
