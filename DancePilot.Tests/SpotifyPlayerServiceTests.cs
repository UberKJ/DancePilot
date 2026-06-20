using System.Net;
using DancePilot.Services.Spotify;
using DancePilot.Services.Spotify.Auth;
using DancePilot.Services.Spotify.Playback;

namespace DancePilot.Tests;

public sealed class SpotifyPlayerServiceTests
{
    [Fact]
    public async Task PlayTrackAsync_SendsTrackUriToSpotifyPlaybackEndpoint()
    {
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler);
        var tokenStore = new InMemorySpotifyTokenStore(new SpotifyTokenSet
        {
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Scope = string.Join(' ', SpotifyScopes.PlaybackControl)
        });
        var authService = new SpotifyAuthService(httpClient, tokenStore);
        var spotifyService = new SpotifyService(httpClient, tokenStore, authService);
        var playerService = new SpotifyPlayerService(spotifyService);

        await playerService.PlayTrackAsync(
            new SpotifySettings { ClientId = "client-id" },
            "device-123",
            "spotify:track:abc123");

        Assert.Equal(HttpMethod.Put, handler.Request?.Method);
        Assert.Equal("https://api.spotify.com/v1/me/player/play?device_id=device-123", handler.Request?.RequestUri?.ToString());
        Assert.Contains("\"uris\":[\"spotify:track:abc123\"]", handler.Body);
        Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.Equal("access-token", handler.Request?.Headers.Authorization?.Parameter);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class InMemorySpotifyTokenStore : ISpotifyTokenStore
    {
        private SpotifyTokenSet? _tokenSet;

        public InMemorySpotifyTokenStore(SpotifyTokenSet tokenSet)
        {
            _tokenSet = tokenSet;
        }

        public Task<SpotifyTokenSet?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_tokenSet);

        public Task SaveAsync(SpotifyTokenSet tokenSet, CancellationToken cancellationToken = default)
        {
            _tokenSet = tokenSet;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _tokenSet = null;
            return Task.CompletedTask;
        }
    }
}
