using System.Windows;
using System.Windows.Media;

namespace MSharpPro.Services;

/// <summary>
/// Store ekran görüntüleri için demo modu: MSHARPPRO_DEMO=1 ortam değişkeniyle açılır.
/// Gerçek müzik, cihaz ve bildirimler yerine örnek veriler gösterilir (kişisel veri ve telifli kapak çıkmasın diye).
/// Normal kullanımda hiçbir etkisi yoktur.
/// </summary>
internal static class Demo
{
    public static bool On { get; } = Environment.GetEnvironmentVariable("MSHARPPRO_DEMO") == "1";

    public const string Title = "Neon Nights";
    public const string Artist = "Lumen Drift";
    public const string App = "Spotify";
    public static readonly TimeSpan Position = TimeSpan.FromSeconds(84);
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(232);

    /// <summary>Telifsiz, kodla çizilmiş örnek albüm kapağı.</summary>
    public static ImageSource Cover()
    {
        var g = new DrawingGroup();
        var bg = new LinearGradientBrush(Color.FromRgb(0xFF, 0x4E, 0x8A), Color.FromRgb(0x5B, 0x3C, 0xFF), 45);
        g.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, 200, 200))));
        var sun = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE0, 0x7A), Color.FromRgb(0xFF, 0x8A, 0x4C), 90);
        g.Children.Add(new GeometryDrawing(sun, null, new EllipseGeometry(new Point(100, 112), 52, 52)));
        var lines = new SolidColorBrush(Color.FromArgb(0xCC, 0x3A, 0x1F, 0x8F));
        for (int i = 0; i < 5; i++)
            g.Children.Add(new GeometryDrawing(lines, null, new RectangleGeometry(new Rect(40, 118 + i * 10, 120, 3 + i))));
        var ground = new SolidColorBrush(Color.FromRgb(0x2A, 0x16, 0x6B));
        g.Children.Add(new GeometryDrawing(ground, null, new RectangleGeometry(new Rect(0, 164, 200, 36))));
        g.Freeze();
        var img = new DrawingImage(g);
        img.Freeze();
        return img;
    }

    public static IEnumerable<NotifItem> Notifications() => new[]
    {
        new NotifItem { App = "Takvim", Title = "Ekip toplantısı", Body = "15:30 · Toplantı odası 2", Time = "15:12" },
        new NotifItem { App = "Mesajlar", Title = "Ayşe", Body = "Akşam yemeğe çıkalım mı? 🍝", Time = "14:58" },
        new NotifItem { App = "E-posta", Title = "Haftalık rapor hazır", Body = "Raporu inceleyip onaylayabilirsiniz", Time = "14:20" },
    };
}
