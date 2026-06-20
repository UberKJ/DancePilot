namespace DancePilot.Core.Spotify;

public sealed record SpotifyUserProfile
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string? Country { get; init; }

    public string? Product { get; init; }
}
