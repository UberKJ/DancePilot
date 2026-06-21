using DancePilot.Core.Models;
using DancePilot.Services.LocalMusic;

namespace DancePilot.Tests;

public sealed class LocalMusicLibraryServiceTests
{
    [Fact]
    public async Task LoadFromFolderAsync_LoadsSupportedMusicFilesAndParsesArtistTitle()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DancePilot.LocalMusic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var nested = Path.Combine(folder, "Neil Diamond");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(nested, "003 - Neil Diamond - Sweet Caroline.mp3"), "fake");
            await File.WriteAllTextAsync(Path.Combine(nested, "Neil Diamond - Cracklin Rosie.mp4"), "fake");
            await File.WriteAllTextAsync(Path.Combine(folder, "Dance Floor - Last Waltz.wav"), "fake");
            await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "not music");

            var service = new LocalMusicLibraryService();
            var tracks = await service.LoadFromFolderAsync(folder);

            Assert.Equal(3, tracks.Count);
            var track = Assert.Single(tracks, track => track.Extension == "MP3");
            Assert.Equal("Neil Diamond", track.Artist);
            Assert.Equal("Sweet Caroline", track.Title);
            Assert.Equal("MP3", track.Extension);
            Assert.Contains(tracks, track => track.Extension == "MP4");
            Assert.Contains(tracks, track => track.Extension == "WAV");

            var searchResults = service.Search(tracks, "sweet");
            Assert.Single(searchResults);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_MatchesEmbeddedMixMetadata()
    {
        var service = new LocalMusicLibraryService();
        var tracks = new[]
        {
            new LocalMusicTrack
            {
                FilePath = @"C:\Music\DJ Test - Sunset Salsa.mp3",
                Title = "Sunset Salsa",
                Artist = "DJ Test",
                Album = "Dance Night",
                Genre = "Salsa",
                BPM = 128,
                MusicalKey = "Am",
                Extension = "MP3",
                Folder = @"C:\Music",
                LastModifiedAt = DateTimeOffset.UtcNow
            }
        };

        Assert.Single(service.Search(tracks, "128 am"));
        Assert.Single(service.Search(tracks, "salsa"));
    }

    [Fact]
    public void MixDisplay_OnlyShowsAvailableBpmOrKey()
    {
        var baseTrack = new LocalMusicTrack
        {
            FilePath = @"C:\Music\Test.mp3",
            Title = "Test",
            Artist = "DJ Test",
            Extension = "MP3",
            Folder = @"C:\Music",
            LastModifiedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(string.Empty, baseTrack.MixDisplay);
        Assert.Equal("Am", (baseTrack with { MusicalKey = "Am" }).MixDisplay);
        Assert.Equal("128 BPM", (baseTrack with { BPM = 128 }).MixDisplay);
        Assert.Equal("128 BPM / Am", (baseTrack with { BPM = 128, MusicalKey = "Am" }).MixDisplay);
    }
}
