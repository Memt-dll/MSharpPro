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
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

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

    /// <summary>Tam ekran oyun / video / sunum açık mı?</summary>
    public static bool IsFullscreenBusy()
    {
        if (SHQueryUserNotificationState(out int s) != 0) return false;
        return s is 2 /*BUSY*/ or 3 /*D3D FULLSCREEN*/ or 4 /*PRESENTATION*/;
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

internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "M#Pro";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) != null;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(Name, false);
    }
}
