using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EasyNotch;

/// <summary>
/// Linux desktop integration: X11 window hints (Avalonia runs through X11 / XWayland),
/// the click-through input shape, full-screen detection, XDG autostart and opening things.
/// Everything fails quietly: without X11 extensions the notch still works, just with fewer niceties.
/// </summary>
internal static class LinuxDesktop
{
    // ------------------------------------------------------------ X11

    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, ref ClientMessage message);
    [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XFree(IntPtr data);
    [DllImport("libX11.so.6")]
    static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length, bool delete,
        IntPtr reqType, out IntPtr actualType, out int actualFormat, out IntPtr items, out IntPtr bytesAfter, out IntPtr data);
    [DllImport("libXext.so.6")] static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int x, int y, ref XRectangle rect, int count, int operation, int ordering);

    [StructLayout(LayoutKind.Sequential)]
    struct XRectangle { public short X, Y; public ushort Width, Height; }

    [StructLayout(LayoutKind.Sequential, Size = 192)]
    struct ClientMessage
    {
        public int Type; public IntPtr Serial; public int SendEvent; public IntPtr Display; public IntPtr Window;
        public IntPtr MessageType; public int Format; public IntPtr L0, L1, L2, L3, L4;
    }

    static IntPtr _display;
    static bool _x11Failed;

    /// <summary>Our own connection to the X server, used from the UI thread only.</summary>
    static IntPtr Connection()
    {
        if (_display != IntPtr.Zero || _x11Failed) return _display;
        try { _display = XOpenDisplay(IntPtr.Zero); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        _x11Failed = _display == IntPtr.Zero;
        return _display;
    }

    static IntPtr Xid(Window window) =>
        window.TryGetPlatformHandle() is { HandleDescriptor: "XID" } handle ? handle.Handle : IntPtr.Zero;

    static void X11(Action<IntPtr> body)
    {
        IntPtr d = Connection();
        if (d == IntPtr.Zero) return;
        try { body(d); XFlush(d); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Keeps the notch out of the taskbar, pager and Alt+Tab, and on every virtual desktop.</summary>
    internal static void ApplyNotchHints(Window window)
    {
        IntPtr w = Xid(window);
        if (w == IntPtr.Zero) return;
        X11(d =>
        {
            State(d, w, 1, "_NET_WM_STATE_SKIP_TASKBAR", "_NET_WM_STATE_SKIP_PAGER");
            State(d, w, 1, "_KDE_NET_WM_STATE_SKIP_SWITCHER", "_NET_WM_STATE_STICKY");
            State(d, w, 1, "_NET_WM_STATE_ABOVE", null);
            Send(d, w, "_NET_WM_DESKTOP", new IntPtr(0xFFFFFFFF), new IntPtr(1));
        });
        PinOnKde();
    }

    /// <summary>
    /// KDE Plasma doesn't apply the X11 "all desktops" request to XWayland windows when there are
    /// several virtual desktops, so on KDE ask KWin itself (its scripting interface, via busctl).
    /// </summary>
    static void PinOnKde()
    {
        if (!(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("KDE", StringComparison.OrdinalIgnoreCase)) return;
        int pid = Environment.ProcessId;
        Task.Run(() =>
        {
            try
            {
                string dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } r ? r : Path.GetTempPath();
                string script = Path.Combine(dir, $"easy-notch-kwin-{pid}.js"), name = $"easy-notch-pin-{pid}";
                File.WriteAllText(script,
                    "for (const w of workspace.windowList()) {\n" +
                    $"  if (w.pid === {pid} && String(w.caption) === \"Easy Notch\") {{ w.onAllDesktops = true; w.skipTaskbar = true; w.skipPager = true; w.skipSwitcher = true; w.keepAbove = true; }}\n" +
                    "}\n");
                Busctl("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "unloadScript", "s", name);
                string? loaded = Busctl("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "loadScript", "ss", script, name);
                if (loaded != null && loaded.Trim().Split(' ') is [_, var id] && int.TryParse(id, out int number) && number >= 0)
                    Busctl("org.kde.KWin", "/Scripting/Script" + number, "org.kde.kwin.Script", "run", "");
                Thread.Sleep(500);
                Busctl("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "unloadScript", "s", name);
                File.Delete(script);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
        });
    }

    static string? Busctl(string service, string path, string @interface, string method, string signature, params string[] args)
    {
        var start = new ProcessStartInfo("busctl") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "--user", "call", service, path, @interface, method }) start.ArgumentList.Add(a);
        if (signature.Length > 0) { start.ArgumentList.Add(signature); foreach (var a in args) start.ArgumentList.Add(a); }
        using var process = Process.Start(start);
        if (process == null) return null;
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(3000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
        return process.ExitCode == 0 ? output : null;
    }

    /// <summary>
    /// Brings a window to the front and gives it the keyboard. Marked as a pager/taskbar request
    /// (source 2), which window managers' focus-stealing prevention honours.
    /// </summary>
    internal static void Activate(Window window)
    {
        IntPtr w = Xid(window);
        if (w == IntPtr.Zero) return;
        X11(d => Send(d, w, "_NET_ACTIVE_WINDOW", new IntPtr(2), IntPtr.Zero, IntPtr.Zero));
    }

    /// <summary>Only this rectangle (in window pixels) takes the pointer; the rest of the window passes clicks through.</summary>
    internal static void SetInputRegion(Window window, PixelRect r)
    {
        IntPtr w = Xid(window);
        if (w == IntPtr.Zero) return;
        X11(d =>
        {
            var rect = new XRectangle
            {
                X = (short)Math.Clamp(r.X, 0, short.MaxValue), Y = (short)Math.Clamp(r.Y, 0, short.MaxValue),
                Width = (ushort)Math.Clamp(r.Width, 1, ushort.MaxValue), Height = (ushort)Math.Clamp(r.Height, 1, ushort.MaxValue),
            };
            const int ShapeInput = 2, ShapeSet = 0, Unsorted = 0;
            XShapeCombineRectangles(d, w, ShapeInput, 0, 0, ref rect, 1, ShapeSet, Unsorted);
        });
    }

    /// <summary>
    /// True while another window is active and full screen (games, videos, presentations).
    /// Works for X11 / XWayland apps; on Wayland, KDE and GNOME already stack an active
    /// full-screen window above the notch.
    /// </summary>
    internal static bool ActiveWindowIsFullScreen(Window self)
    {
        IntPtr d = Connection();
        if (d == IntPtr.Zero) return false;
        try
        {
            IntPtr root = XDefaultRootWindow(d);
            var active = Longs(d, root, XInternAtom(d, "_NET_ACTIVE_WINDOW", false), 1);
            if (active.Length == 0 || active[0] == 0 || (IntPtr)active[0] == Xid(self)) return false;
            var state = Longs(d, (IntPtr)active[0], XInternAtom(d, "_NET_WM_STATE", false), 32);
            long full = XInternAtom(d, "_NET_WM_STATE_FULLSCREEN", false);
            return state.Contains(full);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>A 32-bit-format window property (atoms, windows, cardinals); Xlib hands them over as longs.</summary>
    static long[] Longs(IntPtr d, IntPtr window, IntPtr property, int max)
    {
        if (XGetWindowProperty(d, window, property, IntPtr.Zero, max, false, IntPtr.Zero,
                out _, out int format, out IntPtr items, out _, out IntPtr data) != 0 || data == IntPtr.Zero) return [];
        try
        {
            if (format != 32) return [];
            var result = new long[(int)items];
            for (int i = 0; i < result.Length; i++) result[i] = Marshal.ReadIntPtr(data, i * IntPtr.Size);
            return result;
        }
        finally { XFree(data); }
    }

    static void State(IntPtr d, IntPtr w, int action, string first, string? second) =>
        Send(d, w, "_NET_WM_STATE", new IntPtr(action), XInternAtom(d, first, false), second == null ? IntPtr.Zero : XInternAtom(d, second, false), new IntPtr(1));

    static void Send(IntPtr d, IntPtr w, string type, params IntPtr[] data)
    {
        var message = new ClientMessage { Type = 33 /* ClientMessage */, SendEvent = 1, Display = d, Window = w, MessageType = XInternAtom(d, type, false), Format = 32 };
        if (data.Length > 0) message.L0 = data[0];
        if (data.Length > 1) message.L1 = data[1];
        if (data.Length > 2) message.L2 = data[2];
        if (data.Length > 3) message.L3 = data[3];
        XSendEvent(d, XDefaultRootWindow(d), false, new IntPtr((1 << 20) | (1 << 19)) /* SubstructureRedirect | SubstructureNotify */, ref message);
    }

    // ------------------------------------------------------------ animations

    /// <summary>Follows the desktop's "animations off" switch (KDE: animation speed "Instant"; GNOME: Reduce animation).</summary>
    internal static bool AnimationsEnabled()
    {
        try
        {
            string kdeglobals = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kdeglobals");
            if (File.Exists(kdeglobals))
                foreach (var line in File.ReadLines(kdeglobals))
                    if (line.StartsWith("AnimationDurationFactor=", StringComparison.Ordinal)
                        && double.TryParse(line[24..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double f))
                        return f > 0;
            if ((Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("GNOME", StringComparison.OrdinalIgnoreCase))
            {
                var start = new ProcessStartInfo("gsettings", "get org.gnome.desktop.interface enable-animations") { RedirectStandardOutput = true, UseShellExecute = false };
                using var p = Process.Start(start);
                if (p != null && p.WaitForExit(1500)) return p.StandardOutput.ReadToEnd().Trim() != "false";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        return true;
    }

    // ------------------------------------------------------------ start at login (XDG autostart)

    static string AutostartFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart", "easy-notch.desktop");
    static string Executable => Environment.ProcessPath ?? "easy-notch";
    static string ExecLine => "Exec=\"" + Executable.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";

    internal static bool StartsAtLogin
    {
        get
        {
            try { return File.Exists(AutostartFile) && File.ReadLines(AutostartFile).Any(l => l.StartsWith("Exec=", StringComparison.Ordinal)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>Writes (or removes) ~/.config/autostart/easy-notch.desktop, pointing at this copy.</summary>
    internal static void SetStartAtLogin(bool on)
    {
        if (!on) { if (File.Exists(AutostartFile)) File.Delete(AutostartFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        File.WriteAllText(AutostartFile, string.Join('\n',
            "[Desktop Entry]", "Type=Application", "Name=Easy Notch", "Comment=A notch at the top of your screen", ExecLine,
            "Icon=easy-notch", "Terminal=false", "X-GNOME-Autostart-enabled=true", "X-KDE-autostart-after=panel", ""));
    }

    // ------------------------------------------------------------ opening things

    /// <summary>Opens a link, file or folder with the desktop's default app.</summary>
    internal static void Open(string target)
    {
        try
        {
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(target);
            Process.Start(start)?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>Opens the file manager with the file selected (FileManager1), or just its folder.</summary>
    internal static void ShowInFolder(string file)
    {
        Task.Run(() =>
        {
            string uri = new Uri(file).AbsoluteUri;
            try
            {
                if (Busctl("org.freedesktop.FileManager1", "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1", "ShowItems", "ass", "1", uri, "") != null) return;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            if (Path.GetDirectoryName(file) is { } dir) Open(dir);
        });
    }
}
