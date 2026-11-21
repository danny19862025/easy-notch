using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyNotch.Controls;

/// <summary>
/// The notch: a dark body hanging from the top edge of the screen, with small
/// concave "shoulders" that blend it into the edge and rounded bottom corners.
/// BodyWidth/BodyHeight are animatable; the child is clipped to the body.
/// </summary>
public sealed class NotchBorder : Decorator
{
    public static readonly DependencyProperty BodyWidthProperty = DependencyProperty.Register(
        nameof(BodyWidth), typeof(double), typeof(NotchBorder),
        new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BodyHeightProperty = DependencyProperty.Register(
        nameof(BodyHeight), typeof(double), typeof(NotchBorder),
        new FrameworkPropertyMetadata(34.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BottomRadiusProperty = DependencyProperty.Register(
        nameof(BottomRadius), typeof(double), typeof(NotchBorder),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(NotchBorder),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(NotchBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double BodyWidth { get => (double)GetValue(BodyWidthProperty); set => SetValue(BodyWidthProperty, value); }
    public double BodyHeight { get => (double)GetValue(BodyHeightProperty); set => SetValue(BodyHeightProperty, value); }
    public double BottomRadius { get => (double)GetValue(BottomRadiusProperty); set => SetValue(BottomRadiusProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>Width of each concave shoulder.</summary>
    public const double Shoulder = 10;

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
        double w = BodyWidth, h = BodyHeight, r = Math.Min(BottomRadius, Math.Min(w, h) / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w, 0), false, false);
            c.LineTo(new Point(w, h - r), false, false);
            c.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r, h), false, false);
            c.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = BodyWidth, h = BodyHeight, s = Shoulder;
        if (w <= 0 || h <= 0) return;
        double r = Math.Min(BottomRadius, Math.Min(w, h) / 2);
        double ss = Math.Min(s, h); // shoulders shrink with very thin (tucked) notches

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w + 2 * s, 0), true, true);
            c.ArcTo(new Point(w + s, ss), new Size(s, ss), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(w + s, h - r), true, true);
            c.ArcTo(new Point(w + s - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(s + r, h), true, true);
            c.ArcTo(new Point(s, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(s, ss), true, true);
            c.ArcTo(new Point(0, 0), new Size(s, ss), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(Fill, Stroke is null ? null : new Pen(Stroke, 1), g);
    }
}
