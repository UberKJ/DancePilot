using DancePilot.Core.Models;

namespace DancePilot.Tests;

public sealed class LiveEventMusicSourcesTests
{
    [Fact]
    public void SourceOptions_KeepRequiredOrderAndProviders()
    {
        Assert.Equal(
            ["Spotify", "TIDAL", "Local", "YouTube"],
            LiveEventMusicSources.All);
    }

    [Fact]
    public void SelectingTidal_ActivatesTidalState()
    {
        var selectedSource = LiveEventMusicSources.Normalize("TIDAL");

        Assert.Equal(LiveEventMusicSources.Tidal, selectedSource);
        Assert.True(LiveEventMusicSources.IsTidal(selectedSource));
    }

    [Fact]
    public void Tidal_IsSelectableButDoesNotSupportDeckPlayback()
    {
        Assert.Contains(LiveEventMusicSources.Tidal, LiveEventMusicSources.All);
        Assert.False(LiveEventMusicSources.SupportsDeckPlayback(LiveEventMusicSources.Tidal));
    }

    [Fact]
    public void ExistingPlayableProviders_RemainAvailable()
    {
        Assert.Contains(LiveEventMusicSources.Spotify, LiveEventMusicSources.All);
        Assert.Contains(LiveEventMusicSources.Local, LiveEventMusicSources.All);
        Assert.Contains(LiveEventMusicSources.YouTube, LiveEventMusicSources.All);
        Assert.True(LiveEventMusicSources.SupportsDeckPlayback(LiveEventMusicSources.Spotify));
        Assert.True(LiveEventMusicSources.SupportsDeckPlayback(LiveEventMusicSources.Local));
        Assert.False(LiveEventMusicSources.SupportsDeckPlayback(LiveEventMusicSources.YouTube));
    }
}
