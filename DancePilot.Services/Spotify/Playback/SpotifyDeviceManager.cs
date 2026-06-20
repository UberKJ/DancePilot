using System.Text;
using System.Text.Json;
using DancePilot.Core.Spotify;

namespace DancePilot.Services.Spotify.Playback;

public sealed class SpotifyDeviceManager
{
    private static readonly Uri ApiBaseUri = new("https://api.spotify.com/v1/");
    private readonly SpotifyService _spotifyService;

    public SpotifyDeviceManager(SpotifyService spotifyService)
    {
        _spotifyService = spotifyService;
    }

    public async Task<IReadOnlyList<SpotifyDevice>> GetAvailableDevicesAsync(
        SpotifySettings settings,
        CancellationToken cancellationToken = default)
    {
        using var response = await _spotifyService.SendAsync(
            settings,
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBaseUri, "me/player/devices")),
            [SpotifyScopes.UserReadPlaybackState],
            cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement
            .GetProperty("devices")
            .EnumerateArray()
            .Select(SpotifyPlaybackJson.ReadDevice)
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .ToList();
    }

    public async Task TransferPlaybackAsync(
        SpotifySettings settings,
        string deviceId,
        bool play = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.NoActiveDevice, "Select a Spotify playback device first.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            device_ids = new[] { deviceId },
            play
        });

        using var response = await _spotifyService.SendAsync(
            settings,
            () => new HttpRequestMessage(HttpMethod.Put, new Uri(ApiBaseUri, "me/player"))
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            },
            [SpotifyScopes.UserModifyPlaybackState],
            cancellationToken);
    }
}
