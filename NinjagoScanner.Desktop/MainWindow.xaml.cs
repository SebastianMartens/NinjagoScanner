using Microsoft.UI.Xaml;

namespace NinjagoScanner_Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Title = "Ninjago Scanner Desktop";

        RootFrame.Navigate(typeof(MainPage));
    }
}
