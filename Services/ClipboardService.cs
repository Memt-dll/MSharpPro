using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MSharpPro.Services;

public sealed class ClipItem
{
    public string Text { get; init; } = "";
    public string Time { get; init; } = "";
    /// <summary>Tek satıra sığdırılmış önizleme.</summary>
    public string Preview => Text.Length > 140 ? Text[..140].ReplaceLineEndings(" ") + "…" : Text.ReplaceLineEndings(" ");
}

/// <summary>
/// Son kopyalanan metinler. Yalnızca bellekte tutulur, diske yazılmaz.
/// Parola yöneticilerinin "geçmişe ekleme" işaretli kopyaları alınmaz.
/// Pano değişikliği Windows'tan olayla gelir (yoklama yok).
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D, Max = 12;
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly AppSettings _settings;
    private HwndSource? _source;
    private string? _ignoreNext;

    public ObservableCollection<ClipItem> Items { get; } = new();

    public ClipboardService(AppSettings settings) => _settings = settings;

    public void SetEnabled(bool on)
    {
        if (on && _source == null)
        {
            // Görünmez, yalnızca mesaj alan pencere
            _source = new HwndSource(new HwndSourceParameters("MSharpPro.Clipboard") { ParentWindow = new IntPtr(-3) });
            _source.AddHook(WndProc);
            AddClipboardFormatListener(_source.Handle);
        }
        else if (!on && _source != null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
            Items.Clear();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) Application.Current?.Dispatcher.InvokeAsync(Capture);
        return IntPtr.Zero;
    }

    private void Capture()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            // Parola yöneticileri bu biçimlerle "geçmişe kaydetme" der
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing") ||
                Clipboard.ContainsData("Clipboard Viewer Ignore")) return;
            if (Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream ms && ms.Length >= 4 &&
                BitConverter.ToInt32(ms.ToArray(), 0) == 0) return;

            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text) || text.Length > 20_000) return;
            if (text == _ignoreNext) { _ignoreNext = null; MoveToTop(text); return; }
            MoveToTop(text);
        }
        catch { /* pano başka uygulamada kilitli olabilir */ }
    }

    private void MoveToTop(string text)
    {
        var existing = Items.FirstOrDefault(i => i.Text == text);
        if (existing != null) Items.Remove(existing);
        Items.Insert(0, new ClipItem { Text = text, Time = DateTime.Now.ToString("HH:mm") });
        while (Items.Count > Max) Items.RemoveAt(Items.Count - 1);
    }

    /// <summary>Listedeki metni yeniden panoya kopyalar.</summary>
    public void Copy(ClipItem item)
    {
        try
        {
            _ignoreNext = item.Text;
            Clipboard.SetText(item.Text);
        }
        catch { }
    }

    public void Clear() => Items.Clear();

    public void Dispose() => SetEnabled(false);
}

public sealed class ShelfItem
{
    public string Path { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } n ? n : Path;
    public ImageSource? Icon { get; init; }
}

/// <summary>Çentiğe sürüklenen dosyaların geçici rafı (yalnızca bellekte; dosyaların kendisine dokunulmaz).</summary>
public sealed class ShelfService
{
    public ObservableCollection<ShelfItem> Items { get; } = new();

    public void Add(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            if (!File.Exists(p) && !Directory.Exists(p)) continue;
            Items.Add(new ShelfItem { Path = p, Icon = FileIcon(p) });
        }
        while (Items.Count > 24) Items.RemoveAt(0);
    }

    public void Remove(ShelfItem item) => Items.Remove(item);
    public void Clear() => Items.Clear();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    private static ImageSource? FileIcon(string path)
    {
        try
        {
            var info = new SHFILEINFO();
            // SHGFI_ICON | SHGFI_LARGEICON
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DestroyIcon(info.hIcon);
            src.Freeze();
            return src;
        }
        catch { return null; }
    }
}
