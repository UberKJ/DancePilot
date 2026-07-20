using System.Security.Cryptography;
using System.Text;

namespace DancePilot.Services.Tidal.Auth;

public static class TidalPkce
{
    private const string VerifierCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
    private static readonly Uri AuthorizationEndpoint = new("https://login.tidal.com/authorize");

    public static string GenerateCodeVerifier(int length = 64)
    {
        if (length is < 43 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "PKCE code verifier length must be between 43 and 128 characters.");
        }

        return GenerateRandomValue(length);
    }

    public static string GenerateState(int length = 48)
    {
        if (length < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "OAuth state length must be at least 32 characters.");
        }

        return GenerateRandomValue(length);
    }

    public static string CreateCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    public static void ValidateState(string expectedState, string? returnedState)
    {
        if (string.IsNullOrEmpty(expectedState) || string.IsNullOrEmpty(returnedState))
        {
            throw new TidalApiException(TidalApiErrorKind.AuthorizationFailed, "TIDAL authorization state validation failed.");
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedState);
        var returnedBytes = Encoding.UTF8.GetBytes(returnedState);
        if (expectedBytes.Length != returnedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(expectedBytes, returnedBytes))
        {
            throw new TidalApiException(TidalApiErrorKind.AuthorizationFailed, "TIDAL authorization state validation failed.");
        }
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
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', scopes.Distinct(StringComparer.Ordinal)),
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = codeChallenge,
            ["state"] = state
        };

        var queryString = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new UriBuilder(AuthorizationEndpoint) { Query = queryString }.Uri;
    }

    private static string GenerateRandomValue(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var builder = new StringBuilder(length);
        foreach (var value in bytes)
        {
            builder.Append(VerifierCharacters[value % VerifierCharacters.Length]);
        }

        return builder.ToString();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
