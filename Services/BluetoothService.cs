using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace MSharpPro.Services;

public sealed class BtDevice : INotifyPropertyChanged
{
    private static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64)));
    private static readonly Brush Gray = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));

    internal readonly HashSet<string> Ids = new();
    internal readonly HashSet<string> ConnectedIds = new();

    public string Name { get; init; } = "";
    public string Glyph { get; set; } = "";
    public bool IsConnected => ConnectedIds.Count > 0;
    public string StatusText => IsConnected ? "Bağlı" : "Bağlı değil";
    public Brush StatusBrush => IsConnected ? Green : Gray;
    public Brush IconBrush => IsConnected ? AccentIcon : IdleIcon;

    private static readonly Brush IdleIcon = Freeze(new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush AccentIcon = Freeze(new LinearGradientBrush(
        Color.FromArgb(0xCC, 0x5A, 0xB8, 0xFF), Color.FromArgb(0xCC, 0x8A, 0x7C, 0xFF), 45));

    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}

/// <summary>Eşleştirilmiş Bluetooth cihazlarını ve bağlantı durumlarını canlı izler.</summary>
public sealed class BluetoothService
{
    private const string ConnProp = "System.Devices.Aep.IsConnected";
    private const string CodProp = "System.Devices.Aep.Bluetooth.Cod.Major";

    private readonly Dispatcher _ui;
    private readonly Dictionary<string, BtDevice> _byId = new();
    private readonly List<DeviceWatcher> _watchers = new();
    private int _completed;

    public ObservableCollection<BtDevice> Devices { get; } = new();

    /// <summary>Bir cihaz bağlandı / bağlantısı koptu (UI thread'inde).</summary>
    public event Action<BtDevice>? ConnectionChanged;

    public BluetoothService(Dispatcher ui) => _ui = ui;

    public void Start()
    {
        if (Demo.On)
        {
            foreach (var (name, glyph, on) in new[] { ("AirPods Pro", "", true), ("MX Keys", "", true), ("Galaxy Buds2", "", false) })
            {
                var d = new BtDevice { Name = name, Glyph = glyph };
                d.Ids.Add(name);
                if (on) d.ConnectedIds.Add(name);
                Devices.Add(d);
            }
            return;
        }
        try { AddWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true)); } catch { }
        try { AddWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)); } catch { }
    }

    private void AddWatcher(string selector)
    {
        var w = DeviceInformation.CreateWatcher(selector, new[] { ConnProp, CodProp },
            DeviceInformationKind.AssociationEndpoint);
        w.Added += (_, d) => _ui.InvokeAsync(() => OnAdded(d));
        w.Updated += (_, u) => _ui.InvokeAsync(() => OnUpdated(u));
        w.Removed += (_, u) => _ui.InvokeAsync(() => OnRemoved(u.Id));
        w.EnumerationCompleted += (_, _) => _ui.InvokeAsync(() => _completed++);
        _watchers.Add(w);
        w.Start();
    }

    private bool Ready => _completed >= _watchers.Count;

    private void OnAdded(DeviceInformation d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return;

        // Klasik + LE olarak iki kez görünen cihazları isimle birleştir
        var dev = Devices.FirstOrDefault(x => string.Equals(x.Name, d.Name, StringComparison.OrdinalIgnoreCase));
        if (dev == null)
        {
            dev = new BtDevice { Name = d.Name, Glyph = GuessGlyph(d) };
            Devices.Add(dev);
        }
        dev.Ids.Add(d.Id);
        _byId[d.Id] = dev;

        bool connected = d.Properties.TryGetValue(ConnProp, out var v) && v is true;
        SetConnected(dev, d.Id, connected);
    }

    private void OnUpdated(DeviceInformationUpdate u)
    {
        if (!_byId.TryGetValue(u.Id, out var dev)) return;
        if (u.Properties.TryGetValue(ConnProp, out var v) && v is bool b)
            SetConnected(dev, u.Id, b);
    }

    private void OnRemoved(string id)
    {
        if (!_byId.Remove(id, out var dev)) return;
        dev.Ids.Remove(id);
        dev.ConnectedIds.Remove(id);
        if (dev.Ids.Count == 0) Devices.Remove(dev);
        else dev.Notify();
    }

    private void SetConnected(BtDevice dev, string id, bool connected)
    {
        bool before = dev.IsConnected;
        if (connected) dev.ConnectedIds.Add(id); else dev.ConnectedIds.Remove(id);
        dev.Notify();

        if (before == dev.IsConnected) return;

        // Bağlı cihazlar üstte dursun
        int idx = Devices.IndexOf(dev);
        if (dev.IsConnected && idx > 0) Devices.Move(idx, 0);
        else if (!dev.IsConnected && idx >= 0 && idx < Devices.Count - 1) Devices.Move(idx, Devices.Count - 1);

        if (Ready) ConnectionChanged?.Invoke(dev);
    }

    private static string GuessGlyph(DeviceInformation d)
    {
        string n = d.Name.ToLowerInvariant();
        if (n.Contains("mouse") || n.Contains("fare")) return "";
        if (n.Contains("keyboard") || n.Contains("klavye")) return "";
        if (n.Contains("buds") || n.Contains("airpods") || n.Contains("head") || n.Contains("kulak") ||
            n.Contains("wh-") || n.Contains("wf-") || n.Contains("jbl")) return "";

        try
        {
            if (d.Properties.TryGetValue(CodProp, out var v) && v != null)
            {
                return Convert.ToInt32(v) switch
                {
                    1 => "", // bilgisayar
                    2 => "", // telefon
                    4 => "", // ses / kulaklık
                    5 => "", // çevre birimi
                    _ => ""
                };
            }
        }
        catch { }
        return "";
    }
}
