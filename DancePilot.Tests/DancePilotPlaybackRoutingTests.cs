using DancePilot.Core.Models;
using DancePilot.Core.Spotify;

namespace DancePilot.Tests;

public sealed class DancePilotPlaybackRoutingTests
{
    [Fact]
    public void SpotifyQueueItem_RoutesToSpotifyPlayback()
    {
        var item = new DancePilotQueueItem
        {
            Id = 1,
            DeckName = "Deck A",
            Source = SongSources.Spotify,
            ExternalUri = "spotify:track:abc123",
            Title = "Spotify Song",
            Artist = "DancePilot"
        };

        var route = DancePilotPlaybackRouter.Resolve(item);

        Assert.Equal(DancePilotPlaybackRouteKind.SpotifyConnect, route.Kind);
        Assert.Equal("spotify:track:abc123", route.SpotifyUri);
        Assert.Null(route.LocalPath);
    }

    [Fact]
    public void LocalQueueItem_RoutesToLocalPlayback()
    {
        var item = new DancePilotQueueItem
        {
            Id = 2,
            DeckName = "Deck B",
            Source = SongSources.Local,
            LocalPath = @"C:\Music\Line Dance\Copperhead Road.mp3",
            Title = "Copperhead Road",
            Artist = "DancePilot"
        };

        var route = DancePilotPlaybackRouter.Resolve(item);

        Assert.Equal(DancePilotPlaybackRouteKind.LocalMediaPlayer, route.Kind);
        Assert.Equal(@"C:\Music\Line Dance\Copperhead Road.mp3", route.LocalPath);
        Assert.Null(route.SpotifyUri);
    }

    [Fact]
    public void LocalQueueItem_WithMissingSpotifyUri_IsNotUnsupported()
    {
        var item = new DancePilotQueueItem
        {
            Id = 3,
            DeckName = "Deck A",
            Source = SongSources.Local,
            ExternalUri = string.Empty,
            LocalPath = @"C:\Music\Line Dance\Cupid Shuffle.wav",
            Title = "Cupid Shuffle",
            Artist = "DancePilot"
        };

        var route = DancePilotPlaybackRouter.Resolve(item);

        Assert.Equal(DancePilotPlaybackRouteKind.LocalMediaPlayer, route.Kind);
        Assert.True(route.IsPlayable);
    }

    [Fact]
    public void DeckState_ReportsQueueCountAndSelectedPausedStatus()
    {
        var currentTrack = new DancePilotQueueItem
        {
            Id = 4,
            DeckName = "Deck B",
            Source = SongSources.Local,
            LocalPath = @"C:\Music\Line Dance\Electric Slide.mp3",
            Title = "Electric Slide",
            Artist = "DancePilot"
        };

        var state = DancePilotDeckState.Create(
            "Deck B",
            isPlaying: false,
            isLoaded: true,
            isSelected: true,
            queueCount: 24,
            currentTrack);

        Assert.Equal("Deck B", state.DeckName);
        Assert.False(state.IsPlaying);
        Assert.True(state.IsPaused);
        Assert.True(state.IsSelected);
        Assert.Equal(24, state.QueueCount);
        Assert.Same(currentTrack, state.CurrentTrack);
        Assert.Equal(SongSources.Local, state.Source);
        Assert.Equal("24 queued", state.QueueCountText);
        Assert.Equal("Paused / Selected / 24 queued", state.StatusText);
    }
}
