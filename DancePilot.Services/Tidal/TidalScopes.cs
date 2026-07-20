namespace DancePilot.Services.Tidal;

public static class TidalScopes
{
    public const string UserRead = "user.read";
    public const string SearchRead = "search.read";
    public const string CollectionRead = "collection.read";
    public const string PlaylistsRead = "playlists.read";

    public static IReadOnlyList<string> CatalogSearch { get; } = [UserRead, SearchRead];

    public static IReadOnlyList<string> UserPlaylistCollection { get; } = [UserRead, CollectionRead];

    public static IReadOnlyList<string> PlaylistResources { get; } = [UserRead, PlaylistsRead];

    public static IReadOnlyList<string> UserPlaylists { get; } = [UserRead, CollectionRead, PlaylistsRead];

    public static IReadOnlyList<string> Requested { get; } = [UserRead, SearchRead, CollectionRead, PlaylistsRead];

    public static IReadOnlySet<string> Parse(string? scopes) =>
        new HashSet<string>(
            (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);

    public static IReadOnlyList<string> Missing(string? grantedScopes, IEnumerable<string> requiredScopes)
    {
        var granted = Parse(grantedScopes);
        return requiredScopes.Where(scope => !granted.Contains(scope)).Distinct(StringComparer.Ordinal).ToList();
    }
}
