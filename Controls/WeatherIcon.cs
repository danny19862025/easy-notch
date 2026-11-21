using System.Windows;
using System.Windows.Media;

namespace EasyNotch.Controls;

/// <summary>
/// Small vector weather icons: clear (sun/moon), partly, cloud, fog, rain, snow, storm.
/// Drawn in a 1×1 unit box scaled to the control's size, so they stay crisp at any size.
/// </summary>
public sealed class WeatherIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(WeatherIcon), new FrameworkPropertyMetadata("cloud", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsNightProperty = DependencyProperty.Register(
        nameof(IsNight), typeof(bool), typeof(WeatherIcon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsNight { get => (bool)GetValue(IsNightProperty); set => SetValue(IsNightProperty, value); }

    static readonly Brush Sun = Frozen(0xFC, 0xD3, 0x4D);
    static readonly Brush Moon = Frozen(0xE5, 0xE7, 0xEB);
    static readonly Brush Cloud = Frozen(0xE5, 0xE7, 0xEB);
    static readonly Brush CloudBack = Frozen(0x9C, 0xA3, 0xAF);
    static readonly Brush Rain = Frozen(0x7D, 0xD3, 0xFC);
    static readonly Brush Snow = Frozen(0xF5, 0xF5, 0xF7);
    static readonly Brush Bolt = Frozen(0xFC, 0xD3, 0x4D);

    static SolidColorBrush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        double line = 0.06;

        switch (Kind)
        {
            case "clear":
                if (IsNight) DrawMoon(dc, 0.5, 0.5, 0.3); else DrawSun(dc, 0.5, 0.5, 0.2, line);
                break;
            case "partly":
                if (IsNight) DrawMoon(dc, 0.38, 0.36, 0.2); else DrawSun(dc, 0.36, 0.36, 0.14, line * 0.8);
                DrawCloud(dc, 0.1, 0.12, 0.95, Cloud);
                break;
            case "fog":
                var fog = new Pen(Cloud, line * 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(fog, new Point(0.18, 0.36), new Point(0.82, 0.36));
                dc.DrawLine(fog, new Point(0.1, 0.52), new Point(0.74, 0.52));
                dc.DrawLine(fog, new Point(0.26, 0.68), new Point(0.9, 0.68));
                break;
            case "rain":
                DrawCloud(dc, 0, -0.12, 1, Cloud);
                var drop = new Pen(Rain, line) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                foreach (double x in new[] { 0.34, 0.52, 0.7 })
                    dc.DrawLine(drop, new Point(x, 0.72), new Point(x - 0.06, 0.9));
                break;
            case "snow":
                DrawCloud(dc, 0, -0.12, 1, Cloud);
                foreach (var (x, y) in new[] { (0.32, 0.78), (0.5, 0.86), (0.68, 0.78) })
                    dc.DrawEllipse(Snow, null, new Point(x, y), 0.045, 0.045);
                break;
            case "storm":
                DrawCloud(dc, 0, -0.14, 1, CloudBack);
                var bolt = new StreamGeometry();
                using (var c = bolt.Open())
                {
                    c.BeginFigure(new Point(0.54, 0.52), true, true);
                    c.LineTo(new Point(0.4, 0.74), true, false);
                    c.LineTo(new Point(0.5, 0.74), true, false);
                    c.LineTo(new Point(0.44, 0.94), true, false);
                    c.LineTo(new Point(0.64, 0.66), true, false);
                    c.LineTo(new Point(0.53, 0.66), true, false);
                    c.LineTo(new Point(0.6, 0.52), true, false);
                }
                bolt.Freeze();
                dc.DrawGeometry(Bolt, null, bolt);
                break;
            default: // cloud
                DrawCloud(dc, -0.06, -0.08, 0.8, CloudBack);
                DrawCloud(dc, 0.06, 0.06, 0.95, Cloud);
                break;
        }
        dc.Pop();
        dc.Pop();
    }

    static void DrawSun(DrawingContext dc, double cx, double cy, double r, double line)
    {
        var pen = new Pen(Sun, line) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawLine(pen, new Point(cx + Math.Cos(a) * r * 1.45, cy + Math.Sin(a) * r * 1.45),
                             new Point(cx + Math.Cos(a) * r * 1.9, cy + Math.Sin(a) * r * 1.9));
        }
        dc.DrawEllipse(Sun, null, new Point(cx, cy), r, r);
    }

    static void DrawMoon(DrawingContext dc, double cx, double cy, double r)
    {
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(cx, cy), r, r),
            new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.35), r * 0.85, r * 0.85));
        moon.Freeze();
        dc.DrawGeometry(Moon, null, moon);
    }

    /// <summary>A puffy cloud in the unit box, offset by (dx, dy) and scaled around the centre.</summary>
    static void DrawCloud(DrawingContext dc, double dx, double dy, double k, Brush fill)
    {
        Geometry g = new EllipseGeometry(new Point(0.36, 0.6), 0.17, 0.17);
        g = new CombinedGeometry(GeometryCombineMode.Union, g, new EllipseGeometry(new Point(0.55, 0.5), 0.22, 0.22));
        g = new CombinedGeometry(GeometryCombineMode.Union, g, new EllipseGeometry(new Point(0.73, 0.62), 0.15, 0.15));
        g = new CombinedGeometry(GeometryCombineMode.Union, g, new RectangleGeometry(new Rect(0.24, 0.6, 0.56, 0.17), 0.085, 0.085));
        var t = new TransformGroup();
        t.Children.Add(new ScaleTransform(k, k, 0.5, 0.5));
        t.Children.Add(new TranslateTransform(dx, dy));
        g.Transform = t;
        g.Freeze();
        dc.DrawGeometry(fill, null, g);
    }
}
