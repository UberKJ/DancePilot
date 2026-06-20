namespace DancePilot.Services.Spotify.Auth;

public sealed record SpotifyTokenSet
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required string Scope { get; init; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}
