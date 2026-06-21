using DancePilot.Core.Spotify;

namespace DancePilot.Core.Models;

public sealed record DancePilotSessionState
{
    public int Version { get; init; } = 1;

    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;

    public string ActiveSource { get; init; } = "Spotify";

    public string ActiveDeckName { get; init; } = "Deck A";

    public string SelectedPlaybackMode { get; init; } = SpotifyPlaybackModes.SpotifyConnect;

    public string PlayingDeckName { get; init; } = "Deck A";

    public int? PlayingDeckQueueItemId { get; init; }

    public int? SelectedDeckAQueueItemId { get; init; }

    public int? SelectedDeckBQueueItemId { get; init; }

    public int? LastPlayedDeckAQueueItemId { get; init; }

    public int? LastPlayedDeckBQueueItemId { get; init; }

    public int NextDeckQueueItemId { get; init; } = 1;

    public List<DancePilotQueueItem> DeckAQueue { get; init; } = [];

    public List<DancePilotQueueItem> DeckBQueue { get; init; } = [];

    public SpotifyPlaylistSummary? SelectedSpotifyPlaylist { get; init; }

    public string? SelectedSpotifyTrackKey { get; init; }

    public string SpotifySearchQuery { get; init; } = string.Empty;

    public string? SelectedSpotifySearchTrackKey { get; init; }

    public List<SpotifyTrackMetadata> SpotifyPreviewTracks { get; init; } = [];

    public List<SpotifyTrackMetadata> SpotifySearchResults { get; init; } = [];

    public string? SelectedImportedSpotifyPlaylistId { get; init; }

    public string? SelectedImportedSpotifyTrackKey { get; init; }

    public string LocalMusicSearchQuery { get; init; } = string.Empty;

    public string LocalMusicFolderPath { get; init; } = string.Empty;

    public string LocalMusicSortOption { get; init; } = "Folder";

    public int? SelectedLocalPlaylistId { get; init; }

    public string? SelectedLocalFilePath { get; init; }
}
