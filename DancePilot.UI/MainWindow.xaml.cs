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
    private bool _mainPageNavigationStarted;
    private bool _shutdownCompleted;
    private bool _shutdownInProgress;

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
        NavigateToMainPage("MainWindow constructor");

        AppWindow.Closing += AppWindow_Closing;
        Activated += (_, _) => NavigateToMainPage("MainWindow activated fallback");

        StartupLog.Write("MainWindow constructor complete");
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        args.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        try
        {
            await ShutdownMainPageAsync();
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "MainWindow shutdown cleanup failed");
        }
        finally
        {
            _shutdownCompleted = true;
            Close();
        }
    }

    private async Task ShutdownMainPageAsync()
    {
        if (RootFrame.Content is MainPage mainPage)
        {
            await mainPage.ShutdownAsync();
        }
    }

    private void NavigateToMainPage(string context)
    {
        if (_mainPageNavigationStarted || RootFrame.Content is not null)
        {
            return;
        }

        _mainPageNavigationStarted = true;
        try
        {
            StartupLog.Write($"{context}; MainPage navigation start");
            RootFrame.Navigate(typeof(MainPage));
            StartupLog.Write($"{context}; MainPage navigation complete");
        }
        catch (Exception ex)
        {
            _mainPageNavigationStarted = false;
            StartupLog.Write(ex, $"{context}; MainPage navigation failed");
            throw;
        }
    }
}
