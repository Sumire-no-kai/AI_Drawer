using System.Runtime.InteropServices;
using AIDrawer.Core;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace AIDrawer;

internal sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 0xA1D0;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int WhGetMessage = 3;
    private const uint WmHotKey = 0x0312;
    private readonly Action _pressed;
    private readonly GetMessageHookProc _messageHookProc;
    private readonly uint _threadId;
    private IntPtr _messageHook;
    private bool _registered;

    internal GlobalHotKey(Window window, Action pressed)
    {
        _pressed = pressed;
        _messageHookProc = MessageHook;
        _threadId = GetWindowThreadProcessId(WindowNative.GetWindowHandle(window), out _);
        _messageHook = SetWindowsHookEx(WhGetMessage, _messageHookProc, IntPtr.Zero, _threadId);
    }

    internal bool TryApply(GlobalShortcutSettings settings, out int errorCode)
    {
        if (_registered)
        {
            _ = UnregisterHotKey(IntPtr.Zero, HotKeyId);
            _registered = false;
        }

        var normalized = GlobalShortcutPolicy.Normalize(settings);
        if (!normalized.Enabled)
        {
            errorCode = 0;
            return true;
        }

        if (_messageHook == IntPtr.Zero)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        var modifiers = ModNoRepeat;
        if (normalized.Modifiers.HasFlag(GlobalShortcutModifiers.Windows))
        {
            modifiers |= ModWin;
        }

        if (normalized.Modifiers.HasFlag(GlobalShortcutModifiers.Control))
        {
            modifiers |= ModControl;
        }

        if (normalized.Modifiers.HasFlag(GlobalShortcutModifiers.Alt))
        {
            modifiers |= ModAlt;
        }

        if (normalized.Modifiers.HasFlag(GlobalShortcutModifiers.Shift))
        {
            modifiers |= ModShift;
        }

        var virtualKey = normalized.Key[0];
        _registered = RegisterHotKey(IntPtr.Zero, HotKeyId, modifiers, virtualKey);
        errorCode = _registered ? 0 : Marshal.GetLastWin32Error();
        return _registered;
    }

    private IntPtr MessageHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && Marshal.PtrToStructure<NativeMessage>(lParam) is { Message: WmHotKey, WParam: var id }
            && id.ToInt32() == HotKeyId)
        {
            _pressed();
        }

        return CallNextHookEx(_messageHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_registered)
        {
            _ = UnregisterHotKey(IntPtr.Zero, HotKeyId);
            _registered = false;
        }

        if (_messageHook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_messageHook);
            _messageHook = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeMessage
    {
        internal readonly IntPtr WindowHandle;
        internal readonly uint Message;
        internal readonly IntPtr WParam;
        internal readonly IntPtr LParam;
        internal readonly uint Time;
        internal readonly NativePoint Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        internal readonly int X;
        internal readonly int Y;
    }

    private delegate IntPtr GetMessageHookProc(int code, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookType, GetMessageHookProc procedure, IntPtr moduleHandle, uint threadId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
