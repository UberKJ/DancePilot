using System.Reflection;
using DancePilot.Services.Tidal;
using DancePilot.Services.Tidal.Auth;

namespace DancePilot.Tests;

public sealed class TidalPkceTests
{
    [Fact]
    public void GenerateCodeVerifier_UsesAllowedPkceFormat()
    {
        const string allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

        var verifier = TidalPkce.GenerateCodeVerifier(96);

        Assert.Equal(96, verifier.Length);
        Assert.All(verifier, character => Assert.Contains(character, allowed));
    }

    [Fact]
    public void CreateCodeChallenge_UsesS256Base64UrlEncoding()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        var challenge = TidalPkce.CreateCodeChallenge(verifier);

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }

    [Fact]
    public void GenerateState_ReturnsUniqueValues()
    {
        var states = Enumerable.Range(0, 20).Select(_ => TidalPkce.GenerateState()).ToList();

        Assert.Equal(states.Count, states.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ValidateState_RejectsMismatch()
    {
        var exception = Assert.Throws<TidalApiException>(() =>
            TidalPkce.ValidateState("expected-state-value-1234567890", "different-state-value-123456789"));

        Assert.Equal(TidalApiErrorKind.AuthorizationFailed, exception.Kind);
    }

    [Fact]
    public void BuildAuthorizationUri_IncludesPkceAndExactRedirectUri()
    {
        const string redirectUri = TidalDefaults.RedirectUri;

        var uri = TidalPkce.BuildAuthorizationUri(
            "client-id",
            redirectUri,
            "state-value",
            ["scope-one", "scope-two"],
            "challenge-value");

        var query = ParseQuery(uri.Query);
        Assert.Equal("https://login.tidal.com/authorize", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal(redirectUri, query["redirect_uri"]);
        Assert.Equal("scope-one scope-two", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("challenge-value", query["code_challenge"]);
        Assert.Equal("state-value", query["state"]);
    }

    [Fact]
    public void TokenExpiry_UsesExpiresAt()
    {
        var token = CreateToken(DateTimeOffset.UtcNow);

        Assert.True(token.IsExpiredAt(token.ExpiresAt));
        Assert.False(token.IsExpiredAt(token.ExpiresAt.AddTicks(-1)));
    }

    [Fact]
    public void TidalSettings_DefaultsDisabled_AndContainsOnlyApprovedFields()
    {
        var settings = new TidalSettings();

        Assert.False(settings.ExperimentalCatalogEnabled);
        Assert.Equal("http://127.0.0.1:8889/callback", settings.RedirectUri);
        Assert.Equal(
            ["ClientId", "CountryCode", "ExperimentalCatalogEnabled", "RedirectUri"],
            typeof(TidalSettings)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    private static TidalTokenSet CreateToken(DateTimeOffset expiresAt) => new()
    {
        AccessToken = "access-token",
        RefreshToken = "refresh-token",
        TokenType = "Bearer",
        Scope = "scope-one",
        ExpiresAt = expiresAt
    };

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')));
}
