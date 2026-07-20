using DancePilot.Core.Models;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Data.Repositories;

public sealed class LocalPlaylistRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public LocalPlaylistRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task<IReadOnlyList<LocalMusicPlaylist>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.name, p.created_at, p.updated_at, COUNT(t.file_path) AS track_count
            FROM local_playlists p
            LEFT JOIN local_playlist_tracks t ON t.playlist_id = p.id
            GROUP BY p.id, p.name, p.created_at, p.updated_at
            ORDER BY p.name COLLATE NOCASE;
            """;

        var playlists = new List<LocalMusicPlaylist>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            playlists.Add(ReadPlaylist(reader));
        }

        return playlists;
    }

    public async Task<LocalMusicPlaylist> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        var playlistName = NormalizeName(name);
        var now = DateTimeOffset.UtcNow.ToString("O");
        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO local_playlists (name, created_at, updated_at)
                VALUES ($name, $created_at, $updated_at)
                ON CONFLICT(name) DO UPDATE SET updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$name", playlistName);
            command.Parameters.AddWithValue("$created_at", now);
            command.Parameters.AddWithValue("$updated_at", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return await GetPlaylistByNameAsync(connection, playlistName, cancellationToken)
            ?? throw new InvalidOperationException("Local playlist could not be created.");
    }

    public async Task DeletePlaylistAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var trackCommand = connection.CreateCommand())
        {
            trackCommand.CommandText = "DELETE FROM local_playlist_tracks WHERE playlist_id = $id;";
            trackCommand.Parameters.AddWithValue("$id", playlistId);
            await trackCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var playlistCommand = connection.CreateCommand())
        {
            playlistCommand.CommandText = "DELETE FROM local_playlists WHERE id = $id;";
            playlistCommand.Parameters.AddWithValue("$id", playlistId);
            await playlistCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<int> AddTracksAsync(
        int playlistId,
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var paths = filePaths
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Select(filePath => filePath.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (playlistId <= 0 || paths.Count == 0)
        {
            return 0;
        }

        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var nextPosition = await GetNextPositionAsync(connection, playlistId, cancellationToken);
        var added = 0;
        foreach (var filePath in paths)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO local_playlist_tracks (playlist_id, file_path, position, added_at)
                VALUES ($playlist_id, $file_path, $position, $added_at);
                """;
            command.Parameters.AddWithValue("$playlist_id", playlistId);
            command.Parameters.AddWithValue("$file_path", filePath);
            command.Parameters.AddWithValue("$position", nextPosition);
            command.Parameters.AddWithValue("$added_at", DateTimeOffset.UtcNow.ToString("O"));
            var rows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (rows > 0)
            {
                added++;
                nextPosition++;
            }
        }

        if (added > 0)
        {
            await using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = (SqliteTransaction)transaction;
            updateCommand.CommandText = "UPDATE local_playlists SET updated_at = $updated_at WHERE id = $id;";
            updateCommand.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            updateCommand.Parameters.AddWithValue("$id", playlistId);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    public async Task<IReadOnlyList<string>> GetTrackFilePathsAsync(
        int playlistId,
        CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT file_path
            FROM local_playlist_tracks
            WHERE playlist_id = $playlist_id
            ORDER BY position, file_path COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$playlist_id", playlistId);

        var paths = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    private static async Task<int> GetNextPositionAsync(
        SqliteConnection connection,
        int playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(position), 0) + 1 FROM local_playlist_tracks WHERE playlist_id = $playlist_id;";
        command.Parameters.AddWithValue("$playlist_id", playlistId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<LocalMusicPlaylist?> GetPlaylistByNameAsync(
        SqliteConnection connection,
        string playlistName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.name, p.created_at, p.updated_at, COUNT(t.file_path) AS track_count
            FROM local_playlists p
            LEFT JOIN local_playlist_tracks t ON t.playlist_id = p.id
            WHERE p.name = $name
            GROUP BY p.id, p.name, p.created_at, p.updated_at
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", playlistName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadPlaylist(reader)
            : null;
    }

    private static LocalMusicPlaylist ReadPlaylist(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            CreatedAt = ParseDate(reader.GetString(2)),
            UpdatedAt = ParseDate(reader.GetString(3)),
            TrackCount = Convert.ToInt32(reader.GetValue(4))
        };

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private static string NormalizeName(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "Local Playlist" : name.Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80].Trim();
    }
}
