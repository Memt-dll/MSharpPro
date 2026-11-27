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
    public const string ThemeSystem = "System";
    public const string ThemeLight = "Light";
    public const string ThemeDark = "Dark";
    /// <summary>Ayarlar penceresinin teması: "System" (Windows'taki açık/koyu mod), "Light" ya da "Dark".</summary>
    public string Theme { get; set; } = ThemeSystem;

    public const string LangSystem = "System";
    /// <summary>Arayüz dili: "System" (Windows dili Türkçe ise Türkçe, değilse İngilizce), "tr" ya da "en".</summary>
    public string Language { get; set; } = LangSystem;

    /// <summary>Çentik sekmelerinin kimlikleri, varsayılan sırayla.</summary>
    public static readonly string[] AllTabs =
        { "media", "lyrics", "bt", "notif", "weather", "timer", "calendar", "clipboard", "shelf", "system" };
    /// <summary>Sekmelerin sırası (eksik olanlar sona eklenir).</summary>
    public List<string> TabOrder { get; set; } = AllTabs.ToList();
    /// <summary>Gizlenen sekmeler.</summary>
    public List<string> HiddenTabs { get; set; } = new();

    /// <summary>Ses / parlaklık değişince çentikte gösterge.</summary>
    public bool ShowOsd { get; set; } = true;
    /// <summary>Şarj aleti takılınca / çıkınca ve pil azalınca çentikte uyarı.</summary>
    public bool ShowBattery { get; set; } = true;
    /// <summary>Mikrofon / kamera kullanılırken çentikte renkli nokta.</summary>
    public bool ShowPrivacyDots { get; set; } = true;
    /// <summary>Yaklaşan takvim etkinliğinden 5 dakika önce uyarı.</summary>
    public bool CalendarReminders { get; set; } = true;
    /// <summary>Kopyalanan metinleri (yalnızca bellekte) listele.</summary>
    public bool ClipboardHistory { get; set; } = true;
    /// <summary>Şarkı sözlerini LRCLIB'den al.</summary>
    public bool LyricsEnabled { get; set; } = true;
    /// <summary>Çentikte açılır bildirimi gösterilmeyen uygulamalar (bildirimleri yine listede birikir).</summary>
    public List<string> MutedApps { get; set; } = new();
    /// <summary>Zamanlayıcı: Pomodoro odak / mola süresi (dakika).</summary>
    public int PomodoroFocus { get; set; } = 25;
    public int PomodoroBreak { get; set; } = 5;

    /// <summary>Görünür sekmeler, kullanıcının sırasıyla.</summary>
    public IEnumerable<string> VisibleTabs => TabOrder.Where(t => !HiddenTabs.Contains(t));

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
                    s.Normalize();
                    return s;
                }
            }
        }
        catch { /* bozuk dosya: varsayılanlar */ }
        return new AppSettings();
    }

    /// <summary>Dosyadan gelen değerleri geçerli aralığa çeker, eksik sekmeleri ekler.</summary>
    private void Normalize()
    {
        if (Theme != ThemeLight && Theme != ThemeDark) Theme = ThemeSystem;
        if (Language != "tr" && Language != "en") Language = LangSystem;
        TabOrder = (TabOrder ?? new()).Where(AllTabs.Contains).Distinct().ToList();
        TabOrder.AddRange(AllTabs.Where(t => !TabOrder.Contains(t)));
        HiddenTabs = (HiddenTabs ?? new()).Where(AllTabs.Contains).Distinct().ToList();
        MutedApps = (MutedApps ?? new()).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
        PomodoroFocus = Math.Clamp(PomodoroFocus, 1, 120);
        PomodoroBreak = Math.Clamp(PomodoroBreak, 1, 60);
    }

    /// <summary>Ayarları bir dosyaya yedekler.</summary>
    public void ExportTo(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Yedek dosyasındaki ayarları yükler (sürüm bilgisi hariç) ve kaydeder. Dosya geçersizse false.</summary>
    public bool ImportFrom(string path)
    {
        AppSettings? s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)); }
        catch { return false; }
        if (s == null) return false;
        s.Normalize();
        foreach (var p in typeof(AppSettings).GetProperties())
        {
            if (!p.CanWrite || p.Name == nameof(LastVersion) || p.Name == nameof(StartWithWindows)) continue;
            p.SetValue(this, p.GetValue(s));
        }
        Scale = Math.Clamp(Scale, MinScale, MaxScale);
        Save();
        return true;
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
        Theme = d.Theme;
        TabOrder = d.TabOrder;
        HiddenTabs = d.HiddenTabs;
        ShowOsd = d.ShowOsd;
        ShowBattery = d.ShowBattery;
        ShowPrivacyDots = d.ShowPrivacyDots;
        CalendarReminders = d.CalendarReminders;
        ClipboardHistory = d.ClipboardHistory;
        LyricsEnabled = d.LyricsEnabled;
        MutedApps = d.MutedApps;
        PomodoroFocus = d.PomodoroFocus;
        PomodoroBreak = d.PomodoroBreak;
        // Dil sıfırlanmaz: yeniden başlatma gerektirir
        // Seçilen şehir sıfırlanmaz: kullanıcıyı yeniden aratmamak için korunur
        Save();
    }
}
