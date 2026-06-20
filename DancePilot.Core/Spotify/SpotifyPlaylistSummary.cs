namespace DancePilot.Core.Spotify;

public sealed record SpotifyPlaylistSummary
{
    public required string SpotifyPlaylistId { get; init; }

    public required string Name { get; init; }

    public string? OwnerName { get; init; }

    public int TrackCount { get; init; }

    public string? SnapshotId { get; init; }

    public string? ExternalUrl { get; init; }
}
