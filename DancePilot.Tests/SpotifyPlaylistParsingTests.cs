using System.Net;
using System.Net.Http.Headers;
using DancePilot.Services.Spotify;
using DancePilot.Services.Spotify.Auth;

namespace DancePilot.Tests;

public sealed class SpotifyPlaylistParsingTests
{
    [Fact]
    public async Task GetUserPlaylistsAsync_ReadsPlaylistTrackTotals()
    {
        var handler = new JsonHandler("""
            {
              "items": [
                {
                  "id": "playlist-123",
                  "name": "Saturday Dance",
                  "owner": { "display_name": "Lynn Haas" },
                  "tracks": {
                    "href": "https://api.spotify.com/v1/playlists/playlist-123/tracks",
                    "total": 42
                  },
                  "snapshot_id": "snapshot-1",
                  "external_urls": { "spotify": "https://open.spotify.com/playlist/playlist-123" }
                }
              ],
              "next": null,
              "total": 1
            }
            """);
        var spotifyService = CreateSpotifyService(handler);

        var playlists = await spotifyService.GetUserPlaylistsAsync(new SpotifySettings { ClientId = "client-id" });

        var playlist = Assert.Single(playlists);
        Assert.Equal("Saturday Dance", playlist.Name);
        Assert.Equal(42, playlist.TrackCount);
    }

