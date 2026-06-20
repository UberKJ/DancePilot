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
                DefaultVolume = 72
            });

            var queueRepository = new PlaybackQueueRepository(connectionFactory);
            await queueRepository.EnqueueAsync(new SpotifyTrackMetadata
            {
                SpotifyTrackId = "track-123",
                Title = "Test Song",
                Artist = "Test Artist",
                DurationMs = 180000,
                SpotifyUri = "spotify:track:track-123"
            });

            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync();

            Assert.True(await TableExistsAsync(connection, "app_settings"));
            Assert.True(await TableExistsAsync(connection, "playback_history"));
            Assert.True(await TableExistsAsync(connection, "queue"));
            Assert.Equal("device-123", await GetSettingAsync(connection, "spotify_selected_device_id"));
            Assert.Equal(1, await CountRowsAsync(connection, "queue"));
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
