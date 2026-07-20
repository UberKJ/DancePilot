namespace DancePilot.Core.Models;

public sealed record AlbumArtCacheEntry
{
    public required string CacheKey { get; init; }

    public required string Source { get; init; }

    public string? ExternalUri { get; init; }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? OriginalUri { get; init; }

    public string? ContentType { get; init; }

    public byte[]? ImageBytes { get; init; }

    public string? LocalFilePath { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
