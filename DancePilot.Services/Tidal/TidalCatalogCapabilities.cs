namespace DancePilot.Services.Tidal;

public static class TidalCatalogCapabilities
{
    public const bool UserPlaylistsAvailable = true;

    public const string UserPlaylistsUnavailableMessage =
        "TIDAL playlist collections require user.read and collection.read; playlist hydration requires playlists.read.";

    public const string ReconnectForScopesMessage =
        "Reconnect to TIDAL after enabling this scope in the TIDAL Developer Dashboard.";
}
