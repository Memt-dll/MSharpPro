using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MSharpPro.Services;

/// <summary>
/// Görev çubuğunun bildirim alanında (saat yanında) ikon.
/// WinForms yüklememek için doğrudan Shell_NotifyIcon ile yazıldı (daha az RAM).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public sealed record MenuItem(string Text, Action? Action, bool Checked = false)
    {
        public static readonly MenuItem Separator = new("-", null);
    }

    private const int WM_TRAY = 0x8001;
    private const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_NULL = 0;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string msg);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    // Windows 11'de sağ tık menüsünün koyu temada görünmesi için (belgelenmemiş ama yaygın kullanılan)
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();

    private readonly HwndSource _source;
    private readonly int _taskbarCreated;
    private IntPtr _icon;
    private string _tip;

    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIcon(string tip)
    {
        _tip = tip;
        try { SetPreferredAppMode(1); FlushMenuThemes(); } catch { }

        // Görünmez bir pencere: tepsi mesajlarını alır ve menü için öne getirilir
        _source = new HwndSource(new HwndSourceParameters("MSharpProTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP (görünmez)
            ExtendedWindowStyle = 0x80               // WS_EX_TOOLWINDOW
        });
        _source.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        // Uygulama ikonunu exe'nin kendisinden al (tek dosya yayında da çalışır)
        if (Environment.ProcessPath is { } exe && ExtractIconEx(exe, 0, out var large, out var small, 1) > 0)
        {
            _icon = small != IntPtr.Zero ? small : large;
            if (small != IntPtr.Zero && large != IntPtr.Zero) DestroyIcon(large);
        }

        if (!Notify(NIM_ADD)) App.LogError(new InvalidOperationException("Tepsi ikonu eklenemedi"));
    }

    public void SetTooltip(string tip)
    {
        _tip = tip;
        Notify(NIM_MODIFY);
    }

    private bool Notify(int msg)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon,
            szTip = _tip,
            szInfo = "",
            szInfoTitle = ""
        };
        return Shell_NotifyIcon(msg, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            int ev = (int)(lParam.ToInt64() & 0xFFFF);
            if (ev == WM_LBUTTONUP) LeftClick?.Invoke();
            else if (ev == WM_RBUTTONUP) RightClick?.Invoke();
            handled = true;
        }
        else if (msg == _taskbarCreated)
        {
            Notify(NIM_ADD); // Explorer yeniden başlarsa ikonu geri koy
        }
        return IntPtr.Zero;
    }

    /// <summary>İmlecin olduğu yerde yerel Windows menüsü açar.</summary>
    public void ShowMenu(IReadOnlyList<MenuItem> items)
    {
        const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_CHECKED = 0x8;
        const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20;

        IntPtr menu = CreatePopupMenu();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (ReferenceEquals(it, MenuItem.Separator)) AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            else AppendMenu(menu, MF_STRING | (it.Checked ? MF_CHECKED : 0), (UIntPtr)(uint)(i + 1), it.Text);
        }

        GetCursorPos(out var p);
        SetForegroundWindow(_source.Handle);
        int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, p.X, p.Y, _source.Handle, IntPtr.Zero);
        PostMessage(_source.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd > 0 && cmd <= items.Count) items[cmd - 1].Action?.Invoke();
    }

    public void Dispose()
    {
        Notify(NIM_DELETE);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _source.Dispose();
    }
}
