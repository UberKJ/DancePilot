namespace DancePilot.Core.Models;

public sealed record LocalLibraryStats
{
    public int TrackCount { get; init; }

    public DateTimeOffset? LastUpdatedAt { get; init; }
}
