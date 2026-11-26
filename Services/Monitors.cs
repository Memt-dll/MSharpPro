using System.Runtime.InteropServices;

namespace MSharpPro.Services;

/// <summary>Bir ekran (fiziksel piksel koordinatlarıyla).</summary>
public sealed record MonitorInfo(string Device, int Number, int Left, int Top, int Width, int Height, bool IsPrimary)
{
    public string Label => $"Ekran {Number}{(IsPrimary ? " (ana)" : "")}  ·  {Width}×{Height}";
}

/// <summary>Bağlı ekranları listeler.</summary>
public static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    public static List<MonitorInfo> GetAll()
    {
        var raw = new List<(string dev, RECT r, bool primary)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref mi)) raw.Add((mi.szDevice, mi.rcMonitor, (mi.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);

        // Soldan sağa (sonra yukarıdan aşağı) numaralandır: "Ekran 1" en soldaki olur
        return raw
            .OrderBy(m => m.r.Left).ThenBy(m => m.r.Top)
            .Select((m, i) => new MonitorInfo(m.dev, i + 1, m.r.Left, m.r.Top,
                m.r.Right - m.r.Left, m.r.Bottom - m.r.Top, m.primary))
            .ToList();
    }

    public static MonitorInfo Primary(List<MonitorInfo>? all = null)
    {
        all ??= GetAll();
        return all.FirstOrDefault(m => m.IsPrimary) ?? all.FirstOrDefault()
               ?? new MonitorInfo("", 1, 0, 0, 1920, 1080, true);
    }

    /// <summary>Ayardaki ekran değerine göre çentiğin gösterileceği ekranlar.</summary>
    public static List<MonitorInfo> Resolve(string display)
    {
        var all = GetAll();
        if (display == AppSettings.DisplayAll && all.Count > 0) return all;
        var match = all.FirstOrDefault(m => string.Equals(m.Device, display, StringComparison.OrdinalIgnoreCase));
        return new List<MonitorInfo> { match ?? Primary(all) };
    }
}
