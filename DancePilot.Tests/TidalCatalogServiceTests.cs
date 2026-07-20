using System.Net;
using System.Text;
using DancePilot.Services.Tidal;
using DancePilot.Services.Tidal.Auth;

namespace DancePilot.Tests;

public sealed class TidalCatalogServiceTests
{
    [Fact]
    public async Task CheckApi_UsesOfficialAlbumHealthContractAndHeaders()
    {
        RequestSnapshot? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = RequestSnapshot.From(request);
            return JsonResponse(AlbumHealthJson);
        });

        var result = await CreateService(handler).CheckApiAsync(EnabledSettings("us"));

        Assert.Equal("59727856", result.AlbumId);
        Assert.Equal("Health Album", result.AlbumTitle);
        Assert.Equal("https://openapi.tidal.com/v2/albums/59727856?countryCode=US", captured!.Uri);
        Assert.Equal("GET", captured.Method);
        Assert.Equal("Bearer access-token", captured.Authorization);
        Assert.Equal("application/vnd.tidal.v1+json", captured.Accept);
        Assert.Equal("application/vnd.tidal.v1+json", captured.ContentType);
    }

    [Fact]
    public async Task CheckApi_FailureIncludesSanitizedJsonApiDiagnostic()
    {
        var handler = new StubHttpMessageHandler(_ => ErrorResponse(
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"code\":\"bad_country\",\"detail\":\"Country is unavailable\"}]}"));

        var exception = await Assert.ThrowsAsync<TidalApiException>(() =>
            CreateService(handler, maxTemporaryRetries: 0).CheckApiAsync(EnabledSettings()));

        Assert.Equal(TidalApiErrorKind.InvalidRequest, exception.Kind);
        Assert.Equal("TIDAL API health check", exception.Diagnostic!.OperationName);
        Assert.Equal("bad_country", exception.Diagnostic.ErrorCode);
        Assert.Equal("Country is unavailable", exception.Diagnostic.ErrorDetail);
        Assert.DoesNotContain("access-token", exception.Diagnostic.ToLogString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_UsesExactPathAndHydratesRelationshipsInSearchOrder()
    {
        var requests = new List<RequestSnapshot>();
        var handler = new StubHttpMessageHandler(request =>
        {
            var snapshot = RequestSnapshot.From(request);
            requests.Add(snapshot);
            return snapshot.Path switch
            {
                "/v2/searchResults/Beatles%20songs" => JsonResponse(SearchRelationshipJson),
                "/v2/tracks" => JsonResponse(TracksHydrationJson),
                "/v2/albums" => JsonResponse(AlbumsHydrationJson),
                "/v2/artists" => JsonResponse(ArtistsHydrationJson),
                _ => throw new InvalidOperationException($"Unexpected request: {snapshot.Uri}")
            };
        });

        var result = await CreateService(handler).SearchAsync(EnabledSettings(), "Beatles songs");

        Assert.Equal(["track-2", "track-1"], result.Tracks.Select(track => track.ProviderTrackId));
        var track = result.Tracks[1];
        Assert.Equal("Exact CASE Title", track.Title);
        Assert.Equal("First Artist, Second Artist", track.Artist);
        Assert.Equal(["shared-id", "artist-2"], track.ArtistIds);
        Assert.Equal("shared-id", track.AlbumId);
        Assert.Equal("Album with Same ID", track.Album);
        Assert.Equal(TimeSpan.FromMinutes(3), track.Duration);
        Assert.True(track.IsExplicit);
        Assert.Equal("STREAM", track.Availability);
        Assert.Equal("https://resources.tidal.com/album.jpg", track.ArtworkReference);
        Assert.Equal("album-2", result.Albums[0].ProviderAlbumId);
        Assert.Equal("artist-2", result.Artists[0].ProviderArtistId);

        var searchRequest = requests[0];
        Assert.Equal("/v2/searchResults/Beatles%20songs", searchRequest.Path);
        Assert.Contains("countryCode=US", searchRequest.Query, StringComparison.Ordinal);
        Assert.Contains("include=tracks,albums,artists", searchRequest.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("page[limit]", searchRequest.Query, StringComparison.Ordinal);
        Assert.Contains(requests, item => item.Path == "/v2/tracks" && item.Query.Contains("filter[id]=track-2,track-1", StringComparison.Ordinal));
        Assert.Contains(requests, item => item.Path == "/v2/tracks" && item.Query.Contains("include=artists,albums", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_FollowsRelationshipNextLinksAndPreservesOrder()
    {
        var trackPage = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/v2/searchResults/paged")
            {
                return JsonResponse(SearchLinksOnlyJson);
            }

            if (uri.AbsolutePath == "/v2/searchResults/paged/relationships/tracks")
            {
                trackPage++;
                return JsonResponse(trackPage == 1 ? TrackRelationshipPageOneJson : TrackRelationshipPageTwoJson);
            }

            if (uri.AbsolutePath.EndsWith("/relationships/albums", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/relationships/artists", StringComparison.Ordinal))
            {
                return JsonResponse(EmptyRelationshipJson);
            }

            if (uri.AbsolutePath == "/v2/tracks")
            {
                return JsonResponse(TracksHydrationJson);
            }

            throw new InvalidOperationException($"Unexpected request: {uri}");
        });

        var result = await CreateService(handler).SearchAsync(EnabledSettings(), "paged");

        Assert.Equal(2, trackPage);
        Assert.Equal(["track-2", "track-1"], result.Tracks.Select(track => track.ProviderTrackId));
    }

    [Fact]
    public async Task Search_EmptyRelationshipsReturnsNoResultsWithoutHydration()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            return JsonResponse(EmptySearchJson);
        });

        var result = await CreateService(handler).SearchAsync(EnabledSettings(), "nothing");

        Assert.Empty(result.Tracks);
        Assert.Empty(result.Albums);
        Assert.Empty(result.Artists);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Search_MalformedJsonMapsMalformedResponse()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{not-json"));

        var exception = await Assert.ThrowsAsync<TidalApiException>(() =>
            CreateService(handler).SearchAsync(EnabledSettings(), "test"));

        Assert.Equal(TidalApiErrorKind.MalformedResponse, exception.Kind);
        Assert.NotNull(exception.Diagnostic);
    }

    [Fact]
    public async Task Search_400IncludesJsonApiDetailAndSanitizedDiagnostic()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"dancepilot-tidal-diagnostic-{Guid.NewGuid():N}.log");
        try
        {
            var handler = new StubHttpMessageHandler(_ => ErrorResponse(
                HttpStatusCode.BadRequest,
                "{\"errors\":[{\"code\":\"bad_query\",\"title\":\"Invalid search\",\"detail\":\"The search query format is invalid\"}]}"));
            var service = CreateService(handler, diagnosticPath: logPath);

            var exception = await Assert.ThrowsAsync<TidalApiException>(() =>
                service.SearchAsync(EnabledSettings(), "test"));

            Assert.Equal(TidalApiErrorKind.InvalidRequest, exception.Kind);
            Assert.Contains("HTTP 400", exception.Message, StringComparison.Ordinal);
            Assert.Contains("The search query format is invalid", exception.Message, StringComparison.Ordinal);
            Assert.Equal("bad_query", exception.Diagnostic!.ErrorCode);
            Assert.Equal("Invalid search", exception.Diagnostic.ErrorTitle);
            var log = await File.ReadAllTextAsync(logPath);
            Assert.Contains("operation=TIDAL search", log, StringComparison.Ordinal);
            Assert.Contains("contentType=application/vnd.tidal.v1+json", log, StringComparison.Ordinal);
            Assert.DoesNotContain("access-token", log, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, TidalApiErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, TidalApiErrorKind.NotFound)]
    public async Task Search_MapsDocumentedHttpFailures(HttpStatusCode statusCode, TidalApiErrorKind expectedKind)
    {
        var handler = new StubHttpMessageHandler(_ => ErrorResponse(
            statusCode,
            $"{{\"errors\":[{{\"code\":\"api_error\",\"detail\":\"Failure {statusCode}\"}}]}}"));

        var exception = await Assert.ThrowsAsync<TidalApiException>(() =>
            CreateService(handler, maxTemporaryRetries: 0).SearchAsync(EnabledSettings(), "test"));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.Equal((int)statusCode, exception.StatusCode);
        Assert.Contains($"HTTP {(int)statusCode}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_401RefreshesTokenAndRetriesOnce()
    {
        var searchCalls = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.Host == "auth.tidal.com")
            {
                return JsonResponse("{\"access_token\":\"refreshed-token\",\"token_type\":\"Bearer\",\"scope\":\"user.read search.read collection.read playlists.read\",\"expires_in\":3600,\"user_id\":42}");
            }

            searchCalls++;
            return searchCalls == 1
                ? ErrorResponse(HttpStatusCode.Unauthorized, "{\"errors\":[{\"code\":\"expired\",\"detail\":\"Token expired\"}]}")
                : JsonResponse(EmptySearchJson);
        });

        var result = await CreateService(handler).SearchAsync(EnabledSettings(), "retry");

        Assert.Empty(result.Tracks);
        Assert.Equal(2, searchCalls);
    }

    [Fact]
    public async Task Search_429HonorsRetryAfterAndRetriesWithinBound()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                var response = ErrorResponse((HttpStatusCode)429, "{\"errors\":[{\"detail\":\"Slow down\"}]}");
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return response;
            }

            return JsonResponse(EmptySearchJson);
        });

        var result = await CreateService(handler, maxTemporaryRetries: 1).SearchAsync(EnabledSettings(), "retry");

        Assert.Empty(result.Tracks);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Search_MissingScopeDoesNotCallApiAndNamesScope()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            return JsonResponse(EmptySearchJson);
        });
        var token = ValidToken(scope: "user.read");

        var exception = await Assert.ThrowsAsync<TidalApiException>(() =>
            CreateService(handler, token: token).SearchAsync(EnabledSettings(), "test"));

        Assert.Equal(TidalApiErrorKind.MissingRequiredScope, exception.Kind);
        Assert.Contains("search.read", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Reconnect to TIDAL", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task LoadUserPlaylists_MissingHydrationScopeDoesNotCallApiAndNamesScope()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            return JsonResponse(EmptyRelationshipJson);
        });
        var token = ValidToken(scope: "user.read collection.read");

        var exception = await Assert.ThrowsAsync<TidalApiException>(() =>
            CreateService(handler, token: token).LoadUserPlaylistsAsync(EnabledSettings()));

        Assert.Equal(TidalApiErrorKind.MissingRequiredScope, exception.Kind);
        Assert.Contains("playlists.read", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task LoadUserPlaylists_UsesResolvedUserIdPaginatesHydratesAndLabelsGroups()
    {
        var collectionPages = 0;
        var requests = new List<RequestSnapshot>();
        var handler = new StubHttpMessageHandler(request =>
        {
            var snapshot = RequestSnapshot.From(request);
            requests.Add(snapshot);
            if (snapshot.Path == "/v2/userCollectionPlaylists/user-7/relationships/items")
            {
                collectionPages++;
                return JsonResponse(collectionPages == 1 ? PlaylistCollectionPageOneJson : PlaylistCollectionPageTwoJson);
            }

            if (snapshot.Path == "/v2/playlists" && snapshot.Query.Contains("filter[owners.id]=me", StringComparison.Ordinal))
            {
                return JsonResponse(OwnedPlaylistIdentifiersJson);
            }

            if (snapshot.Path == "/v2/playlists")
            {
                return JsonResponse(PlaylistHydrationJson);
            }

            throw new InvalidOperationException($"Unexpected request: {snapshot.Uri}");
        });
        var token = ValidToken(userId: "user-7");

        var playlists = await CreateService(handler, token: token).LoadUserPlaylistsAsync(EnabledSettings());

        Assert.Equal(2, collectionPages);
        Assert.Equal(["playlist-2", "playlist-1", "playlist-3"], playlists.Select(value => value.ProviderPlaylistId));
        Assert.Equal("In My Collection", playlists[0].CollectionKind);
        Assert.Equal("Owned by me; In My Collection", playlists[1].CollectionKind);
        Assert.Equal("Owned by me", playlists[2].CollectionKind);
        Assert.Equal(12, playlists[1].NumberOfItems);
        Assert.Equal("Playlist Owner", playlists[1].Owner);
        Assert.Contains(requests, request => request.Query.Contains("include=coverArt,owners", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadUserPlaylists_UsesDocumentedMeAliasWhenTokenHasNoUserId()
    {
        var paths = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return JsonResponse(request.RequestUri.AbsolutePath.Contains("userCollectionPlaylists", StringComparison.Ordinal)
                ? EmptyRelationshipJson
                : "{\"data\":[],\"links\":{\"self\":\"/playlists\"}}");
        });

        var playlists = await CreateService(handler, token: ValidToken(userId: null)).LoadUserPlaylistsAsync(EnabledSettings());

        Assert.Empty(playlists);
        Assert.Contains("/v2/userCollectionPlaylists/me/relationships/items", paths);
    }

    [Fact]
    public async Task LoadPlaylistTracks_FollowsPagesSkipsVideosAndRestoresOrder()
    {
        var relationshipPages = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/playlists/playlist-1/relationships/items")
            {
                relationshipPages++;
                return JsonResponse(relationshipPages == 1 ? PlaylistTracksPageOneJson : PlaylistTracksPageTwoJson);
            }

            if (request.RequestUri.AbsolutePath == "/v2/tracks")
            {
                return JsonResponse(TracksHydrationJson);
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        var tracks = await CreateService(handler).LoadPlaylistTracksAsync(EnabledSettings(), "playlist-1");

        Assert.Equal(2, relationshipPages);
        Assert.Equal(["track-2", "track-1"], tracks.Select(track => track.ProviderTrackId));
        Assert.Equal([1, 2], tracks.Select(track => track.PlaylistPosition));
    }

    [Fact]
    public async Task LoadPlaylistTracks_MissingPlaylistScopeDoesNotCallApi()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            return JsonResponse(EmptyRelationshipJson);
        });
        var token = ValidToken(scope: "user.read collection.read search.read");

        var exception = await Assert.ThrowsAsync<TidalApiException>(() =>
            CreateService(handler, token: token).LoadPlaylistTracksAsync(EnabledSettings(), "playlist-1"));

        Assert.Equal(TidalApiErrorKind.MissingRequiredScope, exception.Kind);
        Assert.Contains("playlists.read", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void DiagnosticSanitization_RemovesTokensCodesSecretsAndVerifiers()
    {
        var uri = new Uri("https://openapi.tidal.com/v2/searchResults/test?countryCode=US&access_token=token-value&code=auth-code&client_secret=secret&code_verifier=verifier");

        var sanitized = TidalRequestDiagnosticWriter.SanitizeUri(uri);

        Assert.Contains("countryCode=US", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("auth-code", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("verifier", sanitized, StringComparison.Ordinal);
    }

    private static TidalCatalogService CreateService(
        HttpMessageHandler handler,
        int maxTemporaryRetries = 2,
        TidalTokenSet? token = null,
        string? diagnosticPath = null)
    {
        var tokenStore = new InMemoryTidalTokenStore(token ?? ValidToken());
        var httpClient = new HttpClient(handler);
        var authService = new TidalAuthService(httpClient, tokenStore);
        return new TidalCatalogService(
            httpClient,
            tokenStore,
            authService,
            maxTemporaryRetries,
            diagnosticPath is null
                ? TidalRequestDiagnosticWriter.Disabled
                : new TidalRequestDiagnosticWriter(diagnosticPath));
    }

    private static TidalTokenSet ValidToken(
        string scope = "user.read search.read collection.read playlists.read",
        string? userId = "user-7") => new()
    {
        AccessToken = "access-token",
        RefreshToken = "refresh-token",
        TokenType = "Bearer",
        Scope = scope,
        RequestedScopes = TidalScopes.Requested,
        UserId = userId,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
    };

    private static TidalSettings EnabledSettings(string countryCode = "US") => new()
    {
        ClientId = "client-id",
        RedirectUri = TidalDefaults.RedirectUri,
        CountryCode = countryCode,
        ExperimentalCatalogEnabled = true
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/vnd.tidal.v1+json")
    };

    private static HttpResponseMessage ErrorResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/vnd.tidal.v1+json")
    };

    private const string AlbumHealthJson = """
        { "data": { "type": "albums", "id": "59727856", "attributes": { "title": "Health Album" } } }
        """;

    private const string SearchRelationshipJson = """
        {
          "data": {
            "type": "searchResults",
            "id": "Beatles songs",
            "relationships": {
              "tracks": { "data": [{ "type": "tracks", "id": "track-2" }, { "type": "tracks", "id": "track-1" }] },
              "albums": { "data": [{ "type": "albums", "id": "album-2" }, { "type": "albums", "id": "shared-id" }] },
              "artists": { "data": [{ "type": "artists", "id": "artist-2" }, { "type": "artists", "id": "shared-id" }] }
            }
          }
        }
        """;

    private const string TracksHydrationJson = """
        {
          "data": [
            {
              "type": "tracks", "id": "track-1",
              "attributes": { "title": "Exact CASE Title", "duration": "PT3M", "explicit": true, "availability": ["STREAM"] },
              "relationships": {
                "artists": { "data": [{ "type": "artists", "id": "shared-id" }, { "type": "artists", "id": "artist-2" }] },
                "albums": { "data": [{ "type": "albums", "id": "shared-id" }] }
              }
            },
            {
              "type": "tracks", "id": "track-2",
              "attributes": { "title": "Second Track", "duration": "PT2M" },
              "relationships": { "artists": { "data": [{ "type": "artists", "id": "artist-2" }] }, "albums": { "data": [{ "type": "albums", "id": "album-2" }] } }
            }
          ],
          "included": [
            { "type": "artists", "id": "shared-id", "attributes": { "name": "First Artist" } },
            { "type": "artists", "id": "artist-2", "attributes": { "name": "Second Artist" } },
            { "type": "albums", "id": "shared-id", "attributes": { "title": "Album with Same ID" }, "relationships": { "coverArt": { "data": [{ "type": "artworks", "id": "art-1" }] } } },
            { "type": "albums", "id": "album-2", "attributes": { "title": "Second Album" } },
            { "type": "artworks", "id": "art-1", "attributes": { "files": [{ "href": "https://resources.tidal.com/album.jpg", "meta": {} }], "mediaType": "IMAGE" } }
          ]
        }
        """;

    private const string AlbumsHydrationJson = """
        {
          "data": [
            { "type": "albums", "id": "shared-id", "attributes": { "title": "Album with Same ID" }, "relationships": { "artists": { "data": [{ "type": "artists", "id": "shared-id" }] } } },
            { "type": "albums", "id": "album-2", "attributes": { "title": "Second Album" }, "relationships": { "artists": { "data": [{ "type": "artists", "id": "artist-2" }] } } }
          ],
          "included": [
            { "type": "artists", "id": "shared-id", "attributes": { "name": "First Artist" } },
            { "type": "artists", "id": "artist-2", "attributes": { "name": "Second Artist" } }
          ]
        }
        """;

    private const string ArtistsHydrationJson = """
        { "data": [{ "type": "artists", "id": "shared-id", "attributes": { "name": "First Artist" } }, { "type": "artists", "id": "artist-2", "attributes": { "name": "Second Artist" } }] }
        """;

    private const string SearchLinksOnlyJson = """
        { "data": { "type": "searchResults", "id": "paged", "relationships": { "tracks": { "links": { "self": "/searchResults/paged/relationships/tracks" } }, "albums": { "links": { "self": "/searchResults/paged/relationships/albums" } }, "artists": { "links": { "self": "/searchResults/paged/relationships/artists" } } } } }
        """;

    private const string TrackRelationshipPageOneJson = """
        { "data": [{ "type": "tracks", "id": "track-2" }], "links": { "self": "/searchResults/paged/relationships/tracks", "next": "/searchResults/paged/relationships/tracks?page[cursor]=next" } }
        """;

    private const string TrackRelationshipPageTwoJson = """
        { "data": [{ "type": "tracks", "id": "track-1" }], "links": { "self": "/searchResults/paged/relationships/tracks?page[cursor]=next" } }
        """;

    private const string EmptyRelationshipJson = """
        { "data": [], "links": { "self": "/relationship" } }
        """;

    private const string EmptySearchJson = """
        { "data": { "type": "searchResults", "id": "empty", "relationships": { "tracks": { "data": [] }, "albums": { "data": [] }, "artists": { "data": [] } } } }
        """;

    private const string PlaylistCollectionPageOneJson = """
        { "data": [{ "type": "playlists", "id": "playlist-2", "meta": { "addedAt": "2026-01-01T00:00:00Z" } }], "links": { "self": "/userCollectionPlaylists/user-7/relationships/items", "next": "/userCollectionPlaylists/user-7/relationships/items?page[cursor]=next" } }
        """;

    private const string PlaylistCollectionPageTwoJson = """
        { "data": [{ "type": "playlists", "id": "playlist-1", "meta": { "addedAt": "2025-01-01T00:00:00Z" } }], "links": { "self": "/userCollectionPlaylists/user-7/relationships/items?page[cursor]=next" } }
        """;

    private const string OwnedPlaylistIdentifiersJson = """
        { "data": [{ "type": "playlists", "id": "playlist-1" }, { "type": "playlists", "id": "playlist-3" }], "links": { "self": "/playlists?filter[owners.id]=me" } }
        """;

    private const string PlaylistHydrationJson = """
        {
          "data": [
            { "type": "playlists", "id": "playlist-3", "attributes": { "name": "Owned Three", "numberOfItems": 3, "accessType": "PRIVATE" }, "relationships": { "owners": { "data": [{ "type": "users", "id": "user-7" }] } } },
            { "type": "playlists", "id": "playlist-1", "attributes": { "name": "Favorite One", "description": "Description", "numberOfItems": 12, "accessType": "PUBLIC" }, "relationships": { "owners": { "data": [{ "type": "users", "id": "user-7" }] } } },
            { "type": "playlists", "id": "playlist-2", "attributes": { "name": "Favorite Two", "numberOfItems": 8, "accessType": "PUBLIC" }, "relationships": { "owners": { "data": [{ "type": "users", "id": "other-user" }] } } }
          ],
          "included": [
            { "type": "users", "id": "user-7", "attributes": { "username": "Playlist Owner" } },
            { "type": "users", "id": "other-user", "attributes": { "username": "Other Owner" } }
          ]
        }
        """;

    private const string PlaylistTracksPageOneJson = """
        { "data": [{ "type": "tracks", "id": "track-2", "meta": { "itemId": "a" } }, { "type": "videos", "id": "video-1", "meta": { "itemId": "b" } }], "links": { "self": "/playlists/playlist-1/relationships/items", "next": "/playlists/playlist-1/relationships/items?page[cursor]=next" } }
        """;

    private const string PlaylistTracksPageTwoJson = """
        { "data": [{ "type": "tracks", "id": "track-1", "meta": { "itemId": "c" } }], "links": { "self": "/playlists/playlist-1/relationships/items?page[cursor]=next" } }
        """;

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }

    private sealed class InMemoryTidalTokenStore(TidalTokenSet token) : ITidalTokenStore
    {
        private TidalTokenSet? _token = token;

        public Task<TidalTokenSet?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_token);

        public Task SaveAsync(TidalTokenSet tokenSet, CancellationToken cancellationToken = default)
        {
            _token = tokenSet;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _token = null;
            return Task.CompletedTask;
        }
    }

    private sealed record RequestSnapshot(
        string Method,
        string Uri,
        string Path,
        string Query,
        string? Authorization,
        string? Accept,
        string? ContentType)
    {
        public static RequestSnapshot From(HttpRequestMessage request) => new(
            request.Method.Method,
            request.RequestUri!.AbsoluteUri,
            request.RequestUri.AbsolutePath,
            request.RequestUri.Query,
            request.Headers.Authorization?.ToString(),
            request.Headers.Accept.FirstOrDefault()?.MediaType,
            request.Content?.Headers.ContentType?.MediaType);
    }
}
