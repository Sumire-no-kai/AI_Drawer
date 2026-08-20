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
        _window.Activate();
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

    internal static void ExitApplication()
    {
        _currentApp?._window?.ExitApplication();
    }
}
