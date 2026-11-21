using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyNotch.Services;

/// <summary>One volume control: the output device, the microphone, or an app.</summary>
public sealed class VolumeChannel
{
    readonly Func<float> _get;
    readonly Action<float> _set;
    readonly Func<bool> _getMute;
    readonly Action<bool> _setMute;

    public required string Key { get; init; }
    public required string Name { get; init; }
    public ImageSource? Icon { get; init; }
    /// <summary>For devices: the Windows name, e.g. "Speakers" or "Headphones".</summary>
    public string? DeviceName { get; init; }
    public bool IsDevice { get; init; }
    public bool IsMic { get; init; }

    public VolumeChannel(Func<float> get, Action<float> set, Func<bool> getMute, Action<bool> setMute)
    {
        _get = get; _set = set; _getMute = getMute; _setMute = setMute;
    }

    public float Volume { get { try { return _get(); } catch { return 0; } } set { try { _set(Math.Clamp(value, 0f, 1f)); } catch { } } }
    public bool Muted { get { try { return _getMute(); } catch { return false; } } set { try { _setMute(value); } catch { } } }
}

/// <summary>
/// Windows Core Audio: master output, microphone, and per-app volumes
/// (the same controls as the Windows volume mixer).
/// </summary>
public static class AudioMixer
{
    static Guid _ctx = Guid.NewGuid(); // identifies our own changes to Windows
    static readonly Dictionary<string, ImageSource?> IconCache = [];

    public static VolumeChannel? Output() => Mta(() => Endpoint(EDataFlow.Render, "output", "Output"));
    public static VolumeChannel? Microphone() => Mta(() => Endpoint(EDataFlow.Capture, "mic", "Microphone"));
    public static List<VolumeChannel> Apps() => Mta(AppsCore);

    /// <summary>
    /// Core Audio's per-app volume objects only answer from a multithreaded COM apartment;
    /// WPF's UI thread is single-threaded, so every audio call hops to a pool (MTA) thread.
    /// </summary>
    static T Mta<T>(Func<T> f) =>
        Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA ? f() : Task.Run(f).GetAwaiter().GetResult();

    static void Mta(Action a) => Mta(() => { a(); return 0; });

