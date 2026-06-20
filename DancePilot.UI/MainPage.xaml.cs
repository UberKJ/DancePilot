using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using DancePilot.UI.ViewModels;
using DancePilot.UI.Diagnostics;
using DancePilot.Core.Models;
using DancePilot.Core.Spotify;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DancePilot.UI;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = CreateViewModel();

    public MainPage()
    {
        StartupLog.Write("MainPage constructor start");
        InitializeComponent();
        StartupLog.Write("MainPage constructor complete");
    }

    private static MainPageViewModel CreateViewModel()
    {
        StartupLog.Write("MainPageViewModel creation start");
        var viewModel = new MainPageViewModel();
        StartupLog.Write("MainPageViewModel creation complete");
        return viewModel;
    }

    private void SpotifyPreviewTracksList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SpotifyPreviewTracksList.SelectedItem is SpotifyTrackMetadata track)
        {
            ViewModel.QueueSpotifyTrackToActiveDeck(track);
        }
    }

    private void SpotifySearchResultsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SpotifySearchResultsList.SelectedItem is SpotifyTrackMetadata track)
        {
            ViewModel.QueueSpotifyTrackToActiveDeck(track);
        }
    }

    private void LocalMusicList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LocalMusicList.SelectedItem is LocalMusicTrack track)
        {
            ViewModel.QueueLocalTrackToActiveDeck(track);
        }
    }

    private async void ActiveDeckQueueList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await ViewModel.PlaySelectedActiveDeckQueueItemAsync();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var clientIdBox = new TextBox
        {
            Header = "Spotify Client ID",
            Text = ViewModel.SpotifyClientId,
            MinHeight = 40
        };
        clientIdBox.TextChanged += (_, _) => ViewModel.SpotifyClientId = clientIdBox.Text;

        var redirectUriBox = new TextBox
        {
            Header = "Redirect URI",
            Text = ViewModel.SpotifyRedirectUri,
            MinHeight = 40
        };
        redirectUriBox.TextChanged += (_, _) => ViewModel.SpotifyRedirectUri = redirectUriBox.Text;

        var playbackModeBox = new ComboBox
        {
            Header = "Playback Mode",
            ItemsSource = ViewModel.PlaybackModeOptions,
            SelectedItem = ViewModel.SelectedPlaybackMode,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        playbackModeBox.SelectionChanged += (_, _) =>
        {
            if (playbackModeBox.SelectedItem is string playbackMode)
            {
                ViewModel.SelectedPlaybackMode = playbackMode;
            }
        };

        var deviceList = new ListView
        {
            ItemsSource = ViewModel.SpotifyDevices,
            SelectedItem = ViewModel.SelectedSpotifyDevice,
            DisplayMemberPath = "DisplaySummary",
            MinHeight = 120,
            MaxHeight = 180
        };
        deviceList.SelectionChanged += (_, _) =>
        {
            if (deviceList.SelectedItem is SpotifyDevice device)
            {
                ViewModel.SelectedSpotifyDevice = device;
            }
        };

        var buttonGrid = new Grid
        {
            ColumnSpacing = 8
        };
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition());
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition());
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition());
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var loginButton = new Button { Content = "LOGIN", Command = ViewModel.SpotifyLoginCommand, MinHeight = 38 };
        var logoutButton = new Button { Content = "LOGOUT", Command = ViewModel.SpotifyLogoutCommand, MinHeight = 38 };
        var refreshButton = new Button { Content = "DEVICES", Command = ViewModel.RefreshSpotifyDevicesCommand, MinHeight = 38 };
        var transferButton = new Button { Content = "TRANSFER", Command = ViewModel.TransferSpotifyPlaybackCommand, MinHeight = 38 };
        Grid.SetColumn(logoutButton, 1);
        Grid.SetColumn(refreshButton, 2);
        Grid.SetColumn(transferButton, 3);
        buttonGrid.Children.Add(loginButton);
        buttonGrid.Children.Add(logoutButton);
        buttonGrid.Children.Add(refreshButton);
        buttonGrid.Children.Add(transferButton);

        var content = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 620
        };
        content.Children.Add(new TextBlock
        {
            Text = "Spotify and Audio Settings",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        content.Children.Add(new TextBlock
        {
            Text = ViewModel.SpotifyConnectionStatus,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(clientIdBox);
        content.Children.Add(redirectUriBox);
        content.Children.Add(buttonGrid);
        content.Children.Add(playbackModeBox);
        content.Children.Add(new TextBlock { Text = "Output Devices" });
        content.Children.Add(deviceList);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Settings",
            Content = content,
            CloseButtonText = "Close"
        };

        await dialog.ShowAsync();
    }
}
