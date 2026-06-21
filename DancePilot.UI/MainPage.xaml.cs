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
using Windows.Storage.Pickers;
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
    private ListView? _lastDragSourceList;
    private int _nextDragOperationId;
    private int? _activeDragOperationId;
    private int? _consumedDragOperationId;

    public MainPage()
    {
        StartupLog.Write("MainPage constructor start");
        InitializeComponent();
        PlaybackPositionSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PlaybackPositionSlider_PointerPressed), true);
        PlaybackPositionSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PlaybackPositionSlider_PointerReleased), true);
        PlaybackPositionSlider.KeyUp += PlaybackPositionSlider_KeyUp;
        VolumeSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(VolumeSlider_PointerReleased), true);
        VolumeSlider.KeyUp += VolumeSlider_KeyUp;
        DeckAVolumeSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(DeckAVolumeSlider_PointerReleased), true);
        DeckAVolumeSlider.KeyUp += DeckAVolumeSlider_KeyUp;
        DeckBVolumeSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(DeckBVolumeSlider_PointerReleased), true);
        DeckBVolumeSlider.KeyUp += DeckBVolumeSlider_KeyUp;
        Unloaded += MainPage_Unloaded;
        StartupLog.Write("MainPage constructor complete");
    }

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveSessionStateNowAsync();
    }

    public Task ShutdownAsync() => ViewModel.ShutdownAsync();

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

    private async void SpotifySearchBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.SearchSpotifyTracksCommand.ExecuteAsync(null);
    }

    private async void LocalMusicSearchBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.SearchLocalMusicCommand.ExecuteAsync(null);
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
        var localPlaylists = GetSelectedItems<LocalMusicPlaylist>(LocalPlaylistsList);

        await ViewModel.QueueSourceSelectionToDeckAsync(
            deckName,
            spotifyTracks,
            localTracks,
            playlists,
            localPlaylists: localPlaylists);
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
        BeginDragOperation(sender, e);
        StartupLog.Write($"Source drag started from {DescribeList(_lastDragSourceList)} with {_draggedItems.Count} item(s): {DescribeItems(_draggedItems)}");
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetText("DancePilot source selection");
    }

    private void QueueList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        BeginDragOperation(sender, e);
        StartupLog.Write($"Queue drag started from {DescribeList(_lastDragSourceList)} with {_draggedItems.Count} item(s): {DescribeItems(_draggedItems)}");
        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.SetText("DancePilot queue selection");
    }

    private void BeginDragOperation(object sender, DragItemsStartingEventArgs e)
    {
        _activeDragOperationId = ++_nextDragOperationId;
        _consumedDragOperationId = null;
        _lastDragSourceList = sender as ListView;
        _draggedItems = ResolveDraggedItems(sender, e);
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
        if (IsActiveDragConsumed())
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        var dropItems = ResolveDropItems();
        e.AcceptedOperation = dropItems.OfType<DancePilotQueueItem>().Any()
            ? DataPackageOperation.Move
            : DataPackageOperation.Copy;
        e.Handled = true;
    }

    private void QueueList_DragOver(object sender, DragEventArgs e)
    {
        if (IsActiveDragConsumed())
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        var dropItems = ResolveDropItems();
        e.AcceptedOperation = dropItems.OfType<DancePilotQueueItem>().Any()
            ? DataPackageOperation.Move
            : DataPackageOperation.Copy;
        e.Handled = true;
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
        await HandleDropToDeckAsync(ViewModel.QueueViewDeckName, e);
    }

    private async Task HandleDropToDeckAsync(string deckName, DragEventArgs e)
    {
        e.Handled = true;

        if (IsActiveDragConsumed())
        {
            StartupLog.Write($"Ignored already consumed drop to {deckName}.");
            return;
        }

        var dragSourceList = _lastDragSourceList;
        var dragOperationId = _activeDragOperationId;
        var draggedItems = ResolveDropItems().ToList();

        StartupLog.Write($"Drop to {deckName}: {draggedItems.Count} item(s): {DescribeItems(draggedItems)}");
        if (draggedItems.Count == 0)
        {
            return;
        }

        var targetItem = FindQueueItemFromDropTarget(e.OriginalSource as DependencyObject);
        ConsumeDropPayload(dragSourceList, dragOperationId);

        var queueItems = draggedItems.OfType<DancePilotQueueItem>().ToList();
        if (queueItems.Count > 0)
        {
            ViewModel.MoveQueueItemsToDeck(queueItems, deckName, targetItem?.Id);
        }
        else
        {
            await ViewModel.QueueSourceSelectionToDeckAsync(
                deckName,
                draggedItems.OfType<SpotifyTrackMetadata>(),
                draggedItems.OfType<LocalMusicTrack>(),
                draggedItems.OfType<SpotifyPlaylistSummary>(),
                targetItem?.Id,
                insertAtTop: targetItem is null,
                localPlaylists: draggedItems.OfType<LocalMusicPlaylist>());
        }
    }

    private bool IsActiveDragConsumed() =>
        _activeDragOperationId is not null
        && _consumedDragOperationId == _activeDragOperationId;

    private void ConsumeDropPayload(ListView? dragSourceList, int? dragOperationId)
    {
        _consumedDragOperationId = dragOperationId ?? _activeDragOperationId;
        _draggedItems = [];
        _lastDragSourceList = null;
        ClearListSelection(dragSourceList);
    }

    private static void ClearListSelection(ListView? list)
    {
        if (list is null)
        {
            return;
        }

        list.SelectedItems.Clear();
        list.SelectedItem = null;
    }

    private IReadOnlyList<object> ResolveDropItems()
    {
        if (IsActiveDragConsumed())
        {
            return [];
        }

        if (_draggedItems.Count > 0)
        {
            return _draggedItems;
        }

        return ResolveSelectedItemsFromList(_lastDragSourceList);
    }

    private IReadOnlyList<object> ResolveSelectedItemsFromList(ListView? list)
    {
        if (list is null)
        {
            return [];
        }

        if (ReferenceEquals(list, SpotifyPreviewTracksList) || ReferenceEquals(list, SpotifySearchResultsList))
        {
            return GetSelectedItems<SpotifyTrackMetadata>(list).Cast<object>().ToList();
        }

        if (ReferenceEquals(list, SpotifyPlaylistsList))
        {
            return GetSelectedItems<SpotifyPlaylistSummary>(list).Cast<object>().ToList();
        }

        if (ReferenceEquals(list, LocalMusicList))
        {
            return GetSelectedItems<LocalMusicTrack>(list).Cast<object>().ToList();
        }

        if (ReferenceEquals(list, LocalPlaylistsList))
        {
            return GetSelectedItems<LocalMusicPlaylist>(list).Cast<object>().ToList();
        }

        if (ReferenceEquals(list, DeckAQueueList) || ReferenceEquals(list, DeckBQueueList) || ReferenceEquals(list, SongQueueList))
        {
            return GetSelectedItems<DancePilotQueueItem>(list).Cast<object>().ToList();
        }

        return [];
    }

    private static string DescribeList(ListView? list) =>
        list?.Name ?? "unknown list";

    private static string DescribeItems(IEnumerable<object> items) =>
        string.Join(", ", items.Select(item => item.GetType().Name).Distinct());

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

        return ViewModel.QueueViewDeckName;
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

    private async void DeckAVolumeSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        await CommitDeckVolumeSliderAsync("Deck A");
    }

    private async void DeckAVolumeSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (IsSliderCommitKey(e.Key))
        {
            await CommitDeckVolumeSliderAsync("Deck A");
            e.Handled = true;
        }
    }

    private async void DeckBVolumeSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        await CommitDeckVolumeSliderAsync("Deck B");
    }

    private async void DeckBVolumeSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (IsSliderCommitKey(e.Key))
        {
            await CommitDeckVolumeSliderAsync("Deck B");
            e.Handled = true;
        }
    }

    private async Task CommitDeckVolumeSliderAsync(string deckName)
    {
        await ViewModel.CommitDeckVolumeChangeAsync(deckName);
    }

    private static bool IsSliderCommitKey(VirtualKey key) =>
        key is VirtualKey.Enter
            or VirtualKey.Space
            or VirtualKey.Left
            or VirtualKey.Right
            or VirtualKey.Up
            or VirtualKey.Down
            or VirtualKey.Home
            or VirtualKey.End
            or VirtualKey.PageUp
            or VirtualKey.PageDown;

    private async void BrowseLocalMusicFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        await ViewModel.LoadLocalMusicFromFolderAsync(folder.Path);
    }

    private async void LocalPlaylistMenu_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadLocalPlaylistsCommand.ExecuteAsync(null);

        var nameBox = new TextBox
        {
            Header = "Playlist Name",
            PlaceholderText = "New local playlist",
            MinHeight = 40
        };

        var playlistList = new ListView
        {
            ItemsSource = ViewModel.LocalMusicPlaylists,
            SelectedItem = ViewModel.SelectedLocalMusicPlaylist,
            DisplayMemberPath = "DisplayName",
            SelectionMode = ListViewSelectionMode.Single,
            MinHeight = 160,
            MaxHeight = 220
        };
        playlistList.SelectionChanged += (_, _) =>
        {
            if (playlistList.SelectedItem is LocalMusicPlaylist playlist)
            {
                ViewModel.SelectedLocalMusicPlaylist = playlist;
            }
        };

        var statusText = new TextBlock
        {
            Text = ViewModel.LocalPlaylistStatus,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 181, 192, 201))
        };

        async Task RefreshMenuStatusAsync()
        {
            await ViewModel.LoadLocalPlaylistsCommand.ExecuteAsync(null);
            playlistList.SelectedItem = ViewModel.SelectedLocalMusicPlaylist;
            statusText.Text = ViewModel.LocalPlaylistStatus;
        }

        var createButton = new Button { Content = "CREATE / SELECT", MinHeight = 38 };
        createButton.Click += async (_, _) =>
        {
            var playlist = await ViewModel.CreateLocalPlaylistAsync(nameBox.Text);
            if (playlist is not null)
            {
                playlistList.SelectedItem = ViewModel.SelectedLocalMusicPlaylist;
            }

            statusText.Text = ViewModel.LocalPlaylistStatus;
        };

        var refreshButton = new Button { Content = "REFRESH", MinHeight = 38 };
        refreshButton.Click += async (_, _) => await RefreshMenuStatusAsync();

        var deleteButton = new Button { Content = "DELETE", MinHeight = 38 };
        deleteButton.Click += async (_, _) =>
        {
            await ViewModel.DeleteSelectedLocalPlaylistCommand.ExecuteAsync(null);
            playlistList.SelectedItem = ViewModel.SelectedLocalMusicPlaylist;
            statusText.Text = ViewModel.LocalPlaylistStatus;
        };

        var addSelectedButton = new Button { Content = "ADD SELECTED SONGS", MinHeight = 38 };
        addSelectedButton.Click += async (_, _) =>
        {
            await ViewModel.AddLocalTracksToSelectedPlaylistAsync(GetSelectedItems<LocalMusicTrack>(LocalMusicList));
            playlistList.SelectedItem = ViewModel.SelectedLocalMusicPlaylist;
            statusText.Text = ViewModel.LocalPlaylistStatus;
        };

        var addFilteredButton = new Button { Content = "ADD FILTERED SONGS", MinHeight = 38 };
        addFilteredButton.Click += async (_, _) =>
        {
            await ViewModel.AddFilteredLocalMusicToPlaylistCommand.ExecuteAsync(null);
            playlistList.SelectedItem = ViewModel.SelectedLocalMusicPlaylist;
            statusText.Text = ViewModel.LocalPlaylistStatus;
        };

        var addDeckAButton = new Button { Content = "ADD TO DECK A", MinHeight = 38 };
        addDeckAButton.Click += async (_, _) =>
        {
            await ViewModel.QueueSelectedLocalPlaylistToDeckAsync("Deck A");
            statusText.Text = ViewModel.LocalPlaylistStatus;
        };

        var addDeckBButton = new Button { Content = "ADD TO DECK B", MinHeight = 38 };
        addDeckBButton.Click += async (_, _) =>
        {
            await ViewModel.QueueSelectedLocalPlaylistToDeckAsync("Deck B");
            statusText.Text = ViewModel.LocalPlaylistStatus;
        };

        static Grid ButtonGrid(params Button[] buttons)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            for (var index = 0; index < buttons.Length; index++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(buttons[index], index);
                grid.Children.Add(buttons[index]);
            }

            return grid;
        }

        var content = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 680
        };
        content.Children.Add(nameBox);
        content.Children.Add(ButtonGrid(createButton, refreshButton, deleteButton));
        content.Children.Add(new TextBlock
        {
            Text = "Saved Local Playlists",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        content.Children.Add(playlistList);
        content.Children.Add(ButtonGrid(addSelectedButton, addFilteredButton));
        content.Children.Add(ButtonGrid(addDeckAButton, addDeckBButton));
        content.Children.Add(statusText);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Local Playlist Menu",
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = 620
            },
            CloseButtonText = "Close"
        };

        await dialog.ShowAsync();
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
