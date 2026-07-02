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
    private const int LocalLibraryPageSize = 500;

    private async Task LoadLocalMusicAsync()
    {
        try
        {
            var musicFolder = string.IsNullOrWhiteSpace(LocalMusicFolderPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                : LocalMusicFolderPath;
            LocalMusicFolderPath = musicFolder;
            if (!Directory.Exists(musicFolder))
            {
                _allLocalMusicTracks.Clear();
                LocalMusicTracks.Clear();
                SelectedLocalMusicTrack = null;
                LocalLibraryStatus = $"Local music folder not found: {musicFolder}";
                SpotifyOperationMessage = LocalLibraryStatus;
                QueueSessionStateSave();
                return;
            }

            var startedAt = DateTimeOffset.UtcNow;
            var previousSettings = await _localLibrarySettingsRepository.LoadAsync();
            var scanSettings = previousSettings with
            {
                LibraryFolderPath = musicFolder,
                LastScanStartedAt = startedAt,
                AlbumArtCacheFolderPath = LocalMusicLibraryService.DefaultAlbumArtCacheFolderPath
            };
            await _localLibrarySettingsRepository.SaveAsync(scanSettings);
            UpdateLocalLibraryDisplay(scanSettings);

            LocalLibraryStatus = $"Scanning local library: {musicFolder}...";
            var scannedTracks = await _localMusicLibraryService.LoadFromFolderAsync(musicFolder);
            var upserted = await _localMusicRepository.UpsertTracksAsync(scannedTracks);
            var removed = await _localMusicRepository.RemoveMissingTracksAsync(scannedTracks.Select(track => track.FilePath));
            var stats = await _localMusicRepository.GetLibraryStatsAsync();
            var completedAt = DateTimeOffset.UtcNow;
            var completedSettings = scanSettings with
            {
                LastScanCompletedAt = completedAt,
                TrackCount = stats.TrackCount
            };
            await _localLibrarySettingsRepository.SaveAsync(completedSettings);
            UpdateLocalLibraryDisplay(completedSettings);

            var savedTracks = await _localMusicRepository.GetAllTracksAsync(LocalLibraryPageSize);
            ReplaceLocalMusicResults(savedTracks);

            LocalLibraryStatus = stats.TrackCount == 0
                ? $"Loaded 0 saved local tracks. Last scan: {completedAt:g}."
                : $"Loaded {stats.TrackCount:N0} saved local tracks. Last scan: {completedAt:g}.";
            if (removed > 0)
            {
                LocalLibraryStatus += $" Removed {removed:N0} missing track(s).";
            }

            CurrentOutputStatus = upserted == 1
                ? "Local library index saved 1 track."
                : $"Local library index saved {upserted:N0} track(s).";
            SpotifyOperationMessage = LocalLibraryStatus;
            QueueSessionStateSave();
        }
        catch (Exception ex)
        {
            LocalLibraryStatus = $"Local music scan failed: {ex.Message}";
            SpotifyOperationMessage = LocalLibraryStatus;
        }
    }

    public async Task LoadLocalMusicFromFolderAsync(string folderPath)
    {
        LocalMusicFolderPath = folderPath;
        await LoadLocalMusicAsync();
    }

    private async Task LoadSavedLocalLibraryAsync()
    {
        try
        {
            var settings = await _localLibrarySettingsRepository.LoadAsync();
            var stats = await _localMusicRepository.GetLibraryStatsAsync();
            var displaySettings = settings with { TrackCount = stats.TrackCount };
            LocalMusicFolderPath = displaySettings.LibraryFolderPath;
            UpdateLocalLibraryDisplay(displaySettings);

            var tracks = string.IsNullOrWhiteSpace(LocalMusicSearchQuery)
                ? await _localMusicRepository.GetAllTracksAsync(LocalLibraryPageSize)
                : await _localMusicRepository.SearchTracksAsync(LocalMusicSearchQuery, LocalLibraryPageSize);
            ReplaceLocalMusicResults(tracks);

            LocalLibraryStatus = stats.TrackCount == 0
                ? "Loaded 0 saved local tracks. Scan Local Library to build the index."
                : $"Loaded {stats.TrackCount:N0} saved local tracks.";
            if (ActiveSource == SourceLocal)
            {
                SpotifyOperationMessage = LocalLibraryStatus;
            }
        }
        catch (Exception ex)
        {
            LocalLibraryStatus = $"Saved local library could not be loaded: {ex.Message}";
            SpotifyOperationMessage = LocalLibraryStatus;
        }
    }

    private async Task LoadLocalPlaylistsAsync()
    {
        await LoadLocalPlaylistsCoreAsync();
        LocalPlaylistStatus = LocalMusicPlaylists.Count == 0
            ? "No saved local playlists yet."
            : $"Loaded {LocalMusicPlaylists.Count} saved local playlist(s).";
    }

    private async Task LoadLocalPlaylistsCoreAsync()
    {
        var selectedId = SelectedLocalMusicPlaylist?.Id;
        var playlists = await _localPlaylistRepository.GetPlaylistsAsync();
        ReplaceCollection(LocalMusicPlaylists, playlists);
        SelectedLocalMusicPlaylist = LocalMusicPlaylists.FirstOrDefault(playlist => playlist.Id == selectedId)
            ?? LocalMusicPlaylists.FirstOrDefault();
    }

    public async Task<LocalMusicPlaylist?> CreateLocalPlaylistAsync(string playlistName)
    {
        try
        {
            var playlist = await _localPlaylistRepository.CreatePlaylistAsync(playlistName);
            await LoadLocalPlaylistsCoreAsync();
            SelectedLocalMusicPlaylist = LocalMusicPlaylists.FirstOrDefault(existing => existing.Id == playlist.Id)
                ?? playlist;
            LocalPlaylistStatus = $"Saved local playlist: {SelectedLocalMusicPlaylist.Name}.";
            QueueSessionStateSave();
            return SelectedLocalMusicPlaylist;
        }
        catch (Exception ex)
        {
            LocalPlaylistStatus = $"Local playlist could not be saved: {ex.Message}";
            SpotifyOperationMessage = LocalPlaylistStatus;
            return null;
        }
    }

    public async Task AddLocalTracksToSelectedPlaylistAsync(IEnumerable<LocalMusicTrack> tracks)
    {
        if (SelectedLocalMusicPlaylist is null)
        {
            LocalPlaylistStatus = "Create or select a local playlist first.";
            SpotifyOperationMessage = LocalPlaylistStatus;
            return;
        }

        var filePaths = tracks
            .Where(track => File.Exists(track.FilePath))
            .Select(track => track.FilePath)
            .ToList();
        if (filePaths.Count == 0)
        {
            LocalPlaylistStatus = "Select local songs before adding to a playlist.";
            SpotifyOperationMessage = LocalPlaylistStatus;
            return;
        }

        var playlistId = SelectedLocalMusicPlaylist.Id;
        var added = await _localPlaylistRepository.AddTracksAsync(playlistId, filePaths);
        await LoadLocalPlaylistsCoreAsync();
        SelectedLocalMusicPlaylist = LocalMusicPlaylists.FirstOrDefault(playlist => playlist.Id == playlistId)
            ?? SelectedLocalMusicPlaylist;
        LocalPlaylistStatus = added == 0
            ? $"Those songs were already in {SelectedLocalMusicPlaylist.Name}."
            : $"Added {added} song(s) to {SelectedLocalMusicPlaylist.Name}.";
        SpotifyOperationMessage = LocalPlaylistStatus;
        QueueSessionStateSave();
    }

    private async Task AddFilteredLocalMusicToSelectedPlaylistAsync()
    {
        await AddLocalTracksToSelectedPlaylistAsync(LocalMusicTracks);
    }

    public async Task QueueSelectedLocalPlaylistToDeckAsync(string deckName)
    {
        if (SelectedLocalMusicPlaylist is null)
        {
            LocalPlaylistStatus = "Select a saved local playlist first.";
            SpotifyOperationMessage = LocalPlaylistStatus;
            return;
        }

        var playlist = SelectedLocalMusicPlaylist;
        var added = await QueueLocalPlaylistsToDeckAsync([playlist], deckName);
        if (added == 0)
        {
            LocalPlaylistStatus = $"{playlist.Name} has no available songs. Scan or reattach the folder that contains them.";
            SpotifyOperationMessage = LocalPlaylistStatus;
        }
    }

    private async Task DeleteSelectedLocalPlaylistAsync()
    {
        if (SelectedLocalMusicPlaylist is null)
        {
            LocalPlaylistStatus = "Select a saved local playlist to delete.";
            return;
        }

        var name = SelectedLocalMusicPlaylist.Name;
        await _localPlaylistRepository.DeletePlaylistAsync(SelectedLocalMusicPlaylist.Id);
        await LoadLocalPlaylistsCoreAsync();
        LocalPlaylistStatus = $"Deleted local playlist: {name}.";
        SpotifyOperationMessage = LocalPlaylistStatus;
        QueueSessionStateSave();
    }

    private async Task<IReadOnlyList<LocalMusicTrack>> LoadSelectedLocalPlaylistTracksAsync()
    {
        if (SelectedLocalMusicPlaylist is null)
        {
            return [];
        }

        return await LoadLocalPlaylistTracksAsync(SelectedLocalMusicPlaylist);
    }

    private async Task<IReadOnlyList<LocalMusicTrack>> LoadLocalPlaylistTracksAsync(LocalMusicPlaylist playlist)
    {
        var paths = await _localPlaylistRepository.GetTrackFilePathsAsync(playlist.Id);
        var order = paths
            .Select((path, index) => new { Path = path, Index = index })
            .ToDictionary(pair => pair.Path, pair => pair.Index, StringComparer.OrdinalIgnoreCase);
        var tracks = new List<LocalMusicTrack>();
        foreach (var path in paths)
        {
            var track = await _localMusicRepository.GetTrackByPathAsync(path);
            if (track is not null)
            {
                tracks.Add(track);
            }
        }

        return tracks
            .OrderBy(track => order.GetValueOrDefault(track.FilePath, int.MaxValue))
            .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task SearchLocalMusicAsync()
    {
        try
        {
            var stats = await _localMusicRepository.GetLibraryStatsAsync();
            var settings = await _localLibrarySettingsRepository.LoadAsync();
            UpdateLocalLibraryDisplay(settings with { TrackCount = stats.TrackCount });

            var tracks = string.IsNullOrWhiteSpace(LocalMusicSearchQuery)
                ? await _localMusicRepository.GetAllTracksAsync(LocalLibraryPageSize)
                : await _localMusicRepository.SearchTracksAsync(LocalMusicSearchQuery, LocalLibraryPageSize);
            ReplaceLocalMusicResults(tracks);

            LocalLibraryStatus = string.IsNullOrWhiteSpace(LocalMusicSearchQuery)
                ? $"Loaded {stats.TrackCount:N0} saved local tracks."
                : $"Showing {LocalMusicTracks.Count:N0} saved local track(s) for \"{LocalMusicSearchQuery.Trim()}\".";
            SpotifyOperationMessage = LocalLibraryStatus;
            QueueSessionStateSave();
        }
        catch (Exception ex)
        {
            LocalLibraryStatus = $"Saved local library search failed: {ex.Message}";
            SpotifyOperationMessage = LocalLibraryStatus;
        }
    }

    private void ApplyLocalMusicFilter()
    {
        var selectedFilePath = SelectedLocalMusicTrack?.FilePath;
        var filteredTracks = SortLocalMusicTracks(_localMusicLibraryService.Search(_allLocalMusicTracks, LocalMusicSearchQuery));
        LocalMusicTracks.Clear();
        foreach (var track in filteredTracks.Take(500))
        {
            LocalMusicTracks.Add(track);
        }

        SelectedLocalMusicTrack = LocalMusicTracks.FirstOrDefault(track =>
            string.Equals(track.FilePath, selectedFilePath, StringComparison.OrdinalIgnoreCase))
            ?? LocalMusicTracks.FirstOrDefault();
    }

    private void ReplaceLocalMusicResults(IEnumerable<LocalMusicTrack> tracks)
    {
        var selectedFilePath = SelectedLocalMusicTrack?.FilePath;
        var sortedTracks = SortLocalMusicTracks(tracks)
            .Take(LocalLibraryPageSize)
            .ToList();
        _allLocalMusicTracks.Clear();
        _allLocalMusicTracks.AddRange(sortedTracks);
        ReplaceCollection(LocalMusicTracks, sortedTracks);
        SelectedLocalMusicTrack = LocalMusicTracks.FirstOrDefault(track =>
            string.Equals(track.FilePath, selectedFilePath, StringComparison.OrdinalIgnoreCase))
            ?? LocalMusicTracks.FirstOrDefault();
    }

    private void UpdateLocalLibraryDisplay(LocalLibrarySettings settings)
    {
        _localLibrarySavedTrackCount = Math.Max(0, settings.TrackCount);
        _localLibraryLastScanCompletedAt = settings.LastScanCompletedAt;
        _localLibraryAlbumArtCacheFolderPath = string.IsNullOrWhiteSpace(settings.AlbumArtCacheFolderPath)
            ? new LocalLibrarySettings().AlbumArtCacheFolderPath
            : settings.AlbumArtCacheFolderPath;
        OnPropertyChanged(nameof(LocalLibraryTrackCountDisplay));
        OnPropertyChanged(nameof(LocalLibraryLastScanDisplay));
        OnPropertyChanged(nameof(LocalLibraryAlbumArtCacheDisplay));
        OnPropertyChanged(nameof(LocalLibraryScanButtonText));
    }

    private IEnumerable<LocalMusicTrack> SortLocalMusicTracks(IEnumerable<LocalMusicTrack> tracks) =>
        SelectedLocalMusicSortOption switch
        {
            LocalSortTitle => tracks
                .OrderBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Folder, StringComparer.OrdinalIgnoreCase),
            LocalSortArtist => tracks
                .OrderBy(track => track.DisplayArtist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Folder, StringComparer.OrdinalIgnoreCase),
            LocalSortBpm => tracks
                .OrderBy(track => track.BPM is null)
                .ThenBy(track => track.BPM)
                .ThenBy(track => track.MusicalKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase),
            LocalSortKey => tracks
                .OrderBy(track => string.IsNullOrWhiteSpace(track.MusicalKey))
                .ThenBy(track => track.MusicalKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.BPM is null)
                .ThenBy(track => track.BPM)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase),
            LocalSortNewest => tracks
                .OrderByDescending(track => track.LastModifiedAt)
                .ThenBy(track => track.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase),
            LocalSortDuration => tracks
                .OrderBy(track => track.Duration is null)
                .ThenBy(track => track.Duration)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase),
            LocalSortFileType => tracks
                .OrderBy(track => track.Extension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase),
            _ => tracks
                .OrderBy(track => track.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
        };

    private async Task QueueSelectedLocalMusicAsync()
    {
        if (SelectedLocalMusicTrack is null)
        {
            SpotifyOperationMessage = "Select a local music file first.";
            return;
        }

        await QueueLocalTrackToDeckAsync(SelectedLocalMusicTrack, ActiveDeckName);
    }

    private MediaPlayer LocalPlayerForPlayerDeck(string deckName) =>
        NormalizeDeckName(deckName) == "Deck B" ? _deckBLocalMediaPlayer : _deckALocalMediaPlayer;

    private LocalDeckPlaybackState LocalPlaybackStateForPlayerDeck(string deckName) =>
        _localDeckPlaybackStates[NormalizeDeckName(deckName)];

    private LocalDeckPlaybackState? FindLocalPlaybackState(string logicalDeckName, int? queueItemId = null)
    {
        var normalizedDeckName = NormalizeDeckName(logicalDeckName);
        return _localDeckPlaybackStates.Values.FirstOrDefault(state =>
            state.IsLoaded
            && string.Equals(state.LogicalDeckName, normalizedDeckName, StringComparison.Ordinal)
            && (queueItemId is null || state.QueueItemId == queueItemId));
    }

    private bool TryGetActiveLocalPlayback(out LocalDeckPlaybackState state, out MediaPlayer player)
    {
        if (_playingDeckQueueItemId is int itemId
            && FindLocalPlaybackState(_playingDeckName, itemId) is LocalDeckPlaybackState activeState)
        {
            state = activeState;
            player = LocalPlayerForPlayerDeck(activeState.PlayerDeckName);
            return true;
        }

        state = LocalPlaybackStateForPlayerDeck("Deck A");
        player = _deckALocalMediaPlayer;
        return false;
    }

    private Task<bool> StartSelectedLocalMusicAsync(
        string? deckName = null,
        int? queueItemId = null,
        string? playerDeckName = null,
        bool startMuted = false)
    {
        if (SelectedLocalMusicTrack is null)
        {
            SpotifyOperationMessage = "Select a local music file first.";
            return Task.FromResult(false);
        }

        if (!File.Exists(SelectedLocalMusicTrack.FilePath))
        {
            SpotifyOperationMessage = $"Local file was not found: {SelectedLocalMusicTrack.FileName}";
            return Task.FromResult(false);
        }

        var fileUri = new Uri(SelectedLocalMusicTrack.FilePath);
        var playbackDeckName = NormalizeDeckName(deckName ?? ActiveDeckName);
        var playerDeck = NormalizeDeckName(playerDeckName ?? playbackDeckName);
        var state = LocalPlaybackStateForPlayerDeck(playerDeck);
        var player = LocalPlayerForPlayerDeck(playerDeck);

        state.LogicalDeckName = playbackDeckName;
        state.QueueItemId = queueItemId;
        state.RequestedAt = DateTimeOffset.UtcNow;
        state.PreviousLastPlayedQueueItemId = _lastPlayedDeckQueueItemIds.GetValueOrDefault(playbackDeckName);
        state.ProgressObserved = false;
        state.Track = SelectedLocalMusicTrack;
        player.Source = MediaSource.CreateFromUri(fileUri);
        player.Volume = startMuted || AlwaysFadeSongs
            ? 0
            : ResolveLocalOutputLevel(playbackDeckName);
        player.Play();
        StartupLog.Write($"Local playback requested for {playbackDeckName} via {playerDeck} player item {queueItemId?.ToString() ?? "<direct>"}: {SelectedLocalMusicTrack.FilePath}");

        SeekPositionMaximumSeconds = SelectedLocalMusicTrack.Duration?.TotalSeconds ?? 1;
        SeekPositionSeconds = 0;
        IsPlaybackPlaying = true;
        SpotifyNowPlayingTitle = SelectedLocalMusicTrack.Title;
        SpotifyNowPlayingArtist = SelectedLocalMusicTrack.DisplayArtist;
        _currentPlaybackAlbumArtUrl = ResolveLocalTrackAlbumArt(SelectedLocalMusicTrack);
        SpotifyPlaybackStatus = "Playing local";
        SpotifyTimeRemaining = SelectedLocalMusicTrack.DurationDisplay;
        SpotifyProgressDisplay = SelectedLocalMusicTrack.Duration is TimeSpan duration
            ? $"0:00 / {duration:m\\:ss}"
            : "0:00 / --:--";
        CurrentOutputStatus = $"Playing local file: {SelectedLocalMusicTrack.FileName}";
        SpotifyOperationMessage = "Local playback is running from this PC. Spotify commands do not affect local files.";
        return Task.FromResult(true);
    }

    private void SeekLocalPlaybackTo(TimeSpan requestedPosition)
    {
        if (!TryGetActiveLocalPlayback(out _, out var player))
        {
            return;
        }

        var session = player.PlaybackSession;
        var target = requestedPosition < TimeSpan.Zero
            ? TimeSpan.Zero
            : requestedPosition;
        if (session.NaturalDuration > TimeSpan.Zero && target > session.NaturalDuration)
        {
            target = session.NaturalDuration;
        }

        if (session.NaturalDuration > TimeSpan.Zero)
        {
            SeekPositionMaximumSeconds = session.NaturalDuration.TotalSeconds;
        }

        session.Position = target;
        SeekPositionSeconds = target.TotalSeconds;
        SpotifyProgressDisplay = session.NaturalDuration > TimeSpan.Zero
            ? $"{target:m\\:ss} / {session.NaturalDuration:m\\:ss}"
            : $"{target:m\\:ss} / --:--";
        SpotifyTimeRemaining = session.NaturalDuration > TimeSpan.Zero
            ? $"{(session.NaturalDuration - target):m\\:ss}"
            : "--:--";
        CurrentOutputStatus = $"Local file position: {target:m\\:ss}.";
    }

    private void PauseLocalMusic()
    {
        if (TryGetActiveLocalPlayback(out _, out var player))
        {
            player.Pause();
        }

        IsPlaybackPlaying = false;
        SpotifyPlaybackStatus = "Local paused";
        CurrentOutputStatus = "Local file playback paused.";
        SpotifyOperationMessage = "Local playback paused.";
    }

    private void ResumeLocalMusic()
    {
        if (!TryGetActiveLocalPlayback(out var state, out var player))
        {
            return;
        }

        player.Volume = ResolveLocalOutputLevel(state.LogicalDeckName);
        player.Play();
        state.RequestedAt ??= DateTimeOffset.UtcNow;
        IsPlaybackPlaying = true;
        SpotifyPlaybackStatus = "Playing local";
        CurrentOutputStatus = $"Resumed local file on {_playingDeckName}.";
        SpotifyOperationMessage = "Local playback resumed.";
        RefreshDeckDisplayProperties();
    }

    private bool IsLocalPlaybackLoadedForQueueItem(string deckName, DancePilotQueueItem item) =>
        FindLocalPlaybackState(deckName, item.Id) is not null;

    private void ClearLocalPlaybackMarker(string playerDeckName)
    {
        LocalPlaybackStateForPlayerDeck(playerDeckName).Clear();
    }

    private void RestoreUnstartedLocalPlaybackCursor(string deckName, int itemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var state = FindLocalPlaybackState(normalizedDeckName, itemId);
        if (_lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName) == itemId)
        {
            _lastPlayedDeckQueueItemIds[normalizedDeckName] = state?.PreviousLastPlayedQueueItemId;
        }

        if (_selectedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName) != itemId)
        {
            _selectedDeckQueueItemIds[normalizedDeckName] = itemId;
        }
    }

    private void ObserveLocalPlaybackProgress(LocalDeckPlaybackState state, TimeSpan position)
    {
        if (state.QueueItemId is null || state.ProgressObserved)
        {
            return;
        }

        if (position >= TimeSpan.FromMilliseconds(500))
        {
            state.ProgressObserved = true;
        }
    }

    private bool HasLocalPlaybackActuallyStarted(string deckName, int? queueItemId = null)
    {
        var state = FindLocalPlaybackState(deckName, queueItemId);
        if (state is null)
        {
            return false;
        }

        if (state.ProgressObserved)
        {
            return true;
        }

        var session = LocalPlayerForPlayerDeck(state.PlayerDeckName).PlaybackSession;
        var position = session.Position < TimeSpan.Zero ? TimeSpan.Zero : session.Position;
        if (position >= TimeSpan.FromMilliseconds(500))
        {
            state.ProgressObserved = true;
            return true;
        }

        var duration = session.NaturalDuration > TimeSpan.Zero
            ? session.NaturalDuration
            : state.Track?.Duration;
        if (duration is TimeSpan durationValue
            && durationValue > TimeSpan.FromSeconds(1)
            && position >= durationValue - TimeSpan.FromMilliseconds(500))
        {
            state.ProgressObserved = true;
            return true;
        }

        return false;
    }

    private void SetPlaybackModeForDeckPlayback(string playbackMode)
    {
        var wasSuppressed = _suppressLocalMusicAutoLoad;
        _suppressLocalMusicAutoLoad = true;
        try
        {
            SelectedPlaybackMode = playbackMode;
        }
        finally
        {
            _suppressLocalMusicAutoLoad = wasSuppressed;
        }
    }

    private async Task PauseSpotifyForLocalPlaybackAsync()
    {
        try
        {
            if (!await _spotifyService.IsConnectedAsync())
            {
                return;
            }

            var missingModifyScopes = await _spotifyService.GetMissingScopesAsync([SpotifyScopes.UserModifyPlaybackState]);
            if (missingModifyScopes.Count > 0)
            {
                StartupLog.Write("Spotify pause before local playback skipped; missing scope(s): " + string.Join(", ", missingModifyScopes));
                return;
            }

            var deviceId = SelectedSpotifyDevice?.Id ?? _selectedOutputDeviceId;
            var missingReadScopes = await _spotifyService.GetMissingScopesAsync([SpotifyScopes.UserReadPlaybackState]);
            if (missingReadScopes.Count == 0)
            {
                var state = await _spotifyPlayerService.GetPlaybackStateAsync(CurrentSpotifySettings);
                if (state?.IsPlaying != true)
                {
                    return;
                }

                deviceId = state.Device?.Id ?? deviceId;
            }

            await _spotifyPlayerService.PauseAsync(CurrentSpotifySettings, deviceId ?? string.Empty);
            StartupLog.Write("Spotify paused before local deck playback.");
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Spotify pause before local playback skipped: {ex.Message}");
        }
    }

    private void PauseLocalForSpotifyPlayback()
    {
        try
        {
            foreach (var state in _localDeckPlaybackStates.Values)
            {
                var queueItemId = state.QueueItemId;
                var logicalDeckName = state.LogicalDeckName;
                var localPlaybackStarted = queueItemId is not null && HasLocalPlaybackActuallyStarted(logicalDeckName, queueItemId);
                LocalPlayerForPlayerDeck(state.PlayerDeckName).Pause();
                if (queueItemId is not null && !localPlaybackStarted)
                {
                    RestoreUnstartedLocalPlaybackCursor(logicalDeckName, queueItemId.Value);
                }

                state.Clear();
            }

            if (ResolvePlayingDeckItem(_playingDeckName)?.Source == SongSources.Local)
            {
                IsPlaybackPlaying = false;
                SpotifyPlaybackStatus = "Local paused";
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Local pause before Spotify playback skipped: {ex.Message}");
        }
    }

    private async Task PrepareOutputForDeckPlaybackAsync(DancePilotQueueItem queueItem)
    {
        if (queueItem.IsLocalSource)
        {
            await PauseSpotifyForLocalPlaybackAsync();
            return;
        }

        if (queueItem.IsSpotifySource)
        {
            PauseLocalForSpotifyPlayback();
        }
    }

}
