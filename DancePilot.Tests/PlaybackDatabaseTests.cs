using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Tests;

public sealed class PlaybackDatabaseTests
{
    [Fact]
    public async Task MigratorAndRepositories_CreatePlaybackSettingsQueueAndHistoryTables()
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
            var settingsRepository = new PlaybackSettingsRepository(connectionFactory);
            await settingsRepository.SaveAsync(new SpotifyPlaybackSettings
            {
                SelectedDeviceId = "device-123",
                SelectedDeviceName = "Living Room",
                PlaybackMode = SpotifyPlaybackModes.SpotifyConnect,
                AutopilotEnabled = true,
                AutoplaySecondsBeforeEnd = 8,
                DefaultVolume = 72,
                DeckAVolume = 66,
                DeckBVolume = 81,
                RemovePlayedQueueItems = false
            });
            var loadedSettings = await settingsRepository.LoadAsync();

            var queueRepository = new PlaybackQueueRepository(connectionFactory);
            await queueRepository.EnqueueAsync(new SpotifyTrackMetadata
            {
                SpotifyTrackId = "track-123",
                Title = "Test Song",
                Artist = "Test Artist",
                DurationMs = 180000,
                SpotifyUri = "spotify:track:track-123"
            });

            var localPlaylistRepository = new LocalPlaylistRepository(connectionFactory);
            var localPlaylist = await localPlaylistRepository.CreatePlaylistAsync("Line Dance Local");
            var addedLocalPlaylistTracks = await localPlaylistRepository.AddTracksAsync(
                localPlaylist.Id,
                [
                    @"C:\Music\Line Dance\Copperhead Road.mp3",
                    @"C:\Music\Line Dance\Cupid Shuffle.wav",
                    @"C:\Music\Line Dance\Copperhead Road.mp3"
                ]);
            var savedLocalPlaylistTracks = await localPlaylistRepository.GetTrackFilePathsAsync(localPlaylist.Id);

            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync();

            Assert.True(await TableExistsAsync(connection, "app_settings"));
            Assert.True(await TableExistsAsync(connection, "album_art_cache"));
            Assert.True(await TableExistsAsync(connection, "local_playlists"));
            Assert.True(await TableExistsAsync(connection, "local_playlist_tracks"));
            Assert.True(await TableExistsAsync(connection, "playback_history"));
            Assert.True(await TableExistsAsync(connection, "queue"));
            Assert.Equal("device-123", await GetSettingAsync(connection, "spotify_selected_device_id"));
            Assert.Equal("66", await GetSettingAsync(connection, "deck_a_volume"));
            Assert.Equal("81", await GetSettingAsync(connection, "deck_b_volume"));
            Assert.Equal("False", await GetSettingAsync(connection, "deck_remove_played_queue_items"));
            Assert.False(loadedSettings.RemovePlayedQueueItems);
            Assert.Equal(66, loadedSettings.DeckAVolume);
            Assert.Equal(81, loadedSettings.DeckBVolume);
            Assert.Equal(1, await CountRowsAsync(connection, "queue"));
            Assert.Equal(2, addedLocalPlaylistTracks);
            Assert.Equal(2, savedLocalPlaylistTracks.Count);
            Assert.Equal(@"C:\Music\Line Dance\Copperhead Road.mp3", savedLocalPlaylistTracks[0]);

            var albumArtRepository = new AlbumArtCacheRepository(connectionFactory);
            await albumArtRepository.SaveAsync(new AlbumArtCacheEntry
            {
                CacheKey = "cover-123",
                Source = SongSources.Spotify,
                ExternalUri = "spotify:track:track-123",
                Title = "Test Song",
                Artist = "Test Artist",
                OriginalUri = "https://image.example/cover.jpg",
                ContentType = "image/jpeg",
                ImageBytes = new byte[] { 1, 2, 3 },
                LocalFilePath = @"C:\DancePilot\AlbumArtCache\cover-123.jpg"
            });
            var albumArt = await albumArtRepository.GetAsync("cover-123");
            Assert.NotNull(albumArt);
            Assert.Equal("image/jpeg", albumArt.ContentType);
            Assert.Equal(new byte[] { 1, 2, 3 }, albumArt.ImageBytes);
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

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table_name;";
        command.Parameters.AddWithValue("$table_name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<string?> GetSettingAsync(SqliteConnection connection, string key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountRowsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
