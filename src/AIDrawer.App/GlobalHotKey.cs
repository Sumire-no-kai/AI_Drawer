using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace AIDrawer;

internal sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 0xA1D0;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint VirtualKeyA = 0x41;
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

    internal bool TryRegister(out int errorCode)
    {
        if (_messageHook == IntPtr.Zero)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        _registered = RegisterHotKey(IntPtr.Zero, HotKeyId, ModWin | ModShift, VirtualKeyA);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookType, GetMessageHookProc procedure, IntPtr moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
