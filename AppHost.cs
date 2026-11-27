using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MSharpPro.Services;
using static MSharpPro.Services.Loc;

namespace MSharpPro;

/// <summary>Çentikte kısa süre görünen bildirim.</summary>
public sealed record PeekItem(string Title, string Text, string Glyph, ImageSource? Image)
{
    /// <summary>Ses / parlaklık göstergesi: 0..1 düzey çubuğu (yalnızca gösterge için).</summary>
    public double? Level { get; init; }
    /// <summary>Bildirime tıklanınca yapılacak iş (ör. uygulamayı ya da sürüm notlarını aç).</summary>
    public Action? Click { get; init; }
}

/// <summary>
/// Uygulamanın merkezi: servisler (medya, Bluetooth, bildirim, güncelleme), tepsi ikonu ve kısayol
/// burada TEK kez oluşturulur; her ekrandaki çentik penceresi bunları paylaşır.
/// Böylece ikinci bir ekran yalnızca hafif bir pencere ekler, ikinci bir uygulama kopyası değil.
/// </summary>
public sealed class AppHost
{
    private readonly Dispatcher _ui;
    private readonly Dictionary<string, MainWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly CtrlWinHook _hotkey = new();
    private readonly DispatcherTimer _updateTimer;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private string _appliedDisplay = "";

    public AppSettings Settings { get; } = AppSettings.Load();
    public MediaService Media { get; }
    public BluetoothService Bluetooth { get; }
    public NotificationService Notifications { get; } = new();
    public UpdateService Updates { get; } = new();
    public WeatherService Weather { get; }
    public VolumeService Volume { get; }
    public BrightnessService Brightness { get; }
    public PowerService Power { get; } = new();
    public PrivacyService Privacy { get; } = new();
    public TimerService Timer { get; }
    public CalendarService Calendar { get; }
    public ClipboardService Clipboard { get; }
    public ShelfService Shelf { get; } = new();
    public StatsService Stats { get; } = new();
    public LyricsService Lyrics { get; } = new();
    public AudioService Audio { get; }
    public KeepAwakeService KeepAwake { get; } = new();
    public AirplaneService Airplane { get; } = new();
    private readonly GlobalHotkey _micHotkey = new(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT, 0x4D /* M */);
    private readonly DispatcherTimer _weatherTimer;
    private string _weatherKey = "";
    public ObservableCollection<NotifItem> NotifItems { get; } = new();
    public int Unread { get; private set; }
    public bool NotificationAccess { get; private set; } = true;

    public event Action? NotificationsChanged;

    public AppHost(Dispatcher ui)
    {
        _ui = ui;
        Loc.Init(Settings.Language);
        Media = new MediaService(ui);
        Bluetooth = new BluetoothService(ui);
        Weather = new WeatherService(Settings);
        Volume = new VolumeService(ui);
        Brightness = new BrightnessService(ui);
        Timer = new TimerService(Settings);
        Calendar = new CalendarService(Settings);
        Clipboard = new ClipboardService(Settings);
        Audio = new AudioService(ui);
        _weatherTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(30) };
        _weatherTimer.Tick += async (_, _) => await Weather.RefreshAsync();

