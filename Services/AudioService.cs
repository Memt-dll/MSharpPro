using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static MSharpPro.Services.Loc;

namespace MSharpPro.Services;

/// <summary>Bir ses çıkış aygıtı (hoparlör, kulaklık...).</summary>
public sealed class AudioOutput
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDefault { get; init; }

    /// <summary>Kısa ad: "Kulaklıklar (Memt AirPods)" → "Memt AirPods".</summary>
    public string ShortName
    {
        get
        {
            int open = Name.IndexOf('('), close = Name.LastIndexOf(')');
            string inner = open >= 0 && close > open ? Name[(open + 1)..close].Trim() : "";
            return inner.Length > 0 ? inner : Name;
        }
    }

    /// <summary>Aygıt türüne göre simge: kulaklık, ekran (HDMI) ya da hoparlör.</summary>
    public string Glyph
    {
        get
        {
            string n = Name.ToLowerInvariant();
            if (n.Contains("kulakl") || n.Contains("headphone") || n.Contains("headset") || n.Contains("airpods")
                || n.Contains("buds") || n.Contains("hands-free")) return "";
            if (n.Contains("hdmi") || n.Contains("displayport") || n.Contains("monitor") || n.Contains("nvidia")
                || n.Contains("intel(r) display") || n.Contains("amd high definition")) return "";
            return "";
        }
    }
}

/// <summary>Ses çıkaran bir uygulama (aynı uygulamanın tüm oturumları tek satırda).</summary>
public sealed class AppVolume : INotifyPropertyChanged
{
    // Ses oturumları yalnızca MTA thread'lerinden kullanılabilir (UI thread'ine taşınamıyor):
    // oturumlar ve ses ayarı thread havuzunda tutulur / yapılır.
    internal List<object> Sessions = new(); // IChannelAudioVolume
    private readonly object _gate = new();
    private float _pending = -1, _applied = -1;
    private bool _running;
    private double _level;

    public string Name { get; init; } = "";
    public string Key { get; init; } = "";

    /// <summary>0..100</summary>
    public double Level
    {
        get => _level;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (Math.Abs(_level - value) < 0.01) return;
            _level = value;
            Apply((float)(value / 100));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelText)));
        }
    }

    /// <summary>Sürüklerken gelen değerleri birleştirir: arka planda yalnızca en son değer uygulanır.</summary>
    private void Apply(float v)
    {
        lock (_gate)
        {
            _pending = v;
            if (_running) return;
            _running = true;
        }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            while (true)
            {
                float target;
                List<object> sessions;
                lock (_gate)
                {
                    if (_pending == _applied) { _running = false; return; }
                    target = _pending;
                    sessions = Sessions;
                }
                var ctx = Guid.Empty;
                foreach (var s in sessions)
                {
                    try
                    {
                        var vol = (AudioService.IChannelAudioVolume)s;
                        vol.GetChannelCount(out uint n);
                        for (uint c = 0; c < n; c++) vol.SetChannelVolume(c, target, ref ctx);
                    }
                    catch { }
                }
                lock (_gate) _applied = target;
            }
        });
    }

    public string LevelText => $"{Math.Round(_level)}";

    internal void SetFromDevice(double v)
    {
        _level = v;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelText)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Ses denetimleri: çıkış aygıtını değiştirme, uygulama bazında ses ve mikrofonu sessize alma.
