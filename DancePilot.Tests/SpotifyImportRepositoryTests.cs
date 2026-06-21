using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Tests;

public sealed class SpotifyImportRepositoryTests
{
    [Fact]
    public async Task ImportPlaylistAsync_MapsSpotifyTracksAndMarksLikelyLocalMatch()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "DancePilot.Tests", $"{Guid.NewGuid():N}.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        try
        {
            var connectionFactory = new SqliteConnectionFactory(new DatabaseOptions
            {
                DatabasePath = databasePath
            });
            await new DancePilotDatabaseMigrator(connectionFactory).MigrateAsync();
            await InsertLocalSongAsync(connectionFactory);

            var repository = new SpotifyImportRepository(connectionFactory);
            var playlist = new SpotifyPlaylistSummary
            {
                SpotifyPlaylistId = "playlist-123",
                Name = "Saturday Night Dance",
                OwnerName = "DancePilot",
                TrackCount = 1,
                SnapshotId = "snapshot-1",
                ExternalUrl = "https://open.spotify.com/playlist/playlist-123"
            };
            var tracks = new[]
            {
                new SpotifyTrackMetadata
                {
                    SpotifyTrackId = "track-456",
                    Title = "Sweet Caroline",
                    Artist = "Neil Diamond",
                    Album = "Brother Love's Travelling Salvation Show",
                    AlbumArtUrl = "https://image.example/sweet-caroline.jpg",
                    DurationMs = 201_500,
                    SpotifyUri = "spotify:track:track-456",
                    ExternalUrl = "https://open.spotify.com/track/track-456",
                    Popularity = 78,
                    AddedAt = DateTimeOffset.Parse("2026-06-20T09:30:00Z")
                }
            };

            var result = await repository.ImportPlaylistAsync(playlist, tracks);

            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(0, result.UpdatedCount);
            Assert.Equal(0, result.UnavailableCount);
            Assert.Equal(1, result.LikelyLocalMatchCount);
            Assert.Equal(1, result.PlaylistTrackCount);

            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync();

            await using var songCommand = connection.CreateCommand();
            songCommand.CommandText = """
                SELECT external_id, external_uri, external_url, album, duration_ms, popularity, imported_from_playlist_id, likely_local_match_song_id, album_art_path
                FROM songs
                WHERE source = 'spotify';
                """;
            await using var songReader = await songCommand.ExecuteReaderAsync();
            Assert.True(await songReader.ReadAsync());
            Assert.Equal("track-456", songReader.GetString(0));
            Assert.Equal("spotify:track:track-456", songReader.GetString(1));
            Assert.Equal("https://open.spotify.com/track/track-456", songReader.GetString(2));
            Assert.Equal("Brother Love's Travelling Salvation Show", songReader.GetString(3));
            Assert.Equal(201_500, songReader.GetInt32(4));
            Assert.Equal(78, songReader.GetInt32(5));
            Assert.Equal("playlist-123", songReader.GetString(6));
            Assert.Equal(1, songReader.GetInt32(7));
            Assert.Equal("https://image.example/sweet-caroline.jpg", songReader.GetString(8));
            await songReader.DisposeAsync();

            Assert.Equal(1, await CountRowsAsync(connection, "spotify_playlists"));
            Assert.Equal(1, await CountRowsAsync(connection, "spotify_playlist_tracks"));

            var libraryRepository = new SpotifyLibraryRepository(connectionFactory);
            var importedTracks = await libraryRepository.GetImportedPlaylistTracksAsync("playlist-123");
            var importedTrack = Assert.Single(importedTracks);
            Assert.Equal(128, importedTrack.BPM);
            Assert.Equal("A", importedTrack.MusicalKey);
            Assert.Equal("https://image.example/sweet-caroline.jpg", importedTrack.AlbumArtUrl);

            var trackByUri = await libraryRepository.GetImportedTrackByUriAsync("spotify:track:track-456", "track-456");
            Assert.NotNull(trackByUri);
            Assert.Equal("Sweet Caroline", trackByUri.Title);
            Assert.Equal("https://image.example/sweet-caroline.jpg", trackByUri.AlbumArtUrl);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static async Task InsertLocalSongAsync(SqliteConnectionFactory connectionFactory)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO songs (id, title, artist, duration, duration_ms, bpm, song_key, source)
            VALUES (1, 'Sweet Caroline', 'Neil Diamond', '00:03:21', 201000, 128, 'A', 'local');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountRowsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
