using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DancePilot.Services.Tidal.Auth;

namespace DancePilot.Services.Tidal;

public sealed class TidalCatalogService
{
    public const string ApiBaseUrl = "https://openapi.tidal.com/v2/";
    private const string MediaType = "application/vnd.tidal.v1+json";
    private const string HealthAlbumId = "59727856";
    private const int HydrationBatchSize = 20;
    private static readonly Uri ApiBaseUri = new(ApiBaseUrl);
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(4);
    private readonly HttpClient _httpClient;
    private readonly ITidalTokenStore _tokenStore;
    private readonly TidalAuthService _authService;
    private readonly TidalRequestDiagnosticWriter _diagnosticWriter;
    private readonly int _maxTemporaryRetries;

    public TidalCatalogService(
        HttpClient httpClient,
        ITidalTokenStore tokenStore,
        TidalAuthService authService,
        int maxTemporaryRetries = 2,
        TidalRequestDiagnosticWriter? diagnosticWriter = null)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
        _authService = authService;
        _maxTemporaryRetries = Math.Max(0, maxTemporaryRetries);
        _diagnosticWriter = diagnosticWriter ?? new TidalRequestDiagnosticWriter();
    }

    public async Task<TidalApiHealthResult> CheckApiAsync(
        TidalSettings settings,
        CancellationToken cancellationToken = default)
    {
        var countryCode = ValidateCountryCode(settings.CountryCode);
        var uri = BuildUri($"albums/{HealthAlbumId}", ("countryCode", countryCode));
        using var response = await SendAsync(settings, uri, "TIDAL API health check", [], cancellationToken);
        using var document = await ParseDocumentAsync(response, uri, "TIDAL API health check", cancellationToken);
        var resource = TidalJsonApiMapper.ReadSingleResource(document.RootElement);
        var index = TidalJsonApiMapper.BuildResourceIndex(document.RootElement);
        var album = TidalJsonApiMapper.MapAlbum(resource, index);
        return new TidalApiHealthResult { AlbumId = album.ProviderAlbumId, AlbumTitle = album.Title };
    }

    public async Task<TidalSearchResult> SearchAsync(
        TidalSettings settings,
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new TidalApiException(TidalApiErrorKind.InvalidRequest, "A TIDAL catalog search query is required.");
        }

        var countryCode = ValidateCountryCode(settings.CountryCode);
        var escapedQuery = Uri.EscapeDataString(query.Trim());
        var searchUri = BuildUri(
            $"searchResults/{escapedQuery}",
            ("countryCode", countryCode),
            ("include", "tracks,albums,artists"));
        using var response = await SendAsync(settings, searchUri, "TIDAL search", TidalScopes.CatalogSearch, cancellationToken);
        using var document = await ParseDocumentAsync(response, searchUri, "TIDAL search", cancellationToken);
        var searchResource = TidalJsonApiMapper.ReadSingleResource(document.RootElement);

        var safeLimit = Math.Clamp(limit, 1, 50);
        var trackIds = await ReadSearchIdentifiersAsync(
            settings, searchResource, escapedQuery, "tracks", countryCode, safeLimit, cancellationToken);
        var albumIds = await ReadSearchIdentifiersAsync(
            settings, searchResource, escapedQuery, "albums", countryCode, safeLimit, cancellationToken);
        var artistIds = await ReadSearchIdentifiersAsync(
            settings, searchResource, escapedQuery, "artists", countryCode, safeLimit, cancellationToken);

        return new TidalSearchResult
        {
            Tracks = await HydrateTracksAsync(settings, trackIds, countryCode, cancellationToken),
            Albums = await HydrateAlbumsAsync(settings, albumIds, countryCode, cancellationToken),
            Artists = await HydrateArtistsAsync(settings, artistIds, countryCode, cancellationToken)
        };
    }

    public async Task<IReadOnlyList<TidalTrackMetadata>> SearchTracksAsync(
        TidalSettings settings,
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(settings, query, limit, cancellationToken)).Tracks;

    public async Task<IReadOnlyList<TidalAlbumMetadata>> SearchAlbumsAsync(
        TidalSettings settings,
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(settings, query, limit, cancellationToken)).Albums;

    public async Task<IReadOnlyList<TidalArtistMetadata>> SearchArtistsAsync(
        TidalSettings settings,
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(settings, query, limit, cancellationToken)).Artists;

    public async Task<TidalTrackMetadata> GetTrackAsync(
        TidalSettings settings,
        string providerTrackId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(providerTrackId, nameof(providerTrackId));
        var tracks = await HydrateTracksAsync(
            settings,
            [new TidalResourceIdentifier("tracks", providerTrackId, default)],
            ValidateCountryCode(settings.CountryCode),
            cancellationToken);
        return tracks.FirstOrDefault()
            ?? throw new TidalApiException(TidalApiErrorKind.NotFound, "The requested TIDAL track was not found.");
    }

    public async Task<IReadOnlyList<TidalPlaylistMetadata>> LoadUserPlaylistsAsync(
        TidalSettings settings,
        CancellationToken cancellationToken = default)
    {
        var token = await GetValidTokenAsync(settings, "TIDAL playlist collection", TidalScopes.UserPlaylists, cancellationToken);
        var collectionId = string.IsNullOrWhiteSpace(token.UserId) ? "me" : token.UserId;
        var collectionUri = BuildUri($"userCollectionPlaylists/{Uri.EscapeDataString(collectionId)}/relationships/items");
        var collected = await ReadRelationshipPagesAsync(
            settings, collectionUri, "TIDAL playlist collection", TidalScopes.UserPlaylistCollection, cancellationToken);

        var countryCode = ValidateCountryCode(settings.CountryCode);
        var ownedUri = BuildUri("playlists", ("filter[owners.id]", "me"), ("countryCode", countryCode));
        var owned = await ReadRelationshipPagesAsync(
            settings, ownedUri, "TIDAL owned playlists", TidalScopes.PlaylistResources, cancellationToken);

        var orderedIds = collected.Concat(owned)
            .Where(identifier => string.Equals(identifier.Type, "playlists", StringComparison.Ordinal))
            .Select(identifier => identifier.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var hydrated = await HydratePlaylistsAsync(settings, orderedIds, countryCode, cancellationToken);
        var collectedIds = collected.Select(identifier => identifier.Id).ToHashSet(StringComparer.Ordinal);
        var ownedIds = owned.Select(identifier => identifier.Id).ToHashSet(StringComparer.Ordinal);
        return hydrated.Select(playlist => playlist with
        {
            CollectionKind = collectedIds.Contains(playlist.ProviderPlaylistId) && ownedIds.Contains(playlist.ProviderPlaylistId)
                ? "Owned by me; In My Collection"
                : ownedIds.Contains(playlist.ProviderPlaylistId) ? "Owned by me" : "In My Collection"
        }).ToList();
    }

    public async Task<IReadOnlyList<TidalTrackMetadata>> LoadPlaylistTracksAsync(
        TidalSettings settings,
        string playlistId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(playlistId, nameof(playlistId));
        var countryCode = ValidateCountryCode(settings.CountryCode);
        var relationshipUri = BuildUri(
            $"playlists/{Uri.EscapeDataString(playlistId)}/relationships/items",
            ("countryCode", countryCode));
        var identifiers = await ReadRelationshipPagesAsync(
            settings, relationshipUri, "TIDAL playlist tracks", TidalScopes.PlaylistResources, cancellationToken);
        var tracks = identifiers.Where(identifier => string.Equals(identifier.Type, "tracks", StringComparison.Ordinal)).ToList();
        return await HydrateTracksAsync(settings, tracks, countryCode, cancellationToken, includePlaylistPositions: true);
    }

    private async Task<IReadOnlyList<TidalResourceIdentifier>> ReadSearchIdentifiersAsync(
        TidalSettings settings,
        JsonElement searchResource,
        string escapedQuery,
        string relationshipName,
        string countryCode,
        int limit,
        CancellationToken cancellationToken)
    {
        if (TidalJsonApiMapper.TryReadRelationshipIdentifiers(searchResource, relationshipName, out var identifiers))
        {
            return identifiers.Take(limit).ToList();
        }

        var relationshipUri = BuildUri(
            $"searchResults/{escapedQuery}/relationships/{relationshipName}",
            ("countryCode", countryCode),
            ("include", relationshipName));
        return (await ReadRelationshipPagesAsync(
            settings,
            relationshipUri,
            $"TIDAL search {relationshipName}",
            TidalScopes.CatalogSearch,
            cancellationToken)).Take(limit).ToList();
    }

    private async Task<IReadOnlyList<TidalResourceIdentifier>> ReadRelationshipPagesAsync(
        TidalSettings settings,
        Uri firstUri,
        string operationName,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var identifiers = new List<TidalResourceIdentifier>();
        Uri? pageUri = firstUri;
        while (pageUri is not null)
        {
            using var response = await SendAsync(settings, pageUri, operationName, requiredScopes, cancellationToken);
            using var document = await ParseDocumentAsync(response, pageUri, operationName, cancellationToken);
            identifiers.AddRange(TidalJsonApiMapper.ReadDocumentIdentifiers(document.RootElement));
            pageUri = ResolveNextUri(TidalJsonApiMapper.ReadNextLink(document.RootElement));
        }

        return identifiers;
    }

    private async Task<IReadOnlyList<TidalTrackMetadata>> HydrateTracksAsync(
        TidalSettings settings,
        IReadOnlyList<TidalResourceIdentifier> identifiers,
        string countryCode,
        CancellationToken cancellationToken,
        bool includePlaylistPositions = false)
    {
        var resources = await HydrateResourcesAsync(
            settings, "tracks", identifiers.Select(value => value.Id).ToList(), countryCode, "artists,albums", cancellationToken);
        return identifiers.Select((identifier, position) =>
                resources.TryGetValue(identifier.Id, out var value)
                    ? TidalJsonApiMapper.MapTrack(value.Resource, value.Index, includePlaylistPositions ? position + 1 : null)
                    : null)
            .Where(track => track is not null)
            .Cast<TidalTrackMetadata>()
            .ToList();
    }

    private async Task<IReadOnlyList<TidalAlbumMetadata>> HydrateAlbumsAsync(
        TidalSettings settings,
        IReadOnlyList<TidalResourceIdentifier> identifiers,
        string countryCode,
        CancellationToken cancellationToken)
    {
        var resources = await HydrateResourcesAsync(
            settings, "albums", identifiers.Select(value => value.Id).ToList(), countryCode, "artists,coverArt", cancellationToken);
        return identifiers.Select(identifier => resources.TryGetValue(identifier.Id, out var value)
                ? TidalJsonApiMapper.MapAlbum(value.Resource, value.Index)
                : null)
            .Where(album => album is not null).Cast<TidalAlbumMetadata>().ToList();
    }

    private async Task<IReadOnlyList<TidalArtistMetadata>> HydrateArtistsAsync(
        TidalSettings settings,
        IReadOnlyList<TidalResourceIdentifier> identifiers,
        string countryCode,
        CancellationToken cancellationToken)
    {
        var resources = await HydrateResourcesAsync(
            settings, "artists", identifiers.Select(value => value.Id).ToList(), countryCode, "profileArt", cancellationToken);
        return identifiers.Select(identifier => resources.TryGetValue(identifier.Id, out var value)
                ? TidalJsonApiMapper.MapArtist(value.Resource, value.Index)
                : null)
            .Where(artist => artist is not null).Cast<TidalArtistMetadata>().ToList();
    }

    private async Task<IReadOnlyList<TidalPlaylistMetadata>> HydratePlaylistsAsync(
        TidalSettings settings,
        IReadOnlyList<string> ids,
        string countryCode,
        CancellationToken cancellationToken)
    {
        var resources = await HydrateResourcesAsync(settings, "playlists", ids, countryCode, "coverArt,owners", cancellationToken);
        return ids.Select(id => resources.TryGetValue(id, out var value)
                ? TidalJsonApiMapper.MapPlaylist(value.Resource, value.Index)
                : null)
            .Where(playlist => playlist is not null).Cast<TidalPlaylistMetadata>().ToList();
    }

    private async Task<IReadOnlyDictionary<string, HydratedResource>> HydrateResourcesAsync(
        TidalSettings settings,
        string resourceType,
        IReadOnlyList<string> ids,
        string countryCode,
        string include,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, HydratedResource>(StringComparer.Ordinal);
        foreach (var batch in ids.Distinct(StringComparer.Ordinal).Chunk(HydrationBatchSize))
        {
            var uri = BuildUri(
                resourceType,
                ("filter[id]", string.Join(',', batch.Select(Uri.EscapeDataString))),
                ("countryCode", countryCode),
                ("include", include));
            var requiredScopes = string.Equals(resourceType, "playlists", StringComparison.Ordinal)
                ? TidalScopes.PlaylistResources
                : (IReadOnlyList<string>)[TidalScopes.UserRead];
            using var response = await SendAsync(
                settings, uri, $"TIDAL {resourceType} hydration", requiredScopes, cancellationToken);
            using var document = await ParseDocumentAsync(response, uri, $"TIDAL {resourceType} hydration", cancellationToken);
            var index = TidalJsonApiMapper.BuildResourceIndex(document.RootElement);
            foreach (var id in batch)
            {
                if (index.TryGetValue(new TidalResourceKey(resourceType, id), out var resource))
                {
                    result[id] = new HydratedResource(resource.Clone(), CloneIndex(index));
                }
            }
        }

        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(
        TidalSettings settings,
        Uri requestUri,
        string operationName,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var token = await GetValidTokenAsync(settings, operationName, requiredScopes, cancellationToken);
        var refreshedAfterUnauthorized = false;
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = CreateRequest(requestUri, token.AccessToken);
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                var diagnostic = CreateDiagnostic(operationName, requestUri, token, null, null, null, null, ex.Message, null);
                _diagnosticWriter.Write(diagnostic);
                if (attempt < _maxTemporaryRetries)
                {
                    await Task.Delay(RetryDelay(attempt, null), cancellationToken);
                    continue;
                }

                throw new TidalApiException(TidalApiErrorKind.NetworkUnavailable, diagnostic.ToDisplayString(), ex)
                {
                    RequestUri = diagnostic.RequestUri,
                    Diagnostic = diagnostic
                };
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var retryAfter = ResolveRetryAfter(response);
            var error = await ReadErrorAsync(response, cancellationToken);
            var diagnosticFailure = CreateDiagnostic(
                operationName,
                requestUri,
                token,
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.MediaType,
                error.Code,
                error.Title,
                error.Detail,
                retryAfter);
            _diagnosticWriter.Write(diagnosticFailure);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedAfterUnauthorized)
            {
                response.Dispose();
                token = await _authService.RefreshAsync(token, cancellationToken);
                refreshedAfterUnauthorized = true;
                continue;
            }

            var kind = MapErrorKind(response.StatusCode);
            if (kind is TidalApiErrorKind.RateLimited or TidalApiErrorKind.TemporaryFailure
                && attempt < _maxTemporaryRetries)
            {
                response.Dispose();
                await Task.Delay(RetryDelay(attempt, retryAfter), cancellationToken);
                continue;
            }

            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new TidalApiException(kind, diagnosticFailure.ToDisplayString())
            {
                RetryAfter = retryAfter,
                StatusCode = statusCode,
                RequestUri = diagnosticFailure.RequestUri,
                Diagnostic = diagnosticFailure
            };
        }
    }

    private async Task<TidalTokenSet> GetValidTokenAsync(
        TidalSettings settings,
        string operationName,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        EnsureEnabled(settings);
        var token = await _tokenStore.GetAsync(cancellationToken)
            ?? throw new TidalApiException(TidalApiErrorKind.Unauthorized, "TIDAL is not authorized.");
        if (token.IsExpired)
        {
            token = await _authService.RefreshAsync(token, cancellationToken);
        }

        var missingScopes = TidalScopes.Missing(token.Scope, requiredScopes);
        if (missingScopes.Count > 0)
        {
            var missing = string.Join(", ", missingScopes);
            throw new TidalApiException(
                TidalApiErrorKind.MissingRequiredScope,
                $"{operationName} requires the missing scope: {missing}. {TidalCatalogCapabilities.ReconnectForScopesMessage}");
        }

        return token;
    }

    private async Task<JsonDocument> ParseDocumentAsync(
        HttpResponseMessage response,
        Uri requestUri,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            var token = await _tokenStore.GetAsync(cancellationToken);
            var diagnostic = CreateDiagnostic(
                operationName,
                requestUri,
                token,
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.MediaType,
                "malformed_json",
                "Malformed JSON:API response",
                ex.Message,
                null);
            _diagnosticWriter.Write(diagnostic);
            throw new TidalApiException(TidalApiErrorKind.MalformedResponse, diagnostic.ToDisplayString(), ex)
            {
                StatusCode = (int)response.StatusCode,
                RequestUri = diagnostic.RequestUri,
                Diagnostic = diagnostic
            };
        }
    }

    private static HttpRequestMessage CreateRequest(Uri requestUri, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri)
        {
            Content = new ByteArrayContent([])
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(MediaType);
        return request;
    }

    private static async Task<TidalErrorFields> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return new TidalErrorFields(null, null, null);
            }

            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Array
                || errors.GetArrayLength() == 0)
            {
                return new TidalErrorFields(null, null, null);
            }

            var error = errors[0];
            return new TidalErrorFields(ReadString(error, "code"), ReadString(error, "title"), ReadString(error, "detail"));
        }
        catch (JsonException)
        {
            return new TidalErrorFields(null, null, null);
        }
    }

    private static TidalRequestDiagnostic CreateDiagnostic(
        string operationName,
        Uri requestUri,
        TidalTokenSet? token,
        int? statusCode,
        string? contentType,
        string? errorCode,
        string? errorTitle,
        string? errorDetail,
        TimeSpan? retryAfter) => new()
    {
        OperationName = operationName,
        Method = HttpMethod.Get.Method,
        RequestUri = TidalRequestDiagnosticWriter.SanitizeUri(requestUri),
        StatusCode = statusCode,
        ContentType = contentType,
        ErrorCode = errorCode,
        ErrorTitle = errorTitle,
        ErrorDetail = errorDetail,
        RetryAfter = retryAfter,
        GrantedScopes = token?.Scope ?? string.Empty
    };

    private static Uri BuildUri(string relativePath, params (string Name, string? Value)[] query)
    {
        var queryString = string.Join('&', query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{item.Name}={EscapeQueryValue(item.Value!)}"));
        return new Uri(ApiBaseUri, string.IsNullOrEmpty(queryString) ? relativePath : $"{relativePath}?{queryString}");
    }

    private static string EscapeQueryValue(string value) =>
        Uri.EscapeDataString(value).Replace("%2C", ",", StringComparison.OrdinalIgnoreCase);

    private static Uri? ResolveNextUri(string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return null;
        }

        var uri = Uri.TryCreate(next, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(ApiBaseUri, next.TrimStart('/'));
        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, ApiBaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(ApiBaseUri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new TidalApiException(TidalApiErrorKind.MalformedResponse, "TIDAL returned an unsafe pagination link.");
        }

        return uri;
    }

    private static IReadOnlyDictionary<TidalResourceKey, JsonElement> CloneIndex(
        IReadOnlyDictionary<TidalResourceKey, JsonElement> source) =>
        source.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());

    private static string ValidateCountryCode(string countryCode)
    {
        var normalized = countryCode.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new TidalApiException(TidalApiErrorKind.InvalidRequest, "TIDAL Country Code must be a two-letter uppercase code.");
        }

        return normalized;
    }

    private static void EnsureEnabled(TidalSettings settings)
    {
        if (!settings.ExperimentalCatalogEnabled)
        {
            throw new TidalApiException(TidalApiErrorKind.InvalidRequest, "Experimental TIDAL catalog access is disabled.");
        }
    }

    private static void EnsureIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A TIDAL provider identifier is required.", parameterName);
        }
    }

    private static TidalApiErrorKind MapErrorKind(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => TidalApiErrorKind.Unauthorized,
        HttpStatusCode.Forbidden => TidalApiErrorKind.Forbidden,
        HttpStatusCode.NotFound => TidalApiErrorKind.NotFound,
        HttpStatusCode.BadRequest => TidalApiErrorKind.InvalidRequest,
        (HttpStatusCode)429 => TidalApiErrorKind.RateLimited,
        >= HttpStatusCode.InternalServerError => TidalApiErrorKind.TemporaryFailure,
        _ => TidalApiErrorKind.Unknown
    };

    private static TimeSpan? ResolveRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private static TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } specified)
        {
            return specified > MaxRetryDelay ? MaxRetryDelay : specified;
        }

        var delay = TimeSpan.FromMilliseconds(BaseRetryDelay.TotalMilliseconds * Math.Pow(2, attempt));
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record HydratedResource(
        JsonElement Resource,
        IReadOnlyDictionary<TidalResourceKey, JsonElement> Index);

    private sealed record TidalErrorFields(string? Code, string? Title, string? Detail);
}
