using DancePilot.Services.Spotify.Auth;

namespace DancePilot.Tests;

public sealed class SpotifyAuthPkceTests
{
    [Fact]
    public void CreateCodeChallenge_UsesSha256Base64UrlEncoding()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = SpotifyAuthPkce.CreateCodeChallenge(verifier);

        Assert.Equal(expectedChallenge, challenge);
    }

    [Fact]
    public void GenerateCodeVerifier_UsesAllowedPkceCharacters()
    {
        const string allowedCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

        var verifier = SpotifyAuthPkce.GenerateCodeVerifier(96);

        Assert.Equal(96, verifier.Length);
        Assert.All(verifier, character => Assert.Contains(character, allowedCharacters));
    }
}
