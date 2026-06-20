using System.Text.Json;
using DancePilot.Core.Spotify;

namespace DancePilot.Services.Spotify.Playback;

internal static class SpotifyPlaybackJson
{
    public static SpotifyDevice ReadDevice(JsonElement device)
    {
        var id = device.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null
            ? idElement.GetString() ?? string.Empty
            : string.Empty;

        return new SpotifyDevice
        {
            Id = id,
            Name = device.TryGetProperty("name", out var name) ? name.GetString() ?? "Spotify Device" : "Spotify Device",
            Type = device.TryGetProperty("type", out var type) ? type.GetString() ?? "Unknown" : "Unknown",
            IsActive = device.TryGetProperty("is_active", out var isActive) && isActive.GetBoolean(),
            IsRestricted = device.TryGetProperty("is_restricted", out var isRestricted) && isRestricted.GetBoolean(),
            SupportsVolume = device.TryGetProperty("supports_volume", out var supportsVolume)
                ? supportsVolume.GetBoolean()
                : !string.IsNullOrWhiteSpace(id),
            VolumePercent = device.TryGetProperty("volume_percent", out var volume)
                && volume.ValueKind != JsonValueKind.Null
                    ? volume.GetInt32()
                    : null
        };
    }

    public static SpotifyPlaybackState ReadPlaybackState(JsonElement root)
    {
        SpotifyDevice? device = null;
        if (root.TryGetProperty("device", out var deviceElement)
            && deviceElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            device = ReadDevice(deviceElement);
        }

        SpotifyTrackMetadata? track = null;
        if (root.TryGetProperty("item", out var item)
            && item.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            && (!item.TryGetProperty("type", out var type) || type.GetString() == "track"))
        {
            track = ReadTrack(item);
        }

        return new SpotifyPlaybackState
        {
            Device = device,
            Track = track,
            IsPlaying = root.TryGetProperty("is_playing", out var isPlaying) && isPlaying.GetBoolean(),
            ProgressMs = root.TryGetProperty("progress_ms", out var progress)
                && progress.ValueKind != JsonValueKind.Null
                    ? progress.GetInt32()
                    : null,
            DurationMs = track?.DurationMs,
            ContextUri = root.TryGetProperty("context", out var context)
                && context.ValueKind != JsonValueKind.Null
                && context.TryGetProperty("uri", out var contextUri)
                    ? contextUri.GetString()
                    : null
        };
    }

    public static SpotifyTrackMetadata ReadTrack(JsonElement track)
    {
        var artists = track.TryGetProperty("artists", out var artistsElement)
            ? string.Join(", ", artistsElement.EnumerateArray()
                .Select(artist => artist.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name)))
            : string.Empty;

        return new SpotifyTrackMetadata
        {
            SpotifyTrackId = track.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Title = track.TryGetProperty("name", out var name) ? name.GetString() ?? "Untitled Track" : "Untitled Track",
            Artist = string.IsNullOrWhiteSpace(artists) ? "Unknown Artist" : artists,
            Album = track.TryGetProperty("album", out var album) && album.TryGetProperty("name", out var albumName)
                ? albumName.GetString()
                : null,
            DurationMs = track.TryGetProperty("duration_ms", out var duration) ? duration.GetInt32() : 0,
            SpotifyUri = track.TryGetProperty("uri", out var uri) ? uri.GetString() : null,
            ExternalUrl = ReadSpotifyExternalUrl(track),
            Popularity = track.TryGetProperty("popularity", out var popularity) ? popularity.GetInt32() : null,
            IsUnavailable = !track.TryGetProperty("id", out var trackId) || string.IsNullOrWhiteSpace(trackId.GetString())
        };
    }

    private static string? ReadSpotifyExternalUrl(JsonElement item)
    {
        return item.TryGetProperty("external_urls", out var urls)
            && urls.TryGetProperty("spotify", out var spotifyUrl)
                ? spotifyUrl.GetString()
                : null;
    }
}
