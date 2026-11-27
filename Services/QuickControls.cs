using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.Devices.Radios;
using static MSharpPro.Services.Loc;

namespace MSharpPro.Services;

/// <summary>
/// "Kahve modu": bilgisayarın uykuya geçmesini ve ekranın kararmasını engeller.
/// Tek bir Windows çağrısıdır; ek bellek ya da arka plan işi yoktur.
/// </summary>
public sealed class KeepAwakeService
{
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;

    private readonly DispatcherTimer _end;

    public bool Active { get; private set; }
    /// <summary>Ne zaman kendiliğinden kapanacak (null: kapatana kadar).</summary>
    public DateTime? Until { get; private set; }
    public event Action? Changed;

    public KeepAwakeService()
    {
        _end = new DispatcherTimer(DispatcherPriority.Background);
        _end.Tick += (_, _) => Stop();
    }

    /// <summary>Kapalı → 1 saat → kapatana kadar → kapalı.</summary>
    public void Cycle()
    {
        if (!Active) Start(TimeSpan.FromHours(1));
        else if (Until != null) Start(null);
        else Stop();
    }

    public void Start(TimeSpan? length)
    {
        // Durum bu (UI) thread'e bağlıdır; UI thread uygulama boyunca yaşar
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
        Active = true;
        _end.Stop();
        Until = length == null ? null : DateTime.Now + length;
        if (length != null) { _end.Interval = length.Value; _end.Start(); }
        Changed?.Invoke();
    }

    public void Stop()
    {
        SetThreadExecutionState(ES_CONTINUOUS);
        Active = false;
        Until = null;
        _end.Stop();
        Changed?.Invoke();
    }

    public string StateText => !Active ? L("Kapalı")
        : Until is { } u ? L("Bitiş {0}", u.ToString("HH:mm"))
        : L("Kapatana kadar");
}

/// <summary>
/// Uçak modu: Windows'un kendi uçak modu anahtarı (Ayarlar'daki ile aynı). Bu kullanılamazsa
/// Wi-Fi, Bluetooth ve mobil bağlantı tek tek kapatılır / açılır.
/// Yalnızca tıklanınca ve Denetim sekmesi açılınca okunur.
/// </summary>
public sealed class AirplaneService
{
    public bool On { get; private set; }
    public bool Supported { get; private set; } = true;

    /// <summary>Durumu okur.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            if (TryGetSystemState(out bool radiosOn)) { On = !radiosOn; return; }
            var radios = await Radio.GetRadiosAsync();
            Supported = radios.Count > 0;
            On = radios.Count > 0 && radios.All(r => r.State != RadioState.On);
        }
        catch { Supported = false; }
    }

    /// <summary>Uçak modunu açar / kapatır; başarılıysa true.</summary>
    public async Task<bool> ToggleAsync()
    {
        await RefreshAsync();
        bool target = !On;
        try
        {
            if (TrySetSystemState(radiosOn: !target))
            {
                await Task.Delay(400);
                await RefreshAsync();
                return On == target;
            }

            // Yedek yol: radyoları tek tek aç / kapat
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return false;
            foreach (var r in await Radio.GetRadiosAsync())
            {
                try { await r.SetStateAsync(target ? RadioState.Off : RadioState.On); } catch { }
            }
            await RefreshAsync();
            return On == target;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }

    // Windows'un uçak modu yöneticisi (Ayarlar uygulamasının kullandığı arayüz)
    [ComImport, Guid("581333F6-28DB-41BE-BC7A-FF201F12F3F6")] private class RadioManagerCom { }

    [ComImport, Guid("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRadioManager
    {
        [PreserveSig] int IsRMSupported(out int supported);
        [PreserveSig] int GetUIRadioInstances(out IntPtr instances);
        [PreserveSig] int GetSystemRadioState(out int enabled, out int unknown1, out int unknown2);
        [PreserveSig] int SetSystemRadioState(int enabled);
        [PreserveSig] int Refresh();
        [PreserveSig] int OnHardwareSliderChange(int a, int b);
    }

    private static bool TryGetSystemState(out bool radiosOn)
    {
        radiosOn = true;
        try
        {
            var rm = (IRadioManager)new RadioManagerCom();
            int hr = rm.GetSystemRadioState(out int enabled, out _, out _);
            Marshal.ReleaseComObject(rm);
            if (hr != 0) return false;
            radiosOn = enabled != 0;
            return true;
        }
        catch { return false; }
    }

    private static bool TrySetSystemState(bool radiosOn)
    {
        try
        {
            var rm = (IRadioManager)new RadioManagerCom();
            int hr = rm.SetSystemRadioState(radiosOn ? 1 : 0);
            Marshal.ReleaseComObject(rm);
            return hr == 0;
        }
        catch { return false; }
    }
}

/// <summary>
/// Genel kısayol (RegisterHotKey): klavye kancası kullanmaz, Windows yalnızca tuşa basılınca haber verir.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    private const int WM_HOTKEY = 0x0312, Id = 0x4D53;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    private readonly uint _mods, _vk;
    private HwndSource? _source;

    public event Action? Pressed;
    /// <summary>Kısayol başka bir uygulamada kayıtlıysa false.</summary>
    public bool Registered { get; private set; }

    public GlobalHotkey(uint mods, uint vk) { _mods = mods; _vk = vk; }

    public void SetEnabled(bool on)
    {
        if (on && _source == null)
        {
            _source = new HwndSource(new HwndSourceParameters("MSharpPro.Hotkey") { ParentWindow = new IntPtr(-3) });
            _source.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == WM_HOTKEY && w.ToInt32() == Id) { handled = true; Pressed?.Invoke(); }
                return IntPtr.Zero;
            });
            Registered = RegisterHotKey(_source.Handle, Id, _mods | MOD_NOREPEAT, _vk);
        }
        else if (!on && _source != null)
        {
            UnregisterHotKey(_source.Handle, Id);
            _source.Dispose();
            _source = null;
            Registered = false;
        }
    }

    public void Dispose() => SetEnabled(false);
}
