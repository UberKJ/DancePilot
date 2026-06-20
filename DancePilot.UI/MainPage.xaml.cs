using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using DancePilot.UI.ViewModels;
using DancePilot.UI.Diagnostics;
using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DancePilot.UI;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = CreateViewModel();
    private IReadOnlyList<object> _draggedItems = [];

    public MainPage()
    {
        StartupLog.Write("MainPage constructor start");
        InitializeComponent();
        PlaybackPositionSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PlaybackPositionSlider_PointerPressed), true);
        PlaybackPositionSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PlaybackPositionSlider_PointerReleased), true);
        PlaybackPositionSlider.KeyUp += PlaybackPositionSlider_KeyUp;
        VolumeSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(VolumeSlider_PointerReleased), true);
        VolumeSlider.KeyUp += VolumeSlider_KeyUp;
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
        var tracks = GetSelectedItems<SpotifyTrackMetadata>(SpotifyPreviewTracksList);
        if (tracks.Count > 0)
        {
            ViewModel.QueueSpotifyTracksToActiveDeck(tracks);
        }
    }

    private void SpotifySearchResultsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var tracks = GetSelectedItems<SpotifyTrackMetadata>(SpotifySearchResultsList);
        if (tracks.Count > 0)
        {
            ViewModel.QueueSpotifyTracksToActiveDeck(tracks);
        }
    }

    private void LocalMusicList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var tracks = GetSelectedItems<LocalMusicTrack>(LocalMusicList);
        if (tracks.Count > 0)
        {
            ViewModel.QueueLocalTracksToActiveDeck(tracks);
        }
    }

    private async void QueueList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not ListView list)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        var item = FindQueueItemFromDropTarget(source) ?? list.SelectedItem as DancePilotQueueItem;
        if (item is null)
        {
            var deckName = ResolveDeckNameFromQueueList(list);
            ShowQueueClearFlyout(list, deckName, source);
            e.Handled = true;
            return;
        }

        ShowQueueItemActionsFlyout(list, item, source);
        e.Handled = true;
    }

    private async void AddSelectedSourceToDeckA_Click(object sender, RoutedEventArgs e)
    {
        await QueueSelectedUiItemsToDeckAsync("Deck A");
    }

    private async void AddSelectedSourceToDeckB_Click(object sender, RoutedEventArgs e)
    {
        await QueueSelectedUiItemsToDeckAsync("Deck B");
    }

    private async Task QueueSelectedUiItemsToDeckAsync(string deckName)
    {
        var spotifyTracks = GetSelectedItems<SpotifyTrackMetadata>(SpotifyPreviewTracksList)
            .Concat(GetSelectedItems<SpotifyTrackMetadata>(SpotifySearchResultsList))
            .ToList();
        var localTracks = GetSelectedItems<LocalMusicTrack>(LocalMusicList);
        var playlists = GetSelectedItems<SpotifyPlaylistSummary>(SpotifyPlaylistsList);

        await ViewModel.QueueSourceSelectionToDeckAsync(deckName, spotifyTracks, localTracks, playlists);
    }

    private static IReadOnlyList<T> GetSelectedItems<T>(ListView list)
    {
        var selected = list.SelectedItems.OfType<T>().ToList();
        if (selected.Count == 0 && list.SelectedItem is T item)
        {
            selected.Add(item);
        }

        return selected;
    }

    private void SourceList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedItems = ResolveDraggedItems(sender, e);
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetText("DancePilot source selection");
    }

    private void QueueList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedItems = ResolveDraggedItems(sender, e);
        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.SetText("DancePilot queue selection");
    }

    private static IReadOnlyList<object> ResolveDraggedItems(object sender, DragItemsStartingEventArgs e)
    {
        if (sender is ListView list && list.SelectedItems.Count > e.Items.Count)
        {
            return list.SelectedItems.Cast<object>().ToList();
        }

        return e.Items.Cast<object>().ToList();
    }

    private void DeckDropTarget_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _draggedItems.OfType<DancePilotQueueItem>().Any()
            ? DataPackageOperation.Move
            : DataPackageOperation.Copy;
    }

    private void QueueList_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _draggedItems.OfType<DancePilotQueueItem>().Any()
            ? DataPackageOperation.Move
            : DataPackageOperation.Copy;
    }

    private async void DeckADropTarget_Drop(object sender, DragEventArgs e)
    {
        await HandleDropToDeckAsync("Deck A", e);
    }

    private async void DeckBDropTarget_Drop(object sender, DragEventArgs e)
    {
        await HandleDropToDeckAsync("Deck B", e);
    }

    private async void DeckAQueueList_Drop(object sender, DragEventArgs e)
    {
        await HandleDropToDeckAsync("Deck A", e);
    }

    private async void DeckBQueueList_Drop(object sender, DragEventArgs e)
    {
        await HandleDropToDeckAsync("Deck B", e);
    }

    private async void SongQueueList_Drop(object sender, DragEventArgs e)
    {
        await HandleDropToDeckAsync(ViewModel.ActiveDeckName, e);
    }

    private async Task HandleDropToDeckAsync(string deckName, DragEventArgs e)
    {
        if (_draggedItems.Count == 0)
        {
            return;
        }

        var targetItem = FindQueueItemFromDropTarget(e.OriginalSource as DependencyObject);
        var queueItems = _draggedItems.OfType<DancePilotQueueItem>().ToList();
        if (queueItems.Count > 0)
        {
            ViewModel.MoveQueueItemsToDeck(queueItems, deckName, targetItem?.Id);
        }
        else
        {
            await ViewModel.QueueSourceSelectionToDeckAsync(
                deckName,
                _draggedItems.OfType<SpotifyTrackMetadata>(),
                _draggedItems.OfType<LocalMusicTrack>(),
                _draggedItems.OfType<SpotifyPlaylistSummary>());
        }

        _draggedItems = [];
        e.Handled = true;
    }

    private string ResolveDeckNameFromQueueList(ListView list)
    {
        if (ReferenceEquals(list, DeckAQueueList))
        {
            return "Deck A";
        }

        if (ReferenceEquals(list, DeckBQueueList))
        {
            return "Deck B";
        }

        return ViewModel.ActiveDeckName;
    }

    private static DancePilotQueueItem? FindQueueItemFromDropTarget(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: DancePilotQueueItem item })
            {
                return item;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ShowQueueItemActionsFlyout(ListView list, DancePilotQueueItem item, DependencyObject? source)
    {
        var deckName = string.IsNullOrWhiteSpace(item.DeckName) ? ViewModel.ActiveDeckName : item.DeckName;
        var flyout = new MenuFlyout();

        var playNowItem = new MenuFlyoutItem { Text = "Play now" };
        playNowItem.Click += async (_, _) => await ViewModel.PlayQueueItemAsync(item);
        flyout.Items.Add(playNowItem);

        var removeItem = new MenuFlyoutItem { Text = "Remove song from queue" };
        removeItem.Click += (_, _) => ViewModel.RemoveQueueItem(item);
        flyout.Items.Add(removeItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var clearItem = new MenuFlyoutItem { Text = $"Clear {deckName} queue" };
        clearItem.Click += (_, _) => ViewModel.ClearDeckQueue(deckName);
        flyout.Items.Add(clearItem);

        flyout.ShowAt(FindFlyoutTarget(source) ?? list);
    }

    private void ShowQueueClearFlyout(ListView list, string deckName, DependencyObject? source)
    {
        var flyout = new MenuFlyout();
        var clearItem = new MenuFlyoutItem { Text = $"Clear {deckName} queue" };
        clearItem.Click += (_, _) => ViewModel.ClearDeckQueue(deckName);
        flyout.Items.Add(clearItem);
        flyout.ShowAt(FindFlyoutTarget(source) ?? list);
    }

    private static FrameworkElement? FindFlyoutTarget(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                return element;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void PlaybackPositionSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.BeginSeekPositionChange();
    }

    private async void PlaybackPositionSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        await CommitPlaybackPositionSliderAsync();
    }

    private async void PlaybackPositionSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            await CommitPlaybackPositionSliderAsync();
            e.Handled = true;
        }
    }

    private async Task CommitPlaybackPositionSliderAsync()
    {
        await ViewModel.CommitSeekPositionChangeAsync();
    }

    private async void VolumeSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        await CommitVolumeSliderAsync();
    }

    private async void VolumeSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter
            or VirtualKey.Space
            or VirtualKey.Left
            or VirtualKey.Right
            or VirtualKey.Up
            or VirtualKey.Down
            or VirtualKey.Home
            or VirtualKey.End
            or VirtualKey.PageUp
            or VirtualKey.PageDown)
        {
            await CommitVolumeSliderAsync();
            e.Handled = true;
        }
    }

    private async Task CommitVolumeSliderAsync()
    {
        await ViewModel.CommitVolumeChangeAsync();
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

        var spotifySettings = new StackPanel
        {
            Spacing = 12
        };
        spotifySettings.Children.Add(clientIdBox);
        spotifySettings.Children.Add(redirectUriBox);
        spotifySettings.Children.Add(buttonGrid);
        spotifySettings.Children.Add(playbackModeBox);
        spotifySettings.Children.Add(new TextBlock { Text = "Output Devices" });
        spotifySettings.Children.Add(deviceList);

        var localSettings = new StackPanel
        {
            Spacing = 10
        };
        localSettings.Children.Add(new TextBlock
        {
            Text = ViewModel.LocalLibraryStatus,
            TextWrapping = TextWrapping.Wrap
        });
        localSettings.Children.Add(new Button
        {
            Content = "SCAN LOCAL MUSIC",
            Command = ViewModel.LoadLocalMusicCommand,
            MinHeight = 38
        });

        var content = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 720
        };
        content.Children.Add(new TextBlock
        {
            Text = "Music Source Settings",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        content.Children.Add(CreateSettingsSection("SPOTIFY", ViewModel.SpotifyConnectionStatus, spotifySettings));
        content.Children.Add(CreateSettingsSection("LOCAL PLAYBACK", "Windows music folder and playable local audio files.", localSettings));
        content.Children.Add(CreateSettingsSection("TIDAL", "Connector setup placeholder.", CreateDisabledSetupButton()));
        content.Children.Add(CreateSettingsSection("YOUTUBE", "Connector setup placeholder.", CreateDisabledSetupButton()));
        content.Children.Add(CreateSettingsSection("OTHER LOCAL APPS", "External app handoff and local playback providers.", CreateDisabledSetupButton()));

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Settings",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 640
            },
            CloseButtonText = "Close"
        };

        await dialog.ShowAsync();
    }

    private static Button CreateDisabledSetupButton() =>
        new()
        {
            Content = "COMING SOON",
            IsEnabled = false,
            MinHeight = 38
        };

    private static Border CreateSettingsSection(string title, string detail, UIElement body)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 181, 192, 201))
        });
        stack.Children.Add(body);

        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 39, 49, 59)),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 11, 16, 21)),
            Child = stack
        };
    }
}
