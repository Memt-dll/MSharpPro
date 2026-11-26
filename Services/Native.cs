using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.Storage.Streams;

namespace MSharpPro.Services;

internal static class Native
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    /// <summary>Alt+Tab'da görünmesin, tıklayınca odak çalmasın.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Diğer "her zaman üstte" pencerelerin de üstüne çıkar (odak çalmadan).</summary>
    public static void BringToTop(IntPtr hwnd)
    {
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>Kullanılmayan belleği işletim sistemine geri verir.</summary>
    public static void TrimWorkingSet()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
        SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);

    /// <summary>
    /// Verilen ekranda tam ekran bir uygulama (oyun, video, sunum) öndeyse true.
    /// Ekran başına çalışır: 2. ekranda film açıkken 1. ekrandaki çentik görünmeye devam eder.
    /// </summary>
    public static bool IsFullscreenOn(MonitorInfo m)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out int pid);
        if (pid == Environment.ProcessId) return false;

        var sb = new System.Text.StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        string cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (!GetWindowRect(fg, out var r)) return false;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Left + m.Width && r.Bottom >= m.Top + m.Height;
    }

    /// <summary>Pencereyi fiziksel piksel koordinatlarına taşır (boyutu ve sırayı değiştirmeden).</summary>
    public static void MoveTo(IntPtr hwnd, int x, int y)
    {
        const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}

internal static class ImageUtil
{
    public static async Task<ImageSource?> LoadAsync(IRandomAccessStreamReference? reference, int px)
        => FromBytes(await ReadBytesAsync(reference), px);

    public static async Task<byte[]?> ReadBytesAsync(IRandomAccessStreamReference? reference)
    {
        if (reference == null) return null;
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resmi verilen genişlikte çözer. Çok küçük bir genişlik (ör. 16 px) verilip büyütülerek
    /// gösterildiğinde, ekstra efekt maliyeti olmadan doğal bir bulanıklık elde edilir.
    /// </summary>
    public static ImageSource? FromBytes(byte[]? data, int px)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = px; // küçük çöz -> az RAM
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// "Bilgisayar açılınca başlat". GitHub sürümünde kayıt defteri (Run anahtarı),
/// Microsoft Store sürümünde (MSIX paketi) Windows'un başlangıç görevi (StartupTask) kullanılır;
/// paketli uygulamalarda Run anahtarı çalışmaz.
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "M#Pro";
    /// <summary>Package.appxmanifest içindeki StartupTask kimliğiyle aynı olmalı.</summary>
    private const string TaskId = "MSharpProStartup";

    public static bool IsEnabled
    {
        get
        {
            if (Package.IsPackaged)
            {
                try
                {
                    var task = Task.Run(async () => await global::Windows.ApplicationModel.StartupTask.GetAsync(TaskId)).Result;
                    return task.State is global::Windows.ApplicationModel.StartupTaskState.Enabled
                        or global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                }
                catch { return false; }
            }
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) != null;
        }
    }

    public static void Set(bool enabled)
    {
        if (Package.IsPackaged)
        {
            try
            {
                Task.Run(async () =>
                {
                    var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                    if (enabled) await task.RequestEnableAsync();
                    else task.Disable();
                }).Wait();
            }
            catch (Exception ex) { App.LogError(ex); }
            return;
        }
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(Name, false);
    }
}

/// <summary>Uygulama Microsoft Store'dan (MSIX paketi olarak) mı kuruldu.</summary>
internal static class Package
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            int len = 0;
            return GetCurrentPackageFullName(ref len, null) != APPMODEL_ERROR_NO_PACKAGE;
        }
        catch { return false; }
    }
}