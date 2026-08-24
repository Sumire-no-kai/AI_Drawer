using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AIDrawer;

internal sealed class MinimumWindowSizeController : IDisposable
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private static long _nextSubclassId;
    private readonly IntPtr _windowHandle;
    private readonly int _minimumWidth;
    private readonly int _minimumHeight;
    private readonly UIntPtr _subclassId;
    private readonly SubclassProc _subclassProc;
    private bool _disposed;

    internal MinimumWindowSizeController(IntPtr windowHandle, int minimumWidth, int minimumHeight)
    {
        ArgumentOutOfRangeException.ThrowIfZero(windowHandle);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumHeight);

        _windowHandle = windowHandle;
        _minimumWidth = minimumWidth;
        _minimumHeight = minimumHeight;
        _subclassId = (UIntPtr)Interlocked.Increment(ref _nextSubclassId);
        _subclassProc = WindowSubclassProc;

        if (!SetWindowSubclass(_windowHandle, _subclassProc, _subclassId, UIntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not apply the minimum window size.");
        }
    }

    private IntPtr WindowSubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        if (message == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var minimumWidth = _minimumWidth;
            var minimumHeight = _minimumHeight;

            var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
            {
                minimumWidth = Math.Min(minimumWidth, monitorInfo.WorkArea.Width);
                minimumHeight = Math.Min(minimumHeight, monitorInfo.WorkArea.Height);
            }

            limits.MinimumTrackSize.X = Math.Max(limits.MinimumTrackSize.X, minimumWidth);
            limits.MinimumTrackSize.Y = Math.Max(limits.MinimumTrackSize.Y, minimumHeight);
            Marshal.StructureToPtr(limits, lParam, false);
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = RemoveWindowSubclass(_windowHandle, _subclassProc, _subclassId);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        internal NativePoint Reserved;
        internal NativePoint MaximumSize;
        internal NativePoint MaximumPosition;
        internal NativePoint MinimumTrackSize;
        internal NativePoint MaximumTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal NativeRect MonitorArea;
        internal NativeRect WorkArea;
        internal uint Flags;
    }

    private delegate IntPtr SubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProc subclassProc,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr windowHandle,
        SubclassProc subclassProc,
        UIntPtr subclassId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
