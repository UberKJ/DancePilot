using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Tests;

public sealed class LocalMusicRepositoryTests
{
    [Fact]
    public async Task UpsertTrackAsync_UpdatesExistingTrackByFilePath()
    {
        var database = CreateTestDatabase();
        try
        {
            var path = CreateAudioFile(database.RootFolder, "Line Dance", "Copperhead Road.mp3");
            var repository = new LocalMusicRepository(database.ConnectionFactory);
            await repository.UpsertTrackAsync(CreateTrack(path) with
            {
                Title = "Old Title",
                Artist = "Old Artist"
            });

            await repository.UpsertTrackAsync(CreateTrack(path) with
            {
                Title = "Copperhead Road",
                Artist = "Steve Earle"
            });

            var tracks = await repository.GetAllTracksAsync();

            var track = Assert.Single(tracks);
            Assert.Equal("Copperhead Road", track.Title);
            Assert.Equal("Steve Earle", track.Artist);
            Assert.Equal(path, track.FilePath);
        }
        finally
        {
            DeleteTestDatabase(database);
        }
    }

    [Fact]
    public async Task SearchTracksAsync_ReturnsSavedTracksWithoutScanning()
    {
        var database = CreateTestDatabase();
        try
        {
            var repository = new LocalMusicRepository(database.ConnectionFactory);
            await repository.UpsertTracksAsync(
            [
                CreateTrack(CreateAudioFile(database.RootFolder, "Salsa", "DJ Test - Sunset Salsa.mp3")) with
                {
                    Title = "Sunset Salsa",
                    Artist = "DJ Test",
                    Album = "Saved Cache"
                },
                CreateTrack(CreateAudioFile(database.RootFolder, "Waltz", "Dance Floor - Last Waltz.mp3")) with
                {
                    Title = "Last Waltz",
                    Artist = "Dance Floor"
                }
            ]);

            var results = await repository.SearchTracksAsync("salsa cache");

            var result = Assert.Single(results);
            Assert.Equal("Sunset Salsa", result.Title);
            Assert.Equal("Saved Cache", result.Album);
        }
        finally
        {
            DeleteTestDatabase(database);
        }
    }

    [Fact]
    public void LocalQueueItem_IncludesLocalPathFromSavedTrack()
    {
        var path = Path.Combine(Path.GetTempPath(), "DancePilot.Tests", $"{Guid.NewGuid():N}", "Local Cue.mp3");
        var track = CreateTrack(path) with
        {
            Title = "Local Cue",
            Artist = "DancePilot",
            Album = "Local Library",
            AlbumArtUrl = "file:///C:/DancePilot/AlbumArt/local-cue.jpg",
            Duration = TimeSpan.FromMinutes(3)
        };
        var displayItem = TrackDisplayItem.FromLocal(track);

        var queueItem = new DancePilotQueueItem
        {
            Id = 1,
            Source = displayItem.Source,
            ExternalUri = displayItem.ExternalUri ?? string.Empty,
            LocalPath = displayItem.LocalPath,
            Title = displayItem.Title,
            Artist = displayItem.Artist,
            Album = displayItem.Album,
            AlbumArtUrl = displayItem.AlbumArt,
            Duration = displayItem.Duration
        };

        Assert.Equal(SongSources.Local, queueItem.Source);
        Assert.Equal(path, queueItem.LocalPath);
        Assert.Equal(path, queueItem.ResolvedLocalPath);
        Assert.Equal("Local Library", queueItem.Album);
        Assert.Equal(TimeSpan.FromMinutes(3), queueItem.Duration);
    }

    [Fact]
    public async Task AlbumArtPath_PersistsWithLocalTrack()
    {
        var database = CreateTestDatabase();
        try
        {
            var path = CreateAudioFile(database.RootFolder, "Covers", "Covered Track.mp3");
            var repository = new LocalMusicRepository(database.ConnectionFactory);
            await repository.UpsertTrackAsync(CreateTrack(path) with
            {
                AlbumArtUrl = "file:///C:/Users/test/AppData/Local/DancePilot/AlbumArt/covered.jpg"
            });

            var saved = await repository.GetTrackByPathAsync(path);

            Assert.NotNull(saved);
            Assert.Equal("file:///C:/Users/test/AppData/Local/DancePilot/AlbumArt/covered.jpg", saved.AlbumArtUrl);
        }
        finally
        {
            DeleteTestDatabase(database);
        }
    }

