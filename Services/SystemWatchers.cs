using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MSharpPro.Services;

/// <summary>
/// Dizüstü ekran parlaklığı değişince haber verir (WMI olayı; masaüstü monitörlerde olay gelmez).
/// </summary>
public sealed class BrightnessService : IDisposable
{
    private readonly Dispatcher _ui;
    private ManagementEventWatcher? _watcher;

    /// <summary>Parlaklık 0..100 (UI thread).</summary>
    public event Action<int>? Changed;

    public BrightnessService(Dispatcher ui) => _ui = ui;

    public void Start()
    {
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            _watcher = new ManagementEventWatcher(scope, new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                try
                {
                    int level = Convert.ToInt32(e.NewEvent["Brightness"]);
                    _ui.InvokeAsync(() => Changed?.Invoke(level));
                }
                catch { }
            };
            _watcher.Start();
        }
        catch
        {
            // Parlaklık denetimi olmayan (harici monitörlü) bilgisayarlar: sessizce devre dışı
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); _watcher?.Dispose(); } catch { }
    }
}

/// <summary>Pil: şarj aleti takıldı / çıkarıldı, pil azaldı.</summary>
public sealed class PowerService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    private readonly DispatcherTimer _lowTimer;
    private bool? _onAc;
    private int _warnedAt = 101;

    public bool HasBattery { get; private set; }
    public int Percent { get; private set; }
    public bool OnAc => _onAc == true;

    /// <summary>Şarj durumu değişti: (şarjda mı, yüzde).</summary>
    public event Action<bool, int>? PlugChanged;
    /// <summary>Pil azaldı: yüzde.</summary>
    public event Action<int>? Low;

    public PowerService()
    {
        _lowTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _lowTimer.Tick += (_, _) => Read(notify: true);
    }

    public void Start()
    {
        Read(notify: false);
        if (!HasBattery) return;
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.StatusChange)
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => Read(notify: true));
        };
        _lowTimer.Start();
    }

    private void Read(bool notify)
    {
        if (!GetSystemPowerStatus(out var s)) return;
        // 128 = pil yok, 255 = bilinmiyor
        HasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255 && s.BatteryLifePercent <= 100;
        if (!HasBattery) return;
        Percent = s.BatteryLifePercent;
        bool ac = s.ACLineStatus == 1;

        if (_onAc != null && _onAc != ac && notify) PlugChanged?.Invoke(ac, Percent);
        _onAc = ac;

        if (ac) { _warnedAt = 101; return; }
        // %20 ve %10'da birer kez uyar
        int threshold = Percent <= 10 ? 10 : Percent <= 20 ? 20 : 101;
        if (threshold < _warnedAt && notify)
        {
            _warnedAt = threshold;
            Low?.Invoke(Percent);
        }
        else if (threshold < _warnedAt) _warnedAt = threshold;
    }

    public static string Glyph(int percent, bool charging)
    {
        if (charging) return "";            // BatteryCharging9
        int step = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
        return step == 10 ? "" : ((char)(0xE850 + step)).ToString(); // Battery0..9, Battery10
    }
}

/// <summary>
/// Mikrofon / kamera şu an kullanılıyor mu: Windows'un gizlilik kayıtlarından okunur
/// (görev çubuğundaki mikrofon simgesinin kullandığı bilgi). 3 saniyede bir hafif kontrol.
/// </summary>
public sealed class PrivacyService
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private readonly DispatcherTimer _timer;

    public bool MicInUse { get; private set; }
    public bool CamInUse { get; private set; }
    public event Action? Changed;

    public PrivacyService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Check();
    }

    public void SetEnabled(bool on)
    {
        if (on) { if (!_timer.IsEnabled) { Check(); _timer.Start(); } }
        else
        {
            _timer.Stop();
            if (MicInUse || CamInUse) { MicInUse = CamInUse = false; Changed?.Invoke(); }
        }
    }

    private void Check()
    {
        bool mic = InUse("microphone"), cam = InUse("webcam");
        if (mic == MicInUse && cam == CamInUse) return;
        MicInUse = mic; CamInUse = cam;
        Changed?.Invoke();
    }

    private static bool InUse(string capability)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root + capability);
            if (key == null) return false;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                if (sub == null) continue;
                if (name == "NonPackaged")
                {
                    foreach (var app in sub.GetSubKeyNames())
                    {
                        using var a = sub.OpenSubKey(app);
                        if (Active(a)) return true;
                    }
                }
                else if (Active(sub)) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool Active(RegistryKey? k) =>
        k?.GetValue("LastUsedTimeStart") is long start && start > 0 &&
        k.GetValue("LastUsedTimeStop") is long stop && stop == 0;
}
