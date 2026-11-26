using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MSharpPro.Services;

namespace MSharpPro;

/// <summary>Çentikte kısa süre görünen bildirim.</summary>
public sealed record PeekItem(string Title, string Text, string Glyph, ImageSource? Image);

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
    private readonly DispatcherTimer _weatherTimer;
    private string _weatherKey = "";
    public ObservableCollection<NotifItem> NotifItems { get; } = new();
    public int Unread { get; private set; }
    public bool NotificationAccess { get; private set; } = true;

    public event Action? NotificationsChanged;

    public AppHost(Dispatcher ui)
    {
        _ui = ui;
        Media = new MediaService(ui);
        Bluetooth = new BluetoothService(ui);
        Weather = new WeatherService(Settings);
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
            Peek(new PeekItem(Media.Title, Media.Artist.Length > 0 ? Media.Artist : $"Şimdi çalıyor · {Media.AppName}",
                "", Media.Art));
        Bluetooth.ConnectionChanged += d =>
            Peek(new PeekItem(d.Name, d.IsConnected ? "Bağlandı" : "Bağlantı kesildi", d.Glyph, null));
        Notifications.Received += n => _ui.InvokeAsync(() =>
        {
            NotifItems.Insert(0, n);
            while (NotifItems.Count > 20) NotifItems.RemoveAt(NotifItems.Count - 1);
            Unread++;
            NotificationsChanged?.Invoke();
            Peek(new PeekItem(n.Title, n.Body.Length > 0 ? n.Body : n.App, "", n.Icon));
        });
        Updates.Installing += v => Peek(new PeekItem("M#Pro güncelleniyor",
            $"Sürüm {UpdateService.Fmt(v)} kuruluyor, birazdan yeniden açılacak", "", null), urgent: true);

        CheckJustUpdated();

        Bluetooth.Start();
        await Media.StartAsync();

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
        foreach (var w in _windows.Values) w.Peek(item, urgent);
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
            Peek(new PeekItem("M#Pro güncellendi", $"Sürüm {current} yüklendi · Yenilikler GitHub'da", "", null), urgent: true);
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

    private void UpdateTooltip() => _tray?.SetTooltip(Settings.Hidden ? "M#Pro (gizli · Ctrl+Win)" : "M#Pro");

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
            new TrayIcon.MenuItem("Ayarlar", OpenSettings),
            TrayIcon.MenuItem.Separator,
            new TrayIcon.MenuItem("Çentiği gizle\tCtrl+Win", ToggleHidden, Settings.Hidden),
            new TrayIcon.MenuItem("Bilgisayar açılınca başlat", () =>
            {
                Settings.StartWithWindows = !Settings.StartWithWindows;
                Settings.Save();
            }, Settings.StartWithWindows),
            new TrayIcon.MenuItem(Updates.IsStoreVersion ? "Microsoft Store'da aç" : "Güncellemeleri denetle", () =>
            {
                if (Updates.IsStoreVersion) OpenStorePage();
                else _ = Updates.CheckAsync(install: true);
            }),
            TrayIcon.MenuItem.Separator,
            new TrayIcon.MenuItem("Çıkış", () => Application.Current.Shutdown())
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
    }
}
