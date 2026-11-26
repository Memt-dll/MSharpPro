using System.Runtime.InteropServices;

namespace MSharpPro.Services;

/// <summary>
/// Ctrl + Win tuş ikilisini algılar. Windows'ta yalnızca değiştirici tuşlardan oluşan bir kısayol
/// RegisterHotKey ile kaydedilemediği için düşük seviyeli klavye kancası kullanılır.
/// İkisi birlikte basılıp başka bir tuşa basılmadan bırakılınca tetiklenir
/// (böylece Ctrl+Win+Sağ ok gibi sanal masaüstü kısayolları etkilenmez). Tuşlar engellenmez.
/// </summary>
internal sealed class CtrlWinHook : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;

    private readonly HookProc _proc;
    private IntPtr _hook;
    private bool _ctrl, _win, _armed, _spoiled;

    public event Action? Triggered;

    public CtrlWinHook() => _proc = Callback;

    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _ctrl = _win = _armed = _spoiled = false;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            int msg = (int)wParam;
            bool isCtrl = vk is 0x11 or 0xA2 or 0xA3;
            bool isWin = vk is 0x5B or 0x5C;

            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                if (isCtrl) _ctrl = true;
                else if (isWin) _win = true;
                else if (_ctrl || _win) _spoiled = true;
                if (_ctrl && _win) _armed = true;
            }
            else if (msg is WM_KEYUP or WM_SYSKEYUP)
            {
                if (isCtrl) _ctrl = false;
                else if (isWin) _win = false;

                if (!_ctrl && !_win)
                {
                    if (_armed && !_spoiled) Triggered?.Invoke();
                    _armed = _spoiled = false;
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose() => Stop();
}
