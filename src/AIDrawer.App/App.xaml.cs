using Microsoft.UI.Xaml;

namespace AIDrawer;

public partial class App : Application
{
    private static App? _currentApp;
    private MainWindow? _window;

    public App()
    {
        _currentApp = this;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        if (!Program.IsStartupActivation)
        {
            _window.ShowAndActivate();
        }
    }

    internal static void ActivateExistingWindow()
    {
        var app = _currentApp;
        if (app?._window is null)
        {
            return;
        }

        _ = app._window.DispatcherQueue.TryEnqueue(app._window.ShowAndActivate);
    }

#if DEBUG
    internal static void RunProfileActionForAcceptance(string action, string testDataRoot)
    {
        var app = _currentApp;
        if (app?._window is null)
        {
            return;
        }

        _ = app._window.DispatcherQueue.TryEnqueue(async () =>
        {
            var resultPath = Path.Combine(testDataRoot, "profile-result.acceptance");
            try
            {
                var result = await app._window.RunProfileActionForAcceptanceAsync(action);
                File.WriteAllText(resultPath, result);
            }
            catch (Exception exception)
            {
                File.WriteAllText(resultPath, $"error:{exception.GetType().Name}");
            }
        });
    }
#endif

    internal static void ExitApplication()
    {
        _currentApp?._window?.ExitApplication();
    }
}
