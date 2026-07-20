namespace DancePilot.Services.Tidal;

public sealed record TidalTrackMetadata
{
    public required string ProviderTrackId { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public IReadOnlyList<string> ArtistIds { get; init; } = [];

    public string? Album { get; init; }

    public string? AlbumId { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? ArtworkReference { get; init; }

    public string? TidalPageUrl { get; init; }

    public bool? IsExplicit { get; init; }

    public string? Availability { get; init; }

    public string DurationDisplay => Duration is { } duration
        ? $"{(int)duration.TotalMinutes}:{duration.Seconds:00}"
        : "--:--";

    public string ExplicitDisplay => IsExplicit == true ? "EXPLICIT" : string.Empty;

    public int? PlaylistPosition { get; init; }
}

public sealed record TidalAlbumMetadata
{
    public required string ProviderAlbumId { get; init; }

    public required string Title { get; init; }

    public string? Artist { get; init; }

    public string? ArtworkReference { get; init; }

    public string? TidalPageUrl { get; init; }

}

public sealed record TidalArtistMetadata
{
    public required string ProviderArtistId { get; init; }

    public required string Name { get; init; }

    public string? ArtworkReference { get; init; }

    public string? TidalPageUrl { get; init; }
}

public sealed record TidalSearchResult
{
    public IReadOnlyList<TidalTrackMetadata> Tracks { get; init; } = [];

    public IReadOnlyList<TidalAlbumMetadata> Albums { get; init; } = [];

    public IReadOnlyList<TidalArtistMetadata> Artists { get; init; } = [];
}

public sealed record TidalApiHealthResult
{
    public required string AlbumId { get; init; }

    public required string AlbumTitle { get; init; }
}

public sealed record TidalPlaylistMetadata
{
    public required string ProviderPlaylistId { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? ArtworkReference { get; init; }

    public string? TidalPageUrl { get; init; }

    public int? NumberOfItems { get; init; }

    public string? Owner { get; init; }

    public string? Availability { get; init; }

    public string CollectionKind { get; init; } = "In My Collection";
}
