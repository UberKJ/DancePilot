namespace DancePilot.Services.Tidal.Auth;

public sealed record TidalTokenSet
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required string TokenType { get; init; }

    public required string Scope { get; init; }

    public IReadOnlyList<string> RequestedScopes { get; init; } = [];

    public string? UserId { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public bool IsExpired => IsExpiredAt(DateTimeOffset.UtcNow);

    public bool IsExpiredAt(DateTimeOffset timestamp) => timestamp >= ExpiresAt;
}
