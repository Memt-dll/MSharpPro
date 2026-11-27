using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MSharpPro.Services;

/// <summary>
/// İşlemci, bellek ve ağ kullanımı. Yalnızca "Sistem" sekmesi açıkken saniyede bir ölçer
/// (sekme kapalıyken hiç çalışmaz).
/// </summary>
public sealed class StatsService
{
    [StructLayout(LayoutKind.Sequential)] private struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    public const int Samples = 40;
    private readonly DispatcherTimer _timer;
    private ulong _lastIdle, _lastTotal;
    private long _lastRx = -1, _lastTx = -1;
    private DateTime _lastNet;
    private int _users;

    public Queue<double> Cpu { get; } = new();
    public Queue<double> Net { get; } = new();
    public double CpuNow { get; private set; }
    public double MemPercent { get; private set; }
    public double MemUsedGb { get; private set; }
    public double MemTotalGb { get; private set; }
    public double DownKBs { get; private set; }
    public double UpKBs { get; private set; }

    public event Action? Changed;

    public StatsService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Sample();
    }

    /// <summary>Bir pencere sekmeyi gösterince çağırır; kimse göstermiyorsa ölçüm durur.</summary>
    public void Acquire() { if (_users++ == 0) { Sample(); _timer.Start(); } }
    public void Release() { if (_users > 0 && --_users == 0) _timer.Stop(); }

    private void Sample()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel.Value + user.Value;
            if (_lastTotal > 0 && total > _lastTotal)
                CpuNow = Math.Clamp(100.0 * (1 - (double)(idle.Value - _lastIdle) / (total - _lastTotal)), 0, 100);
            _lastIdle = idle.Value; _lastTotal = total;
        }
        Push(Cpu, CpuNow);

        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            MemPercent = m.dwMemoryLoad;
            MemTotalGb = m.ullTotalPhys / 1073741824.0;
            MemUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        }

        try
        {
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived; tx += s.BytesSent;
            }
            var now = DateTime.Now;
            if (_lastRx >= 0)
            {
                double sec = Math.Max(0.2, (now - _lastNet).TotalSeconds);
                DownKBs = Math.Max(0, (rx - _lastRx) / 1024.0 / sec);
                UpKBs = Math.Max(0, (tx - _lastTx) / 1024.0 / sec);
            }
            _lastRx = rx; _lastTx = tx; _lastNet = now;
        }
        catch { }
        Push(Net, DownKBs + UpKBs);
        Changed?.Invoke();
    }

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > Samples) q.Dequeue();
    }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")] private static extern bool EmptyWorkingSet(IntPtr h);

    private static ulong AvailableBytes()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? m.ullAvailPhys : 0;
    }

    /// <summary>
    /// Belleği boşaltır: erişilebilen tüm uygulamaların kullanmadığı bellek sayfalarını geri ister
    /// (Windows'un "çalışma kümesini boşalt" işlemi; yönetici izni gerekmez, yalnızca kullanıcının kendi uygulamaları).
    /// Uygulamalar kapanmaz, veri kaybı olmaz; gerekirse sayfalar geri yüklenir. Boşalan bayt sayısını döndürür.
    /// </summary>
    public static Task<long> FreeMemoryAsync() => Task.Run(async () =>
    {
        ulong before = AvailableBytes();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                // PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA
                IntPtr h = OpenProcess(0x1000 | 0x0100, false, p.Id);
                if (h == IntPtr.Zero) continue;
                EmptyWorkingSet(h);
                CloseHandle(h);
            }
            catch { }
            finally { p.Dispose(); }
        }
        await Task.Delay(800); // Windows'un sayıları güncellemesi için
        return (long)AvailableBytes() - (long)before;
    });

    public static string Speed(double kbs) => kbs >= 1024 ? $"{kbs / 1024:0.0} MB/s" : $"{kbs:0} KB/s";

    /// <summary>Değerlerden küçük bir çizgi grafiği (0..max, genişlik x yükseklik).</summary>
    public static Geometry Spark(IEnumerable<double> values, double max, double w, double h)
    {
        var list = values.ToList();
        var g = new StreamGeometry();
        if (list.Count < 2) { g.Freeze(); return g; }
        max = Math.Max(max, list.Max());
        if (max <= 0) max = 1;
        using (var c = g.Open())
        {
            double step = w / (Samples - 1);
            double x0 = w - step * (list.Count - 1);
            c.BeginFigure(new Point(x0, h), true, true);
            for (int i = 0; i < list.Count; i++)
                c.LineTo(new Point(x0 + i * step, h - h * list[i] / max), true, true);
            c.LineTo(new Point(w, h), true, true);
        }
        g.Freeze();
        return g;
    }
}
