using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;

namespace DancePilot.Data.Repositories;

public sealed class SpotifyLibraryRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public SpotifyLibraryRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task<IReadOnlyList<SpotifyPlaylistSummary>> GetImportedPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT spotify_playlist_id, name, owner_name, track_count, snapshot_id, external_url
            FROM spotify_playlists
            ORDER BY name;
            """;

        var playlists = new List<SpotifyPlaylistSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            playlists.Add(new SpotifyPlaylistSummary
            {
                SpotifyPlaylistId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                Name = reader.IsDBNull(1) ? "Untitled Playlist" : reader.GetString(1),
                OwnerName = reader.IsDBNull(2) ? null : reader.GetString(2),
                TrackCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                SnapshotId = reader.IsDBNull(4) ? null : reader.GetString(4),
                ExternalUrl = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return playlists;
    }

    public async Task<IReadOnlyList<SpotifyTrackMetadata>> GetImportedPlaylistTracksAsync(
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.external_id,
                   s.title,
                   s.artist,
                   s.album,
                   s.duration_ms,
                   s.external_uri,
                   s.external_url,
                   s.popularity,
                   COALESCE(s.bpm, local.bpm) AS bpm,
                   COALESCE(s.song_key, local.song_key) AS song_key,
                   s.album_art_path
            FROM spotify_playlist_tracks pt
            INNER JOIN songs s
                ON s.id = pt.song_id
            LEFT JOIN songs local
                ON local.id = s.likely_local_match_song_id
                AND local.source = 'local'
            WHERE pt.spotify_playlist_id = $spotify_playlist_id
            ORDER BY pt.position, pt.id;
            """;
        command.Parameters.AddWithValue("$spotify_playlist_id", spotifyPlaylistId);

        var tracks = new List<SpotifyTrackMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tracks.Add(new SpotifyTrackMetadata
            {
                SpotifyTrackId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                Title = reader.IsDBNull(1) ? "Untitled Track" : reader.GetString(1),
                Artist = reader.IsDBNull(2) ? "Unknown Artist" : reader.GetString(2),
                Album = reader.IsDBNull(3) ? null : reader.GetString(3),
                DurationMs = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                SpotifyUri = reader.IsDBNull(5) ? null : reader.GetString(5),
                ExternalUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                Popularity = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                BPM = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                MusicalKey = reader.IsDBNull(9) ? null : reader.GetString(9),
                AlbumArtUrl = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }

        return tracks;
    }
}
