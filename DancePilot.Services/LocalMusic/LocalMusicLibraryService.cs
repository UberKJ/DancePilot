using DancePilot.Core.Models;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TagFile = TagLib.File;

namespace DancePilot.Services.LocalMusic;

public sealed class LocalMusicLibraryService
{
    private const int MaximumArtworkBytes = 6 * 1024 * 1024;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".mp4",
        ".wav",
        ".m4a",
        ".m4b",
        ".aac",
        ".wma",
        ".flac",
        ".aiff",
        ".aif",
        ".ogg",
        ".opus"
    };

    public Task<IReadOnlyList<LocalMusicTrack>> LoadDefaultMusicLibraryAsync(
        CancellationToken cancellationToken = default)
    {
        var musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return LoadFromFolderAsync(musicFolder, cancellationToken);
    }

    public Task<IReadOnlyList<LocalMusicTrack>> LoadFromFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return Task.FromResult<IReadOnlyList<LocalMusicTrack>>([]);
        }

        return Task.Run<IReadOnlyList<LocalMusicTrack>>(() =>
        {
            var tracks = new List<LocalMusicTrack>();
            foreach (var filePath in EnumerateAudioFiles(folderPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(filePath);
                tracks.Add(CreateTrack(fileInfo, folderPath));
            }

            return OrderFolderFirst(tracks).ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<LocalMusicTrack>> LoadFromFilePathsAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var paths = filePaths
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Select(filePath => filePath.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<LocalMusicTrack>>([]);
        }

        return Task.Run<IReadOnlyList<LocalMusicTrack>>(() =>
        {
            var tracks = new List<LocalMusicTrack>();
            foreach (var filePath in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(filePath) || !SupportedExtensions.Contains(Path.GetExtension(filePath)))
                {
                    continue;
                }

                var fileInfo = new FileInfo(filePath);
                tracks.Add(CreateTrack(fileInfo, fileInfo.Directory?.FullName ?? string.Empty));
            }

            return OrderFolderFirst(tracks).ToList();
        }, cancellationToken);
    }

    public IReadOnlyList<LocalMusicTrack> Search(
        IReadOnlyList<LocalMusicTrack> tracks,
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return tracks;
        }

        var terms = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToLowerInvariant())
            .ToArray();

        return tracks
            .Where(track => terms.All(term => track.SearchText.Contains(term, StringComparison.Ordinal)))
            .ToList();
    }

    private static IEnumerable<string> EnumerateAudioFiles(string folderPath)
    {
        var pending = new Stack<string>();
        pending.Push(folderPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var filePath in files)
            {
                if (SupportedExtensions.Contains(Path.GetExtension(filePath)))
                {
                    yield return filePath;
                }
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private static (string Artist, string Title) ParseArtistAndTitle(FileInfo fileInfo)
    {
        var name = Path.GetFileNameWithoutExtension(fileInfo.Name)
            .Replace('_', ' ')
            .Trim();
        name = Regex.Replace(name, @"^\d+\s*[-.)]?\s*", string.Empty).Trim();
        var parts = name.Split(" - ", 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
        {
            return (parts[0], parts[1]);
        }

        var parentFolder = fileInfo.Directory?.Name;
        return (string.IsNullOrWhiteSpace(parentFolder) ? "Unknown Artist" : parentFolder, name);
    }

    private static LocalMusicTrack CreateTrack(FileInfo fileInfo, string fallbackFolder)
    {
        var parsed = ParseArtistAndTitle(fileInfo);
        var metadata = ReadMetadata(fileInfo);
        return new LocalMusicTrack
        {
            FilePath = fileInfo.FullName,
            Title = FirstNonBlank(metadata.Title, parsed.Title) ?? parsed.Title,
            Artist = FirstNonBlank(metadata.Artist, parsed.Artist) ?? parsed.Artist,
            Album = metadata.Album,
            AlbumArtUrl = metadata.AlbumArtUrl,
            Genre = metadata.Genre,
            Year = metadata.Year,
            TrackNumber = metadata.TrackNumber,
            BPM = metadata.BPM,
            MusicalKey = metadata.MusicalKey,
            Duration = metadata.Duration,
            Extension = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
            Folder = fileInfo.Directory?.FullName ?? fallbackFolder,
            LastModifiedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc)
        };
    }

    private static IOrderedEnumerable<LocalMusicTrack> OrderFolderFirst(IEnumerable<LocalMusicTrack> tracks) =>
        tracks
            .OrderBy(track => track.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase);

    private static LocalFileMetadata ReadMetadata(FileInfo fileInfo)
    {
        try
        {
            using var file = TagFile.Create(fileInfo.FullName);
            var tag = file.Tag;
            var albumArtUrl = TryResolveCachedAlbumArt(fileInfo)
                ?? CacheEmbeddedAlbumArt(fileInfo, tag.Pictures.FirstOrDefault());
            return new LocalFileMetadata(
                Clean(tag.Title),
                FirstNonBlank(tag.FirstPerformer, tag.FirstAlbumArtist),
                Clean(tag.Album),
                albumArtUrl,
                Clean(tag.FirstGenre),
                ToPositiveInt(tag.Year),
                ToPositiveInt(tag.Track),
                ToValidBpm(tag.BeatsPerMinute),
                Clean(tag.InitialKey),
                file.Properties.Duration > TimeSpan.Zero ? file.Properties.Duration : null);
        }
        catch (Exception)
        {
            return LocalFileMetadata.Empty;
        }
    }

    private static string? TryResolveCachedAlbumArt(FileInfo fileInfo)
    {
        try
        {
            var cacheFolder = AlbumArtCacheFolder();
            if (!Directory.Exists(cacheFolder))
            {
                return null;
            }

            var cacheKey = CreateAlbumArtCacheKey(fileInfo);
            var cachedPath = Directory.EnumerateFiles(cacheFolder, $"{cacheKey}.*").FirstOrDefault();
            return cachedPath is null ? null : new Uri(cachedPath).AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    private static string? CacheEmbeddedAlbumArt(FileInfo fileInfo, TagLib.IPicture? picture)
    {
        try
        {
            var bytes = picture?.Data.Data;
            if (bytes is null || bytes.Length == 0 || bytes.Length > MaximumArtworkBytes)
            {
                return null;
            }

            var cacheFolder = AlbumArtCacheFolder();
            Directory.CreateDirectory(cacheFolder);

            var filePath = Path.Combine(cacheFolder, $"{CreateAlbumArtCacheKey(fileInfo)}{ExtensionForContentType(picture?.MimeType)}");
            if (!File.Exists(filePath))
            {
                File.WriteAllBytes(filePath, bytes);
            }

            return new Uri(filePath).AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    private static string CreateAlbumArtCacheKey(FileInfo fileInfo)
    {
        var rawKey = $"{SongSources.Local}|{fileInfo.FullName}|{fileInfo.LastWriteTimeUtc.Ticks}".ToLowerInvariant();
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string AlbumArtCacheFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DancePilot",
            "AlbumArt");

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ToPositiveInt(uint value) =>
        value > 0 && value <= int.MaxValue ? (int)value : null;

    private static int? ToValidBpm(uint value) =>
        value is >= 40 and <= 300 ? (int)value : null;

    private static string ExtensionForContentType(string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".jpg"
        };

    private sealed record LocalFileMetadata(
        string? Title,
        string? Artist,
        string? Album,
        string? AlbumArtUrl,
        string? Genre,
        int? Year,
        int? TrackNumber,
        int? BPM,
        string? MusicalKey,
        TimeSpan? Duration)
    {
        public static LocalFileMetadata Empty { get; } = new(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}
