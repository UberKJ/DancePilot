using DancePilot.Core.Models;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Data.Repositories;

public sealed class LocalMusicRepository
{
    private const int DefaultLimit = 500;
    private const int MaximumLimit = 5000;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public LocalMusicRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task UpsertTrackAsync(LocalMusicTrack track, CancellationToken cancellationToken = default)
    {
        await UpsertTracksAsync([track], cancellationToken);
    }

    public async Task<int> UpsertTracksAsync(
        IEnumerable<LocalMusicTrack> tracks,
        CancellationToken cancellationToken = default)
    {
        var indexedTracks = tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.FilePath))
            .GroupBy(track => NormalizeFilePath(track.FilePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => NormalizeTrack(group.Last()))
            .ToList();
        if (indexedTracks.Count == 0)
        {
            return 0;
        }

        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var track in indexedTracks)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO local_tracks (
                    file_path,
                    title,
                    artist,
                    album,
                    duration_ms,
                    extension,
                    folder,
                    file_name,
                    file_size,
                    last_modified_utc,
                    album_art_path,
                    created_at,
                    updated_at)
                VALUES (
                    $file_path,
                    $title,
                    $artist,
                    $album,
                    $duration_ms,
                    $extension,
                    $folder,
                    $file_name,
                    $file_size,
                    $last_modified_utc,
                    $album_art_path,
                    $created_at,
                    $updated_at)
                ON CONFLICT(file_path) DO UPDATE SET
                    title = excluded.title,
                    artist = excluded.artist,
                    album = excluded.album,
                    duration_ms = excluded.duration_ms,
                    extension = excluded.extension,
                    folder = excluded.folder,
                    file_name = excluded.file_name,
                    file_size = excluded.file_size,
                    last_modified_utc = excluded.last_modified_utc,
                    album_art_path = excluded.album_art_path,
                    updated_at = excluded.updated_at;
                """;
            BindTrack(command, track);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return indexedTracks.Count;
    }

    public async Task<IReadOnlyList<LocalMusicTrack>> SearchTracksAsync(
        string query,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetAllTracksAsync(limit, cancellationToken);
        }

        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var terms = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => $"%{term.ToLowerInvariant()}%")
            .ToArray();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {TrackColumns}
            FROM local_tracks
            WHERE {CreateSearchWhereClause(terms.Length)}
            ORDER BY title COLLATE NOCASE,
                     artist COLLATE NOCASE,
                     folder COLLATE NOCASE,
                     file_name COLLATE NOCASE
            LIMIT $limit;
            """;
        for (var index = 0; index < terms.Length; index++)
        {
            command.Parameters.AddWithValue($"$term{index}", terms[index]);
        }

        command.Parameters.AddWithValue("$limit", NormalizeLimit(limit));
        return await ReadTracksAsync(command, cancellationToken);
    }

    public async Task<LocalMusicTrack?> GetTrackByPathAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {TrackColumns}
            FROM local_tracks
            WHERE file_path = $file_path
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$file_path", NormalizeFilePath(filePath));
        var tracks = await ReadTracksAsync(command, cancellationToken);
        return tracks.FirstOrDefault();
    }

    public async Task<IReadOnlyList<LocalMusicTrack>> GetAllTracksAsync(
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {TrackColumns}
            FROM local_tracks
            ORDER BY folder COLLATE NOCASE,
                     file_name COLLATE NOCASE,
                     title COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", NormalizeLimit(limit));
        return await ReadTracksAsync(command, cancellationToken);
    }

    public async Task<int> RemoveMissingTracksAsync(
        IEnumerable<string> currentFilePaths,
        CancellationToken cancellationToken = default)
    {
        var paths = currentFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeFilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.Transaction = (SqliteTransaction)transaction;
            createCommand.CommandText = "CREATE TEMP TABLE IF NOT EXISTS current_local_track_paths (file_path TEXT PRIMARY KEY);";
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var clearCommand = connection.CreateCommand())
        {
            clearCommand.Transaction = (SqliteTransaction)transaction;
            clearCommand.CommandText = "DELETE FROM current_local_track_paths;";
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var path in paths)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = (SqliteTransaction)transaction;
            insertCommand.CommandText = """
                INSERT OR IGNORE INTO current_local_track_paths (file_path)
                VALUES ($file_path);
                """;
            insertCommand.Parameters.AddWithValue("$file_path", path);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        int removed;
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = (SqliteTransaction)transaction;
            deleteCommand.CommandText = """
                DELETE FROM local_tracks
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM current_local_track_paths current_paths
                    WHERE current_paths.file_path = local_tracks.file_path
                );
                """;
            removed = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    public async Task<LocalLibraryStats> GetLibraryStatsAsync(CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MAX(updated_at) FROM local_tracks;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new LocalLibraryStats();
        }

        return new LocalLibraryStats
        {
            TrackCount = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
            LastUpdatedAt = reader.IsDBNull(1) ? null : ParseDate(reader.GetString(1))
        };
    }

    private const string TrackColumns = """
        id,
        file_path,
        title,
        artist,
        album,
        duration_ms,
        extension,
        folder,
        file_name,
        file_size,
        last_modified_utc,
        album_art_path
        """;

    private static async Task<IReadOnlyList<LocalMusicTrack>> ReadTracksAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var tracks = new List<LocalMusicTrack>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tracks.Add(ReadTrack(reader));
        }

        return tracks;
    }

    private static LocalMusicTrack ReadTrack(SqliteDataReader reader)
    {
        var filePath = reader.GetString(1);
        return new LocalMusicTrack
        {
            FilePath = filePath,
            Title = reader.IsDBNull(2) ? Path.GetFileNameWithoutExtension(filePath) : reader.GetString(2),
            Artist = reader.IsDBNull(3) ? "Unknown Artist" : reader.GetString(3),
            Album = reader.IsDBNull(4) ? null : reader.GetString(4),
            Duration = reader.IsDBNull(5) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(5)),
            Extension = reader.IsDBNull(6)
                ? Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant()
                : reader.GetString(6),
            Folder = reader.IsDBNull(7) ? Path.GetDirectoryName(filePath) ?? string.Empty : reader.GetString(7),
            FileSize = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            LastModifiedAt = reader.IsDBNull(10)
                ? DateTimeOffset.MinValue
                : ParseDate(reader.GetString(10)) ?? DateTimeOffset.MinValue,
            AlbumArtUrl = reader.IsDBNull(11) ? null : reader.GetString(11)
        };
    }

    private static void BindTrack(SqliteCommand command, LocalMusicTrack track)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        long? durationMs = track.Duration is null ? null : Convert.ToInt64(track.Duration.Value.TotalMilliseconds);
        command.Parameters.AddWithValue("$file_path", track.FilePath);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$album", DbValue(track.Album));
        command.Parameters.AddWithValue("$duration_ms", DbValue(durationMs));
        command.Parameters.AddWithValue("$extension", track.Extension);
        command.Parameters.AddWithValue("$folder", track.Folder);
        command.Parameters.AddWithValue("$file_name", track.FileName);
        command.Parameters.AddWithValue("$file_size", DbValue(track.FileSize));
        command.Parameters.AddWithValue("$last_modified_utc", track.LastModifiedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$album_art_path", DbValue(track.AlbumArtUrl));
        command.Parameters.AddWithValue("$created_at", now);
        command.Parameters.AddWithValue("$updated_at", now);
    }

    private static LocalMusicTrack NormalizeTrack(LocalMusicTrack track)
    {
        var filePath = NormalizeFilePath(track.FilePath);
        var fileName = Path.GetFileName(filePath);
        var title = string.IsNullOrWhiteSpace(track.Title)
            ? Path.GetFileNameWithoutExtension(fileName)
            : track.Title.Trim();
        var artist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist.Trim();
        var extension = string.IsNullOrWhiteSpace(track.Extension)
            ? Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant()
            : track.Extension.Trim().TrimStart('.').ToUpperInvariant();
        var folder = string.IsNullOrWhiteSpace(track.Folder)
            ? Path.GetDirectoryName(filePath) ?? string.Empty
            : track.Folder.Trim();
        var fileInfo = TryGetFileInfo(filePath);
        var lastModified = track.LastModifiedAt == default && fileInfo is not null
            ? new DateTimeOffset(fileInfo.LastWriteTimeUtc)
            : track.LastModifiedAt;

        return track with
        {
            FilePath = filePath,
            Title = title,
            Artist = artist,
            Extension = extension,
            Folder = folder,
            FileSize = track.FileSize ?? fileInfo?.Length,
            LastModifiedAt = lastModified == default ? DateTimeOffset.UtcNow : lastModified
        };
    }

    private static FileInfo? TryGetFileInfo(string filePath)
    {
        try
        {
            return File.Exists(filePath) ? new FileInfo(filePath) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeFilePath(string filePath)
    {
        var trimmed = filePath.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private static string CreateSearchWhereClause(int termCount)
    {
        var clauses = new List<string>();
        for (var index = 0; index < termCount; index++)
        {
            clauses.Add($"""
                (
                    lower(title) LIKE $term{index}
                    OR lower(artist) LIKE $term{index}
                    OR lower(COALESCE(album, '')) LIKE $term{index}
                    OR lower(file_name) LIKE $term{index}
                    OR lower(folder) LIKE $term{index}
                )
                """);
        }

        return string.Join(" AND ", clauses);
    }

    private static int NormalizeLimit(int limit) =>
        Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaximumLimit);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(long? value) =>
        value is null ? DBNull.Value : value.Value;
}
