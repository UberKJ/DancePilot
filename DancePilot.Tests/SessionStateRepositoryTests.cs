using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Tests;

public sealed class SessionStateRepositoryTests
{
    [Fact]
    public async Task SaveAsync_RoundTripsDeckQueuesAndLoadedPlaylistRows()
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

            var repository = new SessionStateRepository(connectionFactory);
            var state = new DancePilotSessionState
            {
                ActiveSource = "Spotify",
                ActiveDeckName = "Deck B",
                SelectedDeckBQueueItemId = 7,
                NextDeckQueueItemId = 8,
                DeckBQueue =
                [
                    new DancePilotQueueItem
                    {
                        Id = 7,
                        DeckName = "Deck B",
                        Source = SongSources.Spotify,
                        ExternalUri = "spotify:track:abc",
                        Title = "Test Song",
                        Artist = "Test Artist",
                        QueuePosition = 1
                    }
                ],
                SelectedSpotifyPlaylist = new SpotifyPlaylistSummary
                {
                    SpotifyPlaylistId = "playlist-1",
                    Name = "Dance Floor",
                    TrackCount = 1
                },
                SpotifyPreviewTracks =
                [
                    new SpotifyTrackMetadata
                    {
                        SpotifyTrackId = "abc",
                        Title = "Test Song",
                        Artist = "Test Artist",
                        SpotifyUri = "spotify:track:abc",
                        DurationMs = 180000
                    }
                ]
            };

            await repository.SaveAsync(state);

            var restored = await repository.LoadAsync();

            Assert.NotNull(restored);
            Assert.Equal("Deck B", restored.ActiveDeckName);
            Assert.Equal(8, restored.NextDeckQueueItemId);
            Assert.Equal(7, restored.SelectedDeckBQueueItemId);
            Assert.Equal("Test Song", Assert.Single(restored.DeckBQueue).Title);
            Assert.Equal("Dance Floor", restored.SelectedSpotifyPlaylist?.Name);
            Assert.Equal("spotify:track:abc", Assert.Single(restored.SpotifyPreviewTracks).SpotifyUri);
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
}
