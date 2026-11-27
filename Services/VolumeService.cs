using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace MSharpPro.Services;

/// <summary>
/// Varsayılan hoparlörün ses düzeyi değişince haber verir (Core Audio, olay tabanlı: boşta CPU yok).
/// Varsayılan çıkış aygıtı değişirse (ör. kulaklık takıldı) yeni aygıta geçer.
/// </summary>
public sealed class VolumeService : IDisposable
{
    private readonly Dispatcher _ui;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private readonly VolumeCallback _callback;
    private readonly DeviceCallback _deviceCallback;
    private DateTime _quietUntil;

    /// <summary>(düzey 0..1, sessiz mi) · UI thread.</summary>
    public event Action<float, bool>? Changed;

    public VolumeService(Dispatcher ui)
    {
        _ui = ui;
        _callback = new VolumeCallback(this);
        _deviceCallback = new DeviceCallback(this);
    }

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
            Attach();
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void Attach()
    {
        try
        {
            if (_endpoint != null)
            {
                try { _endpoint.UnregisterControlChangeNotify(_callback); } catch { }
                Marshal.ReleaseComObject(_endpoint);
                _endpoint = null;
            }
            if (_enumerator == null) return;
            if (_enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var device) != 0 || device == null) return;
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var obj);
            Marshal.ReleaseComObject(device);
            _endpoint = (IAudioEndpointVolume)obj;
            _endpoint.RegisterControlChangeNotify(_callback);
            // Aygıt değişiminin hemen ardından gelen ilk bildirim kullanıcı tuşa bastığı için değil
            _quietUntil = DateTime.Now.AddSeconds(1);
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void OnNotify(IntPtr data)
    {
        // AUDIO_VOLUME_NOTIFICATION_DATA: Guid (16) · BOOL bMuted (4) · float fMasterVolume (4) ...
        bool muted = Marshal.ReadInt32(data, 16) != 0;
        float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
        if (DateTime.Now < _quietUntil) return;
        _ui.InvokeAsync(() => Changed?.Invoke(level, muted));
    }

    public void Dispose()
    {
        try
        {
            if (_endpoint != null) _endpoint.UnregisterControlChangeNotify(_callback);
            _enumerator?.UnregisterEndpointNotificationCallback(_deviceCallback);
        }
        catch { }
    }

    // ------------------------------------------------------------------ COM tanımları

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Fmt; public int Pid; }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }

    private sealed class VolumeCallback(VolumeService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            try { owner.OnNotify(data); } catch { }
            return 0;
        }
    }

    private sealed class DeviceCallback(VolumeService owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, int state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            // Aynı thread'de yeniden kayıt olmak kilitlenebilir: UI thread'ine bırak
            if (flow == 0 && role == 1) owner._ui.InvokeAsync(owner.Attach);
            return 0;
        }
    }
}
