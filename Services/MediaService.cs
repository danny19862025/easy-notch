using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace EasyNotch.Services;

public sealed record MediaState(
    string Title, string Artist, string App, bool IsPlaying,
    TimeSpan Position, TimeSpan Duration, BitmapImage? Art, bool CanSeek);

/// <summary>
/// Follows whatever app Windows considers the current media session
/// (Spotify, browsers, Media Player, …) and exposes its controls.
/// </summary>
public sealed class MediaService
{
    readonly Dispatcher _ui;
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _session;
    MediaState? _state;
    DateTime _positionStamp;

    public event Action? Changed;

    public MediaService(Dispatcher ui) => _ui = ui;

    public MediaState? State => _state;

    /// <summary>Position estimated from the last report, since apps only report it occasionally.</summary>
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
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(Attach);
            Attach();
        }
        catch { /* media API unavailable (e.g. N editions): the widget just stays empty */ }
    }

    void Attach()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionEvent;
            _session.PlaybackInfoChanged -= OnSessionEvent;
            _session.TimelinePropertiesChanged -= OnSessionEvent;
        }
        _session = _mgr?.GetCurrentSession();
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionEvent;
            _session.PlaybackInfoChanged += OnSessionEvent;
            _session.TimelinePropertiesChanged += OnSessionEvent;
        }
        _ = RefreshAsync();
    }

    void OnSessionEvent(GlobalSystemMediaTransportControlsSession s, object e) => _ui.InvokeAsync(() => _ = RefreshAsync());

    string? _artKey;
    BitmapImage? _art;

    async Task RefreshAsync()
    {
        var s = _session;
        if (s is null) { _state = null; Changed?.Invoke(); return; }
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var info = s.GetPlaybackInfo();
            var tl = s.GetTimelineProperties();
            if (!ReferenceEquals(s, _session)) return;

            string key = $"{props.Title}\u0001{props.Artist}";
            if (key != _artKey)
            {
                _artKey = key;
                _art = await LoadArtAsync(props.Thumbnail);
            }

            var pos = tl.Position - tl.StartTime;
            var dur = tl.EndTime - tl.StartTime;
            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            // LastUpdatedTime tells us how stale the reported position is.
            if (playing && tl.LastUpdatedTime.Year > 2000)
            {
                var age = DateTimeOffset.UtcNow - tl.LastUpdatedTime;
                if (age > TimeSpan.Zero && age < TimeSpan.FromHours(6)) pos += age;
            }
            _positionStamp = DateTime.UtcNow;
            _state = new MediaState(
                props.Title ?? "", string.IsNullOrEmpty(props.Artist) ? props.AlbumArtist ?? "" : props.Artist,
                FriendlyAppName(s.SourceAppUserModelId), playing,
                pos < TimeSpan.Zero ? TimeSpan.Zero : pos, dur < TimeSpan.Zero ? TimeSpan.Zero : dur,
                _art, info.Controls.IsPlaybackPositionEnabled && dur > TimeSpan.Zero);
        }
        catch { _state = null; }
        Changed?.Invoke();
    }

    static async Task<BitmapImage?> LoadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumb)
    {
        if (thumb is null) return null;
        try
        {
            using var ras = await thumb.OpenReadAsync();
            using var src = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await src.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    static string FriendlyAppName(string aumid)
    {
        string n = aumid;
        int bang = n.IndexOf('!');
        if (bang >= 0) n = n[(bang + 1)..];
        n = Path.GetFileNameWithoutExtension(n);
        int us = n.IndexOf('_');
        if (us > 0) n = n[..us];
        int dot = n.LastIndexOf('.');
        if (dot >= 0 && dot < n.Length - 1) n = n[(dot + 1)..];
        return n.Length > 0 ? char.ToUpper(n[0]) + n[1..] : aumid;
    }

    public async void PlayPause() { if (_session is { } s) await Try(() => s.TryTogglePlayPauseAsync().AsTask()); }
    public async void Next() { if (_session is { } s) await Try(() => s.TrySkipNextAsync().AsTask()); }
    public async void Previous() { if (_session is { } s) await Try(() => s.TrySkipPreviousAsync().AsTask()); }

    public async void Seek(TimeSpan to)
    {
        if (_session is not { } s) return;
        var start = s.GetTimelineProperties().StartTime;
        await Try(() => s.TryChangePlaybackPositionAsync((start + to).Ticks).AsTask());
    }

    static async Task Try(Func<Task<bool>> f) { try { await f(); } catch { } }
}
