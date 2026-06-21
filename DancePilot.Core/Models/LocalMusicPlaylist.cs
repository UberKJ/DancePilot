namespace DancePilot.Core.Models;

public sealed record LocalMusicPlaylist
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public int TrackCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayName => TrackCount == 1
        ? $"{Name} (1 song)"
        : $"{Name} ({TrackCount} songs)";
}
