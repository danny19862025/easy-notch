using Avalonia.Threading;
using System.Diagnostics;

namespace EasyNotch;

/// <summary>
/// Tiny tween engine (the Avalonia counterpart of WPF's BeginAnimation for plain doubles):
/// one timer drives every running tween; starting a tween on the same target and key replaces it.
/// Eases out (cubic) and jumps straight to the end when animations are off.
/// </summary>
internal static class Anim
{
    public static bool Enabled { get; set; } = true;

    sealed class Tween
    {
        public required Action<double> Set;
        public double From, To, Ms, Start;
        public Func<double, double> Ease = CubicOut;
        public Action? Done;
    }

    static readonly Dictionary<(object, string), Tween> Active = [];
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly DispatcherTimer Timer = new(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Step());

    public static double CubicOut(double p) => 1 - Math.Pow(1 - p, 3);

    public static void To(object target, string key, double from, double to, double ms, Action<double> set, Action? done = null, Func<double, double>? ease = null)
    {
        Active.Remove((target, key));
        if (!Enabled || ms <= 0 || Math.Abs(from - to) < 1e-9)
        {
            set(to);
            done?.Invoke();
            return;
        }
        Active[(target, key)] = new Tween { Set = set, From = from, To = to, Ms = ms, Start = Clock.Elapsed.TotalMilliseconds, Done = done, Ease = ease ?? CubicOut };
        set(from);
        if (!Timer.IsEnabled) Timer.Start();
    }

    public static void Stop(object target, string key) => Active.Remove((target, key));

    /// <summary>Fades a control, making it visible first, and optionally hiding it once fully transparent.</summary>
    public static void Fade(Visual el, double to, bool hideAtEnd = false)
    {
        if (to > 0) el.IsVisible = true;
        To(el, "opacity", el.Opacity, to, 180, v => el.Opacity = v,
            hideAtEnd ? () => { if (el.Opacity == 0) el.IsVisible = false; } : null);
    }

    static void Step()
    {
        double now = Clock.Elapsed.TotalMilliseconds;
        foreach (var (key, t) in Active.ToList())
        {
            if (!Active.TryGetValue(key, out var current) || current != t) continue;
            double p = Math.Clamp((now - t.Start) / t.Ms, 0, 1);
            t.Set(t.From + (t.To - t.From) * t.Ease(p));
            if (p >= 1)
            {
                Active.Remove(key);
                t.Done?.Invoke();
            }
        }
        if (Active.Count == 0) Timer.Stop();
    }
}

/// <summary>Application resources by key (works for controls not yet in a window).</summary>
internal static class Res
{
    public static IBrush Brush(string key) =>
        Application.Current!.TryGetResource(key, null, out var v) && v is IBrush b ? b : Brushes.White;

    public static FontFamily Font(string key) =>
        Application.Current!.TryGetResource(key, null, out var v) && v is FontFamily f ? f : FontFamily.Default;

    public static FontFamily Icons => Font("Icons");
}
