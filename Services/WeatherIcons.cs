using System.Windows;
using System.Windows.Media;

namespace MSharpPro.Services;

public enum WeatherKind { Sun, Moon, PartlySun, PartlyMoon, Cloud, Fog, Drizzle, Rain, Snow, Thunder }

/// <summary>
/// Vektörel, renkli hava durumu simgeleri. Her simge bir kez çizilip dondurulur (Freeze)
/// ve tüm pencerelerde paylaşılır; ek RAM maliyeti yok denecek kadar azdır.
/// </summary>
public static class WeatherIcons
{
    private static readonly Dictionary<WeatherKind, DrawingImage> Cache = new();

    public static DrawingImage Get(WeatherKind kind)
    {
        if (Cache.TryGetValue(kind, out var img)) return img;
        var g = new DrawingGroup();
        // Sabit 64x64 tuval: simgeler kayarken boyut zıplamasın
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64))));

        switch (kind)
        {
            case WeatherKind.Sun:
                AddSun(g, 32, 32, 1);
                break;
            case WeatherKind.Moon:
                AddMoon(g, 32, 32, 1);
                break;
            case WeatherKind.PartlySun:
                AddSun(g, 24, 24, 0.72);
                AddCloud(g, 6, 10, light: true);
                break;
            case WeatherKind.PartlyMoon:
                AddMoon(g, 24, 22, 0.7);
                AddCloud(g, 6, 10, light: true);
                break;
            case WeatherKind.Cloud:
                AddCloud(g, 0, 2, light: true);
                break;
            case WeatherKind.Fog:
                AddCloud(g, 0, -6, light: true);
                var fog = Pen(Color.FromRgb(0xC9, 0xD3, 0xE3), 3.5);
                g.Children.Add(Line(fog, 14, 50, 50, 50));
                g.Children.Add(Line(fog, 20, 57, 44, 57));
                break;
            case WeatherKind.Drizzle:
                AddCloud(g, 0, -6, light: true);
                var dot = Brush(Color.FromRgb(0x5A, 0xB8, 0xFF));
                foreach (var x in new[] { 22.0, 32, 42 })
                    g.Children.Add(new GeometryDrawing(dot, null, new EllipseGeometry(new Point(x - 2, 54), 2.4, 2.4)));
                break;
            case WeatherKind.Rain:
                AddCloud(g, 0, -6, light: false);
                var rain = Pen(Color.FromRgb(0x5A, 0xB8, 0xFF), 3.4);
                foreach (var x in new[] { 22.0, 32, 42 })
                    g.Children.Add(Line(rain, x, 48, x - 4, 58));
                break;
            case WeatherKind.Snow:
                AddCloud(g, 0, -6, light: true);
                var flake = Brush(Colors.White);
                foreach (var (x, y) in new[] { (21.0, 52.0), (32.0, 57.0), (43.0, 52.0) })
                    g.Children.Add(new GeometryDrawing(flake, null, new EllipseGeometry(new Point(x, y), 3, 3)));
                break;
            case WeatherKind.Thunder:
                AddCloud(g, 0, -6, light: false);
                var bolt = new PathGeometry(new[]
                {
                    new PathFigure(new Point(34, 44), new PathSegment[]
                    {
                        new PolyLineSegment(new[] { new Point(25, 55), new Point(31, 55), new Point(27, 63), new Point(39, 50), new Point(33, 50), new Point(37, 44) }, true)
                    }, true)
                });
                g.Children.Add(new GeometryDrawing(Brush(Color.FromRgb(0xFF, 0xD2, 0x4A)), null, bolt));
                break;
        }

        g.Freeze();
        img = new DrawingImage(g);
        img.Freeze();
        Cache[kind] = img;
        return img;
    }

    private static void AddSun(DrawingGroup g, double cx, double cy, double s)
    {
        var rays = Pen(Color.FromRgb(0xFF, 0xC4, 0x3D), 3.6 * s);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            g.Children.Add(Line(rays,
                cx + Math.Cos(a) * 18 * s, cy + Math.Sin(a) * 18 * s,
                cx + Math.Cos(a) * 25 * s, cy + Math.Sin(a) * 25 * s));
        }
        var core = new RadialGradientBrush(Color.FromRgb(0xFF, 0xE0, 0x6B), Color.FromRgb(0xFF, 0xA9, 0x2E));
        g.Children.Add(new GeometryDrawing(core, null, new EllipseGeometry(new Point(cx, cy), 12.5 * s, 12.5 * s)));
    }

    private static void AddMoon(DrawingGroup g, double cx, double cy, double s)
    {
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(cx, cy), 17 * s, 17 * s),
            new EllipseGeometry(new Point(cx + 9 * s, cy - 7 * s), 14 * s, 14 * s));
        var fill = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF1, 0xD8), Color.FromRgb(0xD9, 0xD2, 0xA6), 45);
        g.Children.Add(new GeometryDrawing(fill, null, moon));
    }

    private static void AddCloud(DrawingGroup g, double dx, double dy, bool light)
    {
        var cloud = new GeometryGroup { FillRule = FillRule.Nonzero };
        cloud.Children.Add(new EllipseGeometry(new Point(23 + dx, 38 + dy), 11, 11));
        cloud.Children.Add(new EllipseGeometry(new Point(35 + dx, 31 + dy), 14, 14));
        cloud.Children.Add(new EllipseGeometry(new Point(46 + dx, 39 + dy), 10, 10));
        cloud.Children.Add(new RectangleGeometry(new Rect(16 + dx, 36 + dy, 36, 13), 6.5, 6.5));
        var fill = light
            ? new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xC7, 0xD3, 0xE6), 90)
            : new LinearGradientBrush(Color.FromRgb(0xA9, 0xB4, 0xC8), Color.FromRgb(0x6F, 0x7B, 0x90), 90);
        g.Children.Add(new GeometryDrawing(fill, null, cloud));
    }

    private static Pen Pen(Color c, double w) => new(Brush(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    private static SolidColorBrush Brush(Color c) => new(c);
    private static GeometryDrawing Line(Pen p, double x1, double y1, double x2, double y2)
        => new(null, p, new LineGeometry(new Point(x1, y1), new Point(x2, y2)));

    /// <summary>Hava durumuna göre sol kartın arka plan renkleri (iPhone hava durumu gibi).</summary>
    public static Brush CardBrush(WeatherKind kind)
    {
        (Color a, Color b) = kind switch
        {
            WeatherKind.Sun or WeatherKind.PartlySun => (Color.FromRgb(0x2D, 0x7D, 0xD8), Color.FromRgb(0x5C, 0xB6, 0xF0)),
            WeatherKind.Moon or WeatherKind.PartlyMoon => (Color.FromRgb(0x14, 0x1C, 0x3A), Color.FromRgb(0x2E, 0x3F, 0x6E)),
            WeatherKind.Rain or WeatherKind.Drizzle => (Color.FromRgb(0x27, 0x3A, 0x52), Color.FromRgb(0x46, 0x6A, 0x8F)),
            WeatherKind.Thunder => (Color.FromRgb(0x1E, 0x22, 0x36), Color.FromRgb(0x43, 0x46, 0x6B)),
            WeatherKind.Snow => (Color.FromRgb(0x4F, 0x6C, 0x8F), Color.FromRgb(0x86, 0xA3, 0xC4)),
            WeatherKind.Fog => (Color.FromRgb(0x4A, 0x52, 0x60), Color.FromRgb(0x6E, 0x77, 0x86)),
            _ => (Color.FromRgb(0x3C, 0x4B, 0x63), Color.FromRgb(0x5D, 0x6E, 0x88))
        };
        var brush = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }
}
