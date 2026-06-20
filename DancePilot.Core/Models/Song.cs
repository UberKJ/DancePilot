namespace DancePilot.Core.Models;

public sealed record Song
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public required TimeSpan Duration { get; init; }

    public required int BPM { get; init; }

    public required string Key { get; init; }

    public required int EnergyScore { get; init; }

    public required string Genre { get; init; }

    public required string Tags { get; init; }

    public required double Rating { get; init; }

    public required string AlbumArtPath { get; init; }

    public string Source { get; init; } = SongSources.Local;

    public string? ExternalId { get; init; }

    public string? ExternalUri { get; init; }

    public string? ExternalUrl { get; init; }

    public string? Album { get; init; }

    public int? DurationMs { get; init; }

    public int? Popularity { get; init; }

    public string? ImportedFromPlaylistId { get; init; }

    public DateTimeOffset? LastSyncedAt { get; init; }

    public int? LikelyLocalMatchSongId { get; init; }

    public string DurationDisplay => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:00}";
}
