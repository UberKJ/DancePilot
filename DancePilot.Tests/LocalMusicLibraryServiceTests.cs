using DancePilot.Services.LocalMusic;

namespace DancePilot.Tests;

public sealed class LocalMusicLibraryServiceTests
{
    [Fact]
    public async Task LoadFromFolderAsync_LoadsSupportedAudioFilesAndParsesArtistTitle()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DancePilot.LocalMusic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var nested = Path.Combine(folder, "Neil Diamond");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(nested, "003 - Neil Diamond - Sweet Caroline.mp3"), "fake");
            await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "not music");

            var service = new LocalMusicLibraryService();
            var tracks = await service.LoadFromFolderAsync(folder);

            var track = Assert.Single(tracks);
            Assert.Equal("Neil Diamond", track.Artist);
            Assert.Equal("Sweet Caroline", track.Title);
            Assert.Equal("MP3", track.Extension);

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
}
