using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;

namespace DancePilot.Data.Repositories;

public sealed class PlaybackQueueRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public PlaybackQueueRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task<DancePilotQueueItem> EnqueueAsync(
        SpotifyTrackMetadata track,
        int? songId = null,
        CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var nextPosition = await GetNextPositionAsync(connection, cancellationToken);
        var createdAt = DateTimeOffset.UtcNow;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO queue (
                source,
                external_uri,
                local_path,
                song_id,
                title,
                artist,
                album,
                album_art_url,
                duration_ms,
                queue_position,
                status,
                created_at)
            VALUES (
                $source,
                $external_uri,
                $local_path,
                $song_id,
                $title,
                $artist,
                $album,
                $album_art_url,
                $duration_ms,
                $queue_position,
                $status,
                $created_at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$source", SongSources.Spotify);
        command.Parameters.AddWithValue("$external_uri", track.SpotifyUri ?? string.Empty);
        command.Parameters.AddWithValue("$local_path", DBNull.Value);
        command.Parameters.AddWithValue("$song_id", (object?)songId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$album", (object?)track.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$album_art_url", (object?)track.AlbumArtUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration_ms", track.DurationMs > 0 ? track.DurationMs : (object)DBNull.Value);
        command.Parameters.AddWithValue("$queue_position", nextPosition);
        command.Parameters.AddWithValue("$status", "pending");
        command.Parameters.AddWithValue("$created_at", createdAt.ToString("O"));

        var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return new DancePilotQueueItem
        {
            Id = id,
            Source = SongSources.Spotify,
            ExternalUri = track.SpotifyUri ?? string.Empty,
            SongId = songId,
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            AlbumArtUrl = track.AlbumArtUrl,
            Duration = track.DurationMs > 0 ? TimeSpan.FromMilliseconds(track.DurationMs) : null,
            QueuePosition = nextPosition,
            Status = "pending",
            CreatedAt = createdAt
        };
    }

    public async Task<IReadOnlyList<DancePilotQueueItem>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,
                   source,
                   external_uri,
                   local_path,
                   song_id,
                   title,
                   artist,
                   album,
                   album_art_url,
                   duration_ms,
                   queue_position,
                   status,
                   created_at
            FROM queue
            WHERE status = 'pending'
            ORDER BY queue_position, id;
            """;

        var items = new List<DancePilotQueueItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadQueueItem(reader));
        }

        return items;
    }

    public async Task<DancePilotQueueItem?> GetNextPendingAsync(CancellationToken cancellationToken = default) =>
        (await GetPendingAsync(cancellationToken)).FirstOrDefault();

    public async Task MarkStatusAsync(int queueItemId, string status, CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE queue SET status = $status WHERE id = $id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", queueItemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetNextPositionAsync(Microsoft.Data.Sqlite.SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(queue_position), 0) + 1 FROM queue WHERE status = 'pending';";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static DancePilotQueueItem ReadQueueItem(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new DancePilotQueueItem
        {
            Id = reader.GetInt32(0),
            Source = reader.IsDBNull(1) ? SongSources.Spotify : reader.GetString(1),
            ExternalUri = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            LocalPath = reader.IsDBNull(3) ? null : reader.GetString(3),
            SongId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Title = reader.IsDBNull(5) ? "Untitled Track" : reader.GetString(5),
            Artist = reader.IsDBNull(6) ? "Unknown Artist" : reader.GetString(6),
            Album = reader.IsDBNull(7) ? null : reader.GetString(7),
            AlbumArtUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
            Duration = reader.IsDBNull(9) ? null : TimeSpan.FromMilliseconds(reader.GetInt32(9)),
            QueuePosition = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
            Status = reader.IsDBNull(11) ? "pending" : reader.GetString(11),
            CreatedAt = reader.IsDBNull(12) || !DateTimeOffset.TryParse(reader.GetString(12), out var createdAt)
                ? DateTimeOffset.UtcNow
                : createdAt
        };
    }
}
