using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;

namespace DancePilot.Data.Repositories;

public sealed class PlaybackHistoryRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public PlaybackHistoryRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task RecordStartedAsync(SpotifyTrackMetadata track, string? notes = null, CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO playback_history (
                source,
                external_uri,
                title,
                artist,
                started_at,
                notes)
            VALUES (
                $source,
                $external_uri,
                $title,
                $artist,
                $started_at,
                $notes);
            """;
        command.Parameters.AddWithValue("$source", SongSources.Spotify);
        command.Parameters.AddWithValue("$external_uri", track.SpotifyUri ?? string.Empty);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$started_at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
