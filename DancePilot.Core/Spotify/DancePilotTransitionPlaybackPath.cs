using DancePilot.Core.Models;

namespace DancePilot.Core.Spotify;

public enum DancePilotTransitionPlaybackPathKind
{
    None,
    LocalOverlap,
    SpotifyConnectHandoff,
    LocalHandoff
}

public static class DancePilotTransitionPlaybackPath
{
    public static DancePilotTransitionPlaybackPathKind Resolve(
        DancePilotQueueItem? currentItem,
        DancePilotQueueItem? nextItem)
    {
        if (currentItem is null || nextItem is null)
        {
            return DancePilotTransitionPlaybackPathKind.None;
        }

        if (IsLocal(currentItem) && IsLocal(nextItem))
        {
            return DancePilotTransitionPlaybackPathKind.LocalOverlap;
        }

        if (IsSpotify(currentItem) || IsSpotify(nextItem))
        {
            return DancePilotTransitionPlaybackPathKind.SpotifyConnectHandoff;
        }

        return DancePilotTransitionPlaybackPathKind.LocalHandoff;
    }

    private static bool IsLocal(DancePilotQueueItem item) =>
        string.Equals(item.Source, SongSources.Local, StringComparison.OrdinalIgnoreCase);

    private static bool IsSpotify(DancePilotQueueItem item) =>
        string.Equals(item.Source, SongSources.Spotify, StringComparison.OrdinalIgnoreCase);
}
