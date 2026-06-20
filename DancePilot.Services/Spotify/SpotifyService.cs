using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DancePilot.Core.Spotify;
using DancePilot.Services.Spotify.Auth;

namespace DancePilot.Services.Spotify;

public sealed class SpotifyService
{
    private static readonly Uri ApiBaseUri = new("https://api.spotify.com/v1/");
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(8);
    private const int MaxSearchLimit = 10;
    private const int MaxRateLimitRetries = 3;
    private readonly HttpClient _httpClient;
    private readonly ISpotifyTokenStore _tokenStore;
    private readonly SpotifyAuthService _authService;

    public SpotifyService(HttpClient httpClient, ISpotifyTokenStore tokenStore, SpotifyAuthService authService)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
        _authService = authService;
    }

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) =>
        await _tokenStore.GetAsync(cancellationToken) is not null;

    public async Task<IReadOnlyList<string>> GetMissingScopesAsync(
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenStore.GetAsync(cancellationToken);
        return token is null
            ? requiredScopes
            : FindMissingScopes(token.Scope, requiredScopes);
    }

    public Task<SpotifyTokenSet> LoginAsync(SpotifySettings settings, CancellationToken cancellationToken = default)
    {
        var scopes = settings.IncludePlaybackControlScopes
            ? SpotifyScopes.PlaybackControl
            : SpotifyScopes.PlanningMetadata;

        return _authService.LoginAsync(settings, scopes, cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        _authService.LogoutAsync(cancellationToken);

    public async Task<SpotifyUserProfile> GetCurrentUserProfileAsync(
        SpotifySettings settings,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            settings,
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBaseUri, "me")),
            [SpotifyScopes.UserReadPrivate],
            cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return new SpotifyUserProfile
        {
            Id = root.GetProperty("id").GetString() ?? string.Empty,
            DisplayName = root.TryGetProperty("display_name", out var displayName)
                ? displayName.GetString() ?? "Spotify User"
                : "Spotify User",
            Country = root.TryGetProperty("country", out var country) ? country.GetString() : null,
            Product = root.TryGetProperty("product", out var product) ? product.GetString() : null
        };
    }

    public async Task<IReadOnlyList<SpotifyPlaylistSummary>> GetUserPlaylistsAsync(
        SpotifySettings settings,
        CancellationToken cancellationToken = default)
    {
        var playlists = new List<SpotifyPlaylistSummary>();
        var url = new Uri(ApiBaseUri, "me/playlists?limit=50");

        while (url is not null)
        {
            using var response = await SendAsync(
                settings,
                () => new HttpRequestMessage(HttpMethod.Get, url),
                [SpotifyScopes.PlaylistReadPrivate, SpotifyScopes.PlaylistReadCollaborative],
                cancellationToken);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                playlists.Add(ReadPlaylist(item));
            }

            url = root.TryGetProperty("next", out var next) && next.ValueKind != JsonValueKind.Null
                ? new Uri(next.GetString()!)
                : null;
        }

        return playlists;
    }

    public async Task<IReadOnlyList<SpotifyTrackMetadata>> GetPlaylistTracksAsync(
        SpotifySettings settings,
        string playlistId,
        CancellationToken cancellationToken = default)
    {
        var tracks = new List<SpotifyTrackMetadata>();
        var fields = Uri.EscapeDataString("items(added_at,is_local,item(id,name,type,is_local,is_playable,artists(name),album(name),duration_ms,uri,external_urls,popularity),track(id,name,type,is_local,is_playable,artists(name),album(name),duration_ms,uri,external_urls,popularity)),next,total");
        var url = new Uri(ApiBaseUri, $"playlists/{Uri.EscapeDataString(playlistId)}/items?limit=50&fields={fields}");

        while (url is not null)
        {
            using var response = await SendAsync(
                settings,
                () => new HttpRequestMessage(HttpMethod.Get, url),
                [SpotifyScopes.PlaylistReadPrivate, SpotifyScopes.PlaylistReadCollaborative],
                cancellationToken);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var track = ReadPlaylistTrack(item);
                if (track is not null)
                {
                    tracks.Add(track);
                }
            }

            url = root.TryGetProperty("next", out var next) && next.ValueKind != JsonValueKind.Null
                ? new Uri(next.GetString()!)
                : null;
        }

        return tracks;
    }

    public async Task<IReadOnlyList<SpotifyTrackMetadata>> SearchTracksAsync(
        SpotifySettings settings,
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, MaxSearchLimit);
        var url = new Uri(ApiBaseUri, $"search?q={Uri.EscapeDataString(query)}&type=track&limit={safeLimit}");
        using var response = await SendAsync(
            settings,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            SpotifyScopes.PlanningMetadata,
            cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement
            .GetProperty("tracks")
            .GetProperty("items")
            .EnumerateArray()
            .Select(ReadTrack)
            .ToList();
    }

    public async Task<SpotifyTrackMetadata> GetTrackAsync(
        SpotifySettings settings,
        string spotifyTrackId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            settings,
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBaseUri, $"tracks/{Uri.EscapeDataString(spotifyTrackId)}")),
            SpotifyScopes.PlanningMetadata,
            cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadTrack(document.RootElement);
    }

    internal async Task<HttpResponseMessage> SendAsync(
        SpotifySettings settings,
        Func<HttpRequestMessage> requestFactory,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var token = await GetUsableTokenAsync(settings, requiredScopes, cancellationToken);
        for (var attempt = 0; ; attempt++)
        {
            var response = await SendWithTokenAsync(requestFactory, token.AccessToken, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                token = await _authService.RefreshAsync(settings.ClientId.Trim(), token, cancellationToken);
                response = await SendWithTokenAsync(requestFactory, token.AccessToken, cancellationToken);
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var retryAfter = ResolveRetryAfter(response);
            var requestUri = response.RequestMessage?.RequestUri?.ToString();
            var kind = ResolveErrorKind(response.StatusCode, errorBody, requestUri);

            if (kind == SpotifyApiErrorKind.RateLimited && attempt < MaxRateLimitRetries)
            {
                response.Dispose();
                await Task.Delay(ResolveRetryDelay(retryAfter, attempt), cancellationToken);
                continue;
            }

            response.Dispose();
            throw new SpotifyApiException(kind, $"Spotify API request failed ({kind}): {errorBody}")
            {
                RetryAfter = retryAfter,
                ErrorBody = errorBody,
                StatusCode = (int)response.StatusCode,
                RequestUri = requestUri
            };
        }
    }

    private static SpotifyApiErrorKind ResolveErrorKind(HttpStatusCode statusCode, string errorBody, string? requestUri)
    {
        if (statusCode == (HttpStatusCode)429)
        {
            return SpotifyApiErrorKind.RateLimited;
        }

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return SpotifyApiErrorKind.ExpiredToken;
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            if (errorBody.Contains("premium", StringComparison.OrdinalIgnoreCase))
            {
                return SpotifyApiErrorKind.NoPremiumAccount;
            }

            if (errorBody.Contains("scope", StringComparison.OrdinalIgnoreCase))
            {
                return SpotifyApiErrorKind.MissingScopes;
            }

            return requestUri is not null
                && requestUri.Contains("/playlists/", StringComparison.OrdinalIgnoreCase)
                    ? SpotifyApiErrorKind.PlaylistAccessForbidden
                    : SpotifyApiErrorKind.PlaybackForbidden;
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            if (errorBody.Contains("device", StringComparison.OrdinalIgnoreCase))
            {
                return SpotifyApiErrorKind.NoActiveDevice;
            }

            return SpotifyApiErrorKind.UnavailableTrack;
        }

        if (statusCode == HttpStatusCode.BadRequest
            && errorBody.Contains("device", StringComparison.OrdinalIgnoreCase))
        {
            return SpotifyApiErrorKind.DeviceUnavailable;
        }

        return SpotifyApiErrorKind.Unknown;
    }

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

    private static TimeSpan ResolveRetryDelay(TimeSpan? retryAfter, int attempt)
    {
        if (retryAfter is { } retryAfterDelay)
        {
            return retryAfterDelay;
        }

        var multiplier = Math.Pow(2, Math.Max(0, attempt));
        var delay = TimeSpan.FromMilliseconds(BaseRetryDelay.TotalMilliseconds * multiplier);
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private async Task<SpotifyTokenSet> GetUsableTokenAsync(
        SpotifySettings settings,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var token = await _tokenStore.GetAsync(cancellationToken)
            ?? throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Spotify is not connected.");

        if (token.IsExpired)
        {
            token = await _authService.RefreshAsync(settings.ClientId.Trim(), token, cancellationToken);
        }

        EnsureScopes(token, requiredScopes);
        return token;
    }

    private static void EnsureScopes(SpotifyTokenSet token, IReadOnlyList<string> requiredScopes)
    {
        var missingScopes = FindMissingScopes(token.Scope, requiredScopes);
        if (missingScopes.Count > 0)
        {
            throw new SpotifyApiException(
                SpotifyApiErrorKind.MissingScopes,
                $"Spotify connection is missing required scope(s): {string.Join(", ", missingScopes)}.");
        }
    }

    private static IReadOnlyList<string> FindMissingScopes(string tokenScope, IReadOnlyList<string> requiredScopes)
    {
        var granted = tokenScope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requiredScopes.Where(scope => !granted.Contains(scope)).ToList();
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(
        Func<HttpRequestMessage> requestFactory,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.NetworkOffline, "Spotify is unreachable. Check your network connection.", ex);
        }
    }

    private static SpotifyPlaylistSummary ReadPlaylist(JsonElement item)
    {
        return new SpotifyPlaylistSummary
        {
            SpotifyPlaylistId = item.GetProperty("id").GetString() ?? string.Empty,
            Name = item.GetProperty("name").GetString() ?? "Untitled Playlist",
            OwnerName = item.TryGetProperty("owner", out var owner) && owner.TryGetProperty("display_name", out var displayName)
                ? displayName.GetString()
                : null,
            TrackCount = ReadPlaylistTrackCount(item),
            SnapshotId = item.TryGetProperty("snapshot_id", out var snapshotId) ? snapshotId.GetString() : null,
            ExternalUrl = ReadSpotifyExternalUrl(item)
        };
    }

    private static SpotifyTrackMetadata? ReadPlaylistTrack(JsonElement item)
    {
        var addedAt = item.TryGetProperty("added_at", out var addedAtElement)
            && DateTimeOffset.TryParse(addedAtElement.GetString(), out var parsed)
                ? parsed
                : (DateTimeOffset?)null;

        if (!TryGetPlaylistTrackItem(item, out var track)
            || track.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || (track.TryGetProperty("type", out var type) && type.GetString() != "track"))
        {
            return null;
        }

        var metadata = ReadTrack(track);
        var isLocal = (item.TryGetProperty("is_local", out var rowIsLocalElement) && rowIsLocalElement.ValueKind == JsonValueKind.True)
            || (track.TryGetProperty("is_local", out var isLocalElement) && isLocalElement.ValueKind == JsonValueKind.True);
        return metadata with
        {
            IsUnavailable = metadata.IsUnavailable
                || isLocal
                || (track.TryGetProperty("is_playable", out var isPlayable) && isPlayable.ValueKind == JsonValueKind.False),
            AddedAt = addedAt
        };
    }

    private static int ReadPlaylistTrackCount(JsonElement item)
    {
        if (item.TryGetProperty("tracks", out var tracks)
            && tracks.TryGetProperty("total", out var tracksTotal)
            && tracksTotal.TryGetInt32(out var legacyTotal))
        {
            return legacyTotal;
        }

        if (item.TryGetProperty("items", out var items)
            && items.TryGetProperty("total", out var itemsTotal)
            && itemsTotal.TryGetInt32(out var currentTotal))
        {
            return currentTotal;
        }

        return 0;
    }

    private static bool TryGetPlaylistTrackItem(JsonElement item, out JsonElement track)
    {
        if (item.TryGetProperty("track", out track))
        {
            return true;
        }

        if (item.TryGetProperty("item", out track))
        {
            return true;
        }

        track = default;
        return false;
    }

    private static SpotifyTrackMetadata ReadTrack(JsonElement track)
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
            AlbumArtUrl = ReadAlbumArtUrl(track),
            DurationMs = track.TryGetProperty("duration_ms", out var duration) ? duration.GetInt32() : 0,
            SpotifyUri = track.TryGetProperty("uri", out var uri) ? uri.GetString() : null,
            ExternalUrl = ReadSpotifyExternalUrl(track),
            Popularity = track.TryGetProperty("popularity", out var popularity) ? popularity.GetInt32() : null,
            IsUnavailable = !track.TryGetProperty("id", out var trackId) || string.IsNullOrWhiteSpace(trackId.GetString())
        };
    }

    private static string? ReadAlbumArtUrl(JsonElement track)
    {
        if (!track.TryGetProperty("album", out var album)
            || !album.TryGetProperty("images", out var images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return images.EnumerateArray()
            .Select(image => new
            {
                Width = image.TryGetProperty("width", out var width) && width.TryGetInt32(out var value) ? value : 0,
                Url = image.TryGetProperty("url", out var url) ? url.GetString() : null
            })
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .OrderByDescending(image => image.Width)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static string? ReadSpotifyExternalUrl(JsonElement item)
    {
        return item.TryGetProperty("external_urls", out var urls)
            && urls.TryGetProperty("spotify", out var spotifyUrl)
                ? spotifyUrl.GetString()
                : null;
    }
}
