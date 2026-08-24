using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AIDrawer;

public static class Program
{
    private const string SingleInstanceKey = "AI Drawer";
    private static App? _application;

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectToExistingInstance())
        {
            return 0;
        }

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _application = new App();
        });

        return 0;
    }

    private static bool RedirectToExistingInstance()
    {
        var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        var primaryInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
        if (primaryInstance.IsCurrent)
        {
            primaryInstance.Activated += OnActivated;
            return false;
        }

        RedirectActivation(activationArguments, primaryInstance);
        return true;
    }

    private static void RedirectActivation(AppActivationArguments activationArguments, AppInstance primaryInstance)
    {
        var completionEvent = CreateEvent(IntPtr.Zero, true, false, null);
        if (completionEvent == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = Task.Run(() =>
            {
                try
                {
                    primaryInstance.RedirectActivationToAsync(activationArguments).AsTask().GetAwaiter().GetResult();
                }
                finally
                {
                    _ = SetEvent(completionEvent);
                }
            });

            _ = CoWaitForMultipleObjects(0, uint.MaxValue, 1, [completionEvent], out _);
        }
        finally
        {
            _ = CloseHandle(completionEvent);
        }
    }

    private static void OnActivated(object? sender, AppActivationArguments args) =>
        App.ActivateExistingWindow();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState, string? name);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr eventHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr objectHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(
        uint flags,
        uint timeout,
        uint handleCount,
        IntPtr[] handles,
        out uint index);
}