    [Fact]
    public async Task GetUserPlaylistsAsync_ReadsCurrentSpotifyPlaylistItemTotals()
    {
        var handler = new JsonHandler("""
            {
              "items": [
                {
                  "id": "playlist-current",
                  "name": "Sip and Paint",
                  "owner": { "display_name": "Lynn Haas" },
                  "items": {
                    "href": "https://api.spotify.com/v1/playlists/playlist-current/items",
                    "total": 102
                  },
                  "snapshot_id": "snapshot-current",
                  "external_urls": { "spotify": "https://open.spotify.com/playlist/playlist-current" }
                }
              ],
              "next": null,
              "total": 1
            }
            """);
        var spotifyService = CreateSpotifyService(handler);

        var playlists = await spotifyService.GetUserPlaylistsAsync(new SpotifySettings { ClientId = "client-id" });

        var playlist = Assert.Single(playlists);
        Assert.Equal("Sip and Paint", playlist.Name);
        Assert.Equal(102, playlist.TrackCount);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_PreservesSpotifyLocalTrackMetadataForPreview()
    {
        var handler = new JsonHandler("""
            {
              "items": [
                {
                  "added_at": "2026-06-20T10:00:00Z",
                  "track": {
                    "id": null,
                    "name": "Blue Suede Shoes",
                    "type": "track",
                    "is_local": true,
                    "artists": [{ "name": "Elvis Presley" }],
                    "album": { "name": "Local Files" },
                    "duration_ms": 128000,
                    "uri": "spotify:local:Elvis%20Presley:Blue%20Suede%20Shoes",
                    "external_urls": {}
                  }
                }
              ],
              "next": null,
              "total": 1
            }
            """);
        var spotifyService = CreateSpotifyService(handler);

        var tracks = await spotifyService.GetPlaylistTracksAsync(
            new SpotifySettings { ClientId = "client-id" },
            "playlist-123");

        var track = Assert.Single(tracks);
        Assert.Equal("Blue Suede Shoes", track.Title);
        Assert.Equal("Elvis Presley", track.Artist);
        Assert.True(track.IsUnavailable);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_SkipsNullTrackRowsAndKeepsValidTracks()
    {
        var handler = new JsonHandler("""
            {
              "items": [
                { "added_at": "2026-06-20T10:00:00Z", "track": null },
                {
                  "added_at": "2026-06-20T10:01:00Z",
                  "track": {
                    "id": "track-123",
                    "name": "Sweet Caroline",
                    "type": "track",
                    "is_local": false,
                    "artists": [{ "name": "Neil Diamond" }],
                    "album": { "name": "Brother Love" },
                    "duration_ms": 201000,
                    "uri": "spotify:track:track-123",
                    "external_urls": { "spotify": "https://open.spotify.com/track/track-123" },
                    "popularity": 77
                  }
                }
              ],
              "next": null,
              "total": 2
            }
            """);
        var spotifyService = CreateSpotifyService(handler);

        var tracks = await spotifyService.GetPlaylistTracksAsync(
            new SpotifySettings { ClientId = "client-id" },
            "playlist-123");

        var track = Assert.Single(tracks);
        Assert.Equal("track-123", track.SpotifyTrackId);
        Assert.Equal("Sweet Caroline", track.Title);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_ReadsCurrentSpotifyItemRows()
    {
        var handler = new JsonHandler("""
            {
              "items": [
                {
                  "added_at": "2026-03-21T22:39:55Z",
                  "is_local": false,
                  "item": {
                    "id": "2obblQ6tcePeOEVJV6nEGD",
                    "name": "Cat's in the Cradle",
                    "type": "track",
                    "is_playable": true,
                    "is_local": false,
                    "artists": [{ "name": "Harry Chapin" }],
                    "album": { "name": "Verities & Balderdash" },
                    "duration_ms": 222951,
                    "uri": "spotify:track:2obblQ6tcePeOEVJV6nEGD",
                    "external_urls": { "spotify": "https://open.spotify.com/track/2obblQ6tcePeOEVJV6nEGD" },
                    "popularity": 71
                  }
                }
              ],
              "next": null,
              "total": 1
            }
            """);
        var spotifyService = CreateSpotifyService(handler);

        var tracks = await spotifyService.GetPlaylistTracksAsync(
            new SpotifySettings { ClientId = "client-id" },
            "playlist-current");

        var track = Assert.Single(tracks);
        Assert.Equal("2obblQ6tcePeOEVJV6nEGD", track.SpotifyTrackId);
        Assert.Equal("Cat's in the Cradle", track.Title);
        Assert.Equal("Harry Chapin", track.Artist);
        Assert.False(track.IsUnavailable);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_UsesSpotifyAcceptedPageLimit()
    {
        var handler = new JsonHandler("""
            {
              "items": [],
              "next": null,
              "total": 0
            }
            """);
        var spotifyService = CreateSpotifyService(handler);

        await spotifyService.GetPlaylistTracksAsync(
            new SpotifySettings { ClientId = "client-id" },
            "playlist-123");

        Assert.Contains("limit=50", handler.RequestUri?.Query ?? string.Empty);
        Assert.Contains("item%28", handler.RequestUri?.Query ?? string.Empty);
        Assert.Contains("/playlists/playlist-123/items", handler.RequestUri?.AbsolutePath ?? string.Empty);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_RetriesRateLimitedRequestsWithRetryAfter()
    {
        var handler = new RateLimitThenJsonHandler("""
            {
              "items": [],
              "next": null,
              "total": 0
            }
            """);
        var spotifyService = CreateSpotifyService(handler);

        await spotifyService.GetPlaylistTracksAsync(
            new SpotifySettings { ClientId = "client-id" },
            "playlist-123");

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SearchTracksAsync_ClampsLimitToOpenApiMaximum()
    {
        var handler = new JsonHandler("""
            {
              "tracks": {
                "items": []
              }
            }
            """);
        var spotifyService = CreateSpotifyService(handler);

        await spotifyService.SearchTracksAsync(
            new SpotifySettings { ClientId = "client-id" },
            "merle haggard",
            limit: 30);

        Assert.Contains("/search", handler.RequestUri?.AbsolutePath ?? string.Empty);
        Assert.Contains("limit=10", handler.RequestUri?.Query ?? string.Empty);
    }

    private static SpotifyService CreateSpotifyService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var tokenStore = new InMemorySpotifyTokenStore(new SpotifyTokenSet
        {
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Scope = string.Join(' ', SpotifyScopes.PlanningMetadata)
        });
        var authService = new SpotifyAuthService(httpClient, tokenStore);
        return new SpotifyService(httpClient, tokenStore, authService);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(CreateResponse(request));

        private HttpResponseMessage CreateResponse(HttpRequestMessage request)
        {
            RequestUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
        }
    }

    private sealed class RateLimitThenJsonHandler(string json) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                var limited = new HttpResponseMessage((HttpStatusCode)429)
                {
                    RequestMessage = request,
                    Content = new StringContent("""{"error":{"status":429,"message":"rate limited"}}""")
                };
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(limited);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json)
            });
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
