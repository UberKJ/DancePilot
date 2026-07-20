using System.Net;
using System.Text;
using DancePilot.Services.Tidal;
using DancePilot.Services.Tidal.Auth;

namespace DancePilot.Tests;

public sealed class TidalAuthServiceTests
{
    [Fact]
    public async Task ConnectionState_TracksTokenStorageAndLogout()
    {
        var store = new InMemoryTidalTokenStore();
        var service = new TidalAuthService(new HttpClient(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))), store);

        Assert.False(await service.IsConnectedAsync());

        await store.SaveAsync(CreateToken(DateTimeOffset.UtcNow.AddHours(1)));
        Assert.True(await service.IsConnectedAsync());

        await service.LogoutAsync();
        Assert.False(await service.IsConnectedAsync());
    }

    [Fact]
    public async Task Login_RejectsDisabledFeatureBeforeOpeningBrowser()
    {
        var service = new TidalAuthService(
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not run."))),
            new InMemoryTidalTokenStore());

        var exception = await Assert.ThrowsAsync<TidalApiException>(() =>
            service.LoginAsync(new TidalSettings(), []));

        Assert.Equal(TidalApiErrorKind.InvalidRequest, exception.Kind);
        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_RejectsMissingClientIdBeforeOpeningBrowser()
    {
        var service = new TidalAuthService(
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not run."))),
            new InMemoryTidalTokenStore());
        var settings = new TidalSettings
        {
            ExperimentalCatalogEnabled = true,
            RedirectUri = TidalDefaults.RedirectUri
        };

        var exception = await Assert.ThrowsAsync<TidalApiException>(() => service.LoginAsync(settings, []));

        Assert.Equal(TidalApiErrorKind.InvalidRequest, exception.Kind);
        Assert.Contains("Client ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_SendsPkceTokenBody()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return TokenResponse("new-refresh-token");
        });
        var store = new InMemoryTidalTokenStore();
        var service = new TidalAuthService(new HttpClient(handler), store);

        var result = await service.ExchangeAuthorizationCodeAsync(
            "client-id",
            TidalDefaults.RedirectUri,
            "authorization-code",
            "code-verifier",
            TidalScopes.Requested);

        var form = ParseForm(requestBody!);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("client-id", form["client_id"]);
        Assert.Equal("authorization-code", form["code"]);
        Assert.Equal(TidalDefaults.RedirectUri, form["redirect_uri"]);
        Assert.Equal("code-verifier", form["code_verifier"]);
        Assert.DoesNotContain("scope", form.Keys);
        Assert.DoesNotContain(form.Keys, key => key.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(TidalScopes.Requested, result.RequestedScopes);
        Assert.Equal("scope-one", result.Scope);
        Assert.Equal("123456789", result.UserId);
        Assert.Equal(result, await store.GetAsync());
    }

    [Fact]
    public void RequestedScopes_MatchTidalDashboardContract()
    {
        Assert.Equal(
            ["user.read", "search.read", "collection.read", "playlists.read"],
            TidalScopes.Requested);
    }

    [Fact]
    public void ValidateAuthorizationCallback_ReturnsCodeAfterStateValidation()
    {
        var redirectUri = new Uri(TidalDefaults.RedirectUri);
        var callbackUri = new Uri($"{TidalDefaults.RedirectUri}?code=authorization-code&state=expected-state-value-1234567890123456");
        var query = new Dictionary<string, string>
        {
            ["code"] = "authorization-code",
            ["state"] = "expected-state-value-1234567890123456"
        };

        var code = TidalAuthService.ValidateAuthorizationCallback(
            callbackUri,
            redirectUri,
            "expected-state-value-1234567890123456",
            query);

        Assert.Equal("authorization-code", code);
        Assert.Contains("authorization complete", TidalAuthService.CreateCallbackResponseHtml(true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAuthorizationCallback_ReportsDeniedLoginAndFailurePage()
    {
        var redirectUri = new Uri(TidalDefaults.RedirectUri);
        var callbackUri = new Uri($"{TidalDefaults.RedirectUri}?error=access_denied&state=expected-state-value-1234567890123456");
        var query = new Dictionary<string, string>
        {
            ["error"] = "access_denied",
            ["state"] = "expected-state-value-1234567890123456"
        };

        var exception = Assert.Throws<TidalApiException>(() =>
            TidalAuthService.ValidateAuthorizationCallback(
                callbackUri,
                redirectUri,
                "expected-state-value-1234567890123456",
                query));

        Assert.Equal(TidalApiErrorKind.UserCancelled, exception.Kind);
        Assert.Contains("not completed", TidalAuthService.CreateCallbackResponseHtml(false), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveAuthorizationTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TidalAuthService(
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException())),
            new InMemoryTidalTokenStore(),
            TimeSpan.Zero));
    }

    [Fact]
    public void ParseLoopbackRedirectUri_ReturnsConfiguredPortAndPath()
    {
        var uri = TidalAuthService.ParseLoopbackRedirectUri(TidalDefaults.RedirectUri);

        Assert.Equal("127.0.0.1", uri.Host);
        Assert.Equal(8889, uri.Port);
        Assert.Equal("/callback", uri.AbsolutePath);
    }

    [Fact]
    public void ParseLoopbackRedirectUri_RejectsPortPlaceholder()
    {
        var exception = Assert.Throws<TidalApiException>(() =>
            TidalAuthService.ParseLoopbackRedirectUri("http://127.0.0.1:PORT/callback"));

        Assert.Equal(TidalApiErrorKind.InvalidRequest, exception.Kind);
        Assert.Equal(
            "Use an exact HTTP loopback URI with a fixed port, such as http://127.0.0.1:8889/callback, and register the same URI in the TIDAL Developer Dashboard.",
            exception.Message);
    }

    [Fact]
    public async Task Refresh_SendsOnlyRefreshGrantFields_AndRetainsRefreshToken()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return TokenResponse(refreshToken: null);
        });
        var store = new InMemoryTidalTokenStore();
        var service = new TidalAuthService(new HttpClient(handler), store);

        var result = await service.RefreshAsync(CreateToken(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var form = ParseForm(requestBody!);
        Assert.Equal(2, form.Count);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("saved-refresh-token", form["refresh_token"]);
        Assert.Equal("saved-refresh-token", result.RefreshToken);
        Assert.Equal("123456789", result.UserId);
        Assert.Equal(["scope-one"], result.RequestedScopes);
    }

    [Fact]
    public async Task EncryptedTokenStore_RoundTripsWithoutPlaintextOnDisk()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"dancepilot-tidal-{Guid.NewGuid():N}.dat");
        try
        {
            var store = new EncryptedFileTidalTokenStore(path);
            var token = CreateToken(DateTimeOffset.UtcNow.AddHours(1));

            await store.SaveAsync(token);
            var restored = await store.GetAsync();
            var storedText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));

            Assert.NotNull(restored);
            Assert.Equal(token.AccessToken, restored.AccessToken);
            Assert.Equal(token.RefreshToken, restored.RefreshToken);
            Assert.Equal(token.TokenType, restored.TokenType);
            Assert.Equal(token.Scope, restored.Scope);
            Assert.Equal(token.RequestedScopes, restored.RequestedScopes);
            Assert.Equal(token.UserId, restored.UserId);
            Assert.Equal(token.ExpiresAt, restored.ExpiresAt);
            Assert.DoesNotContain(token.AccessToken, storedText, StringComparison.Ordinal);
            Assert.DoesNotContain(token.RefreshToken, storedText, StringComparison.Ordinal);

            await store.ClearAsync();
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static HttpResponseMessage TokenResponse(string? refreshToken)
    {
        var refreshProperty = refreshToken is null ? string.Empty : $",\"refresh_token\":\"{refreshToken}\"";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"access_token\":\"new-access-token\",\"token_type\":\"Bearer\",\"scope\":\"scope-one\",\"expires_in\":3600,\"user_id\":123456789{refreshProperty}}}",
                Encoding.UTF8,
                "application/json")
        };
    }

    private static TidalTokenSet CreateToken(DateTimeOffset expiresAt) => new()
    {
        AccessToken = "saved-access-token",
        RefreshToken = "saved-refresh-token",
        TokenType = "Bearer",
        Scope = "scope-one",
        RequestedScopes = ["scope-one"],
        UserId = "123456789",
        ExpiresAt = expiresAt
    };

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }

    private sealed class InMemoryTidalTokenStore : ITidalTokenStore
    {
        private TidalTokenSet? _token;

        public Task<TidalTokenSet?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_token);

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
}
