using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using DancePilot.UI.Diagnostics;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DancePilot.UI;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        StartupLog.Write("MainWindow constructor start");
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Resize(new SizeInt32(1600, 960));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        AppWindow.SetIcon("Assets/AppIcon.ico");

        Activated += (_, _) =>
        {
            if (RootFrame.Content is null)
            {
                StartupLog.Write("MainWindow activated; queueing MainPage navigation");
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        StartupLog.Write("MainPage navigation start");
                        RootFrame.Navigate(typeof(MainPage));
                        StartupLog.Write("MainPage navigation complete");
                    }
                    catch (Exception ex)
                    {
                        StartupLog.Write(ex, "MainPage navigation failed");
                        throw;
                    }
                });
            }
        };

        StartupLog.Write("MainWindow constructor complete");
    }
}
