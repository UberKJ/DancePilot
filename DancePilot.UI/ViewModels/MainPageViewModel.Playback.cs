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
    private async Task TogglePlaybackAsync()
    {
        if (IsPlaybackPlaying)
        {
            if (ResolvePlayingDeckItem(_playingDeckName)?.Source == SongSources.Local
                || SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                PauseLocalMusic();
            }
            else
            {
                await PauseSpotifyAsync();
            }

            return;
        }

        var deckItem = FindDeckItemForUserPlay(ActiveDeckName);
        if (deckItem is not null)
        {
            await PlayOrResumeDeckItemAsync(deckItem, ActiveDeckName);
            return;
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture || ActiveSource == SourceLocal)
        {
            SpotifyOperationMessage = $"No local song is loaded on {ActiveDeckName}. Select or queue a local file first.";
            return;
        }

        await ResumeSpotifyAsync();
    }

    private async Task ToggleDeckPlaybackAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (IsDeckPlaying(normalizedDeckName))
        {
            if (ResolvePlayingDeckItem(normalizedDeckName)?.Source == SongSources.Local
                || SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                PauseLocalMusic();
            }
            else
            {
                await PauseSpotifyAsync();
            }

            return;
        }

        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        var item = FindDeckItemForUserPlay(normalizedDeckName);
        if (item is null)
        {
            SpotifyOperationMessage = $"{normalizedDeckName} has no queued song to play.";
            return;
        }

        await PlayOrResumeDeckItemAsync(item, normalizedDeckName);
    }

    private async Task PlayOrResumeDeckItemAsync(DancePilotQueueItem item, string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        _selectedDeckQueueItemIds[normalizedDeckName] = item.Id;
        if (string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            SelectedActiveDeckQueueItem = ActiveDeckQueue.FirstOrDefault(queueItem => queueItem.Id == item.Id)
                ?? item;
        }

        if (_playingDeckQueueItemId == item.Id
            && string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            if (item.Source == SongSources.Local)
            {
                if (IsLocalPlaybackLoadedForQueueItem(normalizedDeckName, item))
                {
                    SetPlaybackModeForDeckPlayback(SpotifyPlaybackModes.LocalFilesFuture);
                    ResumeLocalMusic();
                }
                else
                {
                    await PlayDeckQueueItemAsync(item, normalizedDeckName);
                }
            }
            else if (item.Source == SongSources.Spotify)
            {
                SetPlaybackModeForDeckPlayback(SpotifyPlaybackModes.SpotifyConnect);
                await ResumeSpotifyAsync();
            }
            else if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                ResumeLocalMusic();
            }
            else
            {
                await ResumeSpotifyAsync();
            }

            return;
        }

        await PlayDeckQueueItemAsync(item, normalizedDeckName);
    }

    private async Task<bool> PlayDeckQueueItemAsync(DancePilotQueueItem queueItem, string? deckName = null, bool isTransition = false)
    {
        var targetDeckName = NormalizeDeckName(deckName ?? ActiveDeckName);
        if (!string.Equals(ActiveDeckName, targetDeckName, StringComparison.Ordinal))
        {
            ActiveDeckName = targetDeckName;
            RefreshActiveDeckQueue();
        }

        queueItem = await PrepareQueueItemAlbumArtForPlaybackAsync(targetDeckName, queueItem);
        await PrepareOutputForDeckPlaybackAsync(queueItem);
        var route = DancePilotPlaybackRouter.Resolve(queueItem);

        if (route.Kind == DancePilotPlaybackRouteKind.LocalMediaPlayer)
        {
            var localTrack = await ResolveLocalQueueTrackAsync(queueItem);
            if (localTrack is not null)
            {
                SelectedLocalMusicTrack = ApplyQueueAlbumArtToLocalTrack(queueItem, localTrack);
                SetPlaybackModeForDeckPlayback(SpotifyPlaybackModes.LocalFilesFuture);
                var started = await StartSelectedLocalMusicAsync(targetDeckName, queueItem.Id);
                if (!started)
                {
                    return false;
                }

                MarkDeckItemPlaying(targetDeckName, queueItem);
                if (!isTransition)
                {
                    await FadeIncomingPlaybackInAsync();
                }

                return true;
            }

            SpotifyOperationMessage = $"Local file was not found: {queueItem.Title}.";
            return false;
        }

        if (route.Kind == DancePilotPlaybackRouteKind.SpotifyConnect
            && !string.IsNullOrWhiteSpace(route.SpotifyUri))
        {
            if (SelectedPlaybackMode != SpotifyPlaybackModes.SpotifyConnect)
            {
                SetPlaybackModeForDeckPlayback(SpotifyPlaybackModes.SpotifyConnect);
            }

            var spotifyUri = route.SpotifyUri;
            var track = FindSpotifyTrackByUri(spotifyUri) ?? new SpotifyTrackMetadata
            {
                SpotifyTrackId = ExtractSpotifyTrackId(spotifyUri),
                Title = queueItem.Title,
                Artist = queueItem.Artist,
                Album = queueItem.Album,
                AlbumArtUrl = queueItem.AlbumArtUrl,
                DurationMs = queueItem.Duration is TimeSpan duration ? Convert.ToInt32(duration.TotalMilliseconds) : 0,
                SpotifyUri = spotifyUri
            };

            var started = false;
            var operationSucceeded = await RunSpotifyOperationAsync(async () =>
            {
                started = await PlaySpotifyTrackWithFallbackAsync(track, isTransition
                    ? $"Transitioned to {track.Title} on {targetDeckName}."
                    : $"Playing {track.Title} from {targetDeckName}.",
                    allowExternalFallback: false);
                if (!started)
                {
                    return;
                }

                MarkDeckItemPlaying(targetDeckName, queueItem);
                if (!isTransition)
                {
                    await FadeIncomingPlaybackInAsync();
                }
            });

            if (!operationSucceeded || !started)
            {
                return false;
            }

            return true;
        }

        SpotifyOperationMessage = route.ValidationMessage ?? $"Queued item cannot be played yet: {queueItem.Title}.";
        return false;
    }

    public async Task PlaySelectedActiveDeckQueueItemAsync()
    {
        if (SelectedActiveDeckQueueItem is not null)
        {
            await PlayDeckQueueItemAsync(SelectedActiveDeckQueueItem);
        }
    }

    public Task PlayQueueItemAsync(DancePilotQueueItem queueItem) =>
        PlayDeckQueueItemAsync(queueItem, queueItem.DeckName);

    private bool TryValidateDeckQueueItemForPlayback(DancePilotQueueItem queueItem, out string? validationMessage)
    {
        validationMessage = null;
        var route = DancePilotPlaybackRouter.Resolve(queueItem);
        if (route.Kind == DancePilotPlaybackRouteKind.LocalMediaPlayer)
        {
            var localPath = route.LocalPath ?? string.Empty;
            var localTrack = _allLocalMusicTracks.FirstOrDefault(track =>
                string.Equals(track.FilePath, localPath, StringComparison.OrdinalIgnoreCase));
            if (localTrack is null && !File.Exists(localPath))
            {
                validationMessage = $"Local file was not found: {queueItem.Title}.";
                return false;
            }

            if (localTrack is not null && !File.Exists(localTrack.FilePath))
            {
                validationMessage = $"Local file was not found: {localTrack.FileName}.";
                return false;
            }

            return true;
        }

        if (route.Kind == DancePilotPlaybackRouteKind.SpotifyConnect)
        {
            return true;
        }

        validationMessage = route.ValidationMessage ?? $"Queued item cannot be played yet: {queueItem.Title}.";
        return false;
    }

    private void MarkDeckItemPlaying(string deckName, DancePilotQueueItem queueItem)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (_playingDeckQueueItemId != queueItem.Id || !string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            WaveformHistoryForDeck(normalizedDeckName).Clear();
        }

        _playingDeckName = normalizedDeckName;
        _playingDeckQueueItemId = queueItem.Id;
        _lastPlayedDeckQueueItemIds[normalizedDeckName] = queueItem.Id;
        _selectedDeckQueueItemIds[normalizedDeckName] = queueItem.Id;
        if (!string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            ActiveDeckName = normalizedDeckName;
        }

        RefreshActiveDeckQueue();
        OnPropertyChanged(nameof(CurrentDeckHeader));
        OnPropertyChanged(nameof(NextDeckHeader));
        OnPropertyChanged(nameof(DeckAPlayPauseLabel));
        OnPropertyChanged(nameof(DeckBPlayPauseLabel));
        OnPropertyChanged(nameof(DefaultSpotifyVolume));
        OnPropertyChanged(nameof(DefaultSpotifyVolumeDisplay));
        UpdateNextUpFromDecks();
        QueueSessionStateSave();
    }

    private void MarkDeckItemCompleted(string deckName, int itemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        _lastPlayedDeckQueueItemIds[normalizedDeckName] = itemId;
        _selectedDeckQueueItemIds[normalizedDeckName] = GetQueueItemAfter(
            normalizedDeckName,
            itemId,
            skipSamePlaybackSource: true)?.Id;

        if (string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            RefreshActiveDeckQueue();
        }
        else
        {
            UpdateNextUpFromDecks();
        }

        QueueSessionStateSave();
    }

    private void ClearEndedPlayingDeckItemIfCurrent(string deckName, int itemId)
    {
        if (!string.Equals(_playingDeckName, NormalizeDeckName(deckName), StringComparison.Ordinal)
            || _playingDeckQueueItemId != itemId)
        {
            return;
        }

        _playingDeckQueueItemId = null;
        if (FindLocalPlaybackState(deckName, itemId) is LocalDeckPlaybackState state)
        {
            ClearLocalPlaybackMarker(state.PlayerDeckName);
        }

        _currentPlaybackAlbumArtUrl = null;
        OnPropertyChanged(nameof(DeckAPlayPauseLabel));
        OnPropertyChanged(nameof(DeckBPlayPauseLabel));
        RefreshActiveDeckQueue();
        RefreshDeckDisplayProperties();
        QueueSessionStateSave();
    }

    private async Task<bool> TryPlayNextDeckQueueItemAsync()
    {
        if (_playingDeckQueueItemId is not null)
        {
            return await TryStartTransitionTargetAsync(skipFadeOut: !IsPlaybackPlaying, isAutomatic: false);
        }

        var deckName = NormalizeDeckName(ActiveDeckName);
        var sourceDeckName = _playingDeckName;
        var sourceItemId = _playingDeckQueueItemId;
        var sourcePlaybackMode = SelectedPlaybackMode;
        var nextItem = FindNextDeckQueueItemForManualAdvance(deckName);
        if (nextItem is null)
        {
            return false;
        }

        if (IsPlaybackPlaying)
        {
            await FadeCurrentPlaybackOutAsync();
        }

        var started = await PlayDeckQueueItemAsync(nextItem, deckName);
        if (!started)
        {
            await RestoreDeckOutputVolumeAsync(sourceDeckName, sourcePlaybackMode);
            return false;
        }

        if (RemovePlayedQueueItems && sourceItemId is not null && nextItem.Id != sourceItemId.Value)
        {
            RemovePlayedQueueItem(sourceDeckName, sourceItemId.Value);
        }

        return true;
    }

    private DancePilotQueueItem? FindNextDeckQueueItemForManualAdvance(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count == 0)
        {
            return null;
        }

        if (string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal)
            && _playingDeckQueueItemId is not null)
        {
            var currentIndex = queue.FindIndex(item => item.Id == _playingDeckQueueItemId);
            if (currentIndex >= 0)
            {
                if (currentIndex + 1 < queue.Count)
                {
                    return queue[currentIndex + 1];
                }

                return RemovePlayedQueueItems ? null : queue.FirstOrDefault();
            }
        }

        return FindSelectedDeckQueueItem(normalizedDeckName) ?? queue.FirstOrDefault();
    }

    private async Task<bool> MaybeTransitionDeckAsync(SpotifyPlaybackState? currentState)
    {
        if (!DeckTransitionEnabled
            || _isTransitionAdvanceRunning
            || _playingDeckQueueItemId is null
            || currentState is null
            || currentState.DurationMs is null
            || currentState.DurationMs <= 0
            || currentState.ProgressMs is null
            || currentState.ProgressMs < 0)
        {
            return false;
        }

        var remainingMs = currentState.DurationMs.Value - currentState.ProgressMs.Value;
        var remainingSeconds = Math.Max(0, remainingMs / 1000d);
        var transitionTriggerSeconds = AlwaysFadeSongs && StartTransitionOnFade
            ? FadeOutSeconds + TransitionOverlapSeconds
            : TransitionOverlapSeconds;
        if (currentState.IsPlaying && remainingSeconds > transitionTriggerSeconds)
        {
            return false;
        }

        if (!currentState.IsPlaying && remainingSeconds > LateTransitionSkipFadeSeconds)
        {
            return false;
        }

        return await TryStartTransitionTargetAsync(
            skipFadeOut: !currentState.IsPlaying || remainingSeconds <= LateTransitionSkipFadeSeconds,
            fadeOutSeconds: CreateTransitionFadeOutDuration(remainingSeconds));
    }

    private async Task<bool> TryStartTransitionTargetAsync(
        bool skipFadeOut = false,
        double? fadeOutSeconds = null,
        bool isAutomatic = true)
    {
        if (_isTransitionAdvanceRunning)
        {
            return false;
        }

        var sourceDeckName = NormalizeDeckName(_playingDeckName);
        var sourcePlaybackMode = SelectedPlaybackMode;
        _isTransitionAdvanceRunning = true;
        try
        {
            var sourceItemId = _playingDeckQueueItemId;
            if (sourceItemId is null)
            {
                return false;
            }

            var requestedMode = isAutomatic
                ? ResolveRequestedTransitionMode()
                : ResolveManualTransitionMode();
            if (isAutomatic && requestedMode == DancePilotTransitionModes.Off)
            {
                return false;
            }

            var decision = ResolveTransitionDecision(
                item => TryValidateDeckQueueItemForPlayback(item, out _),
                requestedModeOverride: requestedMode,
                currentDeckNameOverride: sourceDeckName,
                currentItemIdOverride: sourceItemId);
            LogTransitionDecision(decision);

            var targetDeckName = decision.ChosenDeckName;
            var next = decision.ChosenItem;
            if (next is null)
            {
                SpotifyOperationMessage = decision.StatusMessage;
                return false;
            }

            if (string.Equals(targetDeckName, sourceDeckName, StringComparison.Ordinal)
                && next.Id == sourceItemId.Value)
            {
                SpotifyOperationMessage = $"Transition target is already playing on {sourceDeckName}; continuing current playback.";
                return false;
            }

            if (_lastTransitionSourceItemId == sourceItemId && _lastTransitionTargetItemId == next.Id)
            {
                SpotifyOperationMessage = $"Transition to {next.Title} on {targetDeckName} is already in progress.";
                return false;
            }

            if (!TryValidateDeckQueueItemForPlayback(next, out var validationMessage))
            {
                SpotifyOperationMessage = validationMessage ?? $"The next song on {targetDeckName} is not playable.";
                return false;
            }

            var currentItem = ResolveLoadedDeckItem(sourceDeckName);
            var playbackPath = DancePilotTransitionPlaybackPath.Resolve(currentItem, next);
            var usedLocalOverlap = false;
            var started = false;

            if (playbackPath == DancePilotTransitionPlaybackPathKind.LocalOverlap
                && currentItem is not null)
            {
                usedLocalOverlap = await TryStartLocalOverlapTransitionAsync(
                    sourceDeckName,
                    sourceItemId.Value,
                    currentItem,
                    targetDeckName,
                    next,
                    skipFadeOut,
                    fadeOutSeconds);
                started = usedLocalOverlap;
                if (!usedLocalOverlap)
                {
                    StartupLog.Write("Local overlap unavailable; falling back to handoff start.");
                }
            }

            if (!started)
            {
                if (playbackPath == DancePilotTransitionPlaybackPathKind.SpotifyConnectHandoff)
                {
                    SpotifyOperationMessage = "Spotify Connect transition: handoff only.";
                }

                if (skipFadeOut)
                {
                    await SilenceCurrentPlaybackForImmediateTransitionAsync();
                }
                else
                {
                    await FadeCurrentPlaybackOutAsync(fadeOutSeconds);
                }

                started = await PlayDeckQueueItemAsync(next, targetDeckName, isTransition: true);
            }

            if (!started || _playingDeckQueueItemId != next.Id)
            {
                await RestoreDeckOutputVolumeAsync(sourceDeckName, sourcePlaybackMode);
                SpotifyOperationMessage = $"Deck transition could not start {next.Title} on {targetDeckName}. Output volume was restored.";
                return false;
            }

            _lastTransitionSourceItemId = sourceItemId;
            _lastTransitionTargetItemId = next.Id;
            MarkDeckItemCompleted(sourceDeckName, sourceItemId.Value);
            if (RemovePlayedQueueItems && next.Id != sourceItemId.Value)
            {
                RemovePlayedQueueItem(sourceDeckName, sourceItemId.Value);
            }

            if (!usedLocalOverlap)
            {
                await FadeIncomingPlaybackInAsync();
            }

            SpotifyOperationMessage = CreateTransitionStartedMessage(
                decision,
                playbackPath,
                usedLocalOverlap,
                next,
                targetDeckName);
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Deck transition failed: {ex.Message}");
            await RestoreDeckOutputVolumeAsync(sourceDeckName, sourcePlaybackMode);
            SpotifyOperationMessage = $"Deck transition failed: {ex.Message}. Output volume was restored.";
            return false;
        }
        finally
        {
            _isTransitionAdvanceRunning = false;
        }
    }

    private void LogTransitionDecision(DancePilotTransitionDecision decision)
    {
        var item = decision.ChosenItem;
        StartupLog.Write(
            "Transition decision: "
            + $"currentDeck={decision.CurrentDeckName}; "
            + $"requestedMode={decision.RequestedMode}; "
            + $"chosenDeck={decision.ChosenDeckName}; "
            + $"chosenItemId={item?.Id.ToString() ?? "<none>"}; "
            + $"title={item?.Title ?? "<none>"}; "
            + $"source={item?.Source ?? "<none>"}; "
            + $"fallbackReason={decision.FallbackReason ?? "<none>"}");
    }

    private async Task<bool> TryStartLocalOverlapTransitionAsync(
        string sourceDeckName,
        int sourceItemId,
        DancePilotQueueItem sourceItem,
        string targetDeckName,
        DancePilotQueueItem next,
        bool skipFadeOut,
        double? fadeOutSeconds)
    {
        var sourceState = FindLocalPlaybackState(sourceDeckName, sourceItemId);
        if (sourceState is null || sourceItem.Source != SongSources.Local || next.Source != SongSources.Local)
        {
            return false;
        }

        var targetPlayerDeckName = ResolveLocalOverlapPlayerDeckName(sourceState.PlayerDeckName, targetDeckName);
        if (targetPlayerDeckName is null)
        {
            return false;
        }

        var localTrack = await ResolveLocalQueueTrackAsync(next);
        if (localTrack is null)
        {
            SpotifyOperationMessage = $"Local file was not found: {next.Title}.";
            return false;
        }

        SelectedLocalMusicTrack = ApplyQueueAlbumArtToLocalTrack(next, localTrack);
        await PrepareOutputForDeckPlaybackAsync(next);
        SetPlaybackModeForDeckPlayback(SpotifyPlaybackModes.LocalFilesFuture);

        var started = await StartSelectedLocalMusicAsync(
            targetDeckName,
            next.Id,
            targetPlayerDeckName,
            startMuted: true);
        if (!started)
        {
            return false;
        }

        MarkDeckItemPlaying(targetDeckName, next);

        var sourcePlayer = LocalPlayerForPlayerDeck(sourceState.PlayerDeckName);
        var targetPlayer = LocalPlayerForPlayerDeck(targetPlayerDeckName);
        var targetVolume = ResolveLocalOutputLevel(targetDeckName);
        if (AlwaysFadeSongs)
        {
            var fadeOutDuration = skipFadeOut
                ? 0
                : Math.Max(0, fadeOutSeconds ?? FadeOutSeconds);
            await Task.WhenAll(
                FadeLocalVolumeAsync(sourcePlayer, sourcePlayer.Volume, 0, fadeOutDuration),
                FadeLocalVolumeAsync(targetPlayer, targetPlayer.Volume, targetVolume, FadeInSeconds));
        }
        else
        {
            targetPlayer.Volume = targetVolume;
        }

        sourcePlayer.Pause();
        ClearLocalPlaybackMarker(sourceState.PlayerDeckName);
        return true;
    }

    private string? ResolveLocalOverlapPlayerDeckName(string sourcePlayerDeckName, string targetDeckName)
    {
        var normalizedSourcePlayerDeckName = NormalizeDeckName(sourcePlayerDeckName);
        var normalizedTargetDeckName = NormalizeDeckName(targetDeckName);
        var preferredState = LocalPlaybackStateForPlayerDeck(normalizedTargetDeckName);
        if (!string.Equals(normalizedSourcePlayerDeckName, normalizedTargetDeckName, StringComparison.Ordinal)
            && !preferredState.IsLoaded)
        {
            return normalizedTargetDeckName;
        }

        var temporaryDeckName = OppositeDeckName(normalizedSourcePlayerDeckName);
        var temporaryState = LocalPlaybackStateForPlayerDeck(temporaryDeckName);
        return temporaryState.IsLoaded ? null : temporaryDeckName;
    }

    private string CreateTransitionStartedMessage(
        DancePilotTransitionDecision decision,
        DancePilotTransitionPlaybackPathKind playbackPath,
        bool usedLocalOverlap,
        DancePilotQueueItem next,
        string targetDeckName)
    {
        if (usedLocalOverlap)
        {
            var prefix = decision.RequestedMode == DancePilotTransitionModes.Auto
                ? decision.FallbackReason is null
                    ? "Auto: using local crossfade."
                    : "Auto: falling back to same deck advance. Local crossfade active."
                : "Local crossfade active.";
            return $"{prefix} Started {next.Title} on {targetDeckName}.";
        }

        if (playbackPath == DancePilotTransitionPlaybackPathKind.SpotifyConnectHandoff)
        {
            return $"Spotify Connect transition: handoff only. Started {next.Title} on {targetDeckName}.";
        }

        return decision.FallbackReason is null
            ? $"Deck transition started {next.Title} on {targetDeckName}."
            : $"{decision.StatusMessage} Started {next.Title} on {targetDeckName}.";
    }

    private double CreateTransitionFadeOutDuration(double remainingSeconds)
    {
        if (remainingSeconds <= LateTransitionSkipFadeSeconds)
        {
            return 0;
        }

        return Math.Clamp(
            remainingSeconds - TransitionOverlapSeconds,
            0,
            FadeOutSeconds);
    }

    private async Task FadeCurrentPlaybackOutAsync(double? fadeOutSeconds = null)
    {
        if (!AlwaysFadeSongs)
        {
            return;
        }

        var seconds = Math.Max(0, fadeOutSeconds ?? FadeOutSeconds);
        if (seconds <= 0.05)
        {
            await SilenceCurrentPlaybackForImmediateTransitionAsync();
            return;
        }

        try
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                if (TryGetActiveLocalPlayback(out _, out var player))
                {
                    await FadeLocalVolumeAsync(player, player.Volume, 0, seconds);
                }

                return;
            }

            await FadeSpotifyVolumeAsync(ResolveMainOutputVolumePercent(), 0, seconds);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Fade-out skipped: {ex.Message}");
        }
    }

    private async Task SilenceCurrentPlaybackForImmediateTransitionAsync()
    {
        if (!AlwaysFadeSongs)
        {
            return;
        }

        try
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                if (TryGetActiveLocalPlayback(out _, out var player))
                {
                    player.Volume = 0;
                }

                return;
            }

            await SetSpotifyVolumeImmediateAsync(0);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Immediate transition volume prep skipped: {ex.Message}");
        }
    }

    private async Task FadeIncomingPlaybackInAsync()
    {
        var targetDeckName = _playingDeckName;
        var targetPlaybackMode = SelectedPlaybackMode;
        if (!AlwaysFadeSongs)
        {
            await RestoreDeckOutputVolumeAsync(targetDeckName, targetPlaybackMode);
            return;
        }

        if (targetPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            var targetVolume = ResolveLocalOutputLevel(targetDeckName);
            try
            {
                if (TryGetActiveLocalPlayback(out _, out var player))
                {
                    await FadeLocalVolumeAsync(player, player.Volume, targetVolume, FadeInSeconds);
                }
            }
            catch (Exception ex)
            {
                StartupLog.Write($"Local fade-in skipped: {ex.Message}");
            }
            finally
            {
                if (TryGetActiveLocalPlayback(out _, out var player))
                {
                    player.Volume = targetVolume;
                }
            }

            return;
        }

        await FadeSpotifyVolumeAsync(0, ResolveMainOutputVolumePercent(), FadeInSeconds);
        await RestoreDeckOutputVolumeAsync(targetDeckName, targetPlaybackMode);
    }

    private async Task RestoreDeckOutputVolumeAsync(string deckName, string playbackMode)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (playbackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            if (FindLocalPlaybackState(normalizedDeckName) is LocalDeckPlaybackState state)
            {
                LocalPlayerForPlayerDeck(state.PlayerDeckName).Volume = ResolveLocalOutputLevel(normalizedDeckName);
            }

            return;
        }

        if (playbackMode == SpotifyPlaybackModes.SpotifyConnect)
        {
            await TrySetSpotifyVolumeImmediateAsync(ResolveMainOutputVolumePercent());
        }
    }

    private async Task TrySetSpotifyVolumeImmediateAsync(int volume)
    {
        try
        {
            await SetSpotifyVolumeImmediateAsync(volume);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Spotify volume restore skipped: {ex.Message}");
        }
    }

    private async Task FadeSpotifyVolumeAsync(int fromVolume, int toVolume, double seconds)
    {
        if (SelectedPlaybackMode != SpotifyPlaybackModes.SpotifyConnect)
        {
            return;
        }

        string deviceId;
        try
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            deviceId = await ResolveSelectedDeviceIdAsync();
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Spotify fade skipped: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            StartupLog.Write("Spotify fade skipped: no Spotify Connect device was resolved.");
            return;
        }

        var steps = Math.Clamp(Convert.ToInt32(Math.Ceiling(seconds * 3)), 6, 30);
        var delay = TimeSpan.FromMilliseconds(Math.Max(120, seconds * 1000 / steps));

        for (var step = 0; step <= steps; step++)
        {
            var percent = step / (double)steps;
            var easedPercent = SmoothFadeEase(percent);
            var volume = Convert.ToInt32(fromVolume + (toVolume - fromVolume) * easedPercent);
            try
            {
                await _spotifyPlayerService.SetVolumeAsync(CurrentSpotifySettings, deviceId, Math.Clamp(volume, 0, 100));
            }
            catch (Exception ex)
            {
                StartupLog.Write($"Spotify fade volume command skipped: {ex.Message}");
                return;
            }

            if (step < steps)
            {
                await Task.Delay(delay);
            }
        }
    }

    private async Task SetSpotifyVolumeImmediateAsync(int volume)
    {
        if (SelectedPlaybackMode != SpotifyPlaybackModes.SpotifyConnect)
        {
            return;
        }

        await EnsurePlaybackScopesAsync();
        EnsureSpotifyConnectPlaybackMode();
        await _spotifyPlayerService.SetVolumeAsync(
            CurrentSpotifySettings,
            await ResolveSelectedDeviceIdAsync(),
            Math.Clamp(volume, 0, 100));
    }

    private async Task FadeLocalVolumeAsync(MediaPlayer player, double fromVolume, double toVolume, double seconds)
    {
        if (seconds <= 0.05)
        {
            player.Volume = Math.Clamp(toVolume, 0, 1);
            return;
        }

        var steps = Math.Clamp(Convert.ToInt32(Math.Ceiling(seconds * 12)), 12, 160);
        var delay = TimeSpan.FromMilliseconds(Math.Max(25, seconds * 1000 / steps));

        for (var step = 0; step <= steps; step++)
        {
            var percent = step / (double)steps;
            var easedPercent = SmoothFadeEase(percent);
            player.Volume = Math.Clamp(fromVolume + (toVolume - fromVolume) * easedPercent, 0, 1);
            if (step < steps)
            {
                await Task.Delay(delay);
            }
        }
    }

    private static double SmoothFadeEase(double percent)
    {
        var clamped = Math.Clamp(percent, 0, 1);
        return clamped * clamped * (3 - (2 * clamped));
    }


    private async Task RefreshSpotifyDevicesCoreAsync()
    {
        var devices = await _spotifyDeviceManager.GetAvailableDevicesAsync(CurrentSpotifySettings);
        StartupLog.Write("Spotify devices: " + string.Join("; ", devices.Select(device =>
            $"{device.Name}/{device.Type}/active={device.IsActive}/restricted={device.IsRestricted}/volume={device.SupportsVolume}")));
        SpotifyDevices.Clear();
        foreach (var device in devices.OrderByDescending(device => device.IsActive).ThenBy(device => device.Name))
        {
            SpotifyDevices.Add(device);
        }

        var saved = await _playbackSettingsRepository.LoadAsync();
        SelectedSpotifyDevice = SpotifyDevices.FirstOrDefault(device => device.Id == saved.SelectedDeviceId && !device.IsRestricted)
            ?? SpotifyDevices.FirstOrDefault(device => device.IsActive && !device.IsRestricted)
            ?? SpotifyDevices.FirstOrDefault(device => !device.IsRestricted)
            ?? SpotifyDevices.FirstOrDefault();

        CurrentOutputStatus = SpotifyDevices.Count == 0
            ? "No Spotify devices found. Open Spotify on a phone, browser, or desktop app, then refresh."
            : $"Found {SpotifyDevices.Count} Spotify device(s).";
        SpotifyOperationMessage = CurrentOutputStatus;
    }

    private async Task RefreshPlaybackCoreAsync(bool runAutopilot)
    {
        var state = await _spotifyPlayerService.GetPlaybackStateAsync(CurrentSpotifySettings);
        ApplyPlaybackState(state);
        await RefreshQueueCoreAsync();

        if (runAutopilot)
        {
            await MaybeTransitionDeckAsync(state);
        }
    }

    private async Task RefreshPlaybackAndAutopilotAsync()
    {
        if (_isPlaybackRefreshRunning || IsSpotifyBusy)
        {
            return;
        }

        try
        {
            _isPlaybackRefreshRunning = true;
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                await RefreshLocalPlaybackCoreAsync(runAutopilot: true);
                return;
            }

            if (!await _spotifyService.IsConnectedAsync())
            {
                return;
            }

            if (!await HasPlaybackScopesAsync())
            {
                return;
            }

            await RefreshPlaybackCoreAsync(runAutopilot: true);
        }
        catch (SpotifyApiException ex)
        {
            CurrentOutputStatus = ToFriendlySpotifyMessage(ex);
        }
        catch
        {
            CurrentOutputStatus = "Spotify playback state is temporarily unavailable.";
        }
        finally
        {
            _isPlaybackRefreshRunning = false;
        }
    }

    private async Task HandleLocalMediaEndedAsync(string playerDeckName)
    {
        var playerDeck = NormalizeDeckName(playerDeckName);
        var localState = LocalPlaybackStateForPlayerDeck(playerDeck);
        var completedDeckName = NormalizeDeckName(localState.LogicalDeckName);
        var completedItemId = localState.QueueItemId;
        var completedTrack = localState.Track;
        if (completedItemId is null
            || !string.Equals(_playingDeckName, completedDeckName, StringComparison.Ordinal)
            || _playingDeckQueueItemId != completedItemId
            || ResolvePlayingDeckItem(completedDeckName)?.Source != SongSources.Local)
        {
            StartupLog.Write("Ignored stale local media ended event.");
            ClearLocalPlaybackMarker(playerDeck);
            return;
        }

        if (!HasLocalPlaybackActuallyStarted(completedDeckName, completedItemId))
        {
            var elapsed = localState.RequestedAt is DateTimeOffset requestedAt
                ? DateTimeOffset.UtcNow - requestedAt
                : TimeSpan.Zero;
            StartupLog.Write($"Ignored premature local media ended event for {completedDeckName} item {completedItemId}; elapsed={elapsed.TotalMilliseconds:0}ms.");
            RestoreUnstartedLocalPlaybackCursor(completedDeckName, completedItemId.Value);
            ClearLocalPlaybackMarker(playerDeck);
            IsPlaybackPlaying = false;
            SpotifyPlaybackStatus = "Local not started";
            CurrentOutputStatus = "Local file did not start. The deck position was kept.";
            SpotifyOperationMessage = $"Local file did not start on {completedDeckName}. Press play again to retry; the queued song was not consumed.";
            OnPropertyChanged(nameof(DeckAPlayPauseLabel));
            OnPropertyChanged(nameof(DeckBPlayPauseLabel));
            RefreshDeckDisplayProperties();
            QueueSessionStateSave();
            return;
        }

        ClearLocalPlaybackMarker(playerDeck);
        IsPlaybackPlaying = false;
        SpotifyPlaybackStatus = "Local ended";
        SpotifyProgressDisplay = completedTrack?.Duration is TimeSpan duration
            ? $"{duration:m\\:ss} / {duration:m\\:ss}"
            : SpotifyProgressDisplay;
        SpotifyTimeRemaining = "0:00";
        if (completedItemId is not null)
        {
            MarkDeckItemCompleted(completedDeckName, completedItemId.Value);
        }
        else
        {
            RefreshDeckDisplayProperties();
        }

        if (!DeckTransitionEnabled || _isTransitionAdvanceRunning || completedItemId is null)
        {
            if (completedItemId is not null)
            {
                ClearEndedPlayingDeckItemIfCurrent(completedDeckName, completedItemId.Value);
            }

            return;
        }

        var transitioned = await TryStartTransitionTargetAsync(skipFadeOut: true);
        if (!transitioned)
        {
            ClearEndedPlayingDeckItemIfCurrent(completedDeckName, completedItemId.Value);
            if (string.IsNullOrWhiteSpace(SpotifyOperationMessage))
            {
                SpotifyOperationMessage = RemovePlayedQueueItems
                    ? "Local song ended. No next queued song is available on this deck."
                    : "Local song ended. Queue loop is ready, but no playable track was found.";
            }
        }
    }

    private async Task RefreshLocalPlaybackCoreAsync(bool runAutopilot)
    {
        if (!TryGetActiveLocalPlayback(out var localState, out var player))
        {
            IsPlaybackPlaying = false;
            RefreshDeckDisplayProperties();
            return;
        }

        var session = player.PlaybackSession;
        var duration = session.NaturalDuration > TimeSpan.Zero
            ? session.NaturalDuration
            : localState.Track?.Duration;
        var position = session.Position < TimeSpan.Zero ? TimeSpan.Zero : session.Position;

        IsPlaybackPlaying = session.PlaybackState is MediaPlaybackState.Playing
            or MediaPlaybackState.Buffering
            or MediaPlaybackState.Opening;
        ObserveLocalPlaybackProgress(localState, position);

        if (duration is TimeSpan durationValue && durationValue > TimeSpan.Zero)
        {
            SeekPositionMaximumSeconds = durationValue.TotalSeconds;
            if (!_isSeekPositionChanging)
            {
                SeekPositionSeconds = Math.Clamp(position.TotalSeconds, 0, SeekPositionMaximumSeconds);
            }

            SpotifyProgressDisplay = $"{position:m\\:ss} / {durationValue:m\\:ss}";
            SpotifyTimeRemaining = $"{(durationValue - position > TimeSpan.Zero ? durationValue - position : TimeSpan.Zero):m\\:ss}";
        }
        else
        {
            if (!_isSeekPositionChanging)
            {
                SeekPositionSeconds = Math.Max(0, position.TotalSeconds);
            }

            SpotifyProgressDisplay = $"{position:m\\:ss} / --:--";
            SpotifyTimeRemaining = "--:--";
        }

        RefreshDeckDisplayProperties();

        if (!runAutopilot
            || !DeckTransitionEnabled
            || _isTransitionAdvanceRunning
            || _playingDeckQueueItemId is null
            || duration is not TimeSpan knownDuration
            || knownDuration <= TimeSpan.Zero)
        {
            return;
        }

        var remainingSeconds = Math.Max(0, (knownDuration - position).TotalSeconds);
        var transitionTriggerSeconds = AlwaysFadeSongs && StartTransitionOnFade
            ? FadeOutSeconds + TransitionOverlapSeconds
            : TransitionOverlapSeconds;
        var isNearEnd = remainingSeconds <= transitionTriggerSeconds;
        var hasEnded = session.PlaybackState is MediaPlaybackState.None or MediaPlaybackState.Paused
            && HasLocalPlaybackActuallyStarted(localState.LogicalDeckName, localState.QueueItemId)
            && remainingSeconds <= LateTransitionSkipFadeSeconds;

        if (isNearEnd || hasEnded)
        {
            await TryStartTransitionTargetAsync(
                skipFadeOut: hasEnded || remainingSeconds <= LateTransitionSkipFadeSeconds,
                fadeOutSeconds: CreateTransitionFadeOutDuration(remainingSeconds));
        }
    }

    private void ApplyPlaybackState(SpotifyPlaybackState? state)
    {
        if (state is null)
        {
            SpotifyPlaybackStatus = "No active playback";
            IsPlaybackPlaying = false;
            SpotifyNowPlayingTitle = "Nothing playing";
            SpotifyNowPlayingArtist = "Open Spotify on a device, then refresh devices.";
            SpotifyTimeRemaining = "--:--";
            SpotifyProgressDisplay = "--:-- / --:--";
            if (ResolveLoadedDeckItem(_playingDeckName) is null)
            {
                _currentPlaybackAlbumArtUrl = null;
            }

            RefreshDeckDisplayProperties();
            CurrentOutputStatus = "No active Spotify playback device.";
            ResetSeekPositionRange();
            return;
        }

        SpotifyPlaybackStatus = state.PlaybackStatus;
        IsPlaybackPlaying = state.IsPlaying;
        SpotifyNowPlayingTitle = state.TrackTitle;
        SpotifyNowPlayingArtist = state.TrackArtist;
        if (!string.IsNullOrWhiteSpace(state.Track?.AlbumArtUrl))
        {
            _currentPlaybackAlbumArtUrl = state.Track.AlbumArtUrl;
            ApplyPlaybackAlbumArtToLoadedQueueItem(state.Track);
        }

        RefreshDeckDisplayProperties();
        SpotifyTimeRemaining = state.TimeRemainingDisplay;
        SpotifyProgressDisplay = state.ProgressDisplay;
        CurrentOutputStatus = state.Device is null
            ? "Spotify playback active, but no output device was returned."
            : $"{state.PlaybackStatus} on {state.Device.DisplayName}";

        if (state.IsPlaying
            && SpotifyOperationMessage.StartsWith("Spotify refused that playback command", StringComparison.OrdinalIgnoreCase))
        {
            SpotifyOperationMessage = state.Device is null
                ? "Spotify is playing. DancePilot can see playback state from your account."
                : $"Spotify is playing on {state.Device.DisplayName}.";
        }

        UpdateSeekPositionMaximum(state.DurationMs);

        if (state.ProgressMs is not null && !_isSeekPositionChanging)
        {
            SeekPositionSeconds = Math.Max(0, state.ProgressMs.Value / 1000d);
        }

        if (state.Device is not null)
        {
            SelectedOutputDeviceName = state.Device.DisplayName;
            SelectedSpotifyDevice = SpotifyDevices.FirstOrDefault(device => device.Id == state.Device.Id) ?? SelectedSpotifyDevice;
        }

        if (!string.IsNullOrWhiteSpace(state.ContextUri)
            && state.ContextUri.StartsWith("spotify:playlist:", StringComparison.OrdinalIgnoreCase))
        {
            var playlistId = state.ContextUri.Split(':').Last();
            CurrentSpotifyPlaylistName = ImportedSpotifyPlaylists.FirstOrDefault(playlist => playlist.SpotifyPlaylistId == playlistId)?.Name
                ?? state.ContextUri;
        }
    }

    private void ApplyPlaybackAlbumArtToLoadedQueueItem(SpotifyTrackMetadata track)
    {
        if (!HasUsableAlbumArtSource(track.AlbumArtUrl)
            || _playingDeckQueueItemId is null)
        {
            return;
        }

        var normalizedDeckName = NormalizeDeckName(_playingDeckName);
        var queue = QueueForDeck(normalizedDeckName);
        var queueIndex = queue.FindIndex(item => item.Id == _playingDeckQueueItemId);
        if (queueIndex < 0)
        {
            return;
        }

        var item = queue[queueIndex];
        if (item.Source != SongSources.Spotify
            || !PlaybackTrackMatchesQueueItem(track, item)
            || string.Equals(item.AlbumArtUrl, track.AlbumArtUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        queue[queueIndex] = item with { AlbumArtUrl = track.AlbumArtUrl };
        RefreshActiveDeckQueue();
        QueueSessionStateSave();
    }

    private static bool PlaybackTrackMatchesQueueItem(SpotifyTrackMetadata track, DancePilotQueueItem item)
    {
        if (!string.IsNullOrWhiteSpace(track.SpotifyUri)
            && string.Equals(track.SpotifyUri, item.ExternalUri, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trackId = ExtractSpotifyTrackId(item.ExternalUri);
        if (!string.IsNullOrWhiteSpace(trackId)
            && string.Equals(trackId, track.SpotifyTrackId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(track.Title, item.Title, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(item.Artist)
                || string.Equals(track.Artist, item.Artist, StringComparison.OrdinalIgnoreCase));
    }

    private void ResetSeekPositionRange()
    {
        SeekPositionMaximumSeconds = 1;
        if (!_isSeekPositionChanging)
        {
            SeekPositionSeconds = 0;
        }
    }

    private void UpdateSeekPositionMaximum(int? durationMs)
    {
        if (durationMs is int value && value > 0)
        {
            SeekPositionMaximumSeconds = value / 1000d;
        }
    }

    private async Task RefreshPlaybackCollectionsAsync(bool loadImportedTracks = true)
    {
        await LoadImportedSpotifyPlaylistsCoreAsync(loadTracksForSelection: loadImportedTracks);
        await RefreshQueueCoreAsync();
    }

}
