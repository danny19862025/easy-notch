using Avalonia.Input;

namespace EasyNotch.Controls;

/// <summary>
/// Rotary volume knob: a ring of tick marks over a 270° sweep (lit up to the value),
/// a dark dial, and an indicator line. Drag up/down or scroll to change.
/// </summary>
public sealed class Knob : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Knob, double>(nameof(Value), 0.5, coerce: (_, v) => Math.Clamp(v, 0, 1));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Raised when the user changes the value (not when set from code).</summary>
    public event Action<double>? UserChanged;

    public bool IsDragging { get; private set; }

    const double Sweep = 270, Start = 135; // degrees, clockwise from +x
    const int Ticks = 27;

    static readonly IBrush TickOn = new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7));
    static readonly IBrush TickOff = new ImmutableSolidColorBrush(Color.FromRgb(0x44, 0x44, 0x4A));
    static readonly IBrush Dial = new ImmutableSolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28));
    static readonly IBrush DialEdge = new ImmutableSolidColorBrush(Color.FromRgb(0x30, 0x30, 0x36));

    static Knob() => AffectsRender<Knob>(ValueProperty);

    public Knob()
    {
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        Focusable = false;
    }

    public override void Render(DrawingContext dc)
    {
        double width = Bounds.Width, height = Bounds.Height, size = Math.Min(width, height);
        var c = new Point(width / 2, height / 2);
        double outer = size / 2, inner = outer - size * 0.09, dial = outer - size * 0.17;

        // Hit area: the whole square, so dragging anywhere on the knob works.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        double thickness = Math.Max(1.5, size * 0.018);
        var onPen = new Pen(TickOn, thickness, lineCap: PenLineCap.Round);
        var offPen = new Pen(TickOff, thickness, lineCap: PenLineCap.Round);
        for (int i = 0; i < Ticks; i++)
        {
            double t = i / (double)(Ticks - 1);
            double a = (Start + Sweep * t) * Math.PI / 180;
            double r1 = i % 13 == 0 ? inner - size * 0.03 : inner; // longer ticks at min, middle, max
            dc.DrawLine(t <= Value + 1e-6 ? onPen : offPen,
                new Point(c.X + Math.Cos(a) * r1, c.Y + Math.Sin(a) * r1),
                new Point(c.X + Math.Cos(a) * outer, c.Y + Math.Sin(a) * outer));
        }

        dc.DrawEllipse(Dial, new Pen(DialEdge, 1), c, dial, dial);

        double va = (Start + Sweep * Value) * Math.PI / 180;
        var ind = new Pen(TickOn, Math.Max(2, size * 0.03), lineCap: PenLineCap.Round);
        dc.DrawLine(ind,
            new Point(c.X + Math.Cos(va) * dial * 0.45, c.Y + Math.Sin(va) * dial * 0.45),
            new Point(c.X + Math.Cos(va) * dial * 0.82, c.Y + Math.Sin(va) * dial * 0.82));
    }

    Point _dragFrom;
    double _dragStartValue;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragFrom = e.GetPosition(this);
        _dragStartValue = Value;
        IsDragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!IsDragging) return;
        Set(_dragStartValue + (_dragFrom.Y - e.GetPosition(this).Y) / 150);
    }

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
        Set(Value + (e.Delta.Y > 0 ? 0.02 : -0.02));
        e.Handled = true;
    }

    void Set(double v)
    {
        v = Math.Round(Math.Clamp(v, 0, 1), 2);
        if (Math.Abs(v - Value) < 1e-9) return;
        Value = v;
        UserChanged?.Invoke(v);
    }
}
