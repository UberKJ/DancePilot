using DancePilot.Core.Spotify;

namespace DancePilot.Core.Models;

public sealed record TrackDisplayItem
{
    public required string Source { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public string? Album { get; init; }

    public string? AlbumArt { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? ExternalUri { get; init; }

    public string? LocalPath { get; init; }

    public string? ProviderTrackId { get; init; }

    public string? PlaybackHint { get; init; }

    public bool IsPlayable { get; init; } = true;

    public int? BPM { get; init; }

    public string? MusicalKey { get; init; }

    public string DurationDisplay => Duration is null
        ? "--:--"
        : $"{(int)Duration.Value.TotalMinutes}:{Duration.Value.Seconds:00}";

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

    public static TrackDisplayItem FromSpotify(SpotifyTrackMetadata track) => new()
    {
        Source = SongSources.Spotify,
        Title = track.Title,
        Artist = track.Artist,
        Album = track.Album,
        AlbumArt = track.AlbumArtUrl,
        Duration = TimeSpan.FromMilliseconds(track.DurationMs),
        ExternalUri = track.SpotifyUri,
        ProviderTrackId = track.SpotifyTrackId,
        PlaybackHint = track.IsUnavailable
            ? "Unavailable for Spotify API playback"
            : "Spotify Connect or Spotify app handoff",
        IsPlayable = !track.IsUnavailable && !string.IsNullOrWhiteSpace(track.SpotifyUri),
        BPM = track.BPM,
        MusicalKey = track.MusicalKey
    };

    public static TrackDisplayItem FromLocal(LocalMusicTrack track) => new()
    {
        Source = SongSources.Local,
        Title = track.Title,
        Artist = track.DisplayArtist,
        Album = track.Album,
        AlbumArt = track.AlbumArtUrl,
        Duration = track.Duration,
        ExternalUri = track.FilePath,
        LocalPath = track.FilePath,
        PlaybackHint = "Local file",
        BPM = track.BPM,
        MusicalKey = track.MusicalKey
    };
}
