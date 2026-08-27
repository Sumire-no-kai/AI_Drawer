using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AIDrawer;

public static class Program
{
    private const string SingleInstanceKey = "AI Drawer";
    private static App? _application;

    internal static bool IsStartupActivation { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
#if DEBUG
        IsStartupActivation = ResolveStartupActivation(
            activationArguments.Kind,
            Environment.GetEnvironmentVariable("AI_DRAWER_TEST_DATA_ROOT"));
#else
        IsStartupActivation = activationArguments.Kind == ExtendedActivationKind.StartupTask;
#endif
        if (RedirectToExistingInstance(activationArguments))
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

    private static bool RedirectToExistingInstance(AppActivationArguments activationArguments)
    {
        var primaryInstance = AppInstance.FindOrRegisterForKey(ResolveSingleInstanceKey());
        if (primaryInstance.IsCurrent)
        {
            primaryInstance.Activated += OnActivated;
            return false;
        }

        RedirectActivation(activationArguments, primaryInstance);
        return true;
    }

    private static string ResolveSingleInstanceKey()
    {
#if DEBUG
        return ResolveSingleInstanceKey(Environment.GetEnvironmentVariable("AI_DRAWER_TEST_DATA_ROOT"));
#else
        return SingleInstanceKey;
#endif
    }

    internal static string ResolveSingleInstanceKey(string? testDataRoot)
    {
        if (string.IsNullOrWhiteSpace(testDataRoot)
            || !Path.IsPathFullyQualified(testDataRoot))
        {
            return SingleInstanceKey;
        }

        var normalizedRoot = Path.GetFullPath(testDataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot));
        return $"{SingleInstanceKey} Test {Convert.ToHexString(digest.AsSpan(0, 8))}";
    }

    internal static bool ResolveStartupActivation(
        ExtendedActivationKind activationKind,
        string? testDataRoot)
    {
        if (activationKind != ExtendedActivationKind.StartupTask)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(testDataRoot)
            || !Path.IsPathFullyQualified(testDataRoot);
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

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
#if DEBUG
        if (TryHandleIsolatedTestActivation())
        {
            return;
        }
#endif
        if (args.Kind != ExtendedActivationKind.StartupTask)
        {
            App.ActivateExistingWindow();
        }
    }

#if DEBUG
    private static bool TryHandleIsolatedTestActivation()
    {
        var testDataRoot = Environment.GetEnvironmentVariable("AI_DRAWER_TEST_DATA_ROOT");
        if (!IsIsolatedRuntimeAcceptanceRoot(testDataRoot))
        {
            return false;
        }

        var markerPath = Path.Combine(Path.GetFullPath(testDataRoot!), "profile-action.acceptance");
        try
        {
            if (!File.Exists(markerPath))
            {
                return false;
            }

            if (new FileInfo(markerPath).Length > 32)
            {
                File.Delete(markerPath);
                return false;
            }

            var action = File.ReadAllText(markerPath).Trim();
            File.Delete(markerPath);
            if (action is not ("clear-cache" or "reset-provider" or "reset-all"))
            {
                return false;
            }

            App.RunProfileActionForAcceptance(action, testDataRoot!);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
#endif

    internal static bool IsIsolatedRuntimeAcceptanceRoot(string? testDataRoot)
    {
        if (string.IsNullOrWhiteSpace(testDataRoot) || !Path.IsPathFullyQualified(testDataRoot))
        {
            return false;
        }

        try
        {
            var fullRoot = Path.GetFullPath(testDataRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullRoot).StartsWith(
                    "AI-Drawer-RuntimeAcceptance-",
                    StringComparison.Ordinal)
                || !Directory.Exists(fullRoot))
            {
                return false;
            }

            return (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) == 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

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
