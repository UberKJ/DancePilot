using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DancePilot.Services.Spotify.Auth;

public sealed class SpotifyAuthService
{
    private static readonly Uri TokenEndpoint = new("https://accounts.spotify.com/api/token");
    private readonly HttpClient _httpClient;
    private readonly ISpotifyTokenStore _tokenStore;

    public SpotifyAuthService(HttpClient httpClient, ISpotifyTokenStore tokenStore)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
    }

    public async Task<SpotifyTokenSet> LoginAsync(
        SpotifySettings settings,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Spotify Client ID is required.");
        }

        var redirectUri = ValidateRedirectUri(settings.RedirectUri);
        var state = SpotifyAuthPkce.GenerateCodeVerifier(48);
        var verifier = SpotifyAuthPkce.GenerateCodeVerifier();
        var challenge = SpotifyAuthPkce.CreateCodeChallenge(verifier);
        var authUri = SpotifyAuthPkce.BuildAuthorizationUri(
            settings.ClientId.Trim(),
            redirectUri.ToString(),
            state,
            scopes,
            challenge);

        using var listener = new HttpListener();
        listener.Prefixes.Add(BuildListenerPrefix(redirectUri));
        listener.Start();

        Process.Start(new ProcessStartInfo
        {
            FileName = authUri.ToString(),
            UseShellExecute = true
        });

        var callback = await listener.GetContextAsync().WaitAsync(cancellationToken);
        var query = ParseQuery(callback.Request.Url?.Query ?? string.Empty);

        await WriteCallbackResponseAsync(callback, cancellationToken);
        listener.Stop();

        if (query.TryGetValue("error", out var error))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, $"Spotify login failed: {error}");
        }

        if (!query.TryGetValue("state", out var returnedState) || returnedState != state)
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Spotify login failed state validation.");
        }

        if (!query.TryGetValue("code", out var code))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Spotify login did not return an authorization code.");
        }

        var tokenSet = await ExchangeCodeForTokenAsync(settings.ClientId.Trim(), redirectUri.ToString(), code, verifier, cancellationToken);
        await _tokenStore.SaveAsync(tokenSet, cancellationToken);
        return tokenSet;
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        _tokenStore.ClearAsync(cancellationToken);

    public async Task<SpotifyTokenSet> RefreshAsync(
        string clientId,
        SpotifyTokenSet tokenSet,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokenSet.RefreshToken,
            ["client_id"] = clientId
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(body)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.RefreshTokenFailed, "Spotify refresh token failed.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var refreshed = ReadTokenSet(document.RootElement, tokenSet.RefreshToken);
        if (string.IsNullOrWhiteSpace(refreshed.Scope))
        {
            refreshed = refreshed with { Scope = tokenSet.Scope };
        }
        await _tokenStore.SaveAsync(refreshed, cancellationToken);
        return refreshed;
    }

    private async Task<SpotifyTokenSet> ExchangeCodeForTokenAsync(
        string clientId,
        string redirectUri,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(body)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, $"Spotify token exchange failed: {bodyText}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadTokenSet(document.RootElement, fallbackRefreshToken: null);
    }

    private static SpotifyTokenSet ReadTokenSet(JsonElement root, string? fallbackRefreshToken)
    {
        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Spotify did not return an access token.");
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString()
            : fallbackRefreshToken;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var scope = root.TryGetProperty("scope", out var scopeElement)
            ? scopeElement.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.RefreshTokenFailed, "Spotify did not return a refresh token.");
        }

        return new SpotifyTokenSet
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 60)),
            Scope = scope
        };
    }

    private static async Task WriteCallbackResponseAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        const string html = """
            <!doctype html>
            <html>
            <body style="font-family:Segoe UI,sans-serif;background:#0b1015;color:#f5f8fa">
                <h1>DancePilot Spotify login complete</h1>
                <p>You can close this browser tab and return to DancePilot.</p>
            </body>
            </html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    private static Uri ValidateRedirectUri(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Spotify Redirect URI must be an absolute URI.");
        }

        if (uri.Host.Contains('*', StringComparison.Ordinal))
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Spotify Redirect URI cannot contain wildcards.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            throw new SpotifyApiException(
                SpotifyApiErrorKind.InvalidRequest,
                "This desktop build uses a local loopback callback. Use http://127.0.0.1:8888/callback in Spotify Developer settings.");
        }

        if (uri.Scheme == Uri.UriSchemeHttp
            && string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)
            && uri.AbsolutePath == "/callback")
        {
            return uri;
        }

        throw new SpotifyApiException(
            SpotifyApiErrorKind.InvalidRequest,
            "Spotify Redirect URI must be HTTPS, or http://127.0.0.1:8888/callback for local desktop development. Do not use http://localhost.");
    }

    private static string BuildListenerPrefix(Uri redirectUri)
    {
        var port = redirectUri.IsDefaultPort ? 80 : redirectUri.Port;
        return $"http://127.0.0.1:{port}/";
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')));
    }
}