/// Aygıt ve uygulama listeleri yalnızca "Denetim" sekmesi açıkken okunur; mikrofon durumu Windows'tan olayla gelir.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly Dispatcher _ui;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _mic;
    private readonly MicCallback _micCallback;

    public ObservableCollection<AudioOutput> Outputs { get; } = new();
    public ObservableCollection<AppVolume> Apps { get; } = new();

    public bool HasMic { get; private set; }
    public bool MicMuted { get; private set; }
    /// <summary>Mikrofon sessiz durumu değişti (UI thread).</summary>
    public event Action? MicChanged;

    public AudioService(Dispatcher ui)
    {
        _ui = ui;
        _micCallback = new MicCallback(this);
    }

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            AttachMic();
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    // ------------------------------------------------------------------ mikrofon

    /// <summary>Varsayılan (iletişim) mikrofonuna bağlanır; aygıt değişince yeniden çağrılır.</summary>
    public void AttachMic()
    {
        try
        {
            if (_mic != null)
            {
                try { _mic.UnregisterControlChangeNotify(_micCallback); } catch { }
                Marshal.ReleaseComObject(_mic);
                _mic = null;
            }
            HasMic = false;
            if (_enumerator == null) return;
            // eCapture = 1, eCommunications = 2 (toplantı uygulamalarının kullandığı mikrofon)
            if (_enumerator.GetDefaultAudioEndpoint(1, 2, out var dev) != 0 || dev == null) { RaiseMic(false); return; }
            _mic = Activate<IAudioEndpointVolume>(dev);
            Marshal.ReleaseComObject(dev);
            _mic.RegisterControlChangeNotify(_micCallback);
            _mic.GetMute(out int m);
            HasMic = true;
            RaiseMic(m != 0);
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    /// <summary>Mikrofonu sessize alır / açar. Mikrofon yoksa false.</summary>
    public bool ToggleMic()
    {
        AttachMic(); // varsayılan mikrofon bu arada değişmiş olabilir
        if (_mic == null) return false;
        try
        {
            var ctx = Guid.Empty;
            _mic.SetMute(MicMuted ? 0 : 1, ref ctx);
            _mic.GetMute(out int m);
            RaiseMic(m != 0);
            return true;
        }
        catch (Exception ex) { App.LogError(ex); return false; }
    }

    private void RaiseMic(bool muted)
    {
        if (muted == MicMuted) { MicChanged?.Invoke(); return; }
        MicMuted = muted;
        MicChanged?.Invoke();
    }

    // ------------------------------------------------------------------ çıkış aygıtları

    /// <summary>Etkin hoparlör / kulaklık listesini yeniler.</summary>
    public void RefreshOutputs()
    {
        Outputs.Clear();
        if (_enumerator == null) return;
        try
        {
            string defId = "";
            if (_enumerator.GetDefaultAudioEndpoint(0, 1, out var def) == 0 && def != null)
            {
                def.GetId(out defId);
                Marshal.ReleaseComObject(def);
            }
            // eRender = 0, DEVICE_STATE_ACTIVE = 1
            if (_enumerator.EnumAudioEndpoints(0, 1, out var col) != 0 || col == null) return;
            col.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                if (col.Item(i, out var dev) != 0 || dev == null) continue;
                dev.GetId(out string id);
                string name = FriendlyName(dev) ?? L("Ses aygıtı");
                Outputs.Add(new AudioOutput { Id = id, Name = name, IsDefault = id == defId });
                Marshal.ReleaseComObject(dev);
            }
            Marshal.ReleaseComObject(col);
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    /// <summary>Seçilen aygıtı Windows'un varsayılan ses çıkışı yapar (tüm uygulamalar için).</summary>
    public bool SetDefaultOutput(AudioOutput output)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigCom();
            for (int role = 0; role < 3; role++) policy.SetDefaultEndpoint(output.Id, role);
            Marshal.ReleaseComObject(policy);
            RefreshOutputs();
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }

    private static string? FriendlyName(IMMDevice dev)
    {
        try
        {
            if (dev.OpenPropertyStore(0 /*STGM_READ*/, out var store) != 0 || store == null) return null;
            var key = new PropertyKey { Fmt = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 };
            store.GetValue(ref key, out var pv);
            string? name = pv.Vt == 31 /*VT_LPWSTR*/ ? Marshal.PtrToStringUni(pv.Ptr) : null;
            PropVariantClear(ref pv);
            Marshal.ReleaseComObject(store);
            return name;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ uygulama sesleri

    private bool _refreshingApps;

    /// <summary>
    /// Varsayılan çıkıştaki uygulama ses oturumlarını okur (aynı uygulamanınkiler birleştirilir).
    /// Ses oturumları UI thread'inde kullanılamadığı için okuma thread havuzunda (MTA) yapılır.
    /// </summary>
    public async Task RefreshAppsAsync()
    {
        if (_refreshingApps) return;
        _refreshingApps = true;
        try
        {
            string system = L("Sistem sesleri");
            var found = await Task.Run(() => ReadSessions(system));

            // Listeyi yerinde güncelle (sürüklenen kaydırıcı kaybolmasın)
            foreach (var gone in Apps.Where(a => !found.ContainsKey(a.Key)).ToList()) Apps.Remove(gone);
            foreach (var (name, (level, sessions)) in found.OrderBy(f => f.Key == system ? 1 : 0).ThenBy(f => f.Key))
            {
                var app = Apps.FirstOrDefault(a => a.Key == name);
                if (app == null)
                {
                    app = new AppVolume { Name = name, Key = name };
                    app.SetFromDevice(level);
                    Apps.Add(app);
                }
                app.Sessions = sessions;
            }
        }
        catch (Exception ex) { App.LogError(ex); }
        finally { _refreshingApps = false; }
    }

    private static Dictionary<string, (double Level, List<object> Sessions)> ReadSessions(string systemName)
    {
        var found = new Dictionary<string, (double, List<object>)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var dev) != 0 || dev == null) return found;
            var mgr = Activate<IAudioSessionManager2>(dev);
            Marshal.ReleaseComObject(dev);
            mgr.GetSessionEnumerator(out var en);
            en.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (en.GetSession(i, out var ctl) != 0 || ctl == null) continue;
                ctl.GetState(out int state);
                if (state == 2 /*expired*/) continue;
                bool system = ctl.IsSystemSoundsSession() == 0;
                ctl.GetProcessId(out uint pid);
                string name = system ? systemName : ProcessName(pid, ctl);
                if (name.Length == 0 || ctl is not IChannelAudioVolume vol) continue;
                vol.GetChannelCount(out uint channels);
                float level = 0;
                for (uint c = 0; c < channels; c++) { vol.GetChannelVolume(c, out float cl); level = Math.Max(level, cl); }
                if (!found.TryGetValue(name, out var entry)) entry = (level * 100, new List<object>());
                entry.Item2.Add(vol);
                found[name] = entry;
            }
            Marshal.ReleaseComObject(en);
            Marshal.ReleaseComObject(mgr);
        }
        catch (Exception ex) { App.LogError(ex); }
        return found;
    }

    private static string ProcessName(uint pid, IAudioSessionControl2 ctl)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            try
            {
                string? desc = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) return desc.Trim();
            }
            catch { /* başka kullanıcının / korumalı işlem */ }
            return p.ProcessName;
        }
        catch
        {
            try { ctl.GetDisplayName(out string n); return n ?? ""; } catch { return ""; }
        }
    }

    public void Dispose()
    {
        try { _mic?.UnregisterControlChangeNotify(_micCallback); } catch { }
    }

    private static T Activate<T>(IMMDevice dev)
    {
        var iid = typeof(T).GUID;
        dev.Activate(ref iid, 23, IntPtr.Zero, out var obj);
        return (T)obj;
    }

    private sealed class MicCallback(AudioService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            try
            {
                bool muted = Marshal.ReadInt32(data, 16) != 0;
                owner._ui.InvokeAsync(() => owner.RaiseMic(muted));
            }
            catch { }
            return 0;
        }
    }

    // ------------------------------------------------------------------ COM tanımları

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant pv);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public IntPtr Ptr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Fmt; public int Pid; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCom { }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] private class PolicyConfigCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute(int mute, ref Guid ctx);
        [PreserveSig] int GetMute(out int mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr ctl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr vol);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator en);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    /// <summary>Uygulama oturumunun kanal sesleri (ISimpleAudioVolume bazı Windows sürümlerinde oturumda bulunmuyor).</summary>
    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IChannelAudioVolume
    {
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetChannelVolume(uint index, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolume(uint index, out float level);
    }

    /// <summary>Windows'un varsayılan ses aygıtını değiştiren (belgelenmemiş ama Windows 7'den beri aynı) arayüz.</summary>
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr fmt);
        [PreserveSig] int GetDeviceFormat(string id, int def, IntPtr fmt);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr a, IntPtr b);
        [PreserveSig] int GetProcessingPeriod(string id, int def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr p);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(string id, int visible);
    }
}
