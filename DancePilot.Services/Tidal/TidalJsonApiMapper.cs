using System.Globalization;
using System.Text.Json;
using System.Xml;

namespace DancePilot.Services.Tidal;

internal readonly record struct TidalResourceKey(string Type, string Id);

internal sealed record TidalResourceIdentifier(string Type, string Id, JsonElement Meta);

internal static class TidalJsonApiMapper
{
    public static IReadOnlyDictionary<TidalResourceKey, JsonElement> BuildResourceIndex(JsonElement root)
    {
        var index = new Dictionary<TidalResourceKey, JsonElement>();
        IndexResources(root, "data", index);
        IndexResources(root, "included", index);
        return index;
    }

    public static bool TryReadRelationshipIdentifiers(
        JsonElement resource,
        string relationshipName,
        out IReadOnlyList<TidalResourceIdentifier> identifiers)
    {
        identifiers = [];
        if (!resource.TryGetProperty("relationships", out var relationships)
            || relationships.ValueKind != JsonValueKind.Object
            || !relationships.TryGetProperty(relationshipName, out var relationship)
            || relationship.ValueKind != JsonValueKind.Object
            || !relationship.TryGetProperty("data", out var data))
        {
            return false;
        }

        identifiers = ReadIdentifiers(data);
        return true;
    }

    public static IReadOnlyList<TidalResourceIdentifier> ReadDocumentIdentifiers(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            throw Malformed("TIDAL JSON:API response did not contain data.");
        }

