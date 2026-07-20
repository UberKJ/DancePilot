using System.Text.Json;

namespace DancePilot.Services.Media;

public sealed class ExternalAlbumArtLookupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _lookupGate = new(2);

    public ExternalAlbumArtLookupService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> FindAlbumArtAsync(
        string title,
        string artist,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        var searchTerm = $"{artist.Trim()} {title.Trim()}";
        var requestUri = new Uri(
            $"https://itunes.apple.com/search?term={Uri.EscapeDataString(searchTerm)}&country=US&media=music&entity=song&limit=10");

        await _lookupGate.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.UserAgent.ParseAdd("DancePilot/1.0");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<ItunesSearchResponse>(stream, JsonOptions, cancellationToken);
            return SelectBestArtwork(result?.Results, title, artist);
        }
        catch
        {
            return null;
        }
        finally
        {
            _lookupGate.Release();
        }
    }

    private static string? SelectBestArtwork(
        IReadOnlyList<ItunesSearchResult>? results,
        string title,
        string artist)
    {
        if (results is null || results.Count == 0)
        {
            return null;
        }

        var normalizedTitle = NormalizeMatchText(title);
        var normalizedArtist = NormalizeMatchText(artist);
        var exactMatch = results.FirstOrDefault(result =>
            HasArtwork(result)
            && string.Equals(NormalizeMatchText(result.TrackName), normalizedTitle, StringComparison.Ordinal)
            && string.Equals(NormalizeMatchText(result.ArtistName), normalizedArtist, StringComparison.Ordinal));
        var artistMatch = exactMatch ?? results.FirstOrDefault(result =>
            HasArtwork(result)
            && string.Equals(NormalizeMatchText(result.ArtistName), normalizedArtist, StringComparison.Ordinal));
        var fallback = artistMatch ?? results.FirstOrDefault(HasArtwork);
        return UpgradeArtworkUrl(fallback?.ArtworkUrl100);
    }

    private static bool HasArtwork(ItunesSearchResult result) =>
        !string.IsNullOrWhiteSpace(result.ArtworkUrl100);

    private static string NormalizeMatchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string? UpgradeArtworkUrl(string? artworkUrl)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl))
        {
            return null;
        }

        return artworkUrl
            .Replace("100x100bb", "600x600bb", StringComparison.OrdinalIgnoreCase)
            .Replace("100x100-75", "600x600-75", StringComparison.OrdinalIgnoreCase)
            .Replace("100x100", "600x600", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ItunesSearchResponse
    {
        public IReadOnlyList<ItunesSearchResult> Results { get; init; } = [];
    }

    private sealed record ItunesSearchResult
    {
        public string? ArtistName { get; init; }

        public string? TrackName { get; init; }

        public string? ArtworkUrl100 { get; init; }
    }
}
