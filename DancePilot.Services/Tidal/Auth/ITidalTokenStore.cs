namespace DancePilot.Services.Tidal.Auth;

public interface ITidalTokenStore
{
    Task<TidalTokenSet?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TidalTokenSet tokenSet, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
