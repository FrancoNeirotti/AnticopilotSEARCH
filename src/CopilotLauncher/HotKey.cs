using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CopilotLauncher;

/// <summary>Atajo global clásico (RegisterHotKey), usado como respaldo: Alt+Espacio.</summary>
public sealed class HotKey : IDisposable
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly int _id;
    private readonly uint _modifiers;
    private readonly uint _vk;
    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    public HotKey(IntPtr hwnd, int id, uint modifiers, uint vk)
    {
        _hwnd = hwnd;
        _id = id;
        _modifiers = modifiers;
        _vk = vk;
    }

    public bool Register()
    {
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        _registered = RegisterHotKey(_hwnd, _id, _modifiers | MOD_NOREPEAT, _vk);
        if (!_registered)
            Log.Info($"RegisterHotKey falló: {Marshal.GetLastWin32Error()}");
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(_hwnd, _id);
        _registered = false;
        _source?.RemoveHook(WndProc);
    }
}
