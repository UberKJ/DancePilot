using DancePilot.Core.Models;
using System.Text.RegularExpressions;

namespace DancePilot.Services.LocalMusic;

public sealed class LocalMusicLibraryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".wav",
        ".m4a",
        ".aac",
        ".wma",
        ".flac"
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
                var parsed = ParseArtistAndTitle(fileInfo);
                tracks.Add(new LocalMusicTrack
                {
                    FilePath = fileInfo.FullName,
                    Title = parsed.Title,
                    Artist = parsed.Artist,
                    Extension = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
                    Folder = fileInfo.Directory?.FullName ?? folderPath,
                    LastModifiedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc)
                });
            }

            return tracks
                .OrderBy(track => track.Artist)
                .ThenBy(track => track.Title)
                .ToList();
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
}
