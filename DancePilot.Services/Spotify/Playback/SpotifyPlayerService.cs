using System.Net;
using System.Text;
using System.Text.Json;
using DancePilot.Core.Spotify;

namespace DancePilot.Services.Spotify.Playback;

public sealed class SpotifyPlayerService
{
    private static readonly Uri ApiBaseUri = new("https://api.spotify.com/v1/");
    private readonly SpotifyService _spotifyService;

    public SpotifyPlayerService(SpotifyService spotifyService)
    {
        _spotifyService = spotifyService;
    }

    public async Task<SpotifyPlaybackState?> GetPlaybackStateAsync(
        SpotifySettings settings,
        CancellationToken cancellationToken = default)
    {
        using var response = await _spotifyService.SendAsync(
            settings,
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBaseUri, "me/player")),
            [SpotifyScopes.UserReadPlaybackState],
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return SpotifyPlaybackJson.ReadPlaybackState(document.RootElement);
    }

    public async Task<SpotifyTrackMetadata?> GetCurrentlyPlayingAsync(
        SpotifySettings settings,
        CancellationToken cancellationToken = default)
    {
        using var response = await _spotifyService.SendAsync(
            settings,
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBaseUri, "me/player/currently-playing")),
            [SpotifyScopes.UserReadCurrentlyPlaying],
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return root.TryGetProperty("item", out var item)
            && item.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? SpotifyPlaybackJson.ReadTrack(item)
                : null;
    }

    public Task PlayTrackAsync(
        SpotifySettings settings,
        string deviceId,
        string spotifyTrackUri,
        int positionMs = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyTrackUri))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.UnavailableTrack, "This Spotify track does not have a playable URI.");
        }

        return SendPlayAsync(
            settings,
            deviceId,
            new
            {
                uris = new[] { spotifyTrackUri },
                position_ms = Math.Max(0, positionMs)
            },
            cancellationToken);
    }

    public Task PlayPlaylistAsync(
        SpotifySettings settings,
        string deviceId,
        string spotifyPlaylistId,
        string? startTrackUri = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyPlaylistId))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Select a Spotify playlist first.");
        }

        var contextUri = spotifyPlaylistId.StartsWith("spotify:playlist:", StringComparison.OrdinalIgnoreCase)
            ? spotifyPlaylistId
            : $"spotify:playlist:{spotifyPlaylistId}";

        object payload = string.IsNullOrWhiteSpace(startTrackUri)
            ? new { context_uri = contextUri }
            : new
            {
                context_uri = contextUri,
                offset = new { uri = startTrackUri }
            };

        return SendPlayAsync(settings, deviceId, payload, cancellationToken);
    }

    public Task ResumeAsync(SpotifySettings settings, string deviceId, CancellationToken cancellationToken = default) =>
        SendPlayAsync(settings, deviceId, payload: null, cancellationToken);

    public Task PauseAsync(SpotifySettings settings, string deviceId, CancellationToken cancellationToken = default) =>
        SendEmptyAsync(settings, HttpMethod.Put, BuildPlayerUri("me/player/pause", deviceId), [SpotifyScopes.UserModifyPlaybackState], cancellationToken);

    public Task SkipNextAsync(SpotifySettings settings, string deviceId, CancellationToken cancellationToken = default) =>
        SendEmptyAsync(settings, HttpMethod.Post, BuildPlayerUri("me/player/next", deviceId), [SpotifyScopes.UserModifyPlaybackState], cancellationToken);

    public Task SkipPreviousAsync(SpotifySettings settings, string deviceId, CancellationToken cancellationToken = default) =>
        SendEmptyAsync(settings, HttpMethod.Post, BuildPlayerUri("me/player/previous", deviceId), [SpotifyScopes.UserModifyPlaybackState], cancellationToken);

    public Task SeekAsync(SpotifySettings settings, string deviceId, int positionMs, CancellationToken cancellationToken = default)
    {
        var query = $"position_ms={Math.Max(0, positionMs)}";
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query += $"&device_id={Uri.EscapeDataString(deviceId)}";
        }

        return SendEmptyAsync(settings, HttpMethod.Put, new Uri(ApiBaseUri, $"me/player/seek?{query}"), [SpotifyScopes.UserModifyPlaybackState], cancellationToken);
    }

    public Task SetVolumeAsync(SpotifySettings settings, string deviceId, int volumePercent, CancellationToken cancellationToken = default)
    {
        var clampedVolume = Math.Clamp(volumePercent, 0, 100);
        var query = $"volume_percent={clampedVolume}";
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query += $"&device_id={Uri.EscapeDataString(deviceId)}";
        }

        return SendEmptyAsync(settings, HttpMethod.Put, new Uri(ApiBaseUri, $"me/player/volume?{query}"), [SpotifyScopes.UserModifyPlaybackState], cancellationToken);
    }

    private Task SendPlayAsync(
        SpotifySettings settings,
        string deviceId,
        object? payload,
        CancellationToken cancellationToken)
    {
        return SendEmptyAsync(
            settings,
            HttpMethod.Put,
            BuildPlayerUri("me/player/play", deviceId),
            [SpotifyScopes.UserModifyPlaybackState],
            cancellationToken,
            payload);
    }

    private async Task SendEmptyAsync(
        SpotifySettings settings,
        HttpMethod method,
        Uri uri,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken,
        object? payload = null)
    {
        using var response = await _spotifyService.SendAsync(
            settings,
            () =>
            {
                var request = new HttpRequestMessage(method, uri);
                if (payload is not null)
                {
                    var json = JsonSerializer.Serialize(payload);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                return request;
            },
            scopes,
            cancellationToken);
    }

    private static Uri BuildPlayerUri(string path, string deviceId)
    {
        var query = string.IsNullOrWhiteSpace(deviceId)
            ? string.Empty
            : $"?device_id={Uri.EscapeDataString(deviceId)}";

        return new Uri(ApiBaseUri, $"{path}{query}");
    }
}
