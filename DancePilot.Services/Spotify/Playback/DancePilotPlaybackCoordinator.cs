using DancePilot.Core.Spotify;
using DancePilot.Data.Repositories;

namespace DancePilot.Services.Spotify.Playback;

public sealed class DancePilotPlaybackCoordinator
{
    private readonly SpotifyPlayerService _playerService;
    private readonly PlaybackQueueRepository _queueRepository;
    private readonly PlaybackHistoryRepository _historyRepository;

    public DancePilotPlaybackCoordinator(
        SpotifyPlayerService playerService,
        PlaybackQueueRepository queueRepository,
        PlaybackHistoryRepository historyRepository)
    {
        _playerService = playerService;
        _queueRepository = queueRepository;
        _historyRepository = historyRepository;
    }

    public async Task<DancePilotQueueItem> AddToQueueAsync(
        SpotifyTrackMetadata track,
        CancellationToken cancellationToken = default) =>
        await _queueRepository.EnqueueAsync(track, cancellationToken: cancellationToken);

    public async Task PlayTrackAsync(
        SpotifySettings settings,
        string deviceId,
        SpotifyTrackMetadata track,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        await _playerService.PlayTrackAsync(settings, deviceId, track.SpotifyUri ?? string.Empty, cancellationToken: cancellationToken);
        await _historyRepository.RecordStartedAsync(track, notes ?? "Started directly from DancePilot.", cancellationToken);
    }

    public Task<IReadOnlyList<DancePilotQueueItem>> GetPendingQueueAsync(CancellationToken cancellationToken = default) =>
        _queueRepository.GetPendingAsync(cancellationToken);

    public async Task<SpotifyTrackMetadata?> GetNextUpTrackAsync(CancellationToken cancellationToken = default)
    {
        var next = await _queueRepository.GetNextPendingAsync(cancellationToken);
        if (next is null)
        {
            return null;
        }

        return new SpotifyTrackMetadata
        {
            SpotifyTrackId = string.Empty,
            Title = next.Title,
            Artist = next.Artist,
            DurationMs = 0,
            SpotifyUri = next.ExternalUri
        };
    }

    public async Task PlayQueuedTrackAsync(
        SpotifySettings settings,
        string deviceId,
        DancePilotQueueItem queueItem,
        CancellationToken cancellationToken = default)
    {
        await _playerService.PlayTrackAsync(settings, deviceId, queueItem.ExternalUri, cancellationToken: cancellationToken);
        await _queueRepository.MarkStatusAsync(queueItem.Id, "playing", cancellationToken);
        await _historyRepository.RecordStartedAsync(new SpotifyTrackMetadata
        {
            SpotifyTrackId = string.Empty,
            Title = queueItem.Title,
            Artist = queueItem.Artist,
            DurationMs = 0,
            SpotifyUri = queueItem.ExternalUri
        }, "Started from DancePilot queue.", cancellationToken);
    }

    public async Task<bool> MaybeAutoStartNextAsync(
        SpotifySettings settings,
        string deviceId,
        SpotifyPlaybackSettings playbackSettings,
        SpotifyPlaybackState? currentState,
        CancellationToken cancellationToken = default)
    {
        if (!playbackSettings.AutopilotEnabled
            || currentState?.IsPlaying != true
            || currentState.DurationMs is null
            || currentState.ProgressMs is null)
        {
            return false;
        }

        var remainingMs = currentState.DurationMs.Value - currentState.ProgressMs.Value;
        if (remainingMs > playbackSettings.AutoplaySecondsBeforeEnd * 1000)
        {
            return false;
        }

        var next = await _queueRepository.GetNextPendingAsync(cancellationToken);
        if (next is null)
        {
            return false;
        }

        await PlayQueuedTrackAsync(settings, deviceId, next, cancellationToken);
        return true;
    }
}
