using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;

namespace EasyNotch.Services;

/// <summary>One volume control: the output device, the microphone, or an app.</summary>
public sealed class VolumeChannel
{
    readonly Action<float> _set;
    readonly Action<bool> _setMute;
    float _volume;
    bool _muted;

    public required string Key { get; init; }
    public required string Name { get; init; }
    public Bitmap? Icon { get; init; }
    /// <summary>For devices: the sound server's name, e.g. "Speakers" or "Headphones".</summary>
    public string? DeviceName { get; init; }
    public bool IsDevice { get; init; }
    public bool IsMic { get; init; }

    public VolumeChannel(float volume, bool muted, Action<float> set, Action<bool> setMute)
    {
        _volume = volume; _muted = muted; _set = set; _setMute = setMute;
    }

    /// <summary>0–1. Setting it updates right away here and reaches the sound server shortly after.</summary>
    public float Volume { get => _volume; set { _volume = Math.Clamp(value, 0f, 1f); _set(_volume); } }
    public bool Muted { get => _muted; set { _muted = value; _setMute(value); } }
}

public sealed record MixerSnapshot(VolumeChannel? Output, VolumeChannel? Microphone, List<VolumeChannel> Apps);

/// <summary>
/// Master output, microphone and per-app volumes through PulseAudio's pactl, which also
/// drives PipeWire (pipewire-pulse) – the same controls as the desktop's own volume mixer.
/// </summary>
public static class AudioMixer
{
    /// <summary>Reads everything in one go. Runs pactl, so call it off the UI thread.</summary>
    public static MixerSnapshot Read()
    {
        var info = Json("info");
        string? sinkName = info?.TryGetProperty("default_sink_name", out var ds) == true ? ds.GetString() : null;
        string? sourceName = info?.TryGetProperty("default_source_name", out var dsrc) == true ? dsrc.GetString() : null;

        VolumeChannel? output = null, mic = null;
        if (Json("list", "sinks") is { ValueKind: JsonValueKind.Array } sinks)
            foreach (var s in sinks.EnumerateArray())
                if (Str(s, "name") == sinkName) output = Device(s, "sink", "output", isMic: false);
        if (Json("list", "sources") is { ValueKind: JsonValueKind.Array } sources)
            foreach (var s in sources.EnumerateArray())
                if (Str(s, "name") == sourceName && !sourceName!.EndsWith(".monitor", StringComparison.Ordinal)) mic = Device(s, "source", "mic", isMic: true);

        var apps = new List<VolumeChannel>();
        if (Json("list", "sink-inputs") is { ValueKind: JsonValueKind.Array } inputs)
        {
            // One fader per app, even when it plays several streams.
            var groups = inputs.EnumerateArray()
                .Select(i => (Index: i.GetProperty("index").GetInt64().ToString(CultureInfo.InvariantCulture), Props: i.GetProperty("properties"), Input: i))
                .GroupBy(x => Prop(x.Props, "application.name") ?? Prop(x.Props, "application.process.binary") ?? "Unknown app");
            foreach (var g in groups)
            {
                var ids = g.Select(x => x.Index).ToArray();
                var first = g.First();
                string key = "app:" + g.Key;
                apps.Add(new VolumeChannel(
                    g.Average(x => Level(x.Input)), g.All(x => x.Input.TryGetProperty("mute", out var m) && m.GetBoolean()),
                    v => { foreach (var id in ids) Run(key + id, "set-sink-input-volume", id, Percent(v)); },
                    m => { foreach (var id in ids) Run(key + id + "m", "set-sink-input-mute", id, m ? "1" : "0"); })
                {
                    Key = key,
                    Name = g.Key,
                    Icon = AppIcons.Find(Prop(first.Props, "application.icon_name"), Prop(first.Props, "application.process.binary"), g.Key),
                });
            }
        }
        return new MixerSnapshot(output, mic, apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    static VolumeChannel Device(JsonElement d, string kind, string key, bool isMic)
    {
        string name = Str(d, "name") ?? "";
        return new VolumeChannel(Level(d), d.TryGetProperty("mute", out var m) && m.GetBoolean(),
            v => Run(key, $"set-{kind}-volume", name, Percent(v)),
            muted => Run(key + "m", $"set-{kind}-mute", name, muted ? "1" : "0"))
        {
            Key = key, Name = isMic ? "Microphone" : "Output", DeviceName = Str(d, "description") ?? name, IsDevice = true, IsMic = isMic,
        };
    }

    /// <summary>Average of the channels, 0–1 (65536 is 100%).</summary>
    static float Level(JsonElement e)
    {
        if (!e.TryGetProperty("volume", out var vol) || vol.ValueKind != JsonValueKind.Object) return 0;
        var values = vol.EnumerateObject().Select(c => c.Value.TryGetProperty("value", out var v) ? v.GetDouble() : 0).ToList();
        return values.Count == 0 ? 0 : (float)Math.Clamp(values.Average() / 65536.0, 0, 1.5);
    }

    static string Percent(float v) => Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%";
    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static string? Prop(JsonElement props, string name) => Str(props, name) is { Length: > 0 } s ? s : null;

    static JsonElement? Json(params string[] args)
    {
        string? output = Pactl(["-f", "json", .. args]);
        if (string.IsNullOrWhiteSpace(output)) return null;
        try { using var doc = JsonDocument.Parse(output); return doc.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    static string? Pactl(string[] args)
    {
        try
        {
            var start = new ProcessStartInfo("pactl") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) start.ArgumentList.Add(a);
            using var p = Process.Start(start);
            if (p is null) return null;
            var read = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch (InvalidOperationException) { } return null; }
            return p.ExitCode == 0 ? read.Result : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; } // pactl not installed
    }

    // Changes are sent in the background. While a fader is dragged only the latest value is sent,
    // so the sound server never falls behind.
    static readonly Dictionary<string, string[]> Pending = [];
    static readonly HashSet<string> Running = [];

    static void Run(string key, params string[] args)
    {
        lock (Pending)
        {
            Pending[key] = args;
            if (!Running.Add(key)) return;
        }
        Task.Run(() =>
        {
            while (true)
            {
                string[] next;
                lock (Pending)
                {
                    if (!Pending.Remove(key, out next!)) { Running.Remove(key); return; }
                }
                Pactl(next);
            }
        });
    }
}

/// <summary>App icons for the mixer, from the icon theme folders (PNG only; others fall back to a glyph).</summary>
static class AppIcons
{
    static readonly Dictionary<string, Bitmap?> Cache = [];
    static readonly string[] Sizes = ["48x48", "64x64", "32x32", "128x128", "256x256", "24x24"];

    public static Bitmap? Find(params string?[] names)
    {
        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string name = raw.ToLowerInvariant().Replace(' ', '-');
            lock (Cache)
            {
                if (Cache.TryGetValue(name, out var cached)) { if (cached is not null) return cached; continue; }
            }
            var found = Load(name);
            lock (Cache) Cache[name] = found;
            if (found is not null) return found;
        }
        return null;
    }

    static Bitmap? Load(string name)
    {
        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } x ? x : "/usr/local/share:/usr/share").Split(':').ToList();
        dataDirs.Insert(0, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var candidates = new List<string>();
        foreach (var dir in dataDirs.Where(d => d.Length > 0))
        {
            foreach (var theme in new[] { "hicolor", "breeze", "Adwaita", "Papirus" })
                foreach (var size in Sizes)
                    candidates.Add(Path.Combine(dir, "icons", theme, size, "apps", name + ".png"));
            candidates.Add(Path.Combine(dir, "pixmaps", name + ".png"));
        }
        foreach (var file in candidates)
        {
            if (!File.Exists(file)) continue;
            try { using var s = File.OpenRead(file); return Bitmap.DecodeToWidth(s, 48); }
            catch (Exception) { }
        }
        return null;
    }
}
