using DancePilot.Core.Models;

namespace DancePilot.Core.Spotify;

public enum DancePilotPlaybackRouteKind
{
    Unsupported,
    SpotifyConnect,
    LocalMediaPlayer
}

public sealed record DancePilotPlaybackRoute
{
    public required DancePilotPlaybackRouteKind Kind { get; init; }

    public string? SpotifyUri { get; init; }

    public string? LocalPath { get; init; }

    public string? ValidationMessage { get; init; }

    public bool IsPlayable => Kind != DancePilotPlaybackRouteKind.Unsupported;
}

public static class DancePilotPlaybackRouter
{
    public static DancePilotPlaybackRoute Resolve(DancePilotQueueItem item)
    {
        if (item.IsSpotifySource)
        {
            return string.IsNullOrWhiteSpace(item.ExternalUri)
                ? Unsupported($"{item.Title} does not have a playable Spotify URI.")
                : new DancePilotPlaybackRoute
                {
                    Kind = DancePilotPlaybackRouteKind.SpotifyConnect,
                    SpotifyUri = item.ExternalUri.Trim()
                };
        }

        if (item.IsLocalSource)
        {
            var localPath = item.ResolvedLocalPath;
            return string.IsNullOrWhiteSpace(localPath)
                ? Unsupported($"Local file path is missing for {item.Title}.")
                : new DancePilotPlaybackRoute
                {
                    Kind = DancePilotPlaybackRouteKind.LocalMediaPlayer,
                    LocalPath = localPath
                };
        }

        return Unsupported($"Queued item cannot be played yet: {item.Title}.");
    }

    private static DancePilotPlaybackRoute Unsupported(string message) => new()
    {
        Kind = DancePilotPlaybackRouteKind.Unsupported,
        ValidationMessage = message
    };
}
