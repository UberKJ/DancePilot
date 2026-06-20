namespace DancePilot.Core.Spotify;

public sealed record SpotifyPlaybackState
{
    public SpotifyDevice? Device { get; init; }

    public SpotifyTrackMetadata? Track { get; init; }

    public bool IsPlaying { get; init; }

    public int? ProgressMs { get; init; }

    public int? DurationMs { get; init; }

    public string? ContextUri { get; init; }

    public string PlaybackStatus => IsPlaying ? "Playing" : "Paused";

    public string TrackTitle => Track?.Title ?? "Nothing playing";

    public string TrackArtist => Track?.Artist ?? "Spotify";

    public string TimeRemainingDisplay
    {
        get
        {
            if (DurationMs is null || ProgressMs is null)
            {
                return "--:--";
            }

            var remaining = Math.Max(0, DurationMs.Value - ProgressMs.Value);
            return TimeSpan.FromMilliseconds(remaining).ToString(@"m\:ss");
        }
    }

    public string ProgressDisplay
    {
        get
        {
            if (DurationMs is null || ProgressMs is null)
            {
                return "--:-- / --:--";
            }

            var progress = TimeSpan.FromMilliseconds(Math.Max(0, ProgressMs.Value));
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, DurationMs.Value));
            return $"{progress:m\\:ss} / {duration:m\\:ss}";
        }
    }
}
