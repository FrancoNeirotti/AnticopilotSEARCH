using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CopilotLauncher;

/// <summary>
/// Hook de teclado de bajo nivel. La tecla Copilot de Windows 11 envía Win + Shift + F23.
/// Interceptamos F23 para que Windows no abra la app de Copilot y mostramos el buscador.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_F23 = 0x86;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    // Tecla virtual sin asignar: se inyecta para que soltar Win no abra el menú Inicio.
    private const byte VK_UNASSIGNED = 0xE8;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _f23Down;

    public event Action? CopilotKeyPressed;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public bool Install()
    {
        using var module = Process.GetCurrentProcess().MainModule;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
            Log.Info($"SetWindowsHookEx falló: {Marshal.GetLastWin32Error()}");
        return _hookId != IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (data.vkCode == VK_F23)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    if (!_f23Down)
                    {
                        _f23Down = true;
                        bool winDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
                        if (winDown)
                        {
                            keybd_event(VK_UNASSIGNED, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_UNASSIGNED, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        }
                        try { CopilotKeyPressed?.Invoke(); } catch (Exception ex) { Log.Error(ex); }
                    }
                    return (IntPtr)1;
                }
                if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    _f23Down = false;
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