        // Güncelleme denetimi: açılıştan 30 sn sonra, sonra saatte bir
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(1);
            // Arka plandaki otomatik denetim yalnızca kurulu kopyayı günceller (geliştirme kopyası etkilenmez)
            if (Settings.AutoUpdate) await Updates.CheckAsync(install: Updates.IsInstalled);
        };
    }

    public async Task StartAsync()
    {
        Settings.Changed += OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged += (_, _) => _ui.InvokeAsync(() => RebuildWindows(force: true));

        _hotkey.Triggered += () => _ui.InvokeAsync(ToggleHidden);
        ApplyHotkey();

        _tray = new TrayIcon("M#Pro");
        _tray.LeftClick += OpenSettings;
        _tray.RightClick += ShowMenu;
        UpdateTooltip();

        RebuildWindows(force: true);

        Media.TrackChanged += () =>
            Peek(new PeekItem(Media.Title, Media.Artist.Length > 0 ? Media.Artist : L("Şimdi çalıyor · {0}", Media.AppName),
                "", Media.Art));
        Bluetooth.ConnectionChanged += d =>
            Peek(new PeekItem(d.Name, d.IsConnected ? L("Bağlandı") : L("Bağlantı kesildi"), d.Glyph, null));
        Notifications.Received += n => _ui.InvokeAsync(() =>
        {
            NotifItems.Insert(0, n);
            while (NotifItems.Count > 20) NotifItems.RemoveAt(NotifItems.Count - 1);
            Unread++;
            NotificationsChanged?.Invoke();
            // Sessize alınan uygulamaların bildirimi listede birikir ama çentikte açılmaz
            if (Settings.MutedApps.Contains(n.App, StringComparer.OrdinalIgnoreCase)) return;
            Peek(new PeekItem(n.Title, n.Body.Length > 0 ? n.Body : n.App, "", n.Icon)
            {
                Click = n.AppId.Length > 0 ? () => NotificationService.OpenApp(n) : null
            });
        });
        Updates.Installing += v => Peek(new PeekItem(L("M#Pro güncelleniyor"),
            L("Sürüm {0} kuruluyor, birazdan yeniden açılacak", UpdateService.Fmt(v)), "", null), urgent: true);

        // Ses / parlaklık göstergesi
        Volume.Changed += (level, muted) =>
        {
            if (!Settings.ShowOsd) return;
            int pct = (int)Math.Round(level * 100);
            string glyph = muted || pct == 0 ? "" : pct < 34 ? "" : pct < 67 ? "" : "";
            ShowOsd(new PeekItem(L("Ses"), muted ? L("Sessiz") : $"%{pct}", glyph, null) { Level = muted ? 0 : level });
        };
        Brightness.Changed += level =>
        {
            if (Settings.ShowOsd)
                ShowOsd(new PeekItem(L("Parlaklık"), $"%{level}", "", null) { Level = level / 100.0 });
        };

        // Pil
        Power.PlugChanged += (ac, pct) =>
        {
            if (Settings.ShowBattery)
                Peek(new PeekItem(ac ? L("Şarj oluyor") : L("Şarj aleti çıkarıldı"), L("Pil %{0}", pct),
                    PowerService.Glyph(pct, ac), null), urgent: true);
        };
        Power.Low += pct =>
        {
            if (Settings.ShowBattery)
                Peek(new PeekItem(L("Pil azaldı"), L("%{0} kaldı · şarj aletini takın", pct), PowerService.Glyph(pct, false), null),
                    urgent: true);
        };

        // Zamanlayıcı ve takvim
        Timer.Finished += (title, text) => Peek(new PeekItem(title, text, "", null), urgent: true);
        Calendar.Upcoming += e => Peek(new PeekItem(e.Title,
            L("{0} başlıyor", e.Start.ToString("HH:mm"))
            + (e.JoinUrl.Length > 0 ? " · " + L("katılmak için tıklayın") : e.Location.Length > 0 ? $" · {e.Location}" : ""),
            "", null)
        {
            Click = e.JoinUrl.Length > 0 ? () => OpenUrl(e.JoinUrl) : null
        }, urgent: true);

        // Mikrofon kısayolu (Ctrl + Alt + M)
        _micHotkey.Pressed += ToggleMic;

        CheckJustUpdated();

        Bluetooth.Start();
        Volume.Start();
        Brightness.Start();
        Power.Start();
        Privacy.SetEnabled(Settings.ShowPrivacyDots);
        Audio.Start();
        _micHotkey.SetEnabled(Settings.MicHotkey);
        Clipboard.SetEnabled(Settings.ClipboardHistory);
        await Media.StartAsync();
        _ = Calendar.StartAsync();

        NotificationAccess = await Notifications.StartAsync();
        if (Demo.On)
        {
            foreach (var n in Demo.Notifications()) NotifItems.Add(n);
            Unread = 2;
            // Ekran görüntüsünde kullanıcının gerçek konumu görünmesin (kaydedilmez)
            Settings.WeatherCity = "İstanbul, Türkiye";
            Settings.WeatherLat = 41.01;
            Settings.WeatherLon = 28.979;
        }
        NotificationsChanged?.Invoke();

        if (!Updates.IsStoreVersion) _updateTimer.Start();
        _weatherKey = WeatherKey();
        if (Settings.WeatherEnabled)
        {
            _weatherTimer.Start();
            await Weather.RefreshAsync();
        }
    }

    /// <param name="urgent">Sıradaki bildirimleri beklemeden hemen göster (ör. güncelleme).</param>
    public void Peek(PeekItem item, bool urgent = false)
    {
        // Sessizdeyken çentikte bildirim açılmaz (güncelleme gibi acil olanlar hariç)
        if (!urgent && Settings.MuteNotifications) return;
        foreach (var w in _windows.Values) w.Peek(item, urgent);
    }

    /// <summary>Mikrofonu sessize alır / açar ve çentikte gösterir (kısayol ya da Denetim sekmesinden).</summary>
    public void ToggleMic()
    {
        if (!Audio.ToggleMic())
        {
            ShowOsd(new PeekItem(L("Mikrofon bulunamadı"), L("Bağlı bir mikrofon yok"), "", null));
            return;
        }
        ShowOsd(Audio.MicMuted
            ? new PeekItem(L("Mikrofon kapalı"), L("Tüm uygulamalarda sessiz · Ctrl+Alt+M ile aç"), "", null)
            : new PeekItem(L("Mikrofon açık"), L("Sizi yeniden duyabiliyorlar"), "", null));
    }

    /// <summary>Uçak modunu değiştirir ve sonucu çentikte gösterir.</summary>
    public async Task ToggleAirplaneAsync()
    {
        bool ok = await Airplane.ToggleAsync();
        if (!ok)
        {
            ShowOsd(new PeekItem(L("Uçak modu değiştirilemedi"), L("Ayarlar açılıyor"), "", null));
            OpenUrl("ms-settings:network-airplanemode");
            return;
        }
        ShowOsd(new PeekItem(Airplane.On ? L("Uçak modu açık") : L("Uçak modu kapalı"),
            Airplane.On ? L("Wi-Fi, Bluetooth ve mobil bağlantı kapalı") : L("Kablosuz bağlantılar açık"), "", null));
    }

    /// <summary>Ses / parlaklık göstergesi: bildirim sessizdeyken de görünür, öncekinin yerine hemen geçer.</summary>
    public void ShowOsd(PeekItem item)
    {
        foreach (var w in _windows.Values) w.ShowOsd(item);
    }

    /// <summary>Bir sürümün GitHub'daki yenilikler sayfasını açar.</summary>
    public static void OpenReleaseNotes(string version) =>
        OpenUrl($"https://github.com/{UpdateService.Repo}/releases/tag/v{version}");

    public static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { App.LogError(ex); }
    }

    /// <summary>Uygulamayı yeniden başlatır (ör. dil değişince).</summary>
    public static void Restart()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            // Yeni kopya, bu kopya kapanıp tek-kopya kilidi serbest kalana kadar bekler
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, App.RestartArg) { UseShellExecute = false });
        }
        catch (Exception ex) { App.LogError(ex); return; }
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Güncellemeden sonraki ilk açılışta "M#Pro güncellendi" bildirimi gösterir
    /// ve indirilen kurulum dosyasını geçici klasörden siler (~46 MB yer açar).
    /// </summary>
    private void CheckJustUpdated()
    {
        string current = UpdateService.Fmt(Updates.Current);
        bool updated = Settings.LastVersion.Length > 0 && Settings.LastVersion != current;

        try
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MSharpPro-Update");
            if (System.IO.Directory.Exists(dir))
            {
                foreach (var file in System.IO.Directory.GetFiles(dir, "*.exe"))
                {
                    // Önceki sürümler son sürümü kaydetmiyordu: yeni indirilmiş kurulum dosyası da güncelleme işaretidir
                    var info = new System.IO.FileInfo(file);
                    if (info.Name.Contains(current) && info.LastWriteTime > DateTime.Now.AddMinutes(-15)) updated = true;
                    try { info.Delete(); } catch { }
                }
            }
        }
        catch { }

        if (Settings.LastVersion != current)
        {
            Settings.LastVersion = current;
            Settings.Save();
        }

        if (!updated) return;
        // Pencereler yerleşsin, sonra göster
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            Peek(new PeekItem(L("M#Pro güncellendi"), L("Sürüm {0} yüklendi · yenilikler için tıklayın", current), "", null)
            {
                Click = () => OpenReleaseNotes(current)
            }, urgent: true);
        };
        t.Start();
    }

    // ------------------------------------------------------------------ ekranlar

    /// <summary>Ayardaki ekran seçimine göre çentik pencerelerini açar / kapatır.</summary>
    private void RebuildWindows(bool force = false)
    {
        if (!force && _appliedDisplay == Settings.Display) return;
        _appliedDisplay = Settings.Display;

        var targets = Monitors.Resolve(Settings.Display);
        var wanted = new HashSet<string>(targets.Select(m => m.Device), StringComparer.OrdinalIgnoreCase);

        foreach (var key in _windows.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _windows[key].Close();
            _windows.Remove(key);
        }

        foreach (var m in targets)
        {
            if (_windows.TryGetValue(m.Device, out var existing))
            {
                existing.SetMonitor(m);
                continue;
            }
            var w = new MainWindow(this, m);
            _windows[m.Device] = w;
            w.Show();
        }
        Native.TrimWorkingSet();
    }

    private void OnSettingsChanged()
    {
        ApplyHotkey();
        UpdateTooltip();
        RebuildWindows();
        Privacy.SetEnabled(Settings.ShowPrivacyDots);
        Clipboard.SetEnabled(Settings.ClipboardHistory);
        _micHotkey.SetEnabled(Settings.MicHotkey);
        if (Settings.AutoUpdate && !Updates.IsStoreVersion && !_updateTimer.IsEnabled) _updateTimer.Start();

        // Hava durumu: açıldıysa ya da konum değiştiyse hemen yenile
        string key = WeatherKey();
        if (key != _weatherKey)
        {
            _weatherKey = key;
            if (Settings.WeatherEnabled)
            {
                _weatherTimer.Start();
                _ = Weather.RefreshAsync();
            }
            else
            {
                _weatherTimer.Stop();
            }
        }
    }

    private string WeatherKey() => $"{Settings.WeatherEnabled}|{Settings.WeatherLat}|{Settings.WeatherLon}";

    private void ApplyHotkey()
    {
        if (Settings.HotkeyEnabled) _hotkey.Start(); else _hotkey.Stop();
    }

    private void UpdateTooltip() => _tray?.SetTooltip("M#Pro"
        + (Settings.MuteNotifications ? " · sessiz" : "")
        + (Settings.Hidden ? " · gizli (Ctrl+Win)" : ""));

    // ------------------------------------------------------------------ bildirimler

    public void MarkRead()
    {
        if (Unread == 0) return;
        Unread = 0;
        NotificationsChanged?.Invoke();
    }

    public void ClearNotifications()
    {
        NotifItems.Clear();
        Unread = 0;
        NotificationsChanged?.Invoke();
    }

    // ------------------------------------------------------------------ komutlar

    public void ToggleMute()
    {
        Settings.MuteNotifications = !Settings.MuteNotifications;
        Settings.Save();
    }

    public void ToggleHidden()
    {
        Settings.Hidden = !Settings.Hidden;
        Settings.Save();
    }

    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            Native.TrimWorkingSet();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Hem tepsi ikonunda hem çentikte sağ tıkla açılan menü.</summary>
    public void ShowMenu()
    {
        _tray?.ShowMenu(new[]
        {
            new TrayIcon.MenuItem(L("Ayarlar"), OpenSettings),
            TrayIcon.MenuItem.Separator,
            new TrayIcon.MenuItem(L("Bildirimleri sessize al"), ToggleMute, Settings.MuteNotifications),
            new TrayIcon.MenuItem(L("Çentiği gizle") + "\tCtrl+Win", ToggleHidden, Settings.Hidden),
            new TrayIcon.MenuItem(L("Bilgisayar açılınca başlat"), () =>
            {
                Settings.StartWithWindows = !Settings.StartWithWindows;
                Settings.Save();
            }, Settings.StartWithWindows),
            new TrayIcon.MenuItem(Updates.IsStoreVersion ? L("Microsoft Store'da aç") : L("Güncellemeleri denetle"), () =>
            {
                if (Updates.IsStoreVersion) OpenStorePage();
                else _ = Updates.CheckAsync(install: true);
            }),
            TrayIcon.MenuItem.Separator,
            new TrayIcon.MenuItem(L("Çıkış"), () => Application.Current.Shutdown())
        });
    }

    /// <summary>Store sürümünde uygulamanın Store sayfasını (güncellemeler, puanlama) açar.</summary>
    public static void OpenStorePage()
    {
        try
        {
            string pfn = global::Windows.ApplicationModel.Package.Current.Id.FamilyName;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                $"ms-windows-store://pdp/?PFN={pfn}") { UseShellExecute = true });
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    public void Shutdown()
    {
        _hotkey.Dispose();
        _tray?.Dispose();
        Volume.Dispose();
        Brightness.Dispose();
        Clipboard.Dispose();
        Audio.Dispose();
        _micHotkey.Dispose();
        if (KeepAwake.Active) KeepAwake.Stop();
    }
}
