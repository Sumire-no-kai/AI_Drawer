using AIDrawer.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace AIDrawer;

public sealed partial class MainWindow : Window
{
    private readonly WindowsShellModule _shell;
    private bool _exitRequested;
    private bool _exitInProgress;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1180, 780));
        AppWindow.Closing += AppWindow_Closing;

        _shell = new WindowsShellModule(this);
        _shell.WindowPlacementChanged += WorkspacePage.UpdateWindowPlacement;
        _shell.VisibilityChanged += WorkspacePage.SetWindowVisibility;
        WorkspacePage.AttachShell(_shell);
        if (Program.IsStartupActivation)
        {
            TrayIcon.ForceCreate();
        }

        _ = InitializeShellAsync();
    }

    internal void ShowAndActivate()
    {
        _shell.ShowAndActivate();
    }

#if DEBUG
    internal Task<string> RunProfileActionForAcceptanceAsync(string action) =>
        WorkspacePage.RunProfileActionForAcceptanceAsync(action);
#endif

    internal async void ExitApplication()
    {
        if (_exitInProgress)
        {
            return;
        }

        _exitInProgress = true;
        try
        {
            await WorkspacePage.PersistAndDisposeWorkspaceAsync();
        }
        finally
        {
            _exitRequested = true;
            Close();
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested)
        {
            _shell.Dispose();
            TrayIcon.Dispose();
            return;
        }

        args.Cancel = true;
        if (_shell.CloseToTray)
        {
            _shell.Hide();
            return;
        }

        ExitApplication();
    }

    private void OpenFromTrayCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args) =>
        ShowAndActivate();

    private async void OpenDefaultProviderFromTrayCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        ShowAndActivate();
        await WorkspacePage.OpenDefaultProviderAsync();
    }

    private void SettingsFromTrayCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        ShowAndActivate();
        WorkspacePage.OpenSettings();
    }

    private void ExitFromTrayCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args) =>
        ExitApplication();

    private async Task InitializeShellAsync()
    {
        try
        {
            var settings = await WorkspaceSessionStore.LoadSettingsAsync();
            var registered = _shell.Apply(settings, out var errorCode);
            WorkspacePage.SetShortcutState(
                GlobalShortcutPolicy.Normalize(settings.GlobalShortcut),
                registered,
                errorCode);
        }
        catch (ObjectDisposedException)
        {
            // The window closed before the asynchronous settings read completed.
        }
    }
}
