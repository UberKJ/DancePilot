using DancePilot.Core.Models;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;

namespace DancePilot.Data.Repositories;

public sealed class AlbumArtCacheRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public AlbumArtCacheRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task<AlbumArtCacheEntry?> GetAsync(
        string cacheKey,
        CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cache_key,
                   source,
                   external_uri,
                   title,
                   artist,
                   original_uri,
                   content_type,
                   image_bytes,
                   local_file_path,
                   updated_at
            FROM album_art_cache
            WHERE cache_key = $cache_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$cache_key", cacheKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadEntry(reader)
            : null;
    }

    public async Task SaveAsync(
        AlbumArtCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO album_art_cache (
                cache_key,
                source,
                external_uri,
                title,
                artist,
                original_uri,
                content_type,
                image_bytes,
                local_file_path,
                updated_at)
            VALUES (
                $cache_key,
                $source,
                $external_uri,
                $title,
                $artist,
                $original_uri,
                $content_type,
                $image_bytes,
                $local_file_path,
                $updated_at)
            ON CONFLICT(cache_key) DO UPDATE SET
                source = excluded.source,
                external_uri = excluded.external_uri,
                title = excluded.title,
                artist = excluded.artist,
                original_uri = excluded.original_uri,
                content_type = excluded.content_type,
                image_bytes = excluded.image_bytes,
                local_file_path = excluded.local_file_path,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$cache_key", entry.CacheKey);
        command.Parameters.AddWithValue("$source", entry.Source);
        command.Parameters.AddWithValue("$external_uri", (object?)entry.ExternalUri ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", (object?)entry.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$artist", (object?)entry.Artist ?? DBNull.Value);
        command.Parameters.AddWithValue("$original_uri", (object?)entry.OriginalUri ?? DBNull.Value);
        command.Parameters.AddWithValue("$content_type", (object?)entry.ContentType ?? DBNull.Value);
        command.Parameters.Add("$image_bytes", Microsoft.Data.Sqlite.SqliteType.Blob).Value = (object?)entry.ImageBytes ?? DBNull.Value;
        command.Parameters.AddWithValue("$local_file_path", (object?)entry.LocalFilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", entry.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateSongAlbumArtAsync(
        string source,
        string? externalUri,
        string albumArtPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalUri) || string.IsNullOrWhiteSpace(albumArtPath))
        {
            return;
        }

        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE songs
            SET album_art_path = $album_art_path
            WHERE source = $source
              AND (external_uri = $external_uri OR external_id = $external_uri);
            """;
        command.Parameters.AddWithValue("$album_art_path", albumArtPath);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$external_uri", externalUri);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AlbumArtCacheEntry ReadEntry(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        byte[]? imageBytes = null;
        if (!reader.IsDBNull(7))
        {
            imageBytes = (byte[])reader["image_bytes"];
        }

        return new AlbumArtCacheEntry
        {
            CacheKey = reader.GetString(0),
            Source = reader.GetString(1),
            ExternalUri = reader.IsDBNull(2) ? null : reader.GetString(2),
            Title = reader.IsDBNull(3) ? null : reader.GetString(3),
            Artist = reader.IsDBNull(4) ? null : reader.GetString(4),
            OriginalUri = reader.IsDBNull(5) ? null : reader.GetString(5),
            ContentType = reader.IsDBNull(6) ? null : reader.GetString(6),
            ImageBytes = imageBytes,
            LocalFilePath = reader.IsDBNull(8) ? null : reader.GetString(8),
            UpdatedAt = reader.IsDBNull(9) || !DateTimeOffset.TryParse(reader.GetString(9), out var updatedAt)
                ? DateTimeOffset.UtcNow
                : updatedAt
        };
    }
}
