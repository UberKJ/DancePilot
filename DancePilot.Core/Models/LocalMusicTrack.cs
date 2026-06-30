namespace DancePilot.Core.Models;

public sealed record LocalMusicTrack
{
    public required string FilePath { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtUrl { get; init; }

    public string? Genre { get; init; }

    public int? Year { get; init; }

    public int? TrackNumber { get; init; }

    public int? BPM { get; init; }

    public string? MusicalKey { get; init; }

    public TimeSpan? Duration { get; init; }

    public required string Extension { get; init; }

    public required string Folder { get; init; }

    public long? FileSize { get; init; }

    public DateTimeOffset LastModifiedAt { get; init; }

    public string FileName => Path.GetFileName(FilePath);

    public string DurationDisplay => Duration is null
        ? "--:--"
        : $"{(int)Duration.Value.TotalMinutes}:{Duration.Value.Seconds:00}";

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;

    public string MixDisplay
    {
        get
        {
            var mixParts = new List<string>();
            if (BPM is not null)
            {
                mixParts.Add($"{BPM.Value} BPM");
            }

            if (!string.IsNullOrWhiteSpace(MusicalKey))
            {
                mixParts.Add(MusicalKey.Trim());
            }

            return string.Join(" / ", mixParts);
        }
    }

    public string SearchText => $"{Title} {Artist} {Album} {Genre} {BPM} {MusicalKey} {Year} {TrackNumber} {FileName}".ToLowerInvariant();
}
