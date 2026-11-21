using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNotch.Controls;

/// <summary>
/// Rotary volume knob: a ring of tick marks over a 270° sweep (lit up to the value),
/// a dark dial, and an indicator line. Drag up/down or scroll to change.
/// </summary>
public sealed class Knob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender, null, (_, v) => Math.Clamp((double)v, 0, 1)));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Raised when the user changes the value (not when set from code).</summary>
    public event Action<double>? UserChanged;

    const double Sweep = 270, Start = 135; // degrees, measured clockwise from +x
    const int Ticks = 27;

    static readonly Brush TickOn = Frozen(Color.FromRgb(0xF5, 0xF5, 0xF7));
    static readonly Brush TickOff = Frozen(Color.FromRgb(0x44, 0x44, 0x4A));
    static readonly Brush Dial = Frozen(Color.FromRgb(0x24, 0x24, 0x28));
    static readonly Brush DialEdge = Frozen(Color.FromRgb(0x30, 0x30, 0x36));

    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public Knob()
    {
        Cursor = Cursors.SizeNS;
        Focusable = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double outer = size / 2, inner = outer - size * 0.09, dial = outer - size * 0.17;

        // Hit area: the whole square, so dragging anywhere on the knob works.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var onPen = new Pen(TickOn, Math.Max(1.5, size * 0.018)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var offPen = new Pen(TickOff, onPen.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < Ticks; i++)
        {
            double t = i / (double)(Ticks - 1);
            double a = (Start + Sweep * t) * Math.PI / 180;
            double r1 = i % 13 == 0 ? inner - size * 0.03 : inner; // longer ticks at min, middle, max
            var p1 = new Point(c.X + Math.Cos(a) * r1, c.Y + Math.Sin(a) * r1);
            var p2 = new Point(c.X + Math.Cos(a) * outer, c.Y + Math.Sin(a) * outer);
            dc.DrawLine(t <= Value + 1e-6 ? onPen : offPen, p1, p2);
        }

        dc.DrawEllipse(Dial, new Pen(DialEdge, 1), c, dial, dial);

        double va = (Start + Sweep * Value) * Math.PI / 180;
        var ind = new Pen(TickOn, Math.Max(2, size * 0.03)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(ind,
            new Point(c.X + Math.Cos(va) * dial * 0.45, c.Y + Math.Sin(va) * dial * 0.45),
            new Point(c.X + Math.Cos(va) * dial * 0.82, c.Y + Math.Sin(va) * dial * 0.82));
    }

    Point _dragFrom;
    double _dragStartValue;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragFrom = e.GetPosition(this);
        _dragStartValue = Value;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        double dy = _dragFrom.Y - e.GetPosition(this).Y;
        Set(_dragStartValue + dy / 150);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Set(Value + (e.Delta > 0 ? 0.02 : -0.02));
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
