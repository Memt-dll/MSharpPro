using System.IO;
using System.Text.Json;

namespace MSharpPro.Services;

/// <summary>Kullanıcı ayarları. %AppData%\MSharpPro\settings.json dosyasında saklanır.</summary>
public sealed class AppSettings
{
    public const double MinScale = 0.8, MaxScale = 1.4;
    public const string DefaultFont = "Segoe UI Variable Text";

    public string FontFamily { get; set; } = DefaultFont;
    public double Scale { get; set; } = 1.0;
    /// <summary>Çentik yukarı gizli mi (Ctrl+Win ile değişir).</summary>
    public bool Hidden { get; set; }
    public bool HotkeyEnabled { get; set; } = true;
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
        Save();
    }
}
