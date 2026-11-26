using System.IO;
using System.Text.Json;

namespace MSharpPro.Services;

/// <summary>Kullanıcı ayarları. %AppData%\MSharpPro\settings.json dosyasında saklanır.</summary>
public sealed class AppSettings
{
    public const double MinScale = 0.8, MaxScale = 1.4;
    public const string DefaultFont = "Segoe UI Variable Text";
    public const string DisplayPrimary = "Primary";
    public const string DisplayAll = "All";

    /// <summary>Çentiğin gösterileceği ekran: "Primary", "All" ya da ekran aygıt adı (ör. \\.\DISPLAY2).</summary>
    public string Display { get; set; } = DisplayPrimary;
    /// <summary>GitHub'a yeni sürüm gelince arka planda indirip kur.</summary>
    public bool AutoUpdate { get; set; } = true;

    public const string ShapeNotch = "Notch";
    public const string ShapeIsland = "Island";
    /// <summary>"Notch": ekranın üstüne yapışık çentik · "Island": iPhone'daki gibi yüzen oval hap.</summary>
    public string Shape { get; set; } = ShapeNotch;
    /// <summary>En son çalışan sürüm: güncellemeden sonra "güncellendi" bildirimi göstermek için.</summary>
    public string LastVersion { get; set; } = "";

    /// <summary>Kompakt çentiğin ortasında saat (iPhone gibi).</summary>
    public bool ShowClock { get; set; } = true;

    public bool WeatherEnabled { get; set; } = true;
    /// <summary>Gösterilen konum adı (ör. "Ankara, Türkiye" ya da "Konumum").</summary>
    public string WeatherCity { get; set; } = "";
    public double? WeatherLat { get; set; }
    public double? WeatherLon { get; set; }

    public string FontFamily { get; set; } = DefaultFont;
    public double Scale { get; set; } = 1.0;
    /// <summary>Çentik yukarı gizli mi (Ctrl+Win ile değişir).</summary>
    public bool Hidden { get; set; }
    public bool HotkeyEnabled { get; set; } = true;
    /// <summary>Bildirimleri sessize al: çentikte açılır bildirim gösterilmez (bildirimler listede birikir).</summary>
    public bool MuteNotifications { get; set; }
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>Windows ile başlat: kayıt defterinde tutulur, dosyada değil.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool StartWithWindows
    {
        get => Startup.IsEnabled;
        set => Startup.Set(value);
    }

    /// <summary>Herhangi bir ayar değişince (UI thread'inde).</summary>
    public event Action? Changed;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSharpPro");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    s.Scale = Math.Clamp(s.Scale, MinScale, MaxScale);
                    if (string.IsNullOrWhiteSpace(s.FontFamily)) s.FontFamily = DefaultFont;
                    if (string.IsNullOrWhiteSpace(s.Display)) s.Display = DisplayPrimary;
                    if (s.Shape != ShapeIsland) s.Shape = ShapeNotch;
                    return s;
                }
            }
        }
        catch { /* bozuk dosya: varsayılanlar */ }
        return new AppSettings();
    }

    /// <summary>Kaydet ve değişikliği bildir.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        Changed?.Invoke();
    }

    public void ResetToDefaults()
    {
        var d = new AppSettings();
        FontFamily = d.FontFamily;
        Scale = d.Scale;
        Hidden = d.Hidden;
        HotkeyEnabled = d.HotkeyEnabled;
        HideInFullscreen = d.HideInFullscreen;
        Display = d.Display;
        AutoUpdate = d.AutoUpdate;
        Shape = d.Shape;
        ShowClock = d.ShowClock;
        WeatherEnabled = d.WeatherEnabled;
        // Seçilen şehir sıfırlanmaz: kullanıcıyı yeniden aratmamak için korunur
        Save();
    }
}
