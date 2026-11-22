using Avalonia.Threading;
using System.Net.Http;
using Avalonia.Media.Imaging;
using Tmds.DBus.Protocol;

namespace EasyNotch.Services;

public sealed record MediaState(
    string Title, string Artist, string App, bool IsPlaying,
    TimeSpan Position, TimeSpan Duration, Bitmap? Art, bool CanSeek,
    bool CanShuffle = false, bool Shuffle = false, bool CanRepeat = false, string Repeat = "None");

/// <summary>
/// Follows media players through MPRIS, the D-Bus interface Linux players share (Spotify,
/// browsers, VLC, Elisa, mpv with mpv-mpris…), and exposes their controls. The player that is
/// playing wins; otherwise the last one followed stays.
/// </summary>
public sealed class MediaService
{
    const string Prefix = "org.mpris.MediaPlayer2.";
    const string PlayerIface = "org.mpris.MediaPlayer2.Player";
    const string ObjectPath = "/org/mpris/MediaPlayer2";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    Connection? _bus;
    string? _player;      // bus name being followed
    string? _trackId;
    MediaState? _state;
    DateTime _positionStamp;
    readonly Dictionary<string, string> _identities = [];
    readonly SemaphoreSlim _poll = new(1, 1);
    string? _artUrl;
    Bitmap? _art;

    public event Action? Changed;
    public MediaState? State => _state;

    /// <summary>Position estimated from the last report, since players only report it when asked.</summary>
    public TimeSpan LivePosition
    {
        get
        {
            if (_state is null) return TimeSpan.Zero;
            var p = _state.Position + (_state.IsPlaying ? DateTime.UtcNow - _positionStamp : TimeSpan.Zero);
            return _state.Duration > TimeSpan.Zero && p > _state.Duration ? _state.Duration : p;
        }
    }

