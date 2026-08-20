using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace AIDrawer;

public sealed partial class MainWindow : Window
{
    private readonly GlobalHotKey _globalHotKey;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1180, 780));
        AppWindow.Closing += AppWindow_Closing;

        _globalHotKey = new GlobalHotKey(this, ShowAndActivate);
        WorkspacePage.SetShortcutState(_globalHotKey.TryRegister(out var errorCode), errorCode);
    }

    internal void ShowAndActivate()
    {
        WorkspacePage.SetWindowVisibility(true);
        AppWindow.Show();
        Activate();
    }

    internal void ExitApplication()
    {
        _exitRequested = true;
        WorkspacePage.DisposeWorkspace();
        Close();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested)
        {
            _globalHotKey.Dispose();
            TrayIcon.Dispose();
            return;
        }

        args.Cancel = true;
        WorkspacePage.SetWindowVisibility(false);
        AppWindow.Hide();
    }

    private void OpenFromTrayCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args) =>
        ShowAndActivate();

    private void ExitFromTrayCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args) =>
        ExitApplication();
}
