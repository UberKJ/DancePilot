namespace DancePilot.Core.Spotify;

public sealed record SpotifyTrackMetadata
{
    public required string SpotifyTrackId { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtUrl { get; init; }

    public int? BPM { get; init; }

    public string? MusicalKey { get; init; }

    public int DurationMs { get; init; }

    public string? SpotifyUri { get; init; }

    public string? ExternalUrl { get; init; }

    public int? Popularity { get; init; }

    public bool IsUnavailable { get; init; }

    public DateTimeOffset? AddedAt { get; init; }

    public string DurationDisplay => TimeSpan.FromMilliseconds(DurationMs).ToString(@"m\:ss");

    public string PopularityDisplay => Popularity?.ToString() ?? "-";

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
}
