using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Data.Repositories;

public sealed class SpotifyImportRepository
{
    private const int DurationMatchToleranceMs = 2500;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public SpotifyImportRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task<SpotifyImportResult> ImportPlaylistAsync(
        SpotifyPlaylistSummary playlist,
        IReadOnlyList<SpotifyTrackMetadata> tracks,
        CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var syncedAt = DateTimeOffset.UtcNow.ToString("O");
        await UpsertPlaylistAsync(connection, transaction, playlist, syncedAt, cancellationToken);
        await DeletePlaylistTrackRowsAsync(connection, transaction, playlist.SpotifyPlaylistId, cancellationToken);

        var imported = 0;
        var updated = 0;
        var unavailable = 0;
        var likelyMatches = 0;
        var playlistTrackRows = 0;

        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            if (track.IsUnavailable || string.IsNullOrWhiteSpace(track.SpotifyTrackId))
            {
                unavailable++;
                continue;
            }

            var likelyLocalMatchId = await FindLikelyLocalMatchAsync(connection, transaction, track, cancellationToken);
            if (likelyLocalMatchId is not null)
            {
                likelyMatches++;
            }

            var existingSongId = await FindSpotifySongIdAsync(connection, transaction, track.SpotifyTrackId, cancellationToken);
            var songId = existingSongId ?? await NextSongIdAsync(connection, transaction, cancellationToken);

            if (existingSongId is null)
            {
                imported++;
            }
            else
            {
                updated++;
            }

            await UpsertSpotifySongAsync(
                connection,
                transaction,
                songId,
                playlist.SpotifyPlaylistId,
                track,
                likelyLocalMatchId,
                syncedAt,
                cancellationToken);

            await InsertPlaylistTrackAsync(
                connection,
                transaction,
                playlist.SpotifyPlaylistId,
                track,
                songId,
                index + 1,
                cancellationToken);

            playlistTrackRows++;
        }

        await transaction.CommitAsync(cancellationToken);

        return new SpotifyImportResult
        {
            ImportedCount = imported,
            UpdatedCount = updated,
            UnavailableCount = unavailable,
            LikelyLocalMatchCount = likelyMatches,
            PlaylistTrackCount = playlistTrackRows
        };
    }

    private static async Task UpsertPlaylistAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SpotifyPlaylistSummary playlist,
        string syncedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO spotify_playlists (
                spotify_playlist_id,
                name,
                owner_name,
                track_count,
                snapshot_id,
                external_url,
                last_synced_at)
            VALUES (
                $spotify_playlist_id,
                $name,
                $owner_name,
                $track_count,
                $snapshot_id,
                $external_url,
                $last_synced_at)
            ON CONFLICT(spotify_playlist_id) DO UPDATE SET
                name = excluded.name,
                owner_name = excluded.owner_name,
                track_count = excluded.track_count,
                snapshot_id = excluded.snapshot_id,
                external_url = excluded.external_url,
                last_synced_at = excluded.last_synced_at;
            """;
        command.Parameters.AddWithValue("$spotify_playlist_id", playlist.SpotifyPlaylistId);
        command.Parameters.AddWithValue("$name", playlist.Name);
        command.Parameters.AddWithValue("$owner_name", (object?)playlist.OwnerName ?? DBNull.Value);
        command.Parameters.AddWithValue("$track_count", playlist.TrackCount);
        command.Parameters.AddWithValue("$snapshot_id", (object?)playlist.SnapshotId ?? DBNull.Value);
        command.Parameters.AddWithValue("$external_url", (object?)playlist.ExternalUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_synced_at", syncedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeletePlaylistTrackRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM spotify_playlist_tracks WHERE spotify_playlist_id = $spotify_playlist_id;";
        command.Parameters.AddWithValue("$spotify_playlist_id", playlistId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int?> FindLikelyLocalMatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SpotifyTrackMetadata track,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id
            FROM songs
            WHERE source = 'local'
              AND lower(title) = lower($title)
              AND lower(artist) = lower($artist)
              AND duration_ms IS NOT NULL
              AND ABS(duration_ms - $duration_ms) <= $duration_tolerance
            ORDER BY ABS(duration_ms - $duration_ms)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$duration_ms", track.DurationMs);
        command.Parameters.AddWithValue("$duration_tolerance", DurationMatchToleranceMs);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private static async Task<int?> FindSpotifySongIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string spotifyTrackId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id
            FROM songs
            WHERE source = 'spotify'
              AND external_id = $external_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$external_id", spotifyTrackId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private static async Task<int> NextSongIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(id), 0) + 1 FROM songs;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task UpsertSpotifySongAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int songId,
        string playlistId,
        SpotifyTrackMetadata track,
        int? likelyLocalMatchId,
        string syncedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO songs (
                id,
                title,
                artist,
                duration,
                bpm,
                song_key,
                energy_score,
                genre,
                tags,
                rating,
                album_art_path,
                source,
                external_id,
                external_uri,
                external_url,
                album,
                duration_ms,
                popularity,
                imported_from_playlist_id,
                last_synced_at,
                likely_local_match_song_id)
            VALUES (
                $id,
                $title,
                $artist,
                $duration,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                $album_art_path,
                $source,
                $external_id,
                $external_uri,
                $external_url,
                $album,
                $duration_ms,
                $popularity,
                $imported_from_playlist_id,
                $last_synced_at,
                $likely_local_match_song_id)
            ON CONFLICT(source, external_id) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                duration = excluded.duration,
                external_uri = excluded.external_uri,
                external_url = excluded.external_url,
                album_art_path = excluded.album_art_path,
                album = excluded.album,
                duration_ms = excluded.duration_ms,
                popularity = excluded.popularity,
                imported_from_playlist_id = excluded.imported_from_playlist_id,
                last_synced_at = excluded.last_synced_at,
                likely_local_match_song_id = excluded.likely_local_match_song_id;
            """;
        command.Parameters.AddWithValue("$id", songId);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$duration", TimeSpan.FromMilliseconds(track.DurationMs).ToString());
        command.Parameters.AddWithValue("$source", SongSources.Spotify);
        command.Parameters.AddWithValue("$external_id", track.SpotifyTrackId);
        command.Parameters.AddWithValue("$external_uri", (object?)track.SpotifyUri ?? DBNull.Value);
        command.Parameters.AddWithValue("$external_url", (object?)track.ExternalUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$album_art_path", (object?)track.AlbumArtUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$album", (object?)track.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration_ms", track.DurationMs);
        command.Parameters.AddWithValue("$popularity", (object?)track.Popularity ?? DBNull.Value);
        command.Parameters.AddWithValue("$imported_from_playlist_id", playlistId);
        command.Parameters.AddWithValue("$last_synced_at", syncedAt);
        command.Parameters.AddWithValue("$likely_local_match_song_id", (object?)likelyLocalMatchId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPlaylistTrackAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string playlistId,
        SpotifyTrackMetadata track,
        int songId,
        int position,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO spotify_playlist_tracks (
                spotify_playlist_id,
                spotify_track_id,
                song_id,
                position,
                added_at)
            VALUES (
                $spotify_playlist_id,
                $spotify_track_id,
                $song_id,
                $position,
                $added_at);
            """;
        command.Parameters.AddWithValue("$spotify_playlist_id", playlistId);
        command.Parameters.AddWithValue("$spotify_track_id", track.SpotifyTrackId);
        command.Parameters.AddWithValue("$song_id", songId);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$added_at", (object?)track.AddedAt?.ToString("O") ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
