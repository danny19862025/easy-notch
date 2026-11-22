namespace EasyNotch.Controls;

/// <summary>
/// The notch: a dark body hanging from the top edge of the screen, with small
/// concave "shoulders" that blend it into the edge and rounded bottom corners.
/// BodyWidth/BodyHeight are animated from code; the child is clipped to the body.
/// </summary>
public sealed class NotchBorder : Decorator
{
    public static readonly StyledProperty<double> BodyWidthProperty = AvaloniaProperty.Register<NotchBorder, double>(nameof(BodyWidth), 200);
    public static readonly StyledProperty<double> BodyHeightProperty = AvaloniaProperty.Register<NotchBorder, double>(nameof(BodyHeight), 34);
    public static readonly StyledProperty<double> BottomRadiusProperty = AvaloniaProperty.Register<NotchBorder, double>(nameof(BottomRadius), 16);
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<NotchBorder, IBrush?>(nameof(Fill), Brushes.Black);
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<NotchBorder, IBrush?>(nameof(Stroke));

    public double BodyWidth { get => GetValue(BodyWidthProperty); set => SetValue(BodyWidthProperty, value); }
    public double BodyHeight { get => GetValue(BodyHeightProperty); set => SetValue(BodyHeightProperty, value); }
    public double BottomRadius { get => GetValue(BottomRadiusProperty); set => SetValue(BottomRadiusProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>Width of each concave shoulder.</summary>
    public const double Shoulder = 10;

    static NotchBorder()
    {
        AffectsMeasure<NotchBorder>(BodyWidthProperty, BodyHeightProperty);
        AffectsArrange<NotchBorder>(BottomRadiusProperty);
        AffectsRender<NotchBorder>(BodyWidthProperty, BodyHeightProperty, BottomRadiusProperty, FillProperty, StrokeProperty);
    }

    protected override Size MeasureOverride(Size available)
    {
        Child?.Measure(new Size(BodyWidth, BodyHeight));
        return new Size(BodyWidth + 2 * Shoulder, BodyHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Child is not null)
        {
            Child.Arrange(new Rect(Shoulder, 0, BodyWidth, BodyHeight));
            Child.Clip = BodyClip();
        }
        return new Size(BodyWidth + 2 * Shoulder, BodyHeight);
    }

    Geometry BodyClip()
    {
        double w = BodyWidth, h = BodyHeight, r = Math.Max(0, Math.Min(BottomRadius, Math.Min(w, h) / 2));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true);
            c.LineTo(new Point(w, 0));
            c.LineTo(new Point(w, h - r));
            c.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(r, h));
            c.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.EndFigure(true);
        }
        return g;
    }

    public override void Render(DrawingContext dc)
    {
        double w = BodyWidth, h = BodyHeight, s = Shoulder;
        if (w <= 0 || h <= 0) return;
        double r = Math.Max(0, Math.Min(BottomRadius, Math.Min(w, h) / 2));
        double ss = Math.Min(s, h); // shoulders shrink with very thin (tucked) notches

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true);
            c.LineTo(new Point(w + 2 * s, 0));
            c.ArcTo(new Point(w + s, ss), new Size(s, ss), 0, false, SweepDirection.CounterClockwise);
            c.LineTo(new Point(w + s, h - r));
            c.ArcTo(new Point(w + s - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(s + r, h));
            c.ArcTo(new Point(s, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(s, ss));
            c.ArcTo(new Point(0, 0), new Size(s, ss), 0, false, SweepDirection.CounterClockwise);
            c.EndFigure(true);
        }
        dc.DrawGeometry(Fill, Stroke is null ? null : new Pen(Stroke, 1), g);
    }
}
