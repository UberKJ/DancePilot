using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DancePilot.Services.Tidal.Auth;

public sealed class TidalAuthService
{
    private static readonly Uri TokenEndpoint = new("https://auth.tidal.com/v1/oauth2/token");
    private static readonly TimeSpan DefaultAuthorizationTimeout = TimeSpan.FromMinutes(3);
    private readonly HttpClient _httpClient;
    private readonly ITidalTokenStore _tokenStore;
    private readonly TimeSpan _authorizationTimeout;

    public TidalAuthService(
        HttpClient httpClient,
        ITidalTokenStore tokenStore,
        TimeSpan? authorizationTimeout = null)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
        _authorizationTimeout = authorizationTimeout ?? DefaultAuthorizationTimeout;
        if (_authorizationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(authorizationTimeout), "TIDAL authorization timeout must be greater than zero.");
        }
    }

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) =>
        await _tokenStore.GetAsync(cancellationToken) is not null;

    public async Task<TidalTokenSet> LoginAsync(
        TidalSettings settings,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default)
    {
        EnsureExperimentalCatalogEnabled(settings);
        var clientId = ValidateClientId(settings.ClientId);
        var configuredRedirectUri = settings.RedirectUri.Trim();
        var redirectUri = ParseLoopbackRedirectUri(configuredRedirectUri);
        var state = TidalPkce.GenerateState();
        var verifier = TidalPkce.GenerateCodeVerifier();
        var challenge = TidalPkce.CreateCodeChallenge(verifier);
        var authorizationUri = TidalPkce.BuildAuthorizationUri(
            clientId,
            configuredRedirectUri,
            state,
            scopes,
            challenge);

        using var listener = new HttpListener();
        listener.Prefixes.Add(BuildListenerPrefix(redirectUri));
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new TidalApiException(
                TidalApiErrorKind.RedirectPortConflict,
                $"TIDAL callback port {redirectUri.Port} is already occupied or unavailable. If the port changes, update both the TIDAL Developer Dashboard and DancePilot to the same exact redirect URI.",
                ex);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authorizationUri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new TidalApiException(
                TidalApiErrorKind.AuthorizationFailed,
                "TIDAL authorization could not open the system browser.",
                ex);
        }

        HttpListenerContext callback;
        using var timeoutCancellation = new CancellationTokenSource(_authorizationTimeout);
        using var callbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            callback = await listener.GetContextAsync().WaitAsync(callbackCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
            if (cancellationToken.IsCancellationRequested)
            {
                throw new TidalApiException(TidalApiErrorKind.UserCancelled, "TIDAL authorization was cancelled.");
            }

            throw new TidalApiException(
                TidalApiErrorKind.AuthorizationTimedOut,
                $"TIDAL authorization timed out after {Math.Ceiling(_authorizationTimeout.TotalMinutes):N0} minute(s). Try Connect again.");
        }

        var callbackUri = callback.Request.Url;
        var query = ParseQuery(callbackUri?.Query ?? string.Empty);
        string code;
        try
        {
            code = ValidateAuthorizationCallback(callbackUri, redirectUri, state, query);
            await WriteCallbackResponseAsync(callback, succeeded: true, CancellationToken.None);
        }
        catch (TidalApiException)
        {
            await WriteCallbackResponseAsync(callback, succeeded: false, CancellationToken.None);
            throw;
        }
        finally
        {
            listener.Stop();
        }

        return await ExchangeAuthorizationCodeAsync(
            clientId,
            configuredRedirectUri,
            code,
            verifier,
            scopes,
            cancellationToken);
    }

    public async Task<TidalTokenSet> ExchangeAuthorizationCodeAsync(
        string clientId,
        string redirectUri,
        string authorizationCode,
        string codeVerifier,
        IReadOnlyList<string>? requestedScopes = null,
        CancellationToken cancellationToken = default)
    {
        var exactRedirectUri = redirectUri.Trim();
        ParseLoopbackRedirectUri(exactRedirectUri);
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ValidateClientId(clientId),
            ["code"] = authorizationCode,
            ["redirect_uri"] = exactRedirectUri,
            ["code_verifier"] = codeVerifier
        };

        var tokenSet = await SendTokenRequestAsync(
            body,
            fallbackToken: null,
            requestedScopes ?? [],
            cancellationToken);
        await _tokenStore.SaveAsync(tokenSet, cancellationToken);
        return tokenSet;
    }

    public async Task<TidalTokenSet> RefreshAsync(
        TidalTokenSet tokenSet,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenSet.RefreshToken))
        {
            throw new TidalApiException(TidalApiErrorKind.ExpiredToken, "TIDAL access expired and no refresh token is available.");
        }

        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokenSet.RefreshToken
        };

        try
        {
            var refreshed = await SendTokenRequestAsync(body, tokenSet, tokenSet.RequestedScopes, cancellationToken);
            if (string.IsNullOrWhiteSpace(refreshed.Scope))
            {
                refreshed = refreshed with { Scope = tokenSet.Scope };
            }

            await _tokenStore.SaveAsync(refreshed, cancellationToken);
            return refreshed;
        }
        catch (TidalApiException ex) when (ex.Kind is TidalApiErrorKind.Unauthorized or TidalApiErrorKind.AuthorizationFailed)
        {
            throw new TidalApiException(TidalApiErrorKind.ExpiredToken, "TIDAL access expired and the refresh token was rejected.", ex);
        }
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        _tokenStore.ClearAsync(cancellationToken);

    private async Task<TidalTokenSet> SendTokenRequestAsync(
        IReadOnlyDictionary<string, string> body,
        TidalTokenSet? fallbackToken,
        IReadOnlyList<string> requestedScopes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(body)
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TidalApiException(TidalApiErrorKind.NetworkUnavailable, "TIDAL authorization is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var kind = response.StatusCode == HttpStatusCode.Unauthorized
                    ? TidalApiErrorKind.Unauthorized
                    : TidalApiErrorKind.AuthorizationFailed;
                throw new TidalApiException(kind, $"TIDAL token request failed with HTTP {(int)response.StatusCode}.")
                {
                    StatusCode = (int)response.StatusCode,
                    RequestUri = TokenEndpoint.AbsoluteUri
                };
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return ReadTokenSet(document.RootElement, fallbackToken, requestedScopes);
            }
            catch (JsonException ex)
            {
                throw new TidalApiException(TidalApiErrorKind.MalformedResponse, "TIDAL returned a malformed token response.", ex);
            }
        }
    }

    private static TidalTokenSet ReadTokenSet(
        JsonElement root,
        TidalTokenSet? fallbackToken,
        IReadOnlyList<string> requestedScopes)
    {
        if (!root.TryGetProperty("access_token", out var accessTokenElement)
            || string.IsNullOrWhiteSpace(accessTokenElement.GetString())
            || !root.TryGetProperty("expires_in", out var expiresInElement)
            || !expiresInElement.TryGetInt32(out var expiresIn))
        {
            throw new TidalApiException(TidalApiErrorKind.MalformedResponse, "TIDAL token response is missing required fields.");
        }

        var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString()
            : fallbackToken?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new TidalApiException(TidalApiErrorKind.MalformedResponse, "TIDAL token response did not include a refresh token.");
        }

        return new TidalTokenSet
        {
            AccessToken = accessTokenElement.GetString()!,
            RefreshToken = refreshToken,
            TokenType = root.TryGetProperty("token_type", out var tokenType) ? tokenType.GetString() ?? "Bearer" : "Bearer",
            Scope = root.TryGetProperty("scope", out var scope) ? scope.GetString() ?? fallbackToken?.Scope ?? string.Empty : fallbackToken?.Scope ?? string.Empty,
            RequestedScopes = requestedScopes.Count > 0 ? requestedScopes.ToList() : fallbackToken?.RequestedScopes ?? [],
            UserId = root.TryGetProperty("user_id", out var userId)
                ? userId.ValueKind == JsonValueKind.String ? userId.GetString() : userId.GetRawText()
                : fallbackToken?.UserId,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 60))
        };
    }

    private static string ValidateClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new TidalApiException(TidalApiErrorKind.InvalidRequest, "TIDAL Client ID is required.");
        }

        return clientId.Trim();
    }

    public static Uri ParseLoopbackRedirectUri(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !IsLoopbackHost(uri.Host)
            || uri.IsDefaultPort
            || string.IsNullOrWhiteSpace(uri.AbsolutePath)
            || uri.AbsolutePath == "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new TidalApiException(
                TidalApiErrorKind.InvalidRequest,
                $"Use an exact HTTP loopback URI with a fixed port, such as {TidalDefaults.RedirectUri}, and register the same URI in the TIDAL Developer Dashboard.");
        }

        return uri;
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static string BuildListenerPrefix(Uri redirectUri) =>
        new UriBuilder(Uri.UriSchemeHttp, redirectUri.Host, redirectUri.Port, "/").Uri.AbsoluteUri;

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')),
                StringComparer.Ordinal);

    public static string ValidateAuthorizationCallback(
        Uri? callbackUri,
        Uri redirectUri,
        string expectedState,
        IReadOnlyDictionary<string, string> query)
    {
        if (callbackUri is null
            || !string.Equals(callbackUri.AbsolutePath, redirectUri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new TidalApiException(TidalApiErrorKind.AuthorizationFailed, "TIDAL returned an unexpected callback path.");
        }

        TidalPkce.ValidateState(expectedState, query.GetValueOrDefault("state"));

        if (query.TryGetValue("error", out var error))
        {
            var kind = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                ? TidalApiErrorKind.UserCancelled
                : TidalApiErrorKind.AuthorizationFailed;
            throw new TidalApiException(kind, "TIDAL authorization was not completed.");
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new TidalApiException(TidalApiErrorKind.AuthorizationFailed, "TIDAL did not return an authorization code.");
        }

        return code;
    }

    public static string CreateCallbackResponseHtml(bool succeeded) => succeeded
        ? """
            <!doctype html>
            <html>
            <body style="font-family:Segoe UI,sans-serif;background:#0b1015;color:#f5f8fa">
                <h1>DancePilot TIDAL authorization complete</h1>
                <p>You can close this browser tab and return to DancePilot.</p>
            </body>
            </html>
            """
        : """
            <!doctype html>
            <html>
            <body style="font-family:Segoe UI,sans-serif;background:#0b1015;color:#f5f8fa">
                <h1>DancePilot TIDAL authorization was not completed</h1>
                <p>Return to DancePilot for details, then try Connect again.</p>
            </body>
            </html>
            """;

    private static async Task WriteCallbackResponseAsync(
        HttpListenerContext context,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var html = CreateCallbackResponseHtml(succeeded);
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    private static void EnsureExperimentalCatalogEnabled(TidalSettings settings)
    {
        if (!settings.ExperimentalCatalogEnabled)
        {
            throw new TidalApiException(TidalApiErrorKind.InvalidRequest, "Experimental TIDAL catalog access is disabled.");
        }
    }
}
