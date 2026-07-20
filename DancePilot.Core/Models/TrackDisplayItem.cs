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

    public static TrackDisplayItem FromSong(Song song)
    {
        var source = NormalizeSource(song.Source);
        var isLocal = source == SongSources.Local;
        var externalUri = FirstNonEmpty(song.ExternalUri, song.ExternalId, song.ExternalUrl);
        return new TrackDisplayItem
        {
            Source = source,
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
            AlbumArt = song.AlbumArtPath,
            Duration = song.Duration,
            ExternalUri = isLocal ? null : externalUri,
            LocalPath = isLocal ? FirstNonEmpty(song.ExternalUri, song.ExternalUrl) : null,
            ProviderTrackId = isLocal ? null : song.ExternalId,
            PlaybackHint = source switch
            {
                SongSources.Local => "Local file",
                SongSources.Spotify => "Spotify Connect or Spotify app handoff",
                SongSources.Tidal => "TIDAL playback is not enabled",
                _ => $"{source} playback is not enabled"
            },
            IsPlayable = source switch
            {
                SongSources.Local => !string.IsNullOrWhiteSpace(FirstNonEmpty(song.ExternalUri, song.ExternalUrl)),
                SongSources.Spotify => !string.IsNullOrWhiteSpace(externalUri),
                _ => false
            },
            BPM = song.BPM,
            MusicalKey = song.Key
        };
    }

    private static string NormalizeSource(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        SongSources.Spotify => SongSources.Spotify,
        SongSources.Tidal => SongSources.Tidal,
        SongSources.YouTube => SongSources.YouTube,
        SongSources.Manual => SongSources.Manual,
        _ => SongSources.Local
    };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
