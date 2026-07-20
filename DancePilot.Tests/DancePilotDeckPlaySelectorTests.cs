using DancePilot.Core.Models;
using DancePilot.Core.Spotify;

namespace DancePilot.Tests;

public sealed class DancePilotDeckPlaySelectorTests
{
    [Fact]
    public void FindItemForUserPlay_ReturnsSelectedLocalItemEvenWhenLastPlayedPointsToIt()
    {
        var queue = new[]
        {
            CreateItem(1, SongSources.Local, @"C:\Music\Local One.mp3"),
            CreateItem(2, SongSources.Spotify, "spotify:track:two")
        };

        var selection = DancePilotDeckPlaySelector.FindItemForUserPlay(
            queue,
            selectedId: 1,
            isAudiblyPlaying: false,
            lastPlayedId: 1);

        Assert.Same(queue[0], selection.Item);
        Assert.False(selection.ShouldClearSelectedId);
    }

    [Fact]
    public void FindItemForUserPlay_ReturnsFirstLocalItemWhenNoSelectedItemExists()
    {
        var queue = new[]
        {
            CreateItem(1, SongSources.Local, @"C:\Music\First Local.mp3"),
            CreateItem(2, SongSources.Spotify, "spotify:track:two")
        };

        var selection = DancePilotDeckPlaySelector.FindItemForUserPlay(
            queue,
            selectedId: null,
            isAudiblyPlaying: false,
            lastPlayedId: null);

        Assert.Same(queue[0], selection.Item);
        Assert.False(selection.ShouldClearSelectedId);
    }

    [Fact]
    public void FindItemForUserPlay_ClearsStaleSelectedIdAndReturnsFirstItem()
    {
        var queue = new[]
        {
            CreateItem(1, SongSources.Local, @"C:\Music\First Local.mp3"),
            CreateItem(2, SongSources.Spotify, "spotify:track:two")
        };

        var selection = DancePilotDeckPlaySelector.FindItemForUserPlay(
            queue,
            selectedId: 99,
            isAudiblyPlaying: false,
            lastPlayedId: null);

        Assert.Same(queue[0], selection.Item);
        Assert.True(selection.ShouldClearSelectedId);
    }

    [Fact]
    public void FindItemForUserPlay_DoesNotUseLastPlayedAsInitialNextCursor()
    {
        var queue = new[]
        {
            CreateItem(1, SongSources.Local, @"C:\Music\First Local.mp3"),
            CreateItem(2, SongSources.Spotify, "spotify:track:two")
        };

        var selection = DancePilotDeckPlaySelector.FindItemForUserPlay(
            queue,
            selectedId: null,
            isAudiblyPlaying: false,
            lastPlayedId: 1);

        Assert.Same(queue[0], selection.Item);
    }

    private static DancePilotQueueItem CreateItem(int id, string source, string sourcePath) => new()
    {
        Id = id,
        DeckName = "Deck A",
        Source = source,
        ExternalUri = source == SongSources.Spotify ? sourcePath : string.Empty,
        LocalPath = source == SongSources.Local ? sourcePath : null,
        Title = $"Song {id}",
        Artist = "DancePilot",
        QueuePosition = id
    };
}
