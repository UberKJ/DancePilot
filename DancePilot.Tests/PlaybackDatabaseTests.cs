using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using DancePilot.Services.Media;
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
                Album = "Test Album",
                AlbumArtUrl = "https://image.example/test-song.jpg",
                DurationMs = 180000,
                SpotifyUri = "spotify:track:track-123"
            });
            var pendingQueue = await queueRepository.GetPendingAsync();

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
            Assert.Single(pendingQueue);
            Assert.Equal(SongSources.Spotify, pendingQueue[0].Source);
            Assert.Equal("Test Song", pendingQueue[0].Title);
            Assert.Equal("Test Artist", pendingQueue[0].Artist);
            Assert.Equal("Test Album", pendingQueue[0].Album);
            Assert.Equal("https://image.example/test-song.jpg", pendingQueue[0].AlbumArtUrl);
            Assert.Equal("spotify:track:track-123", pendingQueue[0].ExternalUri);
            Assert.Null(pendingQueue[0].LocalPath);
            Assert.Equal(TimeSpan.FromMinutes(3), pendingQueue[0].Duration);
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

    [Fact]
    public async Task AlbumArtCacheService_ReturnsCachedFileUriBeforeNetworkLookup()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "DancePilot.Tests", $"{Guid.NewGuid():N}.sqlite");
        var cacheFolder = Path.Combine(Path.GetTempPath(), "DancePilot.Tests", $"{Guid.NewGuid():N}", "AlbumArt");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        try
        {
            var connectionFactory = new SqliteConnectionFactory(new DatabaseOptions
            {
                DatabasePath = databasePath
            });
            var repository = new AlbumArtCacheRepository(connectionFactory);
            var item = new DancePilotQueueItem
            {
                Id = 1,
                Source = SongSources.Spotify,
                ExternalUri = "spotify:track:cached-cover",
                Title = "Cached Cover",
                Artist = "DancePilot"
            };
            var cacheKey = AlbumArtCacheService.CreateCacheKey(item);
            await repository.SaveAsync(new AlbumArtCacheEntry
            {
                CacheKey = cacheKey,
                Source = item.Source,
                ExternalUri = item.ExternalUri,
                Title = item.Title,
                Artist = item.Artist,
                OriginalUri = "https://image.example/cached-cover.jpg",
                ContentType = "image/jpeg",
                ImageBytes = new byte[] { 1, 2, 3, 4 },
                LocalFilePath = null
            });

            var service = new AlbumArtCacheService(repository, new HttpClient(), cacheFolder);
            var cachedUri = await service.GetCachedAlbumArtUriAsync(item);

            Assert.NotNull(cachedUri);
            var cachedPath = new Uri(cachedUri).LocalPath;
            Assert.True(File.Exists(cachedPath));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(cachedPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            if (Directory.Exists(cacheFolder))
            {
                Directory.Delete(Path.GetDirectoryName(cacheFolder)!, recursive: true);
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
