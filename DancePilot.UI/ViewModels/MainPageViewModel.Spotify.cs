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
    private async Task LoginSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await SaveSpotifySettingsAsync();
            await _spotifyService.LoginAsync(CurrentSpotifySettings);
            var profile = await _spotifyService.GetCurrentUserProfileAsync(CurrentSpotifySettings);
            SpotifyConnectionStatus = $"Connected as {profile.DisplayName}";
            SpotifyOperationMessage = "Spotify connected for profile, playlists, search, and metadata. Playback control scopes are requested only when you use Spotify Connect controls.";
        });
    }

    private async Task LogoutSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await _spotifyService.LogoutAsync();
            SpotifyPlaylists.Clear();
            SpotifyPreviewTracks.Clear();
            SpotifySearchResults.Clear();
            SpotifyDevices.Clear();
            SelectedSpotifyPlaylist = null;
            SelectedSpotifyTrack = null;
            SelectedSpotifySearchTrack = null;
            SelectedSpotifyDevice = null;
            SpotifyConnectionStatus = "Not connected";
            SpotifyOperationMessage = "Spotify tokens removed from this Windows profile.";
        });
    }

    private async Task LoadSpotifyPlaylistsAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await SaveSpotifySettingsAsync();
            var playlists = await _spotifyPlaylistImporter.GetUserPlaylistsAsync(CurrentSpotifySettings);
            SpotifyPlaylists.Clear();
            foreach (var playlist in playlists.OrderBy(playlist => playlist.Name))
            {
                SpotifyPlaylists.Add(playlist);
            }

            StartupLog.Write("Spotify playlists returned: " + string.Join("; ", SpotifyPlaylists.Select(playlist =>
                $"{playlist.Name}/tracks={playlist.TrackCount}/id={playlist.SpotifyPlaylistId}")));

            _suppressSpotifyPlaylistAutoLoad = true;
            try
            {
                SelectedSpotifyPlaylist = null;
            }
            finally
            {
                _suppressSpotifyPlaylistAutoLoad = false;
            }

            SpotifyOperationMessage = SpotifyPlaylists.Count == 0
                ? "No Spotify playlists were returned for this account."
                : $"Loaded {SpotifyPlaylists.Count} Spotify playlist(s). Select a playlist to load its songs.";
        });
    }

    private async Task PreviewSpotifyPlaylistAsync()
    {
        if (SelectedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select a Spotify playlist first.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await PreviewSpotifyPlaylistCoreAsync();
        });
    }

    private async Task PreviewSpotifyPlaylistFromSelectionAsync(SpotifyPlaylistSummary playlist)
    {
        await RunSpotifyOperationAsync(async () =>
        {
            if (!string.Equals(SelectedSpotifyPlaylist?.SpotifyPlaylistId, playlist.SpotifyPlaylistId, StringComparison.Ordinal))
            {
                return;
            }

            await PreviewSpotifyPlaylistCoreAsync();
        });
    }

    private async Task PreviewSpotifyPlaylistCoreAsync()
    {
        if (SelectedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select a Spotify playlist first.";
            return;
        }

        SpotifyPreviewTracks.Clear();
        SelectedSpotifyTrack = null;
        SpotifyOperationMessage = $"Loading tracks from {SelectedSpotifyPlaylist.Name}...";
        var tracks = await _spotifyPlaylistImporter.PreviewPlaylistTracksAsync(CurrentSpotifySettings, SelectedSpotifyPlaylist.SpotifyPlaylistId);
        foreach (var track in tracks)
        {
            SpotifyPreviewTracks.Add(track);
        }

        StartupLog.Write($"Spotify playlist preview loaded: {SelectedSpotifyPlaylist.Name}/summaryTracks={SelectedSpotifyPlaylist.TrackCount}/loadedTracks={tracks.Count}/id={SelectedSpotifyPlaylist.SpotifyPlaylistId}");
        UpdateSelectedPlaylistTrackCount(tracks.Count);
        SelectedSpotifyTrack = SpotifyPreviewTracks.FirstOrDefault();
        var unavailableCount = SpotifyPreviewTracks.Count(track => track.IsUnavailable);
        SpotifyOperationMessage = tracks.Count == 0
            ? $"Spotify returned no track items for {SelectedSpotifyPlaylist.Name}."
            : unavailableCount == 0
                ? $"Loaded {SpotifyPreviewTracks.Count} track(s) from {SelectedSpotifyPlaylist.Name}."
                : $"Loaded {SpotifyPreviewTracks.Count} track(s) from {SelectedSpotifyPlaylist.Name}; {unavailableCount} are Spotify-local or unavailable for Spotify API playback.";
        QueueSessionStateSave();
    }

    private void UpdateSelectedPlaylistTrackCount(int trackCount)
    {
        if (SelectedSpotifyPlaylist is null)
        {
            return;
        }

        var index = SpotifyPlaylists.IndexOf(SelectedSpotifyPlaylist);
        var displayTrackCount = SelectedSpotifyPlaylist.TrackCount > 0
            ? SelectedSpotifyPlaylist.TrackCount
            : trackCount;
        var updated = SelectedSpotifyPlaylist with { TrackCount = displayTrackCount };
        if (index >= 0)
        {
            SpotifyPlaylists[index] = updated;
        }

        _suppressSpotifyPlaylistAutoLoad = true;
        try
        {
            SelectedSpotifyPlaylist = updated;
        }
        finally
        {
            _suppressSpotifyPlaylistAutoLoad = false;
        }
    }

    private async Task ImportSpotifyPlaylistAsync()
    {
        if (SelectedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select and preview a Spotify playlist first.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            var tracks = SpotifyPreviewTracks.Count > 0
                ? SpotifyPreviewTracks.ToList()
                : (await _spotifyService.GetPlaylistTracksAsync(CurrentSpotifySettings, SelectedSpotifyPlaylist.SpotifyPlaylistId)).ToList();

            var result = await _spotifyImportRepository.ImportPlaylistAsync(SelectedSpotifyPlaylist, tracks);
            await LoadImportedSpotifyPlaylistsCoreAsync();
            SpotifyOperationMessage =
                $"Saved locally: {result.ImportedCount} imported, {result.UpdatedCount} updated, {result.UnavailableCount} unavailable skipped, {result.LikelyLocalMatchCount} likely local matches.";
        });
    }

    private async Task OpenSelectedSpotifyTrackAsync()
    {
        var track = SelectedSpotifyTrack ?? SelectedSpotifySearchTrack ?? SelectedImportedSpotifyTrack;
        if (track?.ExternalUrl is null)
        {
            SpotifyOperationMessage = "Select a Spotify track with an external URL first.";
            return;
        }

        await Launcher.LaunchUriAsync(new Uri(track.ExternalUrl));
    }

    private async Task SearchSpotifyTracksAsync()
    {
        if (string.IsNullOrWhiteSpace(SpotifySearchQuery))
        {
            SpotifyOperationMessage = "Enter a song or artist to search Spotify.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            var tracks = await _spotifyService.SearchTracksAsync(CurrentSpotifySettings, SpotifySearchQuery.Trim(), limit: 10);
            SpotifySearchResults.Clear();
            foreach (var track in tracks)
            {
                SpotifySearchResults.Add(track);
            }

            SelectedSpotifySearchTrack = SpotifySearchResults.FirstOrDefault();
            SpotifyOperationMessage = $"Found {SpotifySearchResults.Count} Spotify search result(s) for \"{SpotifySearchQuery.Trim()}\".";
            QueueSessionStateSave();
        });
    }

    private async Task PlaySelectedSpotifySearchTrackAsync()
    {
        if (SelectedSpotifySearchTrack is null)
        {
            SpotifyOperationMessage = "Select a Spotify search result first.";
            return;
        }

        await QueueSpotifyTrackToDeckAsync(SelectedSpotifySearchTrack, ActiveDeckName);
    }

    private async Task<bool> PlaySpotifyTrackWithFallbackAsync(
        SpotifyTrackMetadata track,
        string successMessage,
        bool allowExternalFallback = true)
    {
        if (allowExternalFallback && await TryExternalHandoffTrackAsync(track))
        {
            return false;
        }

        await EnsurePlaybackScopesAsync();
        EnsureSpotifyConnectPlaybackMode();
        try
        {
            var deviceId = await ResolveSelectedDeviceIdAsync();
            await _playbackCoordinator.PlayTrackAsync(CurrentSpotifySettings, deviceId, track);
            SpotifyOperationMessage = successMessage;
            await RefreshPlaybackCoreAsync(runAutopilot: false);
            return true;
        }
        catch (SpotifyApiException ex) when (ex.Kind is SpotifyApiErrorKind.PlaybackForbidden or SpotifyApiErrorKind.NoActiveDevice or SpotifyApiErrorKind.DeviceUnavailable)
        {
            if (!allowExternalFallback)
            {
                throw;
            }

            StartupLog.Write($"Spotify Connect play failed ({ex.Kind}); falling back to Spotify app for {track.Title}. status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
            if (!await OpenSpotifyTrackLinkAsync(track))
            {
                throw;
            }

            SelectedPlaybackMode = SpotifyPlaybackModes.ExternalSpotifyAppHandoff;
            SpotifyOperationMessage = $"Spotify refused remote control, so DancePilot opened {track.Title} in Spotify. Press Play in Spotify if it does not start automatically.";
            return false;
        }
    }

    private async Task RefreshSpotifyDevicesAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            await RefreshSpotifyDevicesCoreAsync();
        });
    }

    private async Task TransferSpotifyPlaybackAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            var deviceId = await ResolveSelectedDeviceIdAsync();
            await _spotifyDeviceManager.TransferPlaybackAsync(CurrentSpotifySettings, deviceId);
            await SavePlaybackSettingsAsync();
            CurrentOutputStatus = $"Transferred playback to {SelectedSpotifyDevice?.Name ?? "selected Spotify device"}.";
            SpotifyOperationMessage = "Spotify output transferred. Audio remains controlled by Spotify, the selected device, and Windows.";
        });
    }

    private async Task RefreshSpotifyPlaybackAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task PlaySelectedSpotifyTrackAsync()
    {
        var track = SelectedImportedSpotifyTrack ?? SelectedSpotifyTrack ?? SelectedSpotifySearchTrack;
        if (track is null)
        {
            SpotifyOperationMessage = "Select a Spotify track first.";
            return;
        }

        await QueueSpotifyTrackToDeckAsync(track, ActiveDeckName);
    }

    private async Task PlayImportedSpotifyPlaylistAsync()
    {
        if (SelectedImportedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select an imported Spotify playlist first.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            if (await TryExternalHandoffPlaylistAsync(SelectedImportedSpotifyPlaylist))
            {
                return;
            }

            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            try
            {
                var deviceId = await ResolveSelectedDeviceIdAsync();
                await _spotifyPlayerService.PlayPlaylistAsync(CurrentSpotifySettings, deviceId, SelectedImportedSpotifyPlaylist.SpotifyPlaylistId);
            }
            catch (SpotifyApiException ex) when (ex.Kind == SpotifyApiErrorKind.PlaybackForbidden
                && SelectedImportedSpotifyPlaylist is not null
                && HasSpotifyPlaylistLink(SelectedImportedSpotifyPlaylist))
            {
                StartupLog.Write($"Spotify Connect playlist play refused; falling back to Spotify app for {SelectedImportedSpotifyPlaylist.Name}. status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
                SelectedPlaybackMode = SpotifyPlaybackModes.ExternalSpotifyAppHandoff;
                await OpenSpotifyPlaylistLinkAsync(SelectedImportedSpotifyPlaylist);
                SpotifyOperationMessage = $"Spotify refused remote control, so DancePilot opened {SelectedImportedSpotifyPlaylist.Name} in Spotify. Press Play in Spotify if it does not start automatically.";
                return;
            }

            CurrentSpotifyPlaylistName = SelectedImportedSpotifyPlaylist.Name;
            SpotifyOperationMessage = $"Started Spotify playlist: {SelectedImportedSpotifyPlaylist.Name}.";
            await SavePlaybackSettingsAsync();
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task PlayFromSelectedSpotifyTrackAsync()
    {
        if (SelectedImportedSpotifyTrack is null)
        {
            SpotifyOperationMessage = "Select a Spotify playlist track first.";
            return;
        }

        await QueueSpotifyTrackToDeckAsync(SelectedImportedSpotifyTrack, ActiveDeckName);
    }

    private async Task AddSelectedTrackToQueueAsync()
    {
        var track = SelectedImportedSpotifyTrack ?? SelectedSpotifyTrack;
        if (track is null)
        {
            SpotifyOperationMessage = "Select a Spotify track before adding to the DancePilot queue.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            var item = await _playbackCoordinator.AddToQueueAsync(track);
            await RefreshQueueCoreAsync();
            SpotifyOperationMessage = $"Added to DancePilot queue: {item.Title}.";
        });
    }

    private async Task RecommendNextFromPlaylistAsync()
    {
        if (ImportedSpotifyTracks.Count == 0)
        {
            SpotifyOperationMessage = "Load an imported playlist before asking DancePilot to recommend a next Spotify track.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            var candidate = ImportedSpotifyTracks
                .FirstOrDefault(track => !string.Equals(track.SpotifyUri, SelectedImportedSpotifyTrack?.SpotifyUri, StringComparison.OrdinalIgnoreCase))
                ?? ImportedSpotifyTracks.First();

            SelectedImportedSpotifyTrack = candidate;
            var item = await _playbackCoordinator.AddToQueueAsync(candidate);
            await RefreshQueueCoreAsync();
            SpotifyOperationMessage = $"Recommended next from playlist: {item.Title}.";
        });
    }

    private async Task PauseSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.PauseAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            SpotifyOperationMessage = "Spotify playback paused.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task ResumeSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.ResumeAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            SpotifyOperationMessage = "Spotify playback resumed.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task SkipNextSpotifyAsync()
    {
        var hadLoadedDeckItem = _playingDeckQueueItemId is not null;
        if (await TryPlayNextDeckQueueItemAsync())
        {
            return;
        }

        if (hadLoadedDeckItem)
        {
            if (string.IsNullOrWhiteSpace(SpotifyOperationMessage))
            {
                SpotifyOperationMessage = "No playable transition target is available.";
            }

            return;
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SpotifyOperationMessage = $"No next queued song on {NormalizeDeckName(_playingDeckQueueItemId is null ? ActiveDeckName : _playingDeckName)}.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.SkipNextAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            SpotifyOperationMessage = "Skipped to the next Spotify track.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task SkipPreviousSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.SkipPreviousAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            SpotifyOperationMessage = "Returned to the previous Spotify track.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task SetSpotifyVolumeAsync()
    {
        await SetMainOutputVolumeAsync();
    }

    private async Task SetMainOutputVolumeAsync()
    {
        var volumePercent = ResolveMainOutputVolumePercent();
        await SavePlaybackSettingsAsync();

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            ApplyLocalOutputLevelIfDeckIsLive(_playingDeckName);
            SpotifyOperationMessage = $"Main output volume set to {FormatVolume(volumePercent)}. Deck faders control local mix level.";
            return;
        }

        if (SelectedPlaybackMode != SpotifyPlaybackModes.SpotifyConnect)
        {
            SpotifyOperationMessage = $"Main output volume saved at {FormatVolume(volumePercent)}. Open in Spotify App mode uses Spotify or Windows volume controls.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.SetVolumeAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync(), volumePercent);
            await SavePlaybackSettingsAsync();
            SpotifyOperationMessage = $"Requested Spotify output volume {FormatVolume(volumePercent)}. Device support may vary.";
        });
    }

    private async Task SeekSpotifyAsync()
    {
        SeekPositionSeconds = Math.Clamp(SeekPositionSeconds, 0, SeekPositionMaximumSeconds);

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SeekLocalPlaybackTo(TimeSpan.FromSeconds(SeekPositionSeconds));
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            var targetMs = Convert.ToInt32(SeekPositionSeconds * 1000);
            await _spotifyPlayerService.SeekAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync(), targetMs);
            SpotifyOperationMessage = $"Spotify seek requested at {SeekPositionSeconds:N0} seconds.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task SeekRelativePlaybackAsync(int seconds)
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            var currentPosition = TryGetActiveLocalPlayback(out _, out var player)
                ? player.PlaybackSession.Position
                : TimeSpan.Zero;
            SeekLocalPlaybackTo(currentPosition + TimeSpan.FromSeconds(seconds));
            SpotifyOperationMessage = seconds < 0
                ? $"Rewound local playback {Math.Abs(seconds)} seconds."
                : $"Fast-forwarded local playback {seconds} seconds.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            var state = await _spotifyPlayerService.GetPlaybackStateAsync(CurrentSpotifySettings);
            UpdateSeekPositionMaximum(state?.DurationMs);
            var currentMs = state?.ProgressMs ?? Convert.ToInt32(SeekPositionSeconds * 1000);
            var targetMs = Math.Max(0, currentMs + seconds * 1000);
            if (state?.DurationMs is int durationMs)
            {
                targetMs = Math.Min(durationMs, targetMs);
            }

            await _spotifyPlayerService.SeekAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync(), targetMs);
            SeekPositionSeconds = targetMs / 1000d;
            SpotifyOperationMessage = seconds < 0
                ? $"Rewound {Math.Abs(seconds)} seconds."
                : $"Fast-forwarded {seconds} seconds.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task EmergencyStopAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            SpotifyAutopilotEnabled = false;
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.PauseAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            await SavePlaybackSettingsAsync();
            SpotifyOperationMessage = "Emergency stop sent: Spotify paused and DancePilot Spotify Autopilot disabled.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task DisableSpotifyAutopilotAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            SpotifyAutopilotEnabled = false;
            await SavePlaybackSettingsAsync();
            SpotifyOperationMessage = "DancePilot Spotify Autopilot disabled.";
        });
    }

    private async Task LoadImportedSpotifyPlaylistsAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await LoadImportedSpotifyPlaylistsCoreAsync();
            SpotifyOperationMessage = $"Loaded {ImportedSpotifyPlaylists.Count} imported Spotify playlists from local SQLite.";
            QueueSessionStateSave();
        });
    }

    private async Task LoadImportedPlaylistTracksAsync()
    {
        if (SelectedImportedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select an imported Spotify playlist first.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await LoadImportedPlaylistTracksCoreAsync();
            SpotifyOperationMessage = $"Loaded {ImportedSpotifyTracks.Count} imported tracks from {SelectedImportedSpotifyPlaylist.Name}.";
            QueueSessionStateSave();
        });
    }


    private async Task LoadImportedSpotifyPlaylistsCoreAsync(bool loadTracksForSelection = true)
    {
        var playlists = await _spotifyLibraryRepository.GetImportedPlaylistsAsync();
        ImportedSpotifyPlaylists.Clear();
        foreach (var playlist in playlists)
        {
            ImportedSpotifyPlaylists.Add(playlist);
        }

        SelectedImportedSpotifyPlaylist = ImportedSpotifyPlaylists.FirstOrDefault();
        if (loadTracksForSelection && SelectedImportedSpotifyPlaylist is not null)
        {
            await LoadImportedPlaylistTracksCoreAsync();
        }
        else
        {
            ImportedSpotifyTracks.Clear();
            SelectedImportedSpotifyTrack = null;
        }
    }

    private async Task LoadImportedPlaylistTracksCoreAsync()
    {
        ImportedSpotifyTracks.Clear();
        if (SelectedImportedSpotifyPlaylist is null)
        {
            return;
        }

        var tracks = await _spotifyLibraryRepository.GetImportedPlaylistTracksAsync(SelectedImportedSpotifyPlaylist.SpotifyPlaylistId);
        foreach (var track in tracks)
        {
            ImportedSpotifyTracks.Add(track);
        }

        SelectedImportedSpotifyTrack = ImportedSpotifyTracks.FirstOrDefault();
    }

    private async Task RefreshQueueCoreAsync()
    {
        var queuedItems = await _playbackCoordinator.GetPendingQueueAsync();
        DancePilotQueue.Clear();
        foreach (var item in queuedItems)
        {
            DancePilotQueue.Add(item);
        }

        var next = DancePilotQueue.FirstOrDefault();
        NextUpTitle = next?.Title ?? "No queued recommendation";
        NextUpArtist = next?.Artist ?? "DancePilot queue";
    }

    private async Task<bool> TryExternalHandoffTrackAsync(SpotifyTrackMetadata track)
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SpotifyOperationMessage = "Local Music Files mode plays files from this PC. Select a local file and use PLAY FILE, or switch to Spotify Web API / Spotify Connect for Spotify tracks.";
            return true;
        }

        if (SelectedPlaybackMode != SpotifyPlaybackModes.ExternalSpotifyAppHandoff)
        {
            return false;
        }

        if (!HasSpotifyTrackLink(track))
        {
            SpotifyOperationMessage = "This Spotify track does not have a Spotify link to open.";
            return true;
        }

        await OpenSpotifyTrackLinkAsync(track);
        SpotifyOperationMessage = $"Opened {track.Title} in Spotify.";
        return true;
    }

    private async Task<bool> TryExternalHandoffPlaylistAsync(SpotifyPlaylistSummary playlist)
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SpotifyOperationMessage = "Local Music Files mode plays files from this PC. Switch to Spotify Web API / Spotify Connect or Open in Spotify App for Spotify playlists.";
            return true;
        }

        if (SelectedPlaybackMode != SpotifyPlaybackModes.ExternalSpotifyAppHandoff)
        {
            return false;
        }

        if (!HasSpotifyPlaylistLink(playlist))
        {
            SpotifyOperationMessage = "This Spotify playlist does not have a Spotify link to open.";
            return true;
        }

        await OpenSpotifyPlaylistLinkAsync(playlist);
        SpotifyOperationMessage = $"Opened {playlist.Name} in Spotify.";
        return true;
    }

    private static bool HasSpotifyTrackLink(SpotifyTrackMetadata track) =>
        !string.IsNullOrWhiteSpace(track.SpotifyUri)
        || !string.IsNullOrWhiteSpace(track.SpotifyTrackId)
        || !string.IsNullOrWhiteSpace(track.ExternalUrl);

    private static bool HasSpotifyPlaylistLink(SpotifyPlaylistSummary playlist) =>
        !string.IsNullOrWhiteSpace(playlist.SpotifyPlaylistId)
        || !string.IsNullOrWhiteSpace(playlist.ExternalUrl);

    private static async Task<bool> OpenSpotifyTrackLinkAsync(SpotifyTrackMetadata track)
    {
        if (!string.IsNullOrWhiteSpace(track.SpotifyUri)
            && await TryLaunchUriAsync(track.SpotifyUri))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(track.SpotifyTrackId)
            && await TryLaunchUriAsync($"spotify:track:{track.SpotifyTrackId}"))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(track.ExternalUrl)
            && await TryLaunchUriAsync(track.ExternalUrl);
    }

    private static async Task<bool> OpenSpotifyPlaylistLinkAsync(SpotifyPlaylistSummary playlist)
    {
        if (!string.IsNullOrWhiteSpace(playlist.SpotifyPlaylistId)
            && await TryLaunchUriAsync($"spotify:playlist:{playlist.SpotifyPlaylistId}"))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(playlist.ExternalUrl)
            && await TryLaunchUriAsync(playlist.ExternalUrl);
    }

    private static async Task<bool> TryLaunchUriAsync(string uriText)
    {
        return Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
            && await Launcher.LaunchUriAsync(uri);
    }

    private async Task<string> ResolveSelectedDeviceIdAsync()
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Local Music Files mode is active. Use PLAY FILE for local tracks or switch to Spotify Web API / Spotify Connect for Spotify controls.");
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.WebPlaybackSdk)
        {
            throw new SpotifyApiException(
                SpotifyApiErrorKind.WebPlaybackSdkUnsupported,
                "The Spotify Web Playback SDK is a browser/WebView player path. Use Spotify Web API / Spotify Connect or Open in Spotify App for this native Windows build.");
        }

        if (SelectedSpotifyDevice?.IsRestricted == true)
        {
            throw new SpotifyApiException(
                SpotifyApiErrorKind.DeviceUnavailable,
                $"{SelectedSpotifyDevice.DisplayName} is marked restricted by Spotify and cannot accept remote playback commands.");
        }

        if (!string.IsNullOrWhiteSpace(SelectedSpotifyDevice?.Id))
        {
            return SelectedSpotifyDevice.Id;
        }

        if (SpotifyDevices.Count == 0)
        {
            await RefreshSpotifyDevicesCoreAsync();
        }

        if (!string.IsNullOrWhiteSpace(SelectedSpotifyDevice?.Id))
        {
            return SelectedSpotifyDevice.Id;
        }

        if (!string.IsNullOrWhiteSpace(_selectedOutputDeviceId))
        {
            return _selectedOutputDeviceId;
        }

        var saved = await _playbackSettingsRepository.LoadAsync();
        if (!string.IsNullOrWhiteSpace(saved.SelectedDeviceId))
        {
            _selectedOutputDeviceId = saved.SelectedDeviceId;
            SelectedOutputDeviceName = string.IsNullOrWhiteSpace(saved.SelectedDeviceName)
                ? SelectedOutputDeviceName
                : saved.SelectedDeviceName;
            return saved.SelectedDeviceId;
        }

        throw new SpotifyApiException(SpotifyApiErrorKind.NoActiveDevice, "Select a Spotify output device first.");
    }

    private void EnsureSpotifyConnectPlaybackMode()
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.WebPlaybackSdk)
        {
            throw new SpotifyApiException(
                SpotifyApiErrorKind.WebPlaybackSdkUnsupported,
                "Spotify Web Playback SDK is not the native WinUI playback path. Select Spotify Web API / Spotify Connect and use your HAASLAPTOP Spotify device.");
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Local Music Files mode is active. Use PLAY FILE for local tracks or switch to Spotify Web API / Spotify Connect for Spotify controls.");
        }
    }

    private static string NormalizePlaybackMode(string playbackMode) =>
        playbackMode switch
        {
            SpotifyPlaybackModes.LegacySpotifyConnect => SpotifyPlaybackModes.SpotifyConnect,
            SpotifyPlaybackModes.LegacyExternalSpotifyAppHandoff => SpotifyPlaybackModes.ExternalSpotifyAppHandoff,
            _ => playbackMode
        };

    private static ImageSource CreateAlbumArtSource(string? albumArtSource, string fallbackSource)
    {
        var normalized = NormalizeAlbumArtSource(albumArtSource, fallbackSource);
        try
        {
            return new BitmapImage(new Uri(normalized));
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Album art source rejected: {albumArtSource ?? "<empty>"}; {ex.Message}");
            return new BitmapImage(new Uri(NormalizeAlbumArtSource(fallbackSource, DefaultLocalAlbumArtPath)));
        }
    }

    private static ImageSource CreateAlbumArtSource(string? albumArtSource, ImageSource existingSource, string fallbackSource) =>
        string.IsNullOrWhiteSpace(albumArtSource)
            ? existingSource
            : CreateAlbumArtSource(albumArtSource, fallbackSource);

    private static string NormalizeAlbumArtSource(string? albumArtSource, string fallbackSource)
    {
        var fallback = NormalizeAlbumArtSourceCore(fallbackSource, null) ?? DefaultLocalAlbumArtPath;
        return NormalizeAlbumArtSourceCore(albumArtSource, fallback) ?? fallback;
    }

    private static string? NormalizeAlbumArtSourceCore(string? albumArtSource, string? fallbackSource)
    {
        if (string.IsNullOrWhiteSpace(albumArtSource))
        {
            return fallbackSource;
        }

        var source = albumArtSource.Trim();
        if (source.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(@"Assets\", StringComparison.OrdinalIgnoreCase))
        {
            return $"ms-appx:///{source.Replace('\\', '/')}";
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https" or "ms-appx")
            {
                return uri.AbsoluteUri;
            }

            if (uri.Scheme == "file")
            {
                return File.Exists(uri.LocalPath) ? uri.AbsoluteUri : fallbackSource;
            }
        }

        if (Path.IsPathFullyQualified(source) && File.Exists(source))
        {
            return new Uri(source).AbsoluteUri;
        }

        return fallbackSource;
    }

    private static string NormalizeSource(string source) =>
        LiveEventMusicSources.Normalize(source);

    private static bool IsSupportedPlaybackMode(string playbackMode)
    {
        var normalizedPlaybackMode = NormalizePlaybackMode(playbackMode);
        return normalizedPlaybackMode is SpotifyPlaybackModes.SpotifyConnect
            or SpotifyPlaybackModes.ExternalSpotifyAppHandoff
            or SpotifyPlaybackModes.LocalFilesFuture;
    }

    private async Task RefreshSpotifyConnectionStatusAsync()
    {
        try
        {
            if (!await _spotifyService.IsConnectedAsync())
            {
                SpotifyConnectionStatus = "Not connected";
                return;
            }

            var missingPlanningScopes = await _spotifyService.GetMissingScopesAsync(SpotifyScopes.PlanningMetadata);
            if (missingPlanningScopes.Count > 0)
            {
                SpotifyConnectionStatus = "Token stored, metadata scopes missing";
                SpotifyOperationMessage = $"Log out and log back in so DancePilot can request: {string.Join(", ", missingPlanningScopes)}.";
                return;
            }

            var missingPlaybackScopes = await _spotifyService.GetMissingScopesAsync(SpotifyScopes.PlaybackControl);
            SpotifyConnectionStatus = missingPlaybackScopes.Count == 0
                ? "Connected, playback scopes ready"
                : "Connected for Spotify metadata";
        }
        catch
        {
            SpotifyConnectionStatus = "Token unreadable";
        }
    }

    private async Task SaveSpotifySettingsAsync()
    {
        await _spotifySettingsStore.SaveAsync(CurrentSpotifySettings);
    }

    private async Task SavePlaybackSettingsAsync()
    {
        await _playbackSettingsRepository.SaveAsync(CurrentPlaybackSettings);
    }

    private async Task EnsurePlaybackScopesAsync()
    {
        var missingScopes = await _spotifyService.GetMissingScopesAsync(SpotifyScopes.PlaybackControl);
        if (missingScopes.Count == 0)
        {
            return;
        }

        SpotifyOperationMessage = $"Spotify playback needs approval for: {string.Join(", ", missingScopes)}.";
        await SaveSpotifySettingsAsync();
        await _spotifyService.LoginAsync(CurrentSpotifyPlaybackSettings);
        var profile = await _spotifyService.GetCurrentUserProfileAsync(CurrentSpotifySettings);
        SpotifyConnectionStatus = $"Connected as {profile.DisplayName}; playback scopes ready";
        SpotifyOperationMessage = "Spotify playback scopes are ready for Spotify Web API / Spotify Connect controls.";
    }

    private async Task<bool> HasPlaybackScopesAsync() =>
        (await _spotifyService.GetMissingScopesAsync(SpotifyScopes.PlaybackControl)).Count == 0;

    private async Task<bool> RunSpotifyOperationAsync(Func<Task> operation)
    {
        if (IsSpotifyBusy)
        {
            return false;
        }

        try
        {
            IsSpotifyBusy = true;
            await operation();
            return true;
        }
        catch (SpotifyApiException ex)
        {
            StartupLog.Write($"Spotify API error kind={ex.Kind} status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
            SpotifyOperationMessage = ToFriendlySpotifyMessage(ex);
            return false;
        }
        catch (Exception ex)
        {
            SpotifyOperationMessage = $"Spotify operation failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsSpotifyBusy = false;
        }
    }

    private static string ToFriendlySpotifyMessage(SpotifyApiException ex)
    {
        return ex.Kind switch
        {
            SpotifyApiErrorKind.RateLimited when ex.RetryAfter is not null =>
                $"Spotify rate limited this request. Try again in {ex.RetryAfter.Value.TotalSeconds:N0} seconds.",
            SpotifyApiErrorKind.NoPremiumAccount =>
                "Spotify Premium is required for Spotify Web API playback controls such as transfer, play, pause, seek, and volume.",
            SpotifyApiErrorKind.NoActiveDevice =>
                "No active Spotify device was found. Open Spotify on a phone, browser, or desktop app, then refresh devices.",
            SpotifyApiErrorKind.DeviceUnavailable =>
                "The selected Spotify device is unavailable. Refresh devices or choose another output.",
            SpotifyApiErrorKind.MissingScopes =>
                $"{ex.Message} Log out and log back in so DancePilot can request the playback scopes.",
            SpotifyApiErrorKind.RefreshTokenFailed =>
                "Spotify session refresh failed. Log out and log back in.",
            SpotifyApiErrorKind.NetworkOffline =>
                "Spotify is unreachable. Check your network connection.",
            SpotifyApiErrorKind.PlaybackForbidden =>
                "Spotify refused that playback command. Use Spotify Web API / Spotify Connect mode, open Spotify on the selected device, start or pause any song once, then Refresh and Transfer again.",
            SpotifyApiErrorKind.PlaylistAccessForbidden =>
                "Spotify refused access to that playlist's items. It may be private, unavailable, or not readable by this app even though it appears in your playlist list.",
            SpotifyApiErrorKind.UnavailableTrack =>
                "This Spotify track is unavailable or does not have a playable Spotify URI.",
            SpotifyApiErrorKind.WebPlaybackSdkUnsupported =>
                ex.Message,
            _ => ex.Message
        };
    }
}
