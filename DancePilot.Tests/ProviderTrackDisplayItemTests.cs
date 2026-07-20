using DancePilot.Core.Models;
using DancePilot.Services.Tidal;

namespace DancePilot.Tests;

public sealed class ProviderTrackDisplayItemTests
{
    [Fact]
    public void TidalTrackConversion_PreservesCatalogIdentityAndDisablesPlayback()
    {
        var track = new TidalTrackMetadata
        {
            ProviderTrackId = "tidal-track-42",
            Title = "Dance Track",
            Artist = "Test Artist",
            Album = "Test Album",
            ArtworkReference = "https://resources.tidal.com/art.jpg",
            TidalPageUrl = "https://tidal.com/browse/track/tidal-track-42",
            Duration = TimeSpan.FromMinutes(3)
        };

        var display = track.ToTrackDisplayItem();

        Assert.Equal(SongSources.Tidal, display.Source);
        Assert.Equal(track.ProviderTrackId, display.ProviderTrackId);
        Assert.Equal(track.ArtworkReference, display.AlbumArt);
        Assert.False(display.IsPlayable);
        Assert.Null(display.ExternalUri);
        Assert.Contains("not enabled", display.PlaybackHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SongConversion_PreservesOnlineProviderSource()
    {
        var song = CreateSong() with
        {
            Source = SongSources.Tidal,
            ExternalId = "tidal-track-42",
            ExternalUri = "tidal:track:42"
        };

        var display = TrackDisplayItem.FromSong(song);

        Assert.Equal(SongSources.Tidal, display.Source);
        Assert.Equal("tidal-track-42", display.ProviderTrackId);
        Assert.Equal("tidal:track:42", display.ExternalUri);
        Assert.Null(display.LocalPath);
        Assert.False(display.IsPlayable);
    }

    [Fact]
    public void SongConversion_PreservesLocalPathAndPlaybackCapability()
    {
        var song = CreateSong() with
        {
            Source = SongSources.Local,
            ExternalUri = @"C:\Music\Dance Track.flac"
        };

        var display = TrackDisplayItem.FromSong(song);

        Assert.Equal(SongSources.Local, display.Source);
        Assert.Equal(song.ExternalUri, display.LocalPath);
        Assert.Null(display.ExternalUri);
        Assert.True(display.IsPlayable);
    }

    private static Song CreateSong() => new()
    {
        Id = 1,
        Title = "Dance Track",
        Artist = "Test Artist",
        Duration = TimeSpan.FromMinutes(3),
        BPM = 120,
        Key = "8A",
        EnergyScore = 7,
        Genre = "Dance",
        Tags = string.Empty,
        Rating = 5,
        AlbumArtPath = string.Empty
    };
}
