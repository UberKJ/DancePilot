namespace DancePilot.Core.Models;

public sealed record PlaylistSummary
{
    public required string Name { get; init; }

    public required int SongCount { get; init; }

    public bool IsSelected { get; init; }
}
