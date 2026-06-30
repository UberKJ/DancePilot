using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Data.Migrations;

public sealed class DancePilotDatabaseMigrator
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DancePilotDatabaseMigrator(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS songs (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                duration TEXT,
                bpm INTEGER,
                song_key TEXT,
                energy_score INTEGER,
                genre TEXT,
                tags TEXT,
                rating REAL,
                album_art_path TEXT,
                source TEXT DEFAULT 'local',
                external_id TEXT,
                external_uri TEXT,
                external_url TEXT,
                album TEXT,
                duration_ms INTEGER,
                popularity INTEGER,
                imported_from_playlist_id TEXT,
                last_synced_at TEXT,
                likely_local_match_song_id INTEGER
            );
            """, cancellationToken);

        await AddColumnIfMissingAsync(connection, "songs", "source", "TEXT DEFAULT 'local'", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "external_id", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "external_uri", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "external_url", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "album", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "duration_ms", "INTEGER", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "popularity", "INTEGER", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "imported_from_playlist_id", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "last_synced_at", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "songs", "likely_local_match_song_id", "INTEGER", cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS spotify_playlists (
                id INTEGER PRIMARY KEY,
                spotify_playlist_id TEXT UNIQUE,
                name TEXT,
                owner_name TEXT,
                track_count INTEGER,
                snapshot_id TEXT,
                external_url TEXT,
                last_synced_at TEXT
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS spotify_playlist_tracks (
                id INTEGER PRIMARY KEY,
                spotify_playlist_id TEXT,
                spotify_track_id TEXT,
                song_id INTEGER,
                position INTEGER,
                added_at TEXT
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_songs_source_external_id
            ON songs(source, external_id);
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS ix_spotify_playlist_tracks_playlist
            ON spotify_playlist_tracks(spotify_playlist_id, position);
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS playback_history (
                id INTEGER PRIMARY KEY,
                source TEXT,
                external_uri TEXT,
                title TEXT,
                artist TEXT,
                started_at TEXT,
                ended_at TEXT,
                event_id INTEGER,
                crowd_result TEXT,
                notes TEXT
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS queue (
                id INTEGER PRIMARY KEY,
                source TEXT,
                external_uri TEXT,
                local_path TEXT,
                song_id INTEGER,
                title TEXT,
                artist TEXT,
                album TEXT,
                album_art_url TEXT,
                duration_ms INTEGER,
                queue_position INTEGER,
                status TEXT,
                created_at TEXT
            );
            """, cancellationToken);

        await AddColumnIfMissingAsync(connection, "queue", "local_path", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "queue", "album", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "queue", "album_art_url", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "queue", "duration_ms", "INTEGER", cancellationToken);

        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS ix_queue_status_position
            ON queue(status, queue_position);
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS album_art_cache (
                cache_key TEXT PRIMARY KEY,
                source TEXT NOT NULL,
                external_uri TEXT,
                title TEXT,
                artist TEXT,
                original_uri TEXT,
                content_type TEXT,
                image_bytes BLOB,
                local_file_path TEXT,
                updated_at TEXT
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS ix_album_art_cache_source_external_uri
            ON album_art_cache(source, external_uri);
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS local_playlists (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS local_playlist_tracks (
                playlist_id INTEGER NOT NULL,
                file_path TEXT NOT NULL,
                position INTEGER NOT NULL,
                added_at TEXT NOT NULL,
                PRIMARY KEY (playlist_id, file_path),
                FOREIGN KEY (playlist_id) REFERENCES local_playlists(id) ON DELETE CASCADE
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS ix_local_playlist_tracks_playlist_position
            ON local_playlist_tracks(playlist_id, position);
            """, cancellationToken);
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await ExecuteAsync(connection, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};", cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
