using System.Runtime.InteropServices;
using AIDrawer.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;
using Windows.Graphics;
using WinRT.Interop;

namespace AIDrawer;

internal sealed class WindowsShellModule : IDisposable
{
    private const string StartupTaskId = "AIDrawerStartupTask";
    private const int MinimumWindowWidth = 720;
    private const int MinimumWindowHeight = 540;
    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _windowHandle;
    private readonly GlobalHotKey _globalHotKey;
    private readonly DispatcherQueueTimer _placementTimer;
    private bool _suppressPlacementCapture;
    private bool _disposed;

    internal WindowsShellModule(Window window)
    {
        _window = window;
        _appWindow = window.AppWindow;
        _windowHandle = WindowNative.GetWindowHandle(window);
        _globalHotKey = new GlobalHotKey(window, ToggleFromHotKey);
        _placementTimer = window.DispatcherQueue.CreateTimer();
        _placementTimer.Interval = TimeSpan.FromMilliseconds(500);
        _placementTimer.IsRepeating = false;
        _placementTimer.Tick += PlacementTimer_Tick;
        _appWindow.Changed += AppWindow_Changed;
    }

    internal event Action<WindowPlacementSnapshot>? WindowPlacementChanged;

    internal event Action<bool>? VisibilityChanged;

    internal bool CloseToTray { get; private set; } = true;

    internal bool Apply(AppSettings settings, out int shortcutErrorCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CloseToTray = settings.CloseToTray;
        SetAlwaysOnTop(settings.AlwaysOnTop);
        if (settings.WindowPlacement is { } placement)
        {
            RestoreWindowPlacement(placement);
        }

        return _globalHotKey.TryApply(
            GlobalShortcutPolicy.Normalize(settings.GlobalShortcut),
            out shortcutErrorCode);
    }

    internal bool TryApplyShortcut(GlobalShortcutSettings settings, out int errorCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _globalHotKey.TryApply(GlobalShortcutPolicy.Normalize(settings), out errorCode);
    }

    internal void SetCloseToTray(bool closeToTray) => CloseToTray = closeToTray;

    internal void SetAlwaysOnTop(bool alwaysOnTop)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = alwaysOnTop;
        }
    }

    internal void ShowAndActivate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        _appWindow.Show();
        _window.Activate();
        VisibilityChanged?.Invoke(true);
    }

    internal void Hide()
    {
        _appWindow.Hide();
        VisibilityChanged?.Invoke(false);
    }

    internal WindowPlacementSnapshot? CaptureWindowPlacement()
    {
        if (_disposed
            || _appWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored })
        {
            return null;
        }

        var position = _appWindow.Position;
        var size = _appWindow.Size;
        return size.Width >= MinimumWindowWidth && size.Height >= MinimumWindowHeight
            ? new WindowPlacementSnapshot(position.X, position.Y, size.Width, size.Height)
            : null;
    }

    internal async Task<StartupRegistrationResult> GetStartupRegistrationAsync()
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskId);
            return StartupRegistrationResult.FromState(startupTask.State);
        }
        catch
        {
            return StartupRegistrationResult.Unavailable;
        }
    }

    internal async Task<StartupRegistrationResult> SetLaunchOnStartupAsync(bool enabled)
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                return StartupRegistrationResult.FromState(await startupTask.RequestEnableAsync());
            }

            startupTask.Disable();
            return StartupRegistrationResult.FromState(startupTask.State);
        }
        catch
        {
            return StartupRegistrationResult.Unavailable;
        }
    }

    internal void FlushWindowPlacement()
    {
        _placementTimer.Stop();
        if (CaptureWindowPlacement() is { } placement)
        {
            WindowPlacementChanged?.Invoke(placement);
        }
    }

    private void ToggleFromHotKey()
    {
        if (_appWindow.IsVisible && GetForegroundWindow() == _windowHandle)
        {
            Hide();
            return;
        }

        ShowAndActivate();
    }

    private void RestoreWindowPlacement(WindowPlacementSnapshot placement)
    {
        if (placement.Width < MinimumWindowWidth
            || placement.Height < MinimumWindowHeight
            || placement.Width > short.MaxValue
            || placement.Height > short.MaxValue)
        {
            return;
        }

        var desired = new RectInt32(placement.X, placement.Y, placement.Width, placement.Height);
        var displayArea = DisplayArea.GetFromRect(desired, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var minimumWidth = Math.Min(MinimumWindowWidth, workArea.Width);
        var minimumHeight = Math.Min(MinimumWindowHeight, workArea.Height);
        var width = Math.Clamp(placement.Width, minimumWidth, workArea.Width);
        var height = Math.Clamp(placement.Height, minimumHeight, workArea.Height);
        var x = Math.Clamp(placement.X, workArea.X, workArea.X + workArea.Width - width);
        var y = Math.Clamp(placement.Y, workArea.Y, workArea.Y + workArea.Height - height);

        _suppressPlacementCapture = true;
        try
        {
            _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
        finally
        {
            _suppressPlacementCapture = false;
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_disposed && !_suppressPlacementCapture && (args.DidPositionChange || args.DidSizeChange))
        {
            _placementTimer.Stop();
            _placementTimer.Start();
        }
    }

    private void PlacementTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (CaptureWindowPlacement() is { } placement)
        {
            WindowPlacementChanged?.Invoke(placement);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _placementTimer.Stop();
        _placementTimer.Tick -= PlacementTimer_Tick;
        _appWindow.Changed -= AppWindow_Changed;
        _globalHotKey.Dispose();
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

internal sealed record StartupRegistrationResult(
    StartupRegistrationState State,
    bool IsEnabled,
    bool IsAvailable,
    string Message)
{
    internal static StartupRegistrationResult Unavailable { get; } = new(
        StartupRegistrationState.Unavailable,
        false,
        false,
        "Available after AI Drawer is installed as a packaged application.");

    internal static StartupRegistrationResult FromState(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => new(
            StartupRegistrationState.Enabled,
            true,
            true,
            state == StartupTaskState.EnabledByPolicy
                ? "Enabled by your organization."
                : "AI Drawer will start hidden in the notification area after sign-in."),
        StartupTaskState.DisabledByUser => new(
            StartupRegistrationState.DisabledByUser,
            false,
            true,
            "Disabled in Windows Startup settings. Re-enable it there before trying again."),
        StartupTaskState.DisabledByPolicy => new(
            StartupRegistrationState.DisabledByPolicy,
            false,
            true,
            "Disabled by Windows or organization policy."),
        _ => new(
            StartupRegistrationState.Disabled,
            false,
            true,
            "AI Drawer will not start automatically.")
    };
}

internal enum StartupRegistrationState
{
    Unavailable,
    Disabled,
    Enabled,
    DisabledByUser,
    DisabledByPolicy
}
