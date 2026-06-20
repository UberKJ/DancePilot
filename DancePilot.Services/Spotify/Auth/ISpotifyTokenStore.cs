namespace DancePilot.Services.Spotify.Auth;

public interface ISpotifyTokenStore
{
    Task<SpotifyTokenSet?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SpotifyTokenSet tokenSet, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
