using DancePilot.Core.Spotify;

namespace DancePilot.Tests;

public sealed class DancePilotDeckStateTests
{
    [Fact]
    public void Create_MarksSelectedPausedDeckWithBothStates()
    {
        var state = DancePilotDeckState.Create(
            "Deck B",
            isPlaying: false,
            isLoaded: true,
            isSelected: true,
            queueCount: 3,
            currentTrack: null);

        Assert.Equal("Deck B", state.DeckName);
        Assert.False(state.IsPlaying);
        Assert.True(state.IsPaused);
        Assert.True(state.IsSelected);
        Assert.Equal("Paused / Selected / 3 queued", state.StatusText);
    }

    [Fact]
    public void Create_MarksSelectedPlayingDeckWithBothStates()
    {
        var state = DancePilotDeckState.Create(
            "Deck A",
            isPlaying: true,
            isLoaded: true,
            isSelected: true,
            queueCount: 1,
            currentTrack: null);

        Assert.True(state.IsPlaying);
        Assert.False(state.IsPaused);
        Assert.True(state.IsSelected);
        Assert.Equal("Playing / Selected / 1 queued", state.StatusText);
    }
}
