namespace DancePilot.Services.Spotify;

public sealed record SpotifySettings
{
    public string ClientId { get; init; } = string.Empty;

    public string RedirectUri { get; init; } = SpotifyDefaults.RedirectUri;

    public bool IncludePlaybackControlScopes { get; init; }
}
