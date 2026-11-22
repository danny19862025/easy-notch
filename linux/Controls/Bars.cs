using Avalonia.Input;

namespace EasyNotch.Controls;

/// <summary>
/// Vertical mixer fader, 0–100: tick marks either side, a thin track lit up to the value
/// and a flat bar thumb. Click or drag to set; scroll to nudge.
/// </summary>
public sealed class Fader : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Fader, double>(nameof(Value), 0, coerce: (_, v) => Math.Clamp(v, 0, 100));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Raised when the user changes the value (not when set from code).</summary>
    public event Action<double>? UserChanged;
    public bool IsDragging { get; private set; }

    const double Pad = 6; // room for the thumb at both ends

    static Fader() => AffectsRender<Fader>(ValueProperty);

    public Fader()
    {
        Width = 40;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height, cx = w / 2;
        double top = Pad, bottom = h - Pad, len = Math.Max(1, bottom - top);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        var tick = new Pen(Res.Brush("Faint"), 1);
        for (int i = 0; i <= 10; i++)
        {
            double y = Math.Round(bottom - len * i / 10) + 0.5;
            dc.DrawLine(tick, new Point(cx - 14, y), new Point(cx - 6, y));
            dc.DrawLine(tick, new Point(cx + 6, y), new Point(cx + 14, y));
        }
        double vy = bottom - len * Value / 100;
        dc.DrawRectangle(Res.Brush("Line"), null, new Rect(cx - 1.5, top, 3, len), 1.5, 1.5);
        dc.DrawRectangle(Res.Brush("Sub"), null, new Rect(cx - 1.5, vy, 3, bottom - vy), 1.5, 1.5);
        dc.DrawRectangle(Res.Brush("Fg"), null, new Rect(cx - 12, vy - 1.5, 24, 3), 1.5, 1.5);
    }

    void SetFrom(Point p)
    {
        double len = Math.Max(1, Bounds.Height - 2 * Pad);
        Set((Bounds.Height - Pad - p.Y) / len * 100);
    }

    void Set(double v)
    {
        v = Math.Round(Math.Clamp(v, 0, 100));
        if (Math.Abs(v - Value) < 1e-9) return;
        Value = v;
        UserChanged?.Invoke(v);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        IsDragging = true;
        e.Pointer.Capture(this);
        SetFrom(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e) { if (IsDragging) SetFrom(e.GetPosition(this)); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!IsDragging) return;
        IsDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => IsDragging = false;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        Set(Value + (e.Delta.Y > 0 ? 3 : -3));
        e.Handled = true;
    }
}

/// <summary>Thin horizontal seek bar. The thumb shows on hover; the new position is reported on release.</summary>
public sealed class SeekBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SeekBar, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SeekBar, double>(nameof(Maximum), 1);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>Raised with the chosen position when the user lets go.</summary>
    public event Action<double>? Committed;
    public bool IsSeeking { get; private set; }
    bool _hover;

    static SeekBar() => AffectsRender<SeekBar>(ValueProperty, MaximumProperty, IsEnabledProperty);

    public SeekBar()
    {
        Height = 16;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height, cy = h / 2;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        double f = Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;
        dc.DrawRectangle(Res.Brush("Line"), null, new Rect(0, cy - 2, w, 4), 2, 2);
        dc.DrawRectangle(Res.Brush("Sub"), null, new Rect(0, cy - 2, w * f, 4), 2, 2);
        if ((_hover || IsSeeking) && IsEnabled)
            dc.DrawEllipse(Res.Brush("Fg"), null, new Point(Math.Clamp(w * f, 6, Math.Max(6, w - 6)), cy), 6, 6);
    }

    void SetFrom(Point p) => Value = Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1) * Maximum;

    protected override void OnPointerEntered(PointerEventArgs e) { _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { _hover = false; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        IsSeeking = true;
        e.Pointer.Capture(this);
        SetFrom(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e) { if (IsSeeking) SetFrom(e.GetPosition(this)); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!IsSeeking) return;
        IsSeeking = false;
        e.Pointer.Capture(null);
        Committed?.Invoke(Value);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => IsSeeking = false;
}
