using DancePilot.Core.Models;

namespace DancePilot.Core.Spotify;

public sealed record DancePilotQueueItem
{
    private string? _albumArtUrl;

    public int Id { get; init; }

    public string DeckName { get; init; } = "Deck A";

    public string Source { get; init; } = "spotify";

    public string ExternalUri { get; init; } = string.Empty;

    public string? LocalPath { get; init; }

    public int? SongId { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtUrl
    {
        get => _albumArtUrl;
        init => _albumArtUrl = value;
    }

    public string? AlbumArt
    {
        get => _albumArtUrl;
        init => _albumArtUrl = value;
    }

    public TimeSpan? Duration { get; init; }

    public int? BPM { get; init; }

    public string? MusicalKey { get; init; }

    public int QueuePosition { get; init; }

    public string Status { get; init; } = "pending";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsSpotifySource =>
        string.Equals(Source, SongSources.Spotify, StringComparison.OrdinalIgnoreCase);

    public bool IsLocalSource =>
        string.Equals(Source, SongSources.Local, StringComparison.OrdinalIgnoreCase);

    public string? SpotifyUri => IsSpotifySource ? ExternalUri : null;

    public string? ResolvedLocalPath =>
        IsLocalSource
            ? FirstNonEmpty(LocalPath, ExternalUri)
            : LocalPath;

    public string? SourceIdentity =>
        IsLocalSource
            ? ResolvedLocalPath
            : FirstNonEmpty(ExternalUri, LocalPath);

    public string DisplayName => $"{QueuePosition}. {Title} - {Artist}";

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

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
