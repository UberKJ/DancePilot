namespace DancePilot.Services.Spotify;

public sealed class SpotifyApiException : Exception
{
    public SpotifyApiException(SpotifyApiErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public SpotifyApiErrorKind Kind { get; }

    public TimeSpan? RetryAfter { get; init; }

    public string? ErrorBody { get; init; }

    public int? StatusCode { get; init; }

    public string? RequestUri { get; init; }
}
