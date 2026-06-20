namespace DancePilot.Core.Models;

public sealed record SongRequest
{
    public required int Position { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }
}
