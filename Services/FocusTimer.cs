using System.IO;
using System.Media;
using System.Windows.Threading;

namespace EasyNotch.Services;

public enum FocusPhase { Idle, Focus, Break }

/// <summary>Pomodoro-style timer: focus, then a break, repeat.</summary>
public sealed class FocusTimer
{
    readonly Settings _settings;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    DateTime _endsAt;
    TimeSpan _pausedRemaining;

    public FocusPhase Phase { get; private set; } = FocusPhase.Idle;
    public bool IsPaused { get; private set; }
    public int CompletedFocusSessions { get; private set; }
    public TimeSpan Total { get; private set; }

    public TimeSpan Remaining => Phase == FocusPhase.Idle ? TimeSpan.FromMinutes(_settings.FocusMinutes)
        : IsPaused ? _pausedRemaining
        : (_endsAt - DateTime.UtcNow) is var r && r > TimeSpan.Zero ? r : TimeSpan.Zero;

    public bool IsRunning => Phase != FocusPhase.Idle && !IsPaused;

    /// <summary>Raised every second while running, and on any state change.</summary>
    public event Action? Changed;
    /// <summary>Raised when a phase runs out; the argument is the phase that just ended.</summary>
    public event Action<FocusPhase>? PhaseEnded;

    public FocusTimer(Settings settings)
    {
        _settings = settings;
        _tick.Tick += (_, _) =>
        {
            if (Remaining <= TimeSpan.Zero) Advance();
            Changed?.Invoke();
        };
    }

    public void StartFocus() => Begin(FocusPhase.Focus, _settings.FocusMinutes);
    public void StartBreak() => Begin(FocusPhase.Break, _settings.BreakMinutes);

    void Begin(FocusPhase phase, int minutes)
    {
        Phase = phase;
        IsPaused = false;
        Total = TimeSpan.FromMinutes(minutes);
        _endsAt = DateTime.UtcNow + Total;
        _tick.Start();
        Noise.Set(phase == FocusPhase.Focus ? _settings.Noise : "off");
        Changed?.Invoke();
    }

    public void TogglePause()
    {
        if (Phase == FocusPhase.Idle) { StartFocus(); return; }
        if (IsPaused) { _endsAt = DateTime.UtcNow + _pausedRemaining; IsPaused = false; _tick.Start(); Noise.Set(Phase == FocusPhase.Focus ? _settings.Noise : "off"); }
        else { _pausedRemaining = Remaining; IsPaused = true; _tick.Stop(); Noise.Set("off"); }
        Changed?.Invoke();
    }

    public void Stop()
    {
        Phase = FocusPhase.Idle;
        IsPaused = false;
        _tick.Stop();
        Noise.Set("off");
        Changed?.Invoke();
    }

    public void ApplyNoiseSetting()
    {
        if (IsRunning && Phase == FocusPhase.Focus) Noise.Set(_settings.Noise);
    }

    void Advance()
    {
        var ended = Phase;
        SystemSounds.Asterisk.Play();
        if (ended == FocusPhase.Focus) { CompletedFocusSessions++; if (_settings.FocusMode == "zen") Stop(); else StartBreak(); }
        else Stop();
        PhaseEnded?.Invoke(ended);
    }
}

/// <summary>Background noise for focus sessions, generated in memory (no audio files shipped).</summary>
public static class Noise
{
    static SoundPlayer? _player;
    static string _current = "off";

    public static readonly string[] Kinds = ["off", "white", "pink", "brown"];

    public static void Set(string kind)
    {
        if (kind == _current) return;
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _current = kind;
        if (kind == "off") return;
        try
        {
            _player = new SoundPlayer(new MemoryStream(MakeWav(kind)));
            _player.PlayLooping();
        }
        catch { _current = "off"; }
    }

    static byte[] MakeWav(string kind)
    {
        const int rate = 22050, seconds = 12, fade = rate / 2;
        int n = rate * seconds;
        var s = new float[n];
        var rng = new Random(7);
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0, brown = 0;
        for (int i = 0; i < n; i++)
        {
            double w = rng.NextDouble() * 2 - 1;
            double v = kind switch
            {
                "pink" => PinkStep(w, ref b0, ref b1, ref b2, ref b3, ref b4, ref b5, ref b6),
                "brown" => (brown = Math.Clamp((brown + 0.02 * w) / 1.02, -1, 1)) * 3.5,
                _ => w,
            };
            s[i] = (float)v;
        }
        // Crossfade the tail into the head so the loop point doesn't click.
        for (int i = 0; i < fade; i++)
        {
            float t = i / (float)fade;
            s[i] = s[i] * t + s[n - fade + i] * (1 - t);
        }
        n -= fade;

        float peak = 0;
        for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(s[i]));
        float gain = 0.22f / Math.Max(peak, 1e-6f);

        using var ms = new MemoryStream();
        using var w2 = new BinaryWriter(ms);
        w2.Write("RIFF"u8); w2.Write(36 + n * 2); w2.Write("WAVE"u8);
        w2.Write("fmt "u8); w2.Write(16); w2.Write((short)1); w2.Write((short)1);
        w2.Write(rate); w2.Write(rate * 2); w2.Write((short)2); w2.Write((short)16);
        w2.Write("data"u8); w2.Write(n * 2);
        for (int i = 0; i < n; i++) w2.Write((short)Math.Clamp(s[i] * gain * short.MaxValue, short.MinValue, short.MaxValue));
        return ms.ToArray();
    }

    // Paul Kellet's pink-noise filter.
    static double PinkStep(double w, ref double b0, ref double b1, ref double b2, ref double b3, ref double b4, ref double b5, ref double b6)
    {
        b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759;
        b2 = 0.96900 * b2 + w * 0.1538520; b3 = 0.86650 * b3 + w * 0.3104856;
        b4 = 0.55000 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.0168980;
        double pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362;
        b6 = w * 0.115926;
        return pink * 0.11;
    }
}