    [Fact]
    public async Task RemoveMissingTracksAsync_RemovesTracksNotInCurrentScan()
    {
        var database = CreateTestDatabase();
        try
        {
            var keepPath = CreateAudioFile(database.RootFolder, "Keep", "Keep Me.mp3");
            var stalePath = CreateAudioFile(database.RootFolder, "Stale", "Missing Later.mp3");
            var repository = new LocalMusicRepository(database.ConnectionFactory);
            await repository.UpsertTracksAsync(
            [
                CreateTrack(keepPath),
                CreateTrack(stalePath)
            ]);

            var removed = await repository.RemoveMissingTracksAsync([keepPath]);
            var tracks = await repository.GetAllTracksAsync();
            var stats = await repository.GetLibraryStatsAsync();

            Assert.Equal(1, removed);
            var track = Assert.Single(tracks);
            Assert.Equal(keepPath, track.FilePath);
            Assert.Equal(1, stats.TrackCount);
        }
        finally
        {
            DeleteTestDatabase(database);
        }
    }

    [Fact]
    public async Task LocalLibrarySettings_RoundTripsScanState()
    {
        var database = CreateTestDatabase();
        try
        {
            var repository = new LocalLibrarySettingsRepository(database.ConnectionFactory);
            var started = DateTimeOffset.UtcNow.AddMinutes(-2);
            var completed = DateTimeOffset.UtcNow;
            await repository.SaveAsync(new LocalLibrarySettings
            {
                LibraryFolderPath = database.RootFolder,
                LastScanStartedAt = started,
                LastScanCompletedAt = completed,
                TrackCount = 24,
                AlbumArtCacheFolderPath = Path.Combine(database.RootFolder, "AlbumArt")
            });

            var settings = await repository.LoadAsync();

            Assert.Equal(database.RootFolder, settings.LibraryFolderPath);
            Assert.Equal(24, settings.TrackCount);
            Assert.Equal(started.ToUnixTimeSeconds(), settings.LastScanStartedAt?.ToUnixTimeSeconds());
            Assert.Equal(completed.ToUnixTimeSeconds(), settings.LastScanCompletedAt?.ToUnixTimeSeconds());
            Assert.Equal(Path.Combine(database.RootFolder, "AlbumArt"), settings.AlbumArtCacheFolderPath);
        }
        finally
        {
            DeleteTestDatabase(database);
        }
    }

    private static TestDatabase CreateTestDatabase()
    {
        var rootFolder = Path.Combine(Path.GetTempPath(), "DancePilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootFolder);
        var databasePath = Path.Combine(rootFolder, "dancepilot.sqlite");
        return new TestDatabase(
            rootFolder,
            new SqliteConnectionFactory(new DatabaseOptions { DatabasePath = databasePath }));
    }

    private static string CreateAudioFile(string rootFolder, string folderName, string fileName)
    {
        var folder = Path.Combine(rootFolder, folderName);
        Directory.CreateDirectory(folder);
        var path = Path.GetFullPath(Path.Combine(folder, fileName));
        File.WriteAllText(path, "fake audio");
        return path;
    }

    private static LocalMusicTrack CreateTrack(string filePath) => new()
    {
        FilePath = Path.GetFullPath(filePath),
        Title = Path.GetFileNameWithoutExtension(filePath),
        Artist = "Unknown Artist",
        Album = "Test Album",
        Duration = TimeSpan.FromSeconds(185),
        Extension = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
        Folder = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty,
        FileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : null,
        LastModifiedAt = File.Exists(filePath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(filePath))
            : DateTimeOffset.UtcNow
    };

    private static void DeleteTestDatabase(TestDatabase database)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(database.RootFolder))
        {
            Directory.Delete(database.RootFolder, recursive: true);
        }
    }

    private sealed record TestDatabase(
        string RootFolder,
        SqliteConnectionFactory ConnectionFactory);
}