        return ReadIdentifiers(data);
    }

    public static JsonElement ReadSingleResource(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw Malformed("TIDAL JSON:API response did not contain a resource object.");
        }

        return data;
    }

    public static string? ReadNextLink(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(links, "next");
    }

    public static TidalTrackMetadata MapTrack(
        JsonElement resource,
        IReadOnlyDictionary<TidalResourceKey, JsonElement> index,
        int? playlistPosition = null)
    {
        EnsureResourceObject(resource, "tracks");
        var id = ReadRequiredString(resource, "id", "TIDAL track is missing its provider ID.");
        var attributes = ReadAttributes(resource);
        var artistIdentifiers = ReadRelatedIdentifiers(resource, "artists");
        var albumIdentifier = ReadRelatedIdentifiers(resource, "albums").FirstOrDefault();
        var artistNames = ReadRelatedNames(artistIdentifiers, index);
        var albumResource = TryGetRelated(albumIdentifier, index);
        var albumName = albumResource is { } relatedAlbum
            ? ReadString(ReadAttributes(relatedAlbum), "title")
            : null;

        return new TidalTrackMetadata
        {
            ProviderTrackId = id,
            Title = ReadString(attributes, "title") ?? "Untitled Track",
            Artist = artistNames.Count > 0 ? string.Join(", ", artistNames) : "Unknown Artist",
            ArtistIds = artistIdentifiers.Select(identifier => identifier.Id).ToList(),
            Album = albumName,
            AlbumId = albumIdentifier?.Id,
            Duration = ReadDuration(attributes),
            ArtworkReference = albumResource is { } album ? ReadArtworkFromRelationship(album, index, "coverArt") : null,
            TidalPageUrl = ReadPageUrl(attributes) ?? $"https://tidal.com/browse/track/{Uri.EscapeDataString(id)}",
            IsExplicit = ReadBoolean(attributes, "explicit"),
            Availability = ReadAvailability(attributes),
            PlaylistPosition = playlistPosition
        };
    }

    public static TidalAlbumMetadata MapAlbum(
        JsonElement resource,
        IReadOnlyDictionary<TidalResourceKey, JsonElement> index)
    {
        EnsureResourceObject(resource, "albums");
        var id = ReadRequiredString(resource, "id", "TIDAL album is missing its provider ID.");
        var attributes = ReadAttributes(resource);
        var artistNames = ReadRelatedNames(ReadRelatedIdentifiers(resource, "artists"), index);
        return new TidalAlbumMetadata
        {
            ProviderAlbumId = id,
            Title = ReadString(attributes, "title") ?? "Untitled Album",
            Artist = artistNames.Count > 0 ? string.Join(", ", artistNames) : null,
            ArtworkReference = ReadArtworkFromRelationship(resource, index, "coverArt"),
            TidalPageUrl = ReadPageUrl(attributes) ?? $"https://tidal.com/browse/album/{Uri.EscapeDataString(id)}"
        };
    }

    public static TidalArtistMetadata MapArtist(
        JsonElement resource,
        IReadOnlyDictionary<TidalResourceKey, JsonElement> index)
    {
        EnsureResourceObject(resource, "artists");
        var id = ReadRequiredString(resource, "id", "TIDAL artist is missing its provider ID.");
        var attributes = ReadAttributes(resource);
        return new TidalArtistMetadata
        {
            ProviderArtistId = id,
            Name = ReadString(attributes, "name") ?? "Unknown Artist",
            ArtworkReference = ReadArtworkFromRelationship(resource, index, "profileArt"),
            TidalPageUrl = ReadPageUrl(attributes) ?? $"https://tidal.com/browse/artist/{Uri.EscapeDataString(id)}"
        };
    }

    public static TidalPlaylistMetadata MapPlaylist(
        JsonElement resource,
        IReadOnlyDictionary<TidalResourceKey, JsonElement> index)
    {
        EnsureResourceObject(resource, "playlists");
        var id = ReadRequiredString(resource, "id", "TIDAL playlist is missing its provider ID.");
        var attributes = ReadAttributes(resource);
        var owners = ReadRelatedNames(ReadRelatedIdentifiers(resource, "owners"), index);
        return new TidalPlaylistMetadata
        {
            ProviderPlaylistId = id,
            Title = ReadString(attributes, "name") ?? "Untitled Playlist",
            Description = ReadString(attributes, "description"),
            NumberOfItems = ReadInt32(attributes, "numberOfItems"),
            ArtworkReference = ReadArtworkFromRelationship(resource, index, "coverArt"),
            Owner = owners.Count > 0 ? string.Join(", ", owners) : null,
            Availability = ReadString(attributes, "accessType"),
            TidalPageUrl = ReadPageUrl(attributes) ?? $"https://tidal.com/browse/playlist/{Uri.EscapeDataString(id)}"
        };
    }

    private static IReadOnlyList<TidalResourceIdentifier> ReadIdentifiers(JsonElement data)
    {
        var values = data.ValueKind switch
        {
            JsonValueKind.Array => data.EnumerateArray().ToList(),
            JsonValueKind.Object => [data],
            JsonValueKind.Null => [],
            _ => throw Malformed("TIDAL JSON:API data must be a resource identifier object or array.")
        };

        return values.Select(value =>
        {
            var type = ReadRequiredString(value, "type", "TIDAL resource identifier is missing type.");
            var id = ReadRequiredString(value, "id", "TIDAL resource identifier is missing id.");
            var meta = value.TryGetProperty("meta", out var metaValue) && metaValue.ValueKind == JsonValueKind.Object
                ? metaValue
                : default;
            return new TidalResourceIdentifier(type, id, meta);
        }).ToList();
    }

    private static void IndexResources(
        JsonElement root,
        string propertyName,
        IDictionary<TidalResourceKey, JsonElement> index)
    {
        if (!root.TryGetProperty(propertyName, out var resources))
        {
            return;
        }

        var values = resources.ValueKind switch
        {
            JsonValueKind.Array => resources.EnumerateArray().ToList(),
            JsonValueKind.Object => [resources],
            _ => []
        };
        foreach (var resource in values)
        {
            var type = ReadString(resource, "type");
            var id = ReadString(resource, "id");
            if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(id))
            {
                index[new TidalResourceKey(type, id)] = resource;
            }
        }
    }

    private static IReadOnlyList<TidalResourceIdentifier> ReadRelatedIdentifiers(JsonElement resource, string name) =>
        TryReadRelationshipIdentifiers(resource, name, out var identifiers) ? identifiers : [];

    private static List<string> ReadRelatedNames(
        IEnumerable<TidalResourceIdentifier> identifiers,
        IReadOnlyDictionary<TidalResourceKey, JsonElement> index) =>
        identifiers.Select(identifier => TryGetRelated(identifier, index))
            .Where(resource => resource is not null)
            .Select(resource => ReadAttributes(resource!.Value))
            .Select(attributes => ReadString(attributes, "name") ?? ReadString(attributes, "title") ?? ReadString(attributes, "username"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();

    private static JsonElement? TryGetRelated(
        TidalResourceIdentifier? identifier,
        IReadOnlyDictionary<TidalResourceKey, JsonElement> index) =>
        identifier is not null && index.TryGetValue(new TidalResourceKey(identifier.Type, identifier.Id), out var resource)
            ? resource
            : null;

    private static string? ReadArtworkFromRelationship(
        JsonElement resource,
        IReadOnlyDictionary<TidalResourceKey, JsonElement> index,
        string relationshipName)
    {
        foreach (var identifier in ReadRelatedIdentifiers(resource, relationshipName))
        {
            if (!index.TryGetValue(new TidalResourceKey(identifier.Type, identifier.Id), out var artwork))
            {
                continue;
            }

            var attributes = ReadAttributes(artwork);
            if (attributes.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                var href = files.EnumerateArray().Select(file => ReadString(file, "href"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }
        }

        return null;
    }

    private static JsonElement ReadAttributes(JsonElement resource) =>
        resource.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object
            ? attributes
            : default;

    private static TimeSpan? ReadDuration(JsonElement attributes)
    {
        var value = ReadString(attributes, "duration");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return XmlConvert.ToTimeSpan(value);
        }
        catch (FormatException)
        {
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }
    }

    private static string? ReadPageUrl(JsonElement attributes)
    {
        if (!attributes.TryGetProperty("externalLinks", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return links.EnumerateArray().Select(link => ReadString(link, "href"))
            .FirstOrDefault(href => href?.Contains("tidal.com", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string? ReadAvailability(JsonElement attributes)
    {
        if (!attributes.TryGetProperty("availability", out var availability))
        {
            return null;
        }

        return availability.ValueKind == JsonValueKind.Array
            ? string.Join(", ", availability.EnumerateArray().Select(value => value.GetString()).Where(value => value is not null))
            : availability.ValueKind == JsonValueKind.String ? availability.GetString() : null;
    }

    private static bool? ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? ReadInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static string ReadRequiredString(JsonElement element, string propertyName, string message) =>
        ReadString(element, propertyName) is { Length: > 0 } value ? value : throw Malformed(message);

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void EnsureResourceObject(JsonElement resource, string expectedType)
    {
        if (resource.ValueKind != JsonValueKind.Object
            || !string.Equals(ReadString(resource, "type"), expectedType, StringComparison.Ordinal))
        {
            throw Malformed($"TIDAL JSON:API response expected a {expectedType} resource object.");
        }
    }

    private static TidalApiException Malformed(string message) =>
        new(TidalApiErrorKind.MalformedResponse, message);
}
