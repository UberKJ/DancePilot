namespace DancePilot.Core.Models;

public sealed record LocalMusicTrack
{
    public required string FilePath { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public string? Album { get; init; }

    public TimeSpan? Duration { get; init; }

    public required string Extension { get; init; }

    public required string Folder { get; init; }

    public DateTimeOffset LastModifiedAt { get; init; }

    public string FileName => Path.GetFileName(FilePath);

    public string DurationDisplay => Duration is null
        ? "--:--"
        : $"{(int)Duration.Value.TotalMinutes}:{Duration.Value.Seconds:00}";

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;

    public string SearchText => $"{Title} {Artist} {Album} {FileName}".ToLowerInvariant();
}
