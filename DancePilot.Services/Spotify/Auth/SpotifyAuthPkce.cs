using System.Security.Cryptography;
using System.Text;

namespace DancePilot.Services.Spotify.Auth;

public static class SpotifyAuthPkce
{
    private const string VerifierCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    public static string GenerateCodeVerifier(int length = 64)
    {
        if (length is < 43 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "PKCE code verifier length must be between 43 and 128 characters.");
        }

        var bytes = RandomNumberGenerator.GetBytes(length);
        var builder = new StringBuilder(length);
        foreach (var value in bytes)
        {
            builder.Append(VerifierCharacters[value % VerifierCharacters.Length]);
        }

        return builder.ToString();
    }

    public static string CreateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    public static Uri BuildAuthorizationUri(
        string clientId,
        string redirectUri,
        string state,
        IEnumerable<string> scopes,
        string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["scope"] = string.Join(' ', scopes.Distinct()),
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = codeChallenge
        };

        var queryString = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return new Uri($"https://accounts.spotify.com/authorize?{queryString}");
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
