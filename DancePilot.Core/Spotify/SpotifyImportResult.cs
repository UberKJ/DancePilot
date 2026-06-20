namespace DancePilot.Core.Spotify;

public sealed record SpotifyImportResult
{
    public int ImportedCount { get; init; }

    public int UpdatedCount { get; init; }

    public int UnavailableCount { get; init; }

    public int LikelyLocalMatchCount { get; init; }

    public int PlaylistTrackCount { get; init; }
}
