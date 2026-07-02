using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using DancePilot.Services.MockData;
using DancePilot.Services.LocalMusic;
using DancePilot.Services.Media;
using DancePilot.Services.Spotify;
using DancePilot.Services.Spotify.Playback;
using DancePilot.UI.Composition;
using DancePilot.UI.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace DancePilot.UI.ViewModels;

public sealed partial class MainPageViewModel
{
    private void SelectDeck(string deckName)
    {
        ActiveDeckName = deckName;
        RefreshActiveDeckQueue();
        UpdateNextUpFromDecks();
        SpotifyOperationMessage = $"{ActiveDeckName} selected. Cue a song here, then select it for transition.";
    }

    private void ShowDeckQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        QueueViewDeckName = normalizedDeckName;
        CacheDeckAlbumArtForQueue(normalizedDeckName);
        SpotifyOperationMessage = $"Viewing {normalizedDeckName} queue. Playback and transition cursor are unchanged.";
    }

    private async Task<DancePilotQueueItem> PrepareQueueItemAlbumArtForPlaybackAsync(
        string deckName,
        DancePilotQueueItem queueItem)
    {
        await CacheQueuedAlbumArtAsync(deckName, queueItem.Id);
        return QueueForDeck(NormalizeDeckName(deckName)).FirstOrDefault(item => item.Id == queueItem.Id)
            ?? queueItem;
    }

    private async Task<LocalMusicTrack?> ResolveLocalQueueTrackAsync(DancePilotQueueItem queueItem)
    {
        var localPath = queueItem.ResolvedLocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return null;
        }

        var indexedTrack = await _localMusicRepository.GetTrackByPathAsync(localPath);
        if (indexedTrack is not null)
        {
            return indexedTrack;
        }

        var loadedTrack = _allLocalMusicTracks.FirstOrDefault(track =>
            string.Equals(track.FilePath, localPath, StringComparison.OrdinalIgnoreCase));
        if (loadedTrack is not null)
        {
            return loadedTrack;
        }

        if (!File.Exists(localPath))
        {
            return null;
        }

        var tracks = await _localMusicLibraryService.LoadFromFilePathsAsync([localPath]);
        var localTrack = tracks.FirstOrDefault();
        if (localTrack is null)
        {
            return null;
        }

        _allLocalMusicTracks.Add(localTrack);
        return localTrack;
    }

    private LocalMusicTrack ApplyQueueAlbumArtToLocalTrack(DancePilotQueueItem queueItem, LocalMusicTrack track)
    {
        if (!HasUsableAlbumArtSource(queueItem.AlbumArtUrl)
            || string.Equals(track.AlbumArtUrl, queueItem.AlbumArtUrl, StringComparison.OrdinalIgnoreCase))
        {
            return track;
        }

        var updatedTrack = track with { AlbumArtUrl = queueItem.AlbumArtUrl };
        var libraryIndex = _allLocalMusicTracks.FindIndex(existing =>
            string.Equals(existing.FilePath, updatedTrack.FilePath, StringComparison.OrdinalIgnoreCase));
        if (libraryIndex >= 0)
        {
            _allLocalMusicTracks[libraryIndex] = updatedTrack;
        }

        var visibleIndex = LocalMusicTracks
            .Select((existing, index) => new { Track = existing, Index = index })
            .FirstOrDefault(existing =>
                string.Equals(existing.Track.FilePath, updatedTrack.FilePath, StringComparison.OrdinalIgnoreCase))
            ?.Index;
        if (visibleIndex is int index)
        {
            LocalMusicTracks[index] = updatedTrack;
        }

        return updatedTrack;
    }


    private DancePilotQueueItem? FindTransitionTarget(string? deckName = null)
    {
        if (!string.IsNullOrWhiteSpace(deckName))
        {
            var normalizedDeckName = NormalizeDeckName(deckName);
            return ResolveTransitionDecision(
                IsQueueItemRoutableForTransition,
                requestedModeOverride: DancePilotTransitionModes.SameDeck,
                currentDeckNameOverride: normalizedDeckName,
                currentItemIdOverride: string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal)
                    ? _playingDeckQueueItemId
                    : -1).ChosenItem;
        }

        return ResolveTransitionDecision(IsQueueItemRoutableForTransition).ChosenItem;
    }

    private DancePilotTransitionDecision ResolveTransitionDecision(
        Func<DancePilotQueueItem, bool>? isPlayable = null,
        string? requestedModeOverride = null,
        string? currentDeckNameOverride = null,
        int? currentItemIdOverride = null)
    {
        var currentDeckName = NormalizeDeckName(currentDeckNameOverride ?? _playingDeckName);
        return DancePilotTransitionPlanner.Decide(new DancePilotTransitionRequest
        {
            CurrentDeckName = currentDeckName,
            RequestedMode = requestedModeOverride ?? ResolveRequestedTransitionMode(),
            CurrentItemId = currentItemIdOverride ?? _playingDeckQueueItemId,
            DeckAQueue = _deckAQueue,
            DeckBQueue = _deckBQueue,
            SelectedDeckAItemId = _selectedDeckQueueItemIds.GetValueOrDefault("Deck A"),
            SelectedDeckBItemId = _selectedDeckQueueItemIds.GetValueOrDefault("Deck B"),
            LastPlayedDeckAItemId = _lastPlayedDeckQueueItemIds.GetValueOrDefault("Deck A"),
            LastPlayedDeckBItemId = _lastPlayedDeckQueueItemIds.GetValueOrDefault("Deck B"),
            IsPlayable = isPlayable
        });
    }

    private static bool IsQueueItemRoutableForTransition(DancePilotQueueItem item) =>
        DancePilotPlaybackRouter.Resolve(item).IsPlayable;

    private string ResolveRequestedTransitionMode() =>
        DeckTransitionEnabled
            ? DancePilotTransitionModes.Normalize(SelectedTransitionMode)
            : DancePilotTransitionModes.Off;

    private string ResolveManualTransitionMode()
    {
        var normalized = DancePilotTransitionModes.Normalize(SelectedTransitionMode);
        return normalized == DancePilotTransitionModes.Off
            ? DancePilotTransitionModes.Auto
            : normalized;
    }

    private DancePilotQueueItem? GetNextDeckQueueItem(
        string deckName,
        bool includeFirstIfNoLastPlayed = false,
        bool skipSamePlaybackSource = false)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count == 0)
        {
            return null;
        }

        var lastPlayedId = _lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
        if (lastPlayedId is null
            && string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal)
            && _playingDeckQueueItemId is not null)
        {
            lastPlayedId = _playingDeckQueueItemId;
        }

        if (lastPlayedId is null)
        {
            return includeFirstIfNoLastPlayed ? queue.FirstOrDefault() : null;
        }

        var currentIndex = queue.FindIndex(item => item.Id == lastPlayedId.Value);
        if (currentIndex < 0)
        {
            return includeFirstIfNoLastPlayed ? queue.FirstOrDefault() : null;
        }

        if (currentIndex + 1 >= queue.Count)
        {
            return null;
        }

        var currentItem = queue[currentIndex];
        for (var index = currentIndex + 1; index < queue.Count; index++)
        {
            var nextItem = queue[index];
            if (!skipSamePlaybackSource || !IsSamePlaybackSource(currentItem, nextItem))
            {
                return nextItem;
            }
        }

        return null;
    }

    private DancePilotQueueItem? GetQueueItemAfter(
        string deckName,
        int itemId,
        bool skipSamePlaybackSource = false)
    {
        var queue = QueueForDeck(NormalizeDeckName(deckName));
        var currentIndex = queue.FindIndex(item => item.Id == itemId);
        if (currentIndex < 0)
        {
            return null;
        }

        var currentItem = queue[currentIndex];
        for (var index = currentIndex + 1; index < queue.Count; index++)
        {
            var nextItem = queue[index];
            if (!skipSamePlaybackSource || !IsSamePlaybackSource(currentItem, nextItem))
            {
                return nextItem;
            }
        }

        return null;
    }

    private DancePilotQueueItem? GetQueueItemBefore(string deckName, int itemId)
    {
        var queue = QueueForDeck(NormalizeDeckName(deckName));
        var currentIndex = queue.FindIndex(item => item.Id == itemId);
        return currentIndex > 0 ? queue[currentIndex - 1] : null;
    }

    private void RetainRestoredLocalPlaybackCursor()
    {
        var restoredItem = ResolveLoadedDeckItem(_playingDeckName);
        if (restoredItem?.Source != SongSources.Local)
        {
            RepairRestoredUnconsumedLocalQueueHead("Deck A");
            RepairRestoredUnconsumedLocalQueueHead("Deck B");
            return;
        }

        _selectedDeckQueueItemIds[_playingDeckName] = restoredItem.Id;
        if (_lastPlayedDeckQueueItemIds.GetValueOrDefault(_playingDeckName) == restoredItem.Id)
        {
            _lastPlayedDeckQueueItemIds[_playingDeckName] = GetQueueItemBefore(_playingDeckName, restoredItem.Id)?.Id;
        }

        SelectedPlaybackMode = SpotifyPlaybackModes.LocalFilesFuture;
        RepairRestoredUnconsumedLocalQueueHead(OppositeDeckName(_playingDeckName));
    }

    private void RepairRestoredUnconsumedLocalQueueHead(string deckName)
    {
        if (!RemovePlayedQueueItems)
        {
            return;
        }

        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count < 2 || queue[0].Source != SongSources.Local)
        {
            return;
        }

        var queueHead = queue[0];
        var playingIndex = string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal)
            && _playingDeckQueueItemId is int playingItemId
            ? queue.FindIndex(item => item.Id == playingItemId)
            : -1;
        var selectedIndex = _selectedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName) is int selectedItemId
            ? queue.FindIndex(item => item.Id == selectedItemId)
            : -1;
        var lastPlayedIndex = _lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName) is int lastPlayedItemId
            ? queue.FindIndex(item => item.Id == lastPlayedItemId)
            : -1;

        if (playingIndex <= 0 && selectedIndex <= 0 && lastPlayedIndex <= 0)
        {
            return;
        }

        _selectedDeckQueueItemIds[normalizedDeckName] = queueHead.Id;
        _lastPlayedDeckQueueItemIds[normalizedDeckName] = null;
        if (playingIndex > 0)
        {
            _playingDeckQueueItemId = queueHead.Id;
            SelectedPlaybackMode = SpotifyPlaybackModes.LocalFilesFuture;
        }

        StartupLog.Write($"Repaired restored local queue head on {normalizedDeckName}: {queueHead.Title}");
    }

    private string ResolveTransitionDeckName()
    {
        var requestedMode = ResolveRequestedTransitionMode();
        return requestedMode == DancePilotTransitionModes.SameDeck
            || requestedMode == DancePilotTransitionModes.Off
            ? _playingDeckName
            : OppositeDeckName(_playingDeckName);
    }

    private DancePilotQueueItem? FindSelectedDeckQueueItem(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (!_selectedDeckQueueItemIds.TryGetValue(normalizedDeckName, out var selectedId) || selectedId is null)
        {
            return null;
        }

        return QueueForDeck(normalizedDeckName).FirstOrDefault(item => item.Id == selectedId.Value);
    }

    private DancePilotQueueItem? FindDeckItemForUserPlay(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (IsDeckAudiblyPlaying(normalizedDeckName))
        {
            return null;
        }

        var queue = QueueForDeck(normalizedDeckName);
        var selectedId = _selectedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
        var selectedVisibleItem = ResolveSelectedVisibleDeckQueueItem(normalizedDeckName);
        var selectedItem = selectedVisibleItem ?? FindSelectedDeckQueueItem(normalizedDeckName);
        DancePilotQueueItem? chosenItem;
        if (selectedItem is not null)
        {
            chosenItem = selectedItem;
        }
        else
        {
            var selection = DancePilotDeckPlaySelector.FindItemForUserPlay(
                queue,
                selectedId,
                isAudiblyPlaying: false,
                lastPlayedId: _lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName));
            if (selection.ShouldClearSelectedId)
            {
                _selectedDeckQueueItemIds[normalizedDeckName] = null;
            }

            chosenItem = selection.Item;
        }

        if (chosenItem is not null)
        {
            LogDeckPlaySelection(normalizedDeckName, selectedId, chosenItem);
        }

        return chosenItem;
    }

    private DancePilotQueueItem? ResolveSelectedVisibleDeckQueueItem(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (!string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal)
            || SelectedActiveDeckQueueItem is null
            || !string.Equals(SelectedActiveDeckQueueItem.DeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            return null;
        }

        return QueueForDeck(normalizedDeckName).FirstOrDefault(item => item.Id == SelectedActiveDeckQueueItem.Id);
    }

    private void LogDeckPlaySelection(string deckName, int? selectedId, DancePilotQueueItem chosenItem)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        StartupLog.Write(
            "Deck play selection: "
            + $"deck={normalizedDeckName}; "
            + $"selectedId={selectedId?.ToString() ?? "<null>"}; "
            + $"lastPlayedId={_lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName)?.ToString() ?? "<null>"}; "
            + $"chosenId={chosenItem.Id}; "
            + $"title={chosenItem.Title}; "
            + $"source={chosenItem.Source}; "
            + $"localPath={chosenItem.LocalPath ?? "<null>"}; "
            + $"externalUri={chosenItem.ExternalUri}");
    }

    private DancePilotQueueItem? FindSelectedPendingDeckQueueItem(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var selectedItem = FindSelectedDeckQueueItem(normalizedDeckName);
        if (selectedItem is null)
        {
            return null;
        }

        if (string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal)
            && _playingDeckQueueItemId == selectedItem.Id)
        {
            return null;
        }

        var lastPlayedId = _lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
        return lastPlayedId == selectedItem.Id ? null : selectedItem;
    }

    private static bool IsSamePlaybackSource(DancePilotQueueItem first, DancePilotQueueItem second)
    {
        if (!string.Equals(first.Source, second.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(first.SourceIdentity)
            && !string.IsNullOrWhiteSpace(second.SourceIdentity))
        {
            return string.Equals(first.SourceIdentity, second.SourceIdentity, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(first.Title, second.Title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(first.Artist, second.Artist, StringComparison.OrdinalIgnoreCase);
    }

    private int? GetLastPlayedDeckQueueItemId(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        return _lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
    }

    private static string OppositeDeckName(string deckName) =>
        deckName == "Deck B" ? "Deck A" : "Deck B";

    private List<DancePilotQueueItem> QueueForDeck(string deckName) =>
        deckName == "Deck B" ? _deckBQueue : _deckAQueue;

    private bool IsDeckPlaying(string deckName) =>
        IsDeckAudiblyPlaying(deckName);

    private bool IsDeckLoaded(string deckName) =>
        string.Equals(_playingDeckName, NormalizeDeckName(deckName), StringComparison.Ordinal)
        && _playingDeckQueueItemId is not null;

    private bool IsDeckAudiblyPlaying(string deckName) =>
        IsDeckLoaded(deckName) && IsPlaybackPlaying;

    private DancePilotQueueItem? ResolveNextDeckDisplayItem(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count == 0)
        {
            return null;
        }

        var next = FindSelectedPendingDeckQueueItem(normalizedDeckName)
            ?? GetNextDeckQueueItem(
                normalizedDeckName,
                includeFirstIfNoLastPlayed: true,
                skipSamePlaybackSource: true);
        if (next is not null)
        {
            return next;
        }

        return RemovePlayedQueueItems ? null : queue.FirstOrDefault();
    }

    private DancePilotQueueItem? ResolveDeckDisplayItem(string deckName) =>
        ResolveLoadedDeckItem(deckName) ?? ResolveNextDeckDisplayItem(deckName);

    private DancePilotQueueItem? ResolvePlayingDeckItem(string deckName) =>
        ResolveLoadedDeckItem(deckName);

    private DancePilotQueueItem? ResolveLoadedDeckItem(string deckName) =>
        IsDeckLoaded(deckName)
            ? QueueForDeck(NormalizeDeckName(deckName)).FirstOrDefault(item => item.Id == _playingDeckQueueItemId)
            : null;

    private string ResolveDeckStatus(string deckName)
    {
        return CreateDeckState(deckName).StatusText;
    }

    private DancePilotDeckState CreateDeckState(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        return DancePilotDeckState.Create(
            normalizedDeckName,
            IsDeckAudiblyPlaying(normalizedDeckName),
            IsDeckLoaded(normalizedDeckName),
            string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal),
            QueueForDeck(normalizedDeckName).Count,
            ResolveDeckDisplayItem(normalizedDeckName));
    }

    private string ResolveDeckTitle(string deckName)
    {
        return ResolveDeckDisplayItem(deckName)?.Title ?? "No song queued";
    }

    private string ResolveDeckArtist(string deckName)
    {
        return ResolveDeckDisplayItem(deckName)?.Artist ?? "Drag songs or playlists here";
    }

    private string ResolveDeckDetail(string deckName)
    {
        var item = ResolveDeckDisplayItem(deckName);
        if (item is null)
        {
            return "Queue is empty";
        }

        if (ResolveLoadedDeckItem(deckName)?.Id == item.Id)
        {
            return !string.IsNullOrWhiteSpace(item.MixDisplay)
                ? $"{item.MixDisplay} | {SpotifyProgressDisplay}"
                : SpotifyProgressDisplay;
        }

        return item.MixDisplay;
    }

    private string? ResolveDeckAlbumArtSource(string deckName)
    {
        var displayItem = ResolveDeckDisplayItem(deckName);
        if (displayItem is null)
        {
            return null;
        }

        var restoredArtSource = ResolveRestoredDeckAlbumArtSource(deckName, displayItem.Id);
        if (HasUsableAlbumArtSource(restoredArtSource))
        {
            return restoredArtSource;
        }

        var itemArtSource = ResolveQueueItemAlbumArtSource(displayItem);
        if (HasUsableAlbumArtSource(itemArtSource))
        {
            return itemArtSource;
        }

        if (displayItem.Source == SongSources.Spotify
            && PlaybackStateMatchesQueueItem(displayItem)
            && !string.IsNullOrWhiteSpace(_currentPlaybackAlbumArtUrl))
        {
            return _currentPlaybackAlbumArtUrl;
        }

        return itemArtSource;
    }

    private string? ResolveRestoredDeckAlbumArtSource(string deckName, int queueItemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        return _restoredDeckAlbumArt.TryGetValue(normalizedDeckName, out var restored)
            && restored.QueueItemId == queueItemId
            ? restored.AlbumArtSource
            : null;
    }

    private string? ResolveQueueItemAlbumArtSource(DancePilotQueueItem item)
    {
        if (HasUsableAlbumArtSource(item.AlbumArtUrl))
        {
            return item.AlbumArtUrl;
        }

        if (item.Source == SongSources.Spotify)
        {
            var track = FindSpotifyTrackByUri(item.ExternalUri);
            if (HasUsableAlbumArtSource(track?.AlbumArtUrl))
            {
                return track!.AlbumArtUrl;
            }
        }

        if (item.Source == SongSources.Local)
        {
            var localPath = item.ResolvedLocalPath;
            var localTrack = _allLocalMusicTracks.FirstOrDefault(track =>
                string.Equals(track.FilePath, localPath, StringComparison.OrdinalIgnoreCase));
            if (HasUsableAlbumArtSource(localTrack?.AlbumArtUrl))
            {
                return localTrack!.AlbumArtUrl;
            }
        }

        return item.AlbumArtUrl;
    }

    private bool PlaybackStateMatchesQueueItem(DancePilotQueueItem item) =>
        string.Equals(SpotifyNowPlayingTitle, item.Title, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(item.Artist)
            || string.Equals(SpotifyNowPlayingArtist, item.Artist, StringComparison.OrdinalIgnoreCase));

    private static bool IsDefaultAlbumArtSource(string? albumArtSource)
    {
        if (string.IsNullOrWhiteSpace(albumArtSource))
        {
            return true;
        }

        var normalized = NormalizeAlbumArtSource(albumArtSource, DefaultLocalAlbumArtPath);
        return string.Equals(normalized, DefaultLocalAlbumArtPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableAlbumArtSource(string? albumArtSource) =>
        !string.IsNullOrWhiteSpace(albumArtSource)
        && !IsDefaultAlbumArtSource(albumArtSource);

    private bool IsDeckDisplayItem(string deckName, int itemId) =>
        ResolveDeckDisplayItem(NormalizeDeckName(deckName))?.Id == itemId;

    private bool IsDeckNextPlayableItem(string deckName, int itemId) =>
        ResolveNextDeckDisplayItem(NormalizeDeckName(deckName))?.Id == itemId;

    private string? _currentDeckAlbumArtSource = DefaultCurrentAlbumArtPath;
    private string? _nextDeckAlbumArtSource = DefaultNextAlbumArtPath;

    private void RefreshDeckDisplayProperties()
    {
        var currentDeckSource = ResolveDeckAlbumArtSource("Deck A");
        if (!string.Equals(currentDeckSource, _currentDeckAlbumArtSource, StringComparison.OrdinalIgnoreCase))
        {
            CurrentDeckAlbumArt = CreateAlbumArtSource(currentDeckSource, DefaultCurrentAlbumArtPath);
            _currentDeckAlbumArtSource = currentDeckSource;
        }

        var nextDeckSource = ResolveDeckAlbumArtSource("Deck B");
        if (!string.Equals(nextDeckSource, _nextDeckAlbumArtSource, StringComparison.OrdinalIgnoreCase))
        {
            NextDeckAlbumArt = CreateAlbumArtSource(nextDeckSource, DefaultNextAlbumArtPath);
            _nextDeckAlbumArtSource = nextDeckSource;
        }

        OnPropertyChanged(nameof(DeckAStatus));
        OnPropertyChanged(nameof(DeckBStatus));
        OnPropertyChanged(nameof(DeckATitle));
        OnPropertyChanged(nameof(DeckBTitle));
        OnPropertyChanged(nameof(DeckAArtist));
        OnPropertyChanged(nameof(DeckBArtist));
        OnPropertyChanged(nameof(DeckADetail));
        OnPropertyChanged(nameof(DeckBDetail));
        NotifyDeckStateProperties();
    }

    private void UpdateNextUpFromDecks()
    {
        var next = FindTransitionTarget() ?? ActiveDeckQueue.FirstOrDefault();
        NextUpTitle = next?.Title ?? "No queued recommendation";
        NextUpArtist = next?.Artist ?? "DancePilot queue";
        OnPropertyChanged(nameof(NextDeckHeader));
        RefreshDeckDisplayProperties();
    }

    private SpotifyTrackMetadata? FindSpotifyTrackByUri(string spotifyUri) =>
        SpotifyPreviewTracks
            .Concat(SpotifySearchResults)
            .Concat(ImportedSpotifyTracks)
            .FirstOrDefault(track => string.Equals(track.SpotifyUri, spotifyUri, StringComparison.OrdinalIgnoreCase));

    private static string ExtractSpotifyTrackId(string spotifyUri)
    {
        const string prefix = "spotify:track:";
        return spotifyUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? spotifyUri[prefix.Length..]
            : spotifyUri;
    }

    public void QueueSongToActiveDeck(Song song)
    {
        QueueSongToDeck(song, ActiveDeckName);
    }

    private void QueueSongToDeck(Song song, string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        QueueToDeck(new DancePilotQueueItem
        {
            Id = NextDeckQueueId(),
            DeckName = normalizedDeckName,
            Source = SongSources.Local,
            LocalPath = song.ExternalUri ?? song.ExternalUrl,
            SongId = song.Id,
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
            AlbumArtUrl = song.AlbumArtPath,
            Duration = song.Duration,
            BPM = song.BPM,
            MusicalKey = song.Key,
            Status = "pending"
        }, normalizedDeckName);
    }

    private static TrackDisplayItem CreateTrackDisplayItem(SpotifyTrackMetadata track) =>
        TrackDisplayItem.FromSpotify(track);

    private static TrackDisplayItem CreateTrackDisplayItem(LocalMusicTrack track) =>
        TrackDisplayItem.FromLocal(track) with { AlbumArt = ResolveLocalTrackAlbumArt(track) };

    public void QueueSpotifyTrackToActiveDeck(SpotifyTrackMetadata track)
    {
        DispatchAsync(() => QueueSpotifyTrackToDeckAsync(track, ActiveDeckName));
    }

    public void QueueSpotifyTracksToActiveDeck(IEnumerable<SpotifyTrackMetadata> tracks)
    {
        DispatchAsync(() => QueueSpotifyTracksToDeckAsync(tracks, ActiveDeckName));
    }

    private async Task<int> QueueSpotifyTracksToDeckAsync(IEnumerable<SpotifyTrackMetadata> tracks, string deckName, int? beforeItemId = null)
    {
        return await QueueTrackDisplayItemsToDeckAsync(
            tracks.Select(CreateTrackDisplayItem),
            deckName,
            "Spotify track(s)",
            beforeItemId);
    }

    private Task<bool> QueueSpotifyTrackToDeckAsync(
        SpotifyTrackMetadata track,
        string deckName,
        bool announce = true,
        bool selectQueuedItem = true,
        int? beforeItemId = null)
    {
        return QueueTrackDisplayItemToDeckAsync(
            CreateTrackDisplayItem(track),
            deckName,
            announce,
            selectQueuedItem,
            beforeItemId);
    }

    public void QueueLocalTrackToActiveDeck(LocalMusicTrack track)
    {
        DispatchAsync(() => QueueLocalTrackToDeckAsync(track, ActiveDeckName));
    }

    public void QueueLocalTracksToActiveDeck(IEnumerable<LocalMusicTrack> tracks)
    {
        DispatchAsync(() => QueueLocalTracksToDeckAsync(tracks, ActiveDeckName));
    }

    private async Task<int> QueueLocalTracksToDeckAsync(IEnumerable<LocalMusicTrack> tracks, string deckName, int? beforeItemId = null)
    {
        var added = 0;
        var normalizedDeckName = NormalizeDeckName(deckName);
        foreach (var track in tracks)
        {
            if (await QueueLocalTrackToDeckAsync(track, normalizedDeckName, announce: false, selectQueuedItem: added == 0, beforeItemId: beforeItemId))
            {
                added++;
            }
        }

        if (added > 0)
        {
            SpotifyOperationMessage = $"Queued {added} local track(s) on {normalizedDeckName}.";
            QueueSessionStateSave();
        }

        return added;
    }

    private async Task<bool> QueueLocalTrackToDeckAsync(
        LocalMusicTrack track,
        string deckName,
        bool announce = true,
        bool selectQueuedItem = true,
        int? beforeItemId = null)
    {
        var indexedTrack = await _localMusicRepository.GetTrackByPathAsync(track.FilePath) ?? track;
        return await QueueTrackDisplayItemToDeckAsync(
            CreateTrackDisplayItem(indexedTrack),
            deckName,
            announce,
            selectQueuedItem,
            beforeItemId);
    }

    private async Task<int> QueueTrackDisplayItemsToDeckAsync(
        IEnumerable<TrackDisplayItem> tracks,
        string deckName,
        string sourceDescription,
        int? beforeItemId = null)
    {
        var added = 0;
        var normalizedDeckName = NormalizeDeckName(deckName);
        foreach (var track in tracks)
        {
            if (await QueueTrackDisplayItemToDeckAsync(track, normalizedDeckName, announce: false, selectQueuedItem: added == 0, beforeItemId: beforeItemId))
            {
                added++;
            }
        }

        if (added > 0)
        {
            SpotifyOperationMessage = $"Queued {added} {sourceDescription} on {normalizedDeckName}.";
            QueueSessionStateSave();
        }

        return added;
    }

    private async Task<bool> QueueTrackDisplayItemToDeckAsync(
        TrackDisplayItem track,
        string deckName,
        bool announce = true,
        bool selectQueuedItem = true,
        int? beforeItemId = null)
    {
        if (track.Source == SongSources.Spotify
            && (!track.IsPlayable || string.IsNullOrWhiteSpace(track.ExternalUri)))
        {
            SpotifyOperationMessage = $"{track.Title} is not playable through Spotify API controls. Open it in Spotify or choose another track.";
            return false;
        }

        var normalizedDeckName = NormalizeDeckName(deckName);
        var queueItem = new DancePilotQueueItem
        {
            Id = NextDeckQueueId(),
            DeckName = normalizedDeckName,
            Source = track.Source,
            ExternalUri = ResolveTrackDisplayExternalUri(track),
            LocalPath = ResolveTrackDisplayLocalPath(track),
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtUrl = ResolveTrackDisplayAlbumArt(track),
            Duration = track.Duration,
            BPM = track.BPM,
            MusicalKey = track.MusicalKey,
            Status = "pending"
        };

        if (queueItem.Source == SongSources.Local)
        {
            queueItem = await ResolveDisplayQueueItemAlbumArtAsync(queueItem);
        }

        return QueueToDeck(queueItem, normalizedDeckName, announce, selectQueuedItem, beforeItemId);
    }

    private static string ResolveTrackDisplayExternalUri(TrackDisplayItem track)
    {
        if (track.Source == SongSources.Local)
        {
            return string.Empty;
        }

        return track.ExternalUri ?? track.ProviderTrackId ?? string.Empty;
    }

    private static string? ResolveTrackDisplayLocalPath(TrackDisplayItem track) =>
        track.Source == SongSources.Local
            ? track.LocalPath ?? track.ExternalUri
            : null;

    private static string? ResolveTrackDisplayAlbumArt(TrackDisplayItem track)
    {
        if (!string.IsNullOrWhiteSpace(track.AlbumArt))
        {
            return track.AlbumArt.Trim();
        }

        return track.Source == SongSources.Local
            ? DefaultLocalAlbumArtPath
            : null;
    }

    private static string ResolveLocalTrackAlbumArt(LocalMusicTrack track) =>
        string.IsNullOrWhiteSpace(track.AlbumArtUrl)
            ? DefaultLocalAlbumArtPath
            : track.AlbumArtUrl.Trim();

    private async Task<DancePilotQueueItem> ResolveDisplayQueueItemAlbumArtAsync(DancePilotQueueItem queueItem)
    {
        var resolvedItem = await ResolveQueueItemAlbumArtAsync(queueItem);
        if (!HasUsableAlbumArtSource(resolvedItem.AlbumArtUrl))
        {
            return resolvedItem;
        }

        try
        {
            var cachedAlbumArtUri = await _albumArtCacheService.CacheAlbumArtAsync(resolvedItem);
            return string.IsNullOrWhiteSpace(cachedAlbumArtUri)
                ? resolvedItem
                : resolvedItem with { AlbumArtUrl = cachedAlbumArtUri };
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Album art could not be cached for {queueItem.Title}: {ex.Message}");
            return resolvedItem;
        }
    }

    public async Task QueueSourceSelectionToDeckAsync(
        string deckName,
        IEnumerable<SpotifyTrackMetadata> spotifyTracks,
        IEnumerable<LocalMusicTrack> localTracks,
        IEnumerable<SpotifyPlaylistSummary> spotifyPlaylists,
        int? insertBeforeItemId = null,
        bool insertAtTop = false,
        IEnumerable<LocalMusicPlaylist>? localPlaylists = null)
    {
        var selectedTracks = localTracks
            .Select(CreateTrackDisplayItem)
            .Concat(spotifyTracks.Select(CreateTrackDisplayItem));

        await QueueSourceSelectionToDeckAsync(
            deckName,
            selectedTracks,
            spotifyPlaylists,
            insertBeforeItemId,
            insertAtTop,
            localPlaylists);
    }

    public async Task QueueSourceSelectionToDeckAsync(
        string deckName,
        IEnumerable<TrackDisplayItem> tracks,
        IEnumerable<SpotifyPlaylistSummary> spotifyPlaylists,
        int? insertBeforeItemId = null,
        bool insertAtTop = false,
        IEnumerable<LocalMusicPlaylist>? localPlaylists = null)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var beforeItemId = insertAtTop && QueueForDeck(normalizedDeckName).Count > 0
            ? QueueForDeck(normalizedDeckName).First().Id
            : insertBeforeItemId;

        var selectedTracks = tracks
            .DistinctBy(CreateTrackDisplayRequestKey)
            .ToList();
        var selectedPlaylists = spotifyPlaylists
            .DistinctBy(playlist => playlist.SpotifyPlaylistId)
            .ToList();
        var selectedLocalPlaylists = (localPlaylists ?? [])
            .DistinctBy(playlist => playlist.Id)
            .ToList();
        var hasExplicitSelection = selectedTracks.Count > 0
            || selectedPlaylists.Count > 0
            || selectedLocalPlaylists.Count > 0;

        var requestKey = hasExplicitSelection
            ? CreateQueueSelectionRequestKey(
                normalizedDeckName,
                beforeItemId,
                selectedTracks,
                selectedPlaylists,
                selectedLocalPlaylists)
            : null;
        if (requestKey is not null && !_activeQueueSelectionRequestKeys.Add(requestKey))
        {
            StartupLog.Write($"Ignored duplicate active queue request for {normalizedDeckName}.");
            return;
        }

        try
        {
            var queuedCount = 0;
            if (selectedTracks.Count > 0)
            {
                queuedCount += await QueueTrackDisplayItemsToDeckAsync(selectedTracks, normalizedDeckName, "track(s)", beforeItemId);
            }

            if (selectedPlaylists.Count > 0)
            {
                queuedCount += await QueueSpotifyPlaylistsToDeckAsync(selectedPlaylists, normalizedDeckName, beforeItemId);
            }

            if (selectedLocalPlaylists.Count > 0)
            {
                queuedCount += await QueueLocalPlaylistsToDeckAsync(selectedLocalPlaylists, normalizedDeckName, beforeItemId);
            }

            if (queuedCount == 0 && !hasExplicitSelection)
            {
                await QueueSelectedSourceToDeckAsync(normalizedDeckName, beforeItemId);
                return;
            }

            if (queuedCount == 0)
            {
                SpotifyOperationMessage = "No playable songs were found in that drop selection.";
                return;
            }

            SpotifyOperationMessage = $"Queued {queuedCount} item(s) on {normalizedDeckName}.";
            QueueSessionStateSave();
        }
        finally
        {
            if (requestKey is not null)
            {
                _activeQueueSelectionRequestKeys.Remove(requestKey);
            }
        }
    }

    private static string CreateQueueSelectionRequestKey(
        string deckName,
        int? beforeItemId,
        IEnumerable<TrackDisplayItem> tracks,
        IEnumerable<SpotifyPlaylistSummary> spotifyPlaylists,
        IEnumerable<LocalMusicPlaylist> localPlaylists)
    {
        var parts = new List<string>
        {
            $"deck:{deckName}",
            $"before:{beforeItemId?.ToString() ?? "end"}"
        };

        parts.AddRange(tracks.Select(track => $"track:{NormalizeQueueRequestKey(CreateTrackDisplayRequestKey(track))}"));
        parts.AddRange(spotifyPlaylists.Select(playlist => $"spotify-playlist:{NormalizeQueueRequestKey(playlist.SpotifyPlaylistId)}"));
        parts.AddRange(localPlaylists.Select(playlist => $"local-playlist:{playlist.Id}"));

        return string.Join("|", parts.Order(StringComparer.Ordinal));
    }

    private static string CreateTrackDisplayRequestKey(TrackDisplayItem track)
    {
        var source = string.IsNullOrWhiteSpace(track.Source)
            ? "unknown"
            : track.Source.Trim();
        var sourceKey = track.Source == SongSources.Local
            ? track.LocalPath ?? track.ExternalUri
            : track.ExternalUri ?? track.ProviderTrackId;

        return $"{source}:{NormalizeQueueRequestKey(sourceKey)}";
    }

    private static string NormalizeQueueRequestKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeExactSourceValue(value);

    private async Task<int> QueueSpotifyPlaylistsToDeckAsync(IReadOnlyList<SpotifyPlaylistSummary> playlists, string deckName, int? beforeItemId = null)
    {
        var added = 0;
        var wasBusy = IsSpotifyBusy;
        try
        {
            if (!wasBusy)
            {
                IsSpotifyBusy = true;
            }

            foreach (var playlist in playlists)
            {
                var tracks = SelectedSpotifyPlaylist is not null
                    && string.Equals(SelectedSpotifyPlaylist.SpotifyPlaylistId, playlist.SpotifyPlaylistId, StringComparison.Ordinal)
                    && SpotifyPreviewTracks.Count > 0
                        ? SpotifyPreviewTracks.ToList()
                        : (await _spotifyPlaylistImporter.PreviewPlaylistTracksAsync(CurrentSpotifySettings, playlist.SpotifyPlaylistId)).ToList();

                added += await QueueSpotifyTracksToDeckAsync(
                    tracks.Where(track => !track.IsUnavailable && !string.IsNullOrWhiteSpace(track.SpotifyUri)),
                    deckName,
                    beforeItemId);
            }
        }
        catch (SpotifyApiException ex)
        {
            StartupLog.Write($"Spotify playlist queue failed kind={ex.Kind} status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
            SpotifyOperationMessage = ToFriendlySpotifyMessage(ex);
        }
        catch (Exception ex)
        {
            SpotifyOperationMessage = $"Spotify playlist could not be queued: {ex.Message}";
        }
        finally
        {
            if (!wasBusy)
            {
                IsSpotifyBusy = false;
            }
        }

        if (added > 0)
        {
            SpotifyOperationMessage = playlists.Count == 1
                ? $"Appended {added} songs to {NormalizeDeckName(deckName)} from {playlists[0].Name}."
                : $"Appended {added} songs to {NormalizeDeckName(deckName)} from {playlists.Count} playlists.";
            QueueSessionStateSave();
        }

        return added;
    }

    private async Task<int> QueueLocalPlaylistsToDeckAsync(IReadOnlyList<LocalMusicPlaylist> playlists, string deckName, int? beforeItemId = null)
    {
        var added = 0;
        foreach (var playlist in playlists)
        {
            var tracks = await LoadLocalPlaylistTracksAsync(playlist);
            if (tracks.Count == 0)
            {
                continue;
            }

            added += await QueueLocalTracksToDeckAsync(tracks, deckName, beforeItemId);
        }

        if (added > 0)
        {
            LocalPlaylistStatus = playlists.Count == 1
                ? $"Appended {added} songs to {NormalizeDeckName(deckName)} from {playlists[0].Name}."
                : $"Appended {added} songs to {NormalizeDeckName(deckName)} from {playlists.Count} local playlists.";
            SpotifyOperationMessage = LocalPlaylistStatus;
            QueueSessionStateSave();
        }
        else if (playlists.Count > 0)
        {
            LocalPlaylistStatus = "Those local playlist songs are not available in the selected folder anymore.";
            SpotifyOperationMessage = LocalPlaylistStatus;
        }

        return added;
    }

    private async Task QueueSelectedSourceToDeckAsync(string deckName, int? insertBeforeItemId = null)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);

        if (ActiveSource == SourceLocal)
        {
            if (SelectedLocalMusicTrack is null)
            {
                SpotifyOperationMessage = "Select a local music file first.";
                return;
            }

            await QueueLocalTrackToDeckAsync(SelectedLocalMusicTrack, normalizedDeckName, beforeItemId: insertBeforeItemId);
            return;
        }

        var track = SelectedSpotifyTrack ?? SelectedSpotifySearchTrack ?? SelectedImportedSpotifyTrack;
        if (track is not null)
        {
            await QueueTrackDisplayItemToDeckAsync(
                CreateTrackDisplayItem(track),
                normalizedDeckName,
                beforeItemId: insertBeforeItemId);
            return;
        }

        if (ActiveSource is SourceYouTube or SourceTidal)
        {
            SpotifyOperationMessage = $"{ActiveSource} source setup is visible, but queueing from that provider is not implemented yet.";
            return;
        }

        await Task.CompletedTask;
        SpotifyOperationMessage = "Select a playlist track, search result, imported track, or local file first.";
    }

    private async Task AddLoadedPlaylistToActiveDeckAsync()
    {
        if (ActiveSource != SourceSpotify)
        {
            SpotifyOperationMessage = "Switch to Spotify playlists and load a playlist window before adding loaded tracks.";
            return;
        }

        var tracks = GetLoadedPlayableSpotifyPreviewTracks();
        if (tracks.Count == 0)
        {
            SpotifyOperationMessage = "Load a Spotify playlist into the playlist tracks window before adding it to the selected deck.";
            return;
        }

        var normalizedDeckName = NormalizeDeckName(ActiveDeckName);
        var playlistName = ResolveLoadedPlaylistName();
        var added = await QueueSpotifyTracksToDeckAsync(tracks, normalizedDeckName);
        SpotifyOperationMessage = added > 0
            ? $"Appended {added} songs to {normalizedDeckName} from {playlistName}."
            : "No playable loaded playlist tracks were added.";
        QueueSessionStateSave();
    }

    private async Task ReplaceLoadedPlaylistOnActiveDeckAsync()
    {
        if (ActiveSource != SourceSpotify)
        {
            SpotifyOperationMessage = "Switch to Spotify playlists and load a playlist window before replacing a deck.";
            return;
        }

        var tracks = GetLoadedPlayableSpotifyPreviewTracks();
        if (tracks.Count == 0)
        {
            SpotifyOperationMessage = "Load a Spotify playlist into the playlist tracks window before replacing the selected deck.";
            return;
        }

        var normalizedDeckName = NormalizeDeckName(ActiveDeckName);
        var playlistName = ResolveLoadedPlaylistName();
        ClearDeckForPlaylistReplace(normalizedDeckName);
        var added = await QueueSpotifyTracksToDeckAsync(tracks, normalizedDeckName);
        SpotifyOperationMessage = added > 0
            ? $"Replaced {normalizedDeckName} with {added} songs from {playlistName}."
            : $"Replaced {normalizedDeckName}, but no playable loaded playlist tracks were added.";
        QueueSessionStateSave();
    }

    private string ResolveLoadedPlaylistName() =>
        SelectedSpotifyPlaylist?.Name ?? "loaded playlist";

    private void ClearDeckForPlaylistReplace(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        queue.Clear();
        _selectedDeckQueueItemIds[normalizedDeckName] = null;
        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        RefreshDeckDisplayProperties();
    }

    private Task RandomizeDeckQueueAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (TryRandomizeDeckQueue(normalizedDeckName))
        {
            return Task.CompletedTask;
        }

        var queue = QueueForDeck(normalizedDeckName);
        SpotifyOperationMessage = queue.Count < 2
            ? $"Add at least two songs to {normalizedDeckName} before randomizing the deck queue."
            : $"Add at least two non-playing songs to {normalizedDeckName} before randomizing around the current song.";
        return Task.CompletedTask;
    }

    private async Task RandomizeSourcePlaylistBeforeLoadingToDeckAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);

        if (ActiveSource == SourceLocal)
        {
            var tracks = LocalMusicTracks.Count > 0
                ? LocalMusicTracks.ToList()
                : _allLocalMusicTracks.ToList();
            if (tracks.Count > 0)
            {
                await RandomizeLocalTracksToDeckAsync(tracks, normalizedDeckName);
            }
            else
            {
                SpotifyOperationMessage = "Scan local music before randomizing local tracks before loading.";
            }

            return;
        }

        if (ActiveSource is SourceYouTube or SourceTidal)
        {
            SpotifyOperationMessage = $"{ActiveSource} randomize-before-loading will be enabled when that provider connector can return playable tracks.";
            return;
        }

        var loadedTracks = SpotifyPreviewTracks.ToList();
        if (loadedTracks.Count > 0)
        {
            await RandomizeSpotifyPlaylistToDeckAsync(loadedTracks, normalizedDeckName);
            return;
        }

        SpotifyOperationMessage = "Load a Spotify playlist into the playlist tracks window before randomizing a playlist before loading.";
    }

    private async Task RandomizeSpotifyPlaylistToDeckAsync(IReadOnlyList<SpotifyTrackMetadata> tracks, string deckName)
    {
        var randomizedTracks = Shuffle(tracks);
        var playableTracks = randomizedTracks
            .Where(IsPlayableSpotifyTrack)
            .ToList();
        if (playableTracks.Count == 0)
        {
            SpotifyOperationMessage = "The loaded playlist was randomized, but no playable tracks were available for the deck.";
            ReplaceCollection(SpotifyPreviewTracks, randomizedTracks);
            SelectedSpotifyTrack = SpotifyPreviewTracks.FirstOrDefault();
            QueueSessionStateSave();
            return;
        }

        ReplaceCollection(SpotifyPreviewTracks, randomizedTracks);
        SelectedSpotifyTrack = SpotifyPreviewTracks.FirstOrDefault();

        PrepareDeckForRandomizedLoad(deckName);
        var added = await QueueSpotifyTracksToDeckAsync(playableTracks, deckName);
        SpotifyOperationMessage = added > 0
            ? $"Randomized the loaded playlist and loaded {added} track(s) onto {NormalizeDeckName(deckName)}."
            : "The loaded playlist was randomized, but no playable tracks were added.";
        CacheDeckAlbumArtForQueue(deckName);
        QueueSessionStateSave();
    }

    private List<SpotifyTrackMetadata> GetLoadedPlayableSpotifyPreviewTracks() =>
        SpotifyPreviewTracks
            .Where(IsPlayableSpotifyTrack)
            .ToList();

    private static bool IsPlayableSpotifyTrack(SpotifyTrackMetadata track) =>
        !track.IsUnavailable && !string.IsNullOrWhiteSpace(track.SpotifyUri);

    private async Task RandomizeLocalTracksToDeckAsync(IReadOnlyList<LocalMusicTrack> tracks, string deckName)
    {
        if (tracks.Count == 0)
        {
            SpotifyOperationMessage = "Scan local music before randomizing local tracks.";
            return;
        }

        var randomizedTracks = Shuffle(tracks);
        if (LocalMusicTracks.Count > 0)
        {
            ReplaceCollection(LocalMusicTracks, randomizedTracks);
            SelectedLocalMusicTrack = LocalMusicTracks.FirstOrDefault();
        }

        PrepareDeckForRandomizedLoad(deckName);
        var added = await QueueLocalTracksToDeckAsync(randomizedTracks, deckName);
        SpotifyOperationMessage = $"Randomized {added} local track(s) onto {NormalizeDeckName(deckName)}.";
        CacheDeckAlbumArtForQueue(deckName);
        QueueSessionStateSave();
    }

    private bool TryRandomizeDeckQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count < 2)
        {
            return false;
        }

        var protectedItemId = ResolveLoadedDeckItem(normalizedDeckName)?.Id;
        var selectedId = _selectedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
        if (!DancePilotQueueOperations.RandomizeDeckQueue(queue, normalizedDeckName, protectedItemId))
        {
            return false;
        }

        _selectedDeckQueueItemIds[normalizedDeckName] =
            selectedId is not null && queue.Any(item => item.Id == selectedId.Value)
                ? selectedId
                : queue.FirstOrDefault(item => protectedItemId is null || item.Id != protectedItemId.Value)?.Id
                    ?? queue.FirstOrDefault()?.Id;

        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        RefreshDeckDisplayProperties();
        CacheDeckAlbumArtForQueue(normalizedDeckName);
        SpotifyOperationMessage = $"Randomized {normalizedDeckName} queue: {queue.Count} songs.";
        QueueSessionStateSave();
        return true;
    }

    private void PrepareDeckForRandomizedLoad(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var playingItem = ResolveLoadedDeckItem(normalizedDeckName);

        queue.Clear();
        if (playingItem is not null)
        {
            queue.Add(playingItem);
        }

        RenumberQueue(queue, normalizedDeckName);
        _selectedDeckQueueItemIds[normalizedDeckName] = playingItem?.Id;
        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
    }

    private bool QueueToDeck(
        DancePilotQueueItem item,
        string deckName,
        bool announce = true,
        bool selectQueuedItem = true,
        int? beforeItemId = null)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var queuedItem = item with { DeckName = normalizedDeckName };
        var duplicateIndex = FindExactQueueSourceIndex(queue, queuedItem);
        if (duplicateIndex >= 0)
        {
            var existingItem = queue[duplicateIndex];
            if (beforeItemId is int duplicateInsertBeforeId && existingItem.Id != duplicateInsertBeforeId)
            {
                queue.RemoveAt(duplicateIndex);
                var insertIndex = queue.FindIndex(existing => existing.Id == duplicateInsertBeforeId);
                if (insertIndex < 0)
                {
                    queue.Add(existingItem);
                }
                else
                {
                    queue.Insert(insertIndex, existingItem);
                }
            }

            RenumberQueue(queue, normalizedDeckName);
            ActiveDeckName = normalizedDeckName;
            if (selectQueuedItem)
            {
                _selectedDeckQueueItemIds[normalizedDeckName] = existingItem.Id;
            }

            RefreshActiveDeckQueue();
            if (selectQueuedItem)
            {
                SelectedActiveDeckQueueItem = ActiveDeckQueue.FirstOrDefault(queueItem => queueItem.Id == existingItem.Id)
                    ?? existingItem;
            }

            UpdateNextUpFromDecks();
            if (announce)
            {
                SpotifyOperationMessage = $"{existingItem.Title} is already queued on {normalizedDeckName}.";
            }

            if (IsDeckDisplayItem(normalizedDeckName, existingItem.Id)
                || IsDeckNextPlayableItem(normalizedDeckName, existingItem.Id))
            {
                DispatchAsync(() => CacheQueuedAlbumArtAsync(normalizedDeckName, existingItem.Id));
            }

            QueueSessionStateSave();
            return false;
        }

        if (beforeItemId is int insertBeforeId)
        {
            var insertIndex = queue.FindIndex(existing => existing.Id == insertBeforeId);
            if (insertIndex < 0)
            {
                queue.Add(queuedItem);
            }
            else
            {
                queue.Insert(insertIndex, queuedItem);
            }
        }
        else
        {
            queue.Add(queuedItem);
        }

        RenumberQueue(queue, normalizedDeckName);
        ActiveDeckName = normalizedDeckName;
        if (selectQueuedItem)
        {
            _selectedDeckQueueItemIds[normalizedDeckName] = queuedItem.Id;
        }

        RefreshActiveDeckQueue();
        if (selectQueuedItem)
        {
            SelectedActiveDeckQueueItem = ActiveDeckQueue.FirstOrDefault(queueItem => queueItem.Id == queuedItem.Id)
                ?? queuedItem;
        }

        UpdateNextUpFromDecks();
        if (announce)
        {
            SpotifyOperationMessage = $"Queued on {normalizedDeckName}: {queuedItem.Title}. Select it for transition, or double-click the deck row to start it now.";
        }

        var isVisibleDisplayItem = IsDeckDisplayItem(normalizedDeckName, queuedItem.Id);
        if (isVisibleDisplayItem)
        {
            RefreshDeckDisplayProperties();
        }

        if (isVisibleDisplayItem || IsDeckNextPlayableItem(normalizedDeckName, queuedItem.Id))
        {
            DispatchAsync(() => CacheQueuedAlbumArtAsync(normalizedDeckName, queuedItem.Id));
        }

        QueueSessionStateSave();
        return true;
    }

    private static int FindExactQueueSourceIndex(IReadOnlyList<DancePilotQueueItem> queue, DancePilotQueueItem item)
    {
        var sourceKey = CreateExactQueueSourceKey(item);
        if (sourceKey is null)
        {
            return -1;
        }

        for (var index = 0; index < queue.Count; index++)
        {
            if (string.Equals(CreateExactQueueSourceKey(queue[index]), sourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? CreateExactQueueSourceKey(DancePilotQueueItem item)
    {
        var source = string.IsNullOrWhiteSpace(item.Source)
            ? "unknown"
            : item.Source.Trim();
        var sourceIdentity = item.SourceIdentity;
        if (!string.IsNullOrWhiteSpace(sourceIdentity))
        {
            return $"{source}:{NormalizeExactSourceValue(sourceIdentity)}";
        }

        return item.SongId is int songId
            ? $"{source}:song:{songId}"
            : null;
    }

    private static string NormalizeExactSourceValue(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        try
        {
            if (!trimmed.Contains(':') && (trimmed.Contains('\\') || trimmed.Contains('/')))
            {
                return Path.GetFullPath(trimmed);
            }
        }
        catch
        {
        }

        return trimmed;
    }

    private async Task CacheQueuedAlbumArtAsync(string deckName, int itemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var queueIndex = queue.FindIndex(item => item.Id == itemId);
        if (queueIndex < 0)
        {
            return;
        }

        var item = queue[queueIndex];
        item = await ResolveQueueItemAlbumArtAsync(item);
        queueIndex = queue.FindIndex(existing => existing.Id == itemId);
        if (queueIndex < 0)
        {
            return;
        }

        if (!string.Equals(queue[queueIndex].AlbumArtUrl, item.AlbumArtUrl, StringComparison.OrdinalIgnoreCase))
        {
            queue[queueIndex] = item;
            StartupLog.Write($"Album art resolved for {normalizedDeckName} item {itemId}: {item.Title} -> {item.AlbumArtUrl}");
            RefreshDeckAfterAlbumArtUpdate(normalizedDeckName, itemId);
            QueueSessionStateSave();
        }

        var itemForCache = queue[queueIndex];
        var cachedAlbumArtUri = await _albumArtCacheService.CacheAlbumArtAsync(itemForCache);
        if (string.IsNullOrWhiteSpace(cachedAlbumArtUri))
        {
            StartupLog.Write($"Album art cache returned no image for {normalizedDeckName} item {itemId}: {itemForCache.Title}");
            return;
        }

        queueIndex = queue.FindIndex(existing => existing.Id == itemId);
        if (queueIndex < 0
            || string.Equals(queue[queueIndex].AlbumArtUrl, cachedAlbumArtUri, StringComparison.OrdinalIgnoreCase)
            || !ShouldReplaceQueueAlbumArtWithCachedUri(queue[queueIndex]))
        {
            return;
        }

        queue[queueIndex] = queue[queueIndex] with { AlbumArtUrl = cachedAlbumArtUri };
        StartupLog.Write($"Album art cached for {normalizedDeckName} item {itemId}: {queue[queueIndex].Title} -> {cachedAlbumArtUri}");
        RefreshDeckAfterAlbumArtUpdate(normalizedDeckName, itemId);
        QueueSessionStateSave();
    }

    private async Task<DancePilotQueueItem> ResolveQueueItemAlbumArtAsync(DancePilotQueueItem item)
    {
        var cachedAlbumArtUri = await ResolveCachedAlbumArtUriAsync(item);
        if (HasUsableAlbumArtSource(cachedAlbumArtUri))
        {
            return item with { AlbumArtUrl = cachedAlbumArtUri };
        }

        if (item.Source == SongSources.Local)
        {
            var embeddedAlbumArtUri = await CacheLocalEmbeddedAlbumArtAsync(item);
            if (HasUsableAlbumArtSource(embeddedAlbumArtUri))
            {
                return item with { AlbumArtUrl = embeddedAlbumArtUri };
            }
        }

        if (HasUsableAlbumArtSource(item.AlbumArtUrl))
        {
            return item;
        }

        if (item.Source == SongSources.Spotify)
        {
            var spotifyItem = await ResolveSpotifyQueueItemAlbumArtAsync(item);
            if (HasUsableAlbumArtSource(spotifyItem.AlbumArtUrl))
            {
                return spotifyItem;
            }

            var externalItem = await ResolveExternalQueueItemAlbumArtAsync(spotifyItem);
            return HasUsableAlbumArtSource(externalItem.AlbumArtUrl)
                ? externalItem
                : spotifyItem;
        }

        if (item.Source == SongSources.Local)
        {
            var localPath = item.ResolvedLocalPath;
            var localTrack = _allLocalMusicTracks.FirstOrDefault(track =>
                string.Equals(track.FilePath, localPath, StringComparison.OrdinalIgnoreCase));
            if (HasUsableAlbumArtSource(localTrack?.AlbumArtUrl))
            {
                return item with { AlbumArtUrl = localTrack!.AlbumArtUrl };
            }

            return await ResolveExternalQueueItemAlbumArtAsync(item);
        }

        return await ResolveExternalQueueItemAlbumArtAsync(item);
    }

    private async Task<string?> ResolveCachedAlbumArtUriAsync(DancePilotQueueItem item)
    {
        try
        {
            return await _albumArtCacheService.GetCachedAlbumArtUriAsync(item);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Album art cache lookup skipped for {item.Title}: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> CacheLocalEmbeddedAlbumArtAsync(DancePilotQueueItem item)
    {
        try
        {
            return await _albumArtCacheService.CacheAlbumArtAsync(item);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Embedded local album art lookup skipped for {item.Title}: {ex.Message}");
            return null;
        }
    }

    private async Task<DancePilotQueueItem> ResolveSpotifyQueueItemAlbumArtAsync(DancePilotQueueItem item)
    {
        if (item.Source != SongSources.Spotify || HasUsableAlbumArtSource(item.AlbumArtUrl))
        {
            return item;
        }

        var inMemoryTrack = FindSpotifyTrackByUri(item.ExternalUri);
        if (HasUsableAlbumArtSource(inMemoryTrack?.AlbumArtUrl))
        {
            return item with { AlbumArtUrl = inMemoryTrack!.AlbumArtUrl };
        }

        var trackId = ExtractSpotifyTrackId(item.ExternalUri);
        var importedTrack = await FindImportedSpotifyTrackByUriAsync(item.ExternalUri, trackId);
        if (HasUsableAlbumArtSource(importedTrack?.AlbumArtUrl))
        {
            return item with { AlbumArtUrl = importedTrack!.AlbumArtUrl };
        }

        return item;
    }

    private async Task<SpotifyTrackMetadata?> FindImportedSpotifyTrackByUriAsync(string spotifyUri, string spotifyTrackId)
    {
        try
        {
            return await _spotifyLibraryRepository.GetImportedTrackByUriAsync(spotifyUri, spotifyTrackId);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Imported Spotify album art lookup skipped for {spotifyUri}: {ex.Message}");
            return null;
        }
    }

    private async Task<DancePilotQueueItem> ResolveExternalQueueItemAlbumArtAsync(DancePilotQueueItem item)
    {
        try
        {
            var albumArtUrl = await _externalAlbumArtLookupService.FindAlbumArtAsync(item.Title, item.Artist);
            if (HasUsableAlbumArtSource(albumArtUrl))
            {
                StartupLog.Write($"External album art resolved for {item.Title} - {item.Artist}: {albumArtUrl}");
            }

            return HasUsableAlbumArtSource(albumArtUrl)
                ? item with { AlbumArtUrl = albumArtUrl }
                : item;
        }
        catch (Exception ex)
        {
            StartupLog.Write($"External album art lookup skipped for {item.Title}: {ex.Message}");
            return item;
        }
    }

    private static bool ShouldReplaceQueueAlbumArtWithCachedUri(DancePilotQueueItem _) =>
        true;

    private void RefreshDeckAfterAlbumArtUpdate(string deckName, int itemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var updatedItem = QueueForDeck(normalizedDeckName).FirstOrDefault(item => item.Id == itemId);
        if (updatedItem is not null
            && IsDeckDisplayItem(normalizedDeckName, itemId)
            && HasUsableAlbumArtSource(updatedItem.AlbumArtUrl))
        {
            _restoredDeckAlbumArt[normalizedDeckName] = new RestoredDeckAlbumArt(itemId, updatedItem.AlbumArtUrl);
        }

        if (IsDeckDisplayItem(normalizedDeckName, itemId))
        {
            RefreshActiveDeckQueue();
            RefreshDeckDisplayProperties();
        }
    }

    private void CacheDeckAlbumArtForCurrentQueues()
    {
        CacheDeckAlbumArtForQueue("Deck A");
        CacheDeckAlbumArtForQueue("Deck B");
    }

    private async Task HydrateRestoredDeckAlbumArtAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var displayItemId = ResolveDeckDisplayItem(normalizedDeckName)?.Id;
        if (displayItemId is null)
        {
            return;
        }

        await CacheQueuedAlbumArtAsync(normalizedDeckName, displayItemId.Value);
    }

    private void CacheDeckAlbumArtForQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var displayItemId = ResolveDeckDisplayItem(normalizedDeckName)?.Id;
        if (displayItemId is not null)
        {
            DispatchAsync(() => CacheQueuedAlbumArtAsync(normalizedDeckName, displayItemId.Value));
        }
    }

    private List<DancePilotQueueItem> ActiveQueueList() => QueueForDeck(ActiveDeckName);

    private List<DancePilotQueueItem> QueueViewQueueList() => QueueForDeck(QueueViewDeckName);

    private static string NormalizeDeckName(string deckName) =>
        string.Equals(deckName, "Deck B", StringComparison.OrdinalIgnoreCase) ? "Deck B" : "Deck A";

    private static List<T> Shuffle<T>(IEnumerable<T> items)
    {
        var shuffled = items.ToList();
        if (shuffled.Count < 2)
        {
            return shuffled;
        }

        var original = shuffled.ToList();
        for (var index = shuffled.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        if (shuffled.SequenceEqual(original))
        {
            var first = shuffled[0];
            shuffled.RemoveAt(0);
            shuffled.Add(first);
        }

        return shuffled;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private int NextDeckQueueId() => _nextDeckQueueItemId++;

    public void MoveSelectedQueueItemUp()
    {
        if (SelectedQueueViewDeckQueueItem is null)
        {
            SpotifyOperationMessage = "Select a queued song before moving it up.";
            return;
        }

        MoveQueueItem(SelectedQueueViewDeckQueueItem, moveUp: true);
    }

    public void MoveSelectedQueueItemDown()
    {
        if (SelectedQueueViewDeckQueueItem is null)
        {
            SpotifyOperationMessage = "Select a queued song before moving it down.";
            return;
        }

        MoveQueueItem(SelectedQueueViewDeckQueueItem, moveUp: false);
    }

    public void RemoveSelectedQueueItem()
    {
        if (SelectedQueueViewDeckQueueItem is null)
        {
            SpotifyOperationMessage = "Select a queued song before removing it.";
            return;
        }

        RemoveQueueItem(SelectedQueueViewDeckQueueItem);
    }

    public void MoveQueueItemUp(DancePilotQueueItem item) =>
        MoveQueueItem(item, moveUp: true);

    public void MoveQueueItemDown(DancePilotQueueItem item) =>
        MoveQueueItem(item, moveUp: false);

    private void MoveQueueItem(DancePilotQueueItem item, bool moveUp)
    {
        var deckName = NormalizeDeckName(item.DeckName);
        var queue = QueueForDeck(deckName);
        var moved = moveUp
            ? DancePilotQueueOperations.MoveItemUp(queue, item.Id, deckName)
            : DancePilotQueueOperations.MoveItemDown(queue, item.Id, deckName);
        if (!moved)
        {
            SpotifyOperationMessage = moveUp
                ? $"{item.Title} is already at the top of {deckName}."
                : $"{item.Title} is already at the bottom of {deckName}.";
            return;
        }

        _selectedDeckQueueItemIds[deckName] = item.Id;
        ActiveDeckName = deckName;
        QueueViewDeckName = deckName;
        RefreshActiveDeckQueue();
        RefreshDeckDisplayProperties();
        CacheDeckAlbumArtForQueue(deckName);
        SpotifyOperationMessage = moveUp
            ? $"Moved {item.Title} up on {deckName}."
            : $"Moved {item.Title} down on {deckName}.";
        QueueSessionStateSave();
    }

    public void MoveQueueItemsToDeck(IEnumerable<DancePilotQueueItem> items, string targetDeckName, int? beforeItemId = null)
    {
        var normalizedTargetDeckName = NormalizeDeckName(targetDeckName);
        var itemIds = items
            .Select(item => item.Id)
            .Distinct()
            .ToList();
        if (itemIds.Count == 0)
        {
            return;
        }

        var moving = _deckAQueue
            .Concat(_deckBQueue)
            .Where(item => itemIds.Contains(item.Id))
            .OrderBy(item => item.DeckName)
            .ThenBy(item => item.QueuePosition)
            .ToList();
        if (moving.Count == 0)
        {
            return;
        }

        _deckAQueue.RemoveAll(item => itemIds.Contains(item.Id));
        _deckBQueue.RemoveAll(item => itemIds.Contains(item.Id));

        var targetQueue = QueueForDeck(normalizedTargetDeckName);
        var insertIndex = beforeItemId is null
            ? targetQueue.Count
            : targetQueue.FindIndex(item => item.Id == beforeItemId.Value);
        if (insertIndex < 0)
        {
            insertIndex = targetQueue.Count;
        }

        targetQueue.InsertRange(insertIndex, moving.Select(item => item with { DeckName = normalizedTargetDeckName }));
        RenumberQueue(_deckAQueue, "Deck A");
        RenumberQueue(_deckBQueue, "Deck B");

        ActiveDeckName = normalizedTargetDeckName;
        RefreshActiveDeckQueue();
        SpotifyOperationMessage = moving.Count == 1
            ? $"Moved {moving[0].Title} on {normalizedTargetDeckName}."
            : $"Moved {moving.Count} queued songs on {normalizedTargetDeckName}.";
        QueueSessionStateSave();
    }

    public void RemoveQueueItem(DancePilotQueueItem item)
    {
        var deckName = NormalizeDeckName(item.DeckName);
        var queue = QueueForDeck(deckName);
        if (!DancePilotQueueOperations.RemoveItem(queue, item.Id, deckName))
        {
            SpotifyOperationMessage = $"{item.Title} is no longer in the queue.";
            return;
        }

        if (_selectedDeckQueueItemIds.GetValueOrDefault(deckName) == item.Id)
        {
            _selectedDeckQueueItemIds[deckName] = null;
        }

        ActiveDeckName = deckName;
        RefreshActiveDeckQueue();
        SpotifyOperationMessage = $"Removed {item.Title} from {deckName}.";
        QueueSessionStateSave();
    }

    private void RemovePlayedQueueItem(string deckName, int itemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (!DancePilotQueueOperations.RemoveItem(queue, itemId, normalizedDeckName))
        {
            return;
        }

        if (_selectedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName) == itemId)
        {
            _selectedDeckQueueItemIds[normalizedDeckName] = queue.FirstOrDefault(item => item.Id == _playingDeckQueueItemId)?.Id
                ?? queue.FirstOrDefault()?.Id;
        }

        RefreshActiveDeckQueue();
        RefreshDeckDisplayProperties();
        CacheDeckAlbumArtForQueue(normalizedDeckName);
        QueueSessionStateSave();
    }

    public void ClearPendingQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var protectedItemId = string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal)
            ? _playingDeckQueueItemId
            : null;
        var removedCount = DancePilotQueueOperations.ClearPending(queue, normalizedDeckName, protectedItemId);
        if (removedCount == 0)
        {
            QueueViewDeckName = normalizedDeckName;
            RefreshQueueViewDeckQueue();
            SpotifyOperationMessage = $"{normalizedDeckName} has no pending queued songs to clear.";
            return;
        }

        var selectedId = _selectedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
        if (selectedId is not null && queue.All(item => item.Id != selectedId.Value))
        {
            _selectedDeckQueueItemIds[normalizedDeckName] = protectedItemId is not null && queue.Any(item => item.Id == protectedItemId.Value)
                ? protectedItemId
                : queue.FirstOrDefault()?.Id;
        }

        ActiveDeckName = normalizedDeckName;
        QueueViewDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        RefreshDeckDisplayProperties();
        CacheDeckAlbumArtForQueue(normalizedDeckName);
        SpotifyOperationMessage = $"Cleared {removedCount} pending song(s) from {normalizedDeckName}.";
        QueueSessionStateSave();
    }

    public void ClearDeckQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var removedCount = queue.Count;
        if (removedCount == 0)
        {
            ActiveDeckName = normalizedDeckName;
            RefreshActiveDeckQueue();
            SpotifyOperationMessage = $"{normalizedDeckName} queue is already empty.";
            QueueSessionStateSave();
            return;
        }

        queue.Clear();
        _selectedDeckQueueItemIds[normalizedDeckName] = null;
        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        SpotifyOperationMessage = $"Cleared {removedCount} song(s) from {normalizedDeckName}. Add different tracks to this deck when ready.";
        QueueSessionStateSave();
    }

    private static void RenumberQueue(List<DancePilotQueueItem> queue, string deckName)
    {
        DancePilotQueueOperations.Renumber(queue, deckName);
    }

    private void RefreshActiveDeckQueue()
    {
        RefreshDeckQueueCollection(DeckAQueueItems, _deckAQueue);
        RefreshDeckQueueCollection(DeckBQueueItems, _deckBQueue);
        ActiveDeckQueue.Clear();
        var position = 1;
        foreach (var item in ActiveQueueList())
        {
            ActiveDeckQueue.Add(item with { DeckName = ActiveDeckName, QueuePosition = position++ });
        }

        _selectedDeckQueueItemIds.TryGetValue(ActiveDeckName, out var selectedId);
        SelectedActiveDeckQueueItem = selectedId is not null
            ? ActiveDeckQueue.FirstOrDefault(item => item.Id == selectedId)
            : null;
        RefreshQueueViewDeckQueue();
        UpdateNextUpFromDecks();
    }

    private void RefreshQueueViewDeckQueue()
    {
        QueueViewDeckQueue.Clear();
        var position = 1;
        foreach (var item in QueueViewQueueList().OrderBy(item => item.QueuePosition))
        {
            QueueViewDeckQueue.Add(item with { DeckName = QueueViewDeckName, QueuePosition = position++ });
        }

        _selectedDeckQueueItemIds.TryGetValue(QueueViewDeckName, out var selectedId);
        SelectedQueueViewDeckQueueItem = selectedId is not null
            ? QueueViewDeckQueue.FirstOrDefault(item => item.Id == selectedId)
            : null;
        OnPropertyChanged(nameof(QueueViewDeckCountDisplay));
    }

    private static void RefreshDeckQueueCollection(
        ObservableCollection<DancePilotQueueItem> target,
        IReadOnlyList<DancePilotQueueItem> source)
    {
        target.Clear();
        foreach (var item in source.OrderBy(item => item.QueuePosition))
        {
            target.Add(item);
        }
    }

}
