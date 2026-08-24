using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace AIDock.CompatibilityLab;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1400, 900));

        RootFrame.Navigate(typeof(MainPage));
    }
}