    static VolumeChannel? Endpoint(EDataFlow flow, string key, string name)
    {
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var dev) != 0 || dev is null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object o);
            var v = (IAudioEndpointVolume)o;
            return new VolumeChannel(
                () => Mta(() => { v.GetMasterVolumeLevelScalar(out float f); return f; }),
                f => Mta(() => v.SetMasterVolumeLevelScalar(f, ref _ctx)),
                () => Mta(() => { v.GetMute(out bool m); return m; }),
                m => Mta(() => v.SetMute(m, ref _ctx)))
            { Key = key, Name = name, DeviceName = FriendlyName(dev), IsDevice = true, IsMic = flow == EDataFlow.Capture };
        }
        catch { return null; }
    }

    /// <summary>Apps that currently have an audio session on the default output, one row per app.</summary>
    static List<VolumeChannel> AppsCore()
    {
        var result = new List<VolumeChannel>();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var dev) != 0 || dev is null) return result;
            var iid = typeof(IAudioSessionManager2).GUID;
            dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object o);
            var mgr = (IAudioSessionManager2)o;
            mgr.GetSessionEnumerator(out var sessions);
            sessions.GetCount(out int count);

            // A browser or game can have several sessions; group them so the app gets one slider.
            var groups = new Dictionary<string, (string Name, string? Exe, List<SessionVolume> Vols)>();
            for (int i = 0; i < count; i++)
            {
                try
                {
                    sessions.GetSession(i, out var ctl);
                    ctl.GetState(out int state);
                    if (state == 2 /* expired */) continue;
                    // Most Windows builds offer the simple per-app control; some (e.g. 26300) only the per-channel one.
                    SessionVolume? vol = ctl is ISimpleAudioVolume s ? new SimpleVolume(s)
                        : ctl is IChannelAudioVolume c ? new ChannelVolume(c) : null;
                    if (vol is null) continue;
                    bool system = ctl.IsSystemSoundsSession() == 0;
                    ctl.GetProcessId(out uint pid);
                    string key, name;
                    string? exe = null;
                    if (system) { key = "system"; name = "System sounds"; }
                    else
                    {
                        (name, exe) = Describe((int)pid, ctl);
                        key = exe ?? name;
                    }
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = (name, exe, []);
                    g.Vols.Add(vol);
                }
                catch { /* one odd session shouldn't hide the rest */ }
            }

            foreach (var (key, g) in groups.OrderBy(kv => kv.Key == "system" ? 1 : 0).ThenBy(kv => kv.Value.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var vols = g.Vols;
                string k = key;
                result.Add(new VolumeChannel(
                    () => Mta(() => vols[0].Get()),
                    f => Mta(() => { foreach (var v in vols) v.Set(f); }),
                    () => Mta(() => vols[0].GetMute(k)),
                    m => Mta(() => { foreach (var v in vols) v.SetMute(k, m); }))
                { Key = key, Name = g.Name, Icon = g.Exe is null ? null : IconFor(g.Exe) });
            }
        }
        catch { }
        return result;
    }

    /// <summary>"Speakers (Realtek Audio)" → "Speakers".</summary>
    static string? FriendlyName(IMMDevice dev)
    {
        try
        {
            dev.OpenPropertyStore(0 /* STGM_READ */, out var store);
            var key = new PropertyKey { Fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Pid = 14 };
            store.GetValue(ref key, out var pv);
            try
            {
                if (pv.Vt != 31 /* VT_LPWSTR */) return null;
                string? full = Marshal.PtrToStringUni(pv.Ptr);
                if (full is null) return null;
                int paren = full.IndexOf(" (", StringComparison.Ordinal);
                return paren > 0 ? full[..paren] : full;
            }
            finally { PropVariantClear(ref pv); }
        }
        catch { return null; }
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid Fmtid; public int Pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort Vt; public ushort R1, R2, R3; public IntPtr Ptr; public IntPtr Ptr2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    abstract class SessionVolume
    {
        public abstract float Get();
        public abstract void Set(float level);
        public abstract bool GetMute(string key);
        public abstract void SetMute(string key, bool mute);
    }

    sealed class SimpleVolume(ISimpleAudioVolume v) : SessionVolume
    {
        public override float Get() { v.GetMasterVolume(out float f); return f; }
        public override void Set(float level) => v.SetMasterVolume(level, ref _ctx);
        public override bool GetMute(string key) { v.GetMute(out bool m); return m; }
        public override void SetMute(string key, bool mute) => v.SetMute(mute, ref _ctx);
    }

    /// <summary>Levels saved when muting through the per-channel control, which has no mute switch.</summary>
    static readonly Dictionary<string, float> MutedLevels = [];

    sealed class ChannelVolume(IChannelAudioVolume v) : SessionVolume
    {
        public override float Get()
        {
            v.GetChannelCount(out uint n);
            float max = 0;
            for (uint i = 0; i < n; i++) { v.GetChannelVolume(i, out float f); max = Math.Max(max, f); }
            return max;
        }

        public override void Set(float level)
        {
            v.GetChannelCount(out uint n);
            for (uint i = 0; i < n; i++) v.SetChannelVolume(i, level, ref _ctx);
        }

        public override bool GetMute(string key)
        {
            if (!MutedLevels.ContainsKey(key)) return false;
            if (Get() > 0.001f) { MutedLevels.Remove(key); return false; } // raised elsewhere: no longer muted
            return true;
        }

        public override void SetMute(string key, bool mute)
        {
            if (mute)
            {
                if (!MutedLevels.ContainsKey(key)) MutedLevels[key] = Get();
                Set(0);
            }
            else if (MutedLevels.Remove(key, out float level))
                Set(level > 0.001f ? level : 1f);
        }
    }

    static (string Name, string? Exe) Describe(int pid, IAudioSessionControl2 ctl)
    {
        string? exe = null, desc = null, procName = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            procName = p.ProcessName;
            exe = p.MainModule?.FileName;
            if (exe is not null) desc = FileVersionInfo.GetVersionInfo(exe).FileDescription;
        }
        catch { /* elevated or exited processes can't be inspected */ }

        ctl.GetDisplayName(out string? display);
        string name = !string.IsNullOrWhiteSpace(display) && !display.StartsWith('@') ? display
            : !string.IsNullOrWhiteSpace(desc) ? desc
            : procName ?? $"App {pid}";
        return (name.Trim(), exe);
    }

    static ImageSource? IconFor(string exe)
    {
        if (IconCache.TryGetValue(exe, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ico is not null)
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                img = bs;
            }
        }
        catch { }
        return IconCache[exe] = img;
    }

    // ------------------------------------------------------------------ COM interop

    const int ClsCtxAll = 23;
    enum EDataFlow { Render = 0, Capture = 1 }
    enum ERole { Console = 0, Multimedia = 1 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        void OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr n);
        void UnregisterControlChangeNotify(IntPtr n);
        void GetChannelCount(out uint c);
        void SetMasterVolumeLevel(float db, ref Guid ctx);
        void SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        void GetMasterVolumeLevel(out float db);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        void SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        void GetChannelVolumeLevel(uint ch, out float db);
        void GetChannelVolumeLevelScalar(uint ch, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr guid, int flags, out IntPtr ctl);
        void GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr vol);
        void GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string? path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
        void GetGroupingParam(out Guid g);
        void SetGroupingParam(ref Guid g, ref Guid ctx);
        void RegisterAudioSessionNotification(IntPtr n);
        void UnregisterAudioSessionNotification(IntPtr n);
        // IAudioSessionControl2
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        void GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession(); // S_OK (0) = yes, S_FALSE (1) = no
    }

    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IChannelAudioVolume
    {
        void GetChannelCount(out uint count);
        void SetChannelVolume(uint index, float level, ref Guid ctx);
        void GetChannelVolume(uint index, out float level);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6F4BA6D7D2B6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid ctx);
        void GetMasterVolume(out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
