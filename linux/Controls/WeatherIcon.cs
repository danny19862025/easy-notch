namespace EasyNotch.Controls;

/// <summary>
/// Small vector weather icons: clear (sun/moon), partly, cloud, fog, rain, snow, storm.
/// Drawn in a 1×1 unit box scaled to the control's size, so they stay crisp at any size.
/// </summary>
public sealed class WeatherIcon : Control
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<WeatherIcon, string>(nameof(Kind), "cloud");
    public static readonly StyledProperty<bool> IsNightProperty = AvaloniaProperty.Register<WeatherIcon, bool>(nameof(IsNight));

    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsNight { get => GetValue(IsNightProperty); set => SetValue(IsNightProperty, value); }

    static readonly IBrush Sun = Solid(0xFC, 0xD3, 0x4D);
    static readonly IBrush Moon = Solid(0xE5, 0xE7, 0xEB);
    static readonly IBrush Cloud = Solid(0xE5, 0xE7, 0xEB);
    static readonly IBrush CloudBack = Solid(0x9C, 0xA3, 0xAF);
    static readonly IBrush Rain = Solid(0x7D, 0xD3, 0xFC);
    static readonly IBrush Snow = Solid(0xF5, 0xF5, 0xF7);
    static readonly IBrush Bolt = Solid(0xFC, 0xD3, 0x4D);

    static IBrush Solid(byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));

    static WeatherIcon() => AffectsRender<WeatherIcon>(KindProperty, IsNightProperty);

    static Pen Round(IBrush brush, double thickness) => new(brush, thickness, lineCap: PenLineCap.Round);

    public override void Render(DrawingContext dc)
    {
        double s = Math.Min(Bounds.Width, Bounds.Height);
        if (s <= 0) return;
        using var _ = dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation((Bounds.Width - s) / 2, (Bounds.Height - s) / 2));
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
                var fog = Round(Cloud, line * 1.2);
                dc.DrawLine(fog, new Point(0.18, 0.36), new Point(0.82, 0.36));
                dc.DrawLine(fog, new Point(0.1, 0.52), new Point(0.74, 0.52));
                dc.DrawLine(fog, new Point(0.26, 0.68), new Point(0.9, 0.68));
                break;
            case "rain":
                DrawCloud(dc, 0, -0.12, 1, Cloud);
                var drop = Round(Rain, line);
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
                    c.BeginFigure(new Point(0.54, 0.52), true);
                    c.LineTo(new Point(0.4, 0.74));
                    c.LineTo(new Point(0.5, 0.74));
                    c.LineTo(new Point(0.44, 0.94));
                    c.LineTo(new Point(0.64, 0.66));
                    c.LineTo(new Point(0.53, 0.66));
                    c.LineTo(new Point(0.6, 0.52));
                    c.EndFigure(true);
                }
                dc.DrawGeometry(Bolt, null, bolt);
                break;
            default: // cloud
                DrawCloud(dc, -0.06, -0.08, 0.8, CloudBack);
                DrawCloud(dc, 0.06, 0.06, 0.95, Cloud);
                break;
        }
    }

    static void DrawSun(DrawingContext dc, double cx, double cy, double r, double line)
    {
        var pen = Round(Sun, line);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawLine(pen, new Point(cx + Math.Cos(a) * r * 1.45, cy + Math.Sin(a) * r * 1.45),
                             new Point(cx + Math.Cos(a) * r * 1.9, cy + Math.Sin(a) * r * 1.9));
        }
        dc.DrawEllipse(Sun, null, new Point(cx, cy), r, r);
    }

    static EllipseGeometry Circle(double cx, double cy, double r) => new(new Rect(cx - r, cy - r, 2 * r, 2 * r));

    static void DrawMoon(DrawingContext dc, double cx, double cy, double r)
    {
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude, Circle(cx, cy, r), Circle(cx + r * 0.55, cy - r * 0.35, r * 0.85));
        dc.DrawGeometry(Moon, null, moon);
    }

    /// <summary>A puffy cloud in the unit box, offset by (dx, dy) and scaled around the centre.</summary>
    static void DrawCloud(DrawingContext dc, double dx, double dy, double k, IBrush fill)
    {
        Geometry g = Circle(0.36, 0.6, 0.17);
        g = new CombinedGeometry(GeometryCombineMode.Union, g, Circle(0.55, 0.5, 0.22));
        g = new CombinedGeometry(GeometryCombineMode.Union, g, Circle(0.73, 0.62, 0.15));
        g = new CombinedGeometry(GeometryCombineMode.Union, g, RoundRect(new Rect(0.24, 0.6, 0.56, 0.17), 0.085));
        using var _ = dc.PushTransform(
            Matrix.CreateTranslation(-0.5, -0.5) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(0.5 + dx, 0.5 + dy));
        dc.DrawGeometry(fill, null, g);
    }

    static StreamGeometry RoundRect(Rect r, double radius)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        var size = new Size(radius, radius);
        c.BeginFigure(new Point(r.Left + radius, r.Top), true);
        c.LineTo(new Point(r.Right - radius, r.Top));
        c.ArcTo(new Point(r.Right, r.Top + radius), size, 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(r.Right, r.Bottom - radius));
        c.ArcTo(new Point(r.Right - radius, r.Bottom), size, 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(r.Left + radius, r.Bottom));
        c.ArcTo(new Point(r.Left, r.Bottom - radius), size, 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(r.Left, r.Top + radius));
        c.ArcTo(new Point(r.Left + radius, r.Top), size, 0, false, SweepDirection.Clockwise);
        c.EndFigure(true);
        return g;
    }
}
