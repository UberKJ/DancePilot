namespace DancePilot.Core.Spotify;

public sealed record DancePilotQueueItem
{
    public int Id { get; init; }

    public string DeckName { get; init; } = "Deck A";

    public string Source { get; init; } = "spotify";

    public required string ExternalUri { get; init; }

    public int? SongId { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public string? AlbumArtUrl { get; init; }

    public int? BPM { get; init; }

    public string? MusicalKey { get; init; }

    public int QueuePosition { get; init; }

    public string Status { get; init; } = "pending";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayName => $"{QueuePosition}. {Title} - {Artist}";

    public string MixDisplay =>
        $"{(BPM is null ? "--" : BPM.Value.ToString())} BPM / {(!string.IsNullOrWhiteSpace(MusicalKey) ? MusicalKey : "--")}";
}
