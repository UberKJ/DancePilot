namespace DancePilot.Services.Tidal;

public sealed class TidalApiException : Exception
{
    public TidalApiException(TidalApiErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public TidalApiErrorKind Kind { get; }

    public TimeSpan? RetryAfter { get; init; }

    public int? StatusCode { get; init; }

    public string? RequestUri { get; init; }

    public TidalRequestDiagnostic? Diagnostic { get; init; }
}
