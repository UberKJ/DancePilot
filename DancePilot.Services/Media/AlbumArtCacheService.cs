using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Repositories;
using System.Security.Cryptography;
using TagFile = TagLib.File;

namespace DancePilot.Services.Media;

public sealed class AlbumArtCacheService
{
    private const int MaximumArtworkBytes = 6 * 1024 * 1024;
    private readonly AlbumArtCacheRepository _repository;
    private readonly HttpClient _httpClient;
    private readonly string _cacheFolder;
    private readonly SemaphoreSlim _cacheGate = new(4);

    public AlbumArtCacheService(
        AlbumArtCacheRepository repository,
        HttpClient httpClient,
        string cacheFolder)
    {
        _repository = repository;
        _httpClient = httpClient;
        _cacheFolder = cacheFolder;
    }

    public async Task<string?> CacheAlbumArtAsync(
        DancePilotQueueItem item,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CreateCacheKey(item);
        var sourceIdentity = item.SourceIdentity;
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _repository.GetAsync(cacheKey, cancellationToken);
            var existingUri = await EnsureCachedFileAsync(existing, cancellationToken);
            if (!string.IsNullOrWhiteSpace(existingUri))
            {
                return existingUri;
            }

            var image = await ReadImageAsync(item, cancellationToken);
            if (image is null)
            {
                return null;
            }

            Directory.CreateDirectory(_cacheFolder);
            var extension = ExtensionForContentType(image.Value.ContentType);
            var filePath = Path.Combine(_cacheFolder, $"{cacheKey}{extension}");
            await File.WriteAllBytesAsync(filePath, image.Value.Bytes, cancellationToken);

            var localUri = new Uri(filePath).AbsoluteUri;
            await _repository.SaveAsync(new AlbumArtCacheEntry
            {
                CacheKey = cacheKey,
                Source = item.Source,
                ExternalUri = sourceIdentity,
                Title = item.Title,
                Artist = item.Artist,
                OriginalUri = item.AlbumArtUrl,
                ContentType = image.Value.ContentType,
                ImageBytes = image.Value.Bytes,
                LocalFilePath = filePath,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
            await _repository.UpdateSongAlbumArtAsync(item.Source, sourceIdentity, localUri, cancellationToken);
            return localUri;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    public async Task<string?> GetCachedAlbumArtUriAsync(
        DancePilotQueueItem item,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CreateCacheKey(item);
        var existing = await _repository.GetAsync(cacheKey, cancellationToken);
        return await EnsureCachedFileAsync(existing, cancellationToken);
    }

    public static string CreateCacheKey(DancePilotQueueItem item)
    {
        var stableId = CreateStableCacheId(item);
        var raw = $"{item.Source}|{stableId}".ToLowerInvariant();
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateStableCacheId(DancePilotQueueItem item)
    {
        var localPath = item.ResolvedLocalPath;
        if (item.Source == SongSources.Local
            && !string.IsNullOrWhiteSpace(localPath)
            && File.Exists(localPath))
        {
            return $"{localPath}|{File.GetLastWriteTimeUtc(localPath).Ticks}";
        }

        return !string.IsNullOrWhiteSpace(item.SourceIdentity)
            ? item.SourceIdentity
            : $"{item.Title}|{item.Artist}";
    }

    private async Task<string?> EnsureCachedFileAsync(
        AlbumArtCacheEntry? existing,
        CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(existing.LocalFilePath) && File.Exists(existing.LocalFilePath))
        {
            return new Uri(existing.LocalFilePath).AbsoluteUri;
        }

        if (existing.ImageBytes is null || existing.ImageBytes.Length == 0)
        {
            return null;
        }

        Directory.CreateDirectory(_cacheFolder);
        var filePath = Path.Combine(_cacheFolder, $"{existing.CacheKey}{ExtensionForContentType(existing.ContentType)}");
        await File.WriteAllBytesAsync(filePath, existing.ImageBytes, cancellationToken);
        await _repository.SaveAsync(existing with
        {
            LocalFilePath = filePath,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        return new Uri(filePath).AbsoluteUri;
    }

    private async Task<CachedImage?> ReadImageAsync(
        DancePilotQueueItem item,
        CancellationToken cancellationToken)
    {
        var localPath = item.ResolvedLocalPath;
        if (item.Source == SongSources.Local
            && !string.IsNullOrWhiteSpace(localPath)
            && File.Exists(localPath))
        {
            return ReadEmbeddedLocalArtwork(localPath);
        }

        if (string.IsNullOrWhiteSpace(item.AlbumArtUrl)
            || !Uri.TryCreate(item.AlbumArtUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0 || bytes.Length > MaximumArtworkBytes)
        {
            return null;
        }

        return new CachedImage(bytes, response.Content.Headers.ContentType?.MediaType);
    }

    private static CachedImage? ReadEmbeddedLocalArtwork(string filePath)
    {
        try
        {
            using var file = TagFile.Create(filePath);
            var picture = file.Tag.Pictures.FirstOrDefault();
            var bytes = picture?.Data.Data;
            if (bytes is null || bytes.Length == 0 || bytes.Length > MaximumArtworkBytes)
            {
                return null;
            }

            return new CachedImage(bytes, picture?.MimeType);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtensionForContentType(string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };

    private readonly record struct CachedImage(byte[] Bytes, string? ContentType);
}