    public async Task StartAsync()
    {
        try
        {
            string address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")
                             ?? $"unix:path={Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")}/bus";
            _bus = new Connection(address);
            await _bus.ConnectAsync();
        }
        catch (Exception) { _bus = null; return; } // no session bus: the page just stays empty

        // Players don't announce position changes, and polling once a second is cheap on D-Bus.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => _ = PollAsync();
        timer.Start();
        await PollAsync();
    }

    async Task PollAsync()
    {
        if (_bus is null || !await _poll.WaitAsync(0)) return;
        try
        {
            var names = (await Call(_bus, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames", null, ReadStrings))
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).ToList();

            var players = new List<(string Name, Dictionary<string, VariantValue> Props)>();
            foreach (var name in names)
            {
                try { players.Add((name, await GetAll(name))); }
                catch (DBusException) { }
            }

            static bool Playing(Dictionary<string, VariantValue> p) => Str(p, "PlaybackStatus") == "Playing";
            var pick = players.FirstOrDefault(p => p.Name == _player && Playing(p.Props));
            if (pick.Name is null) pick = players.FirstOrDefault(p => Playing(p.Props));
            if (pick.Name is null) pick = players.FirstOrDefault(p => p.Name == _player);
            if (pick.Name is null) pick = players.FirstOrDefault(p => Str(p.Props, "PlaybackStatus") == "Paused");
            if (pick.Name is null) pick = players.FirstOrDefault();

            if (pick.Name is null) { _player = null; Publish(null); return; }
            _player = pick.Name;
            await Publish(pick.Name, pick.Props);
        }
        catch (Exception e) when (e is DBusException or DisconnectedException or ObjectDisposedException or InvalidOperationException) { }
        finally { _poll.Release(); }
    }

    async Task Publish(string name, Dictionary<string, VariantValue> p)
    {
        var meta = p.TryGetValue("Metadata", out var m) && m.Type == VariantValueType.Dictionary
            ? m.GetDictionary<string, VariantValue>() : [];
        string title = Str(meta, "xesam:title") ?? "";
        string artist = meta.TryGetValue("xesam:artist", out var a) && a.Type == VariantValueType.Array && a.ItemType == VariantValueType.String
            ? string.Join(", ", a.GetArray<string>()) : Str(meta, "xesam:artist") ?? "";
        if (artist.Length == 0) artist = Str(meta, "xesam:albumArtist") ?? "";
        _trackId = meta.TryGetValue("mpris:trackid", out var t)
            ? t.Type == VariantValueType.ObjectPath ? t.GetObjectPathAsString() : t.Type == VariantValueType.String ? t.GetString() : null
            : null;

        string? artUrl = Str(meta, "mpris:artUrl");
        if (artUrl != _artUrl)
        {
            _artUrl = artUrl;
            _art = await LoadArtAsync(artUrl);
        }

        var duration = TimeSpan.FromMicroseconds(Math.Max(0, Num(meta, "mpris:length")));
        var position = TimeSpan.FromMicroseconds(Math.Max(0, Num(p, "Position")));
        bool playing = Str(p, "PlaybackStatus") == "Playing";
        string loop = Str(p, "LoopStatus") ?? "";

        var state = new MediaState(
            title, artist, await Identity(name), playing,
            position, duration, _art, Bool(p, "CanSeek") && duration > TimeSpan.Zero,
            p.ContainsKey("Shuffle"), Bool(p, "Shuffle"),
            loop.Length > 0, loop is "Track" or "Playlist" ? loop : "None");
        Dispatcher.UIThread.Post(() =>
        {
            var old = _state;
            var predicted = LivePosition;
            _state = state;
            _positionStamp = DateTime.UtcNow;
            // Ticking positions are shown from LivePosition; only real changes are announced.
            bool jumped = Math.Abs((state.Position - predicted).TotalSeconds) > 2;
            if (old is null || jumped || old with { Position = default } != state with { Position = default }) Changed?.Invoke();
        });
    }

    void Publish(MediaState? state) => Dispatcher.UIThread.Post(() =>
    {
        if (_state is null && state is null) return;
        _state = state;
        Changed?.Invoke();
    });

    async Task<string> Identity(string name)
    {
        if (_identities.TryGetValue(name, out var known)) return known;
        string id;
        try
        {
            var v = await Call(_bus!, name, ObjectPath, "org.freedesktop.DBus.Properties", "Get",
                (ref MessageWriter w) => { w.WriteString("org.mpris.MediaPlayer2"); w.WriteString("Identity"); }, (m, _) => m.GetBodyReader().ReadVariantValue(), "ss");
            id = v.Type == VariantValueType.String ? v.GetString() : "";
        }
        catch (DBusException) { id = ""; }
        if (id.Length == 0)
        {
            // org.mpris.MediaPlayer2.firefox.instance_1_23 → Firefox
            string n = name[Prefix.Length..].Split('.')[0];
            id = n.Length > 0 ? char.ToUpper(n[0]) + n[1..] : name;
        }
        return _identities[name] = id;
    }

    Task<Dictionary<string, VariantValue>> GetAll(string name) =>
        Call(_bus!, name, ObjectPath, "org.freedesktop.DBus.Properties", "GetAll",
            (ref MessageWriter w) => w.WriteString(PlayerIface), (m, _) => m.GetBodyReader().ReadDictionaryOfStringToVariantValue(), "s");

    static async Task<Bitmap?> LoadArtAsync(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        try
        {
            byte[] bytes = uri.Scheme switch
            {
                "file" => await File.ReadAllBytesAsync(uri.LocalPath),
                "http" or "https" => await Http.GetByteArrayAsync(uri),
                _ => [],
            };
            if (bytes.Length == 0) return null;
            using var ms = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(ms, 160);
        }
        catch (Exception) { return null; } // missing file, offline, or not an image
    }

    // ------------------------------------------------------------ controls

    void Command(string method, WriteArgs? args = null, string? signature = null)
    {
        if (_bus is not { } bus || _player is not { } name) return;
        _ = Task.Run(async () =>
        {
            try { await Send(bus, name, ObjectPath, PlayerIface, method, args, signature); }
            catch (Exception e) when (e is DBusException or DisconnectedException) { }
            await Task.Delay(150);
            await PollAsync();
        });
    }

    void SetProperty(string property, WriteArgs writeVariant)
    {
        if (_bus is not { } bus || _player is not { } name) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Send(bus, name, ObjectPath, "org.freedesktop.DBus.Properties", "Set",
                    (ref MessageWriter w) => { w.WriteString(PlayerIface); w.WriteString(property); writeVariant(ref w); }, "ssv");
            }
            catch (Exception e) when (e is DBusException or DisconnectedException) { }
            await Task.Delay(150);
            await PollAsync();
        });
    }

    public void PlayPause() => Command("PlayPause");
    public void Next() => Command("Next");
    public void Previous() => Command("Previous");
    public void ToggleShuffle() { if (_state is { } st) SetProperty("Shuffle", (ref MessageWriter w) => w.WriteVariantBool(!st.Shuffle)); }

    /// <summary>Off → repeat all → repeat one → off.</summary>
    public void CycleRepeat()
    {
        if (_state is not { } st) return;
        string next = st.Repeat switch { "None" => "Playlist", "Playlist" => "Track", _ => "None" };
        SetProperty("LoopStatus", (ref MessageWriter w) => w.WriteVariantString(next));
    }

    public void Seek(TimeSpan to)
    {
        long us = (long)to.TotalMicroseconds;
        if (_trackId is { } id && id.StartsWith('/'))
            Command("SetPosition", (ref MessageWriter w) => { w.WriteObjectPath(id); w.WriteInt64(us); }, "ox");
        else
            Command("Seek", (ref MessageWriter w) => w.WriteInt64(us - (long)LivePosition.TotalMicroseconds), "x");
    }

    // ------------------------------------------------------------ D-Bus helpers

    static async Task<T> Call<T>(Connection bus, string destination, string path, string iface, string member,
        WriteArgs? args, MessageValueReader<T> reader, string? signature = null)
    {
        var message = Build(bus, destination, path, iface, member, args, signature);
        return await bus.CallMethodAsync(message, reader);
    }

    static async Task Send(Connection bus, string destination, string path, string iface, string member,
        WriteArgs? args, string? signature)
    {
        var message = Build(bus, destination, path, iface, member, args, args is null ? null : signature);
        await bus.CallMethodAsync(message);
    }

    /// <summary>Writes a call's arguments. MessageWriter is a struct, so it must be passed by reference.</summary>
    delegate void WriteArgs(ref MessageWriter writer);

    static MessageBuffer Build(Connection bus, string destination, string path, string iface, string member, WriteArgs? args, string? signature)
    {
        var writer = bus.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination, path, iface, member, signature);
            args?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally { writer.Dispose(); }
    }

    static string[] ReadStrings(Message m, object? _) => m.GetBodyReader().ReadArrayOfString();

    static string? Str(Dictionary<string, VariantValue> d, string key) =>
        d.TryGetValue(key, out var v) && v.Type == VariantValueType.String ? v.GetString() : null;

    static bool Bool(Dictionary<string, VariantValue> d, string key) =>
        d.TryGetValue(key, out var v) && v.Type == VariantValueType.Bool && v.GetBool();

    /// <summary>Microsecond values; players disagree on the integer type (and a few send doubles).</summary>
    static long Num(Dictionary<string, VariantValue> d, string key)
    {
        if (!d.TryGetValue(key, out var v)) return 0;
        return v.Type switch
        {
            VariantValueType.Int64 => v.GetInt64(),
            VariantValueType.UInt64 => (long)Math.Min(v.GetUInt64(), long.MaxValue),
            VariantValueType.Int32 => v.GetInt32(),
            VariantValueType.UInt32 => v.GetUInt32(),
            VariantValueType.Double => (long)v.GetDouble(),
            _ => 0,
        };
    }
}
