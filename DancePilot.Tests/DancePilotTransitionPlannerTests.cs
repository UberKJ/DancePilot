using DancePilot.Core.Models;
using DancePilot.Core.Spotify;

namespace DancePilot.Tests;

public sealed class DancePilotTransitionPlannerTests
{
    [Fact]
    public void AutoMode_OneDeckAAdvancesWithinDeckA()
    {
        var deckA = Queue(
            "Deck A",
            LocalItem(1, "Deck A"),
            SpotifyItem(2, "Deck A"));

        var decision = Decide(
            DancePilotTransitionModes.Auto,
            currentDeckName: "Deck A",
            currentItemId: 1,
            deckA,
            deckB: []);

        Assert.True(decision.HasTarget);
        Assert.Equal("Deck A", decision.ChosenDeckName);
        Assert.Equal(2, decision.ChosenItem?.Id);
        Assert.Equal("No playable item on Deck B", decision.FallbackReason);
    }

    [Fact]
    public void AutoMode_OneDeckBAdvancesWithinDeckB()
    {
        var deckB = Queue(
            "Deck B",
            SpotifyItem(10, "Deck B"),
            LocalItem(11, "Deck B"));

        var decision = Decide(
            DancePilotTransitionModes.Auto,
            currentDeckName: "Deck B",
            currentItemId: 10,
            deckA: [],
            deckB: deckB);

        Assert.True(decision.HasTarget);
        Assert.Equal("Deck B", decision.ChosenDeckName);
        Assert.Equal(11, decision.ChosenItem?.Id);
        Assert.Equal("No playable item on Deck A", decision.FallbackReason);
    }

    [Fact]
    public void AlternateMode_UsesOtherDeckWhenAvailable()
    {
        var deckA = Queue("Deck A", LocalItem(1, "Deck A"));
        var deckB = Queue("Deck B", SpotifyItem(10, "Deck B"));

        var decision = Decide(
            DancePilotTransitionModes.AlternateDecks,
            currentDeckName: "Deck A",
            currentItemId: 1,
            deckA,
            deckB);

        Assert.True(decision.HasTarget);
        Assert.Equal("Deck B", decision.ChosenDeckName);
        Assert.Equal(10, decision.ChosenItem?.Id);
        Assert.Null(decision.FallbackReason);
    }

    [Fact]
    public void AutoMode_FallsBackToSameDeckWhenOtherDeckEmpty()
    {
        var deckA = Queue(
            "Deck A",
            SpotifyItem(1, "Deck A"),
            SpotifyItem(2, "Deck A"));

        var decision = Decide(
            DancePilotTransitionModes.Auto,
            currentDeckName: "Deck A",
            currentItemId: 1,
            deckA,
            deckB: []);

        Assert.True(decision.HasTarget);
        Assert.Equal("Deck A", decision.ChosenDeckName);
        Assert.Equal(2, decision.ChosenItem?.Id);
        Assert.Equal("No playable item on Deck B", decision.FallbackReason);
        Assert.Equal("No playable item on Deck B; continuing Deck A.", decision.StatusMessage);
    }

    [Fact]
    public void AlternateMode_EmptyOtherDeckDoesNotChooseEarlyStopTarget()
    {
        var deckA = Queue(
            "Deck A",
            LocalItem(1, "Deck A"),
            LocalItem(2, "Deck A"));

        var decision = Decide(
            DancePilotTransitionModes.AlternateDecks,
            currentDeckName: "Deck A",
            currentItemId: 1,
            deckA,
            deckB: []);

        Assert.False(decision.HasTarget);
        Assert.Equal("Deck B", decision.ChosenDeckName);
        Assert.Equal("No playable item on Deck B", decision.FallbackReason);
        Assert.Equal("No playable item on Deck B; continuing Deck A.", decision.StatusMessage);
    }

    [Fact]
    public void Decide_DoesNotAdvanceCursorBeforeNextItemStarts()
    {
        var deckA = Queue(
            "Deck A",
            LocalItem(1, "Deck A", status: "playing"),
            LocalItem(2, "Deck A"));
        var request = new DancePilotTransitionRequest
        {
            CurrentDeckName = "Deck A",
            CurrentItemId = 1,
            RequestedMode = DancePilotTransitionModes.SameDeck,
            DeckAQueue = deckA,
            DeckBQueue = [],
            LastPlayedDeckAItemId = null
        };

        var decision = DancePilotTransitionPlanner.Decide(request);

        Assert.True(decision.HasTarget);
        Assert.Equal(2, decision.ChosenItem?.Id);
        Assert.Null(request.LastPlayedDeckAItemId);
        Assert.Equal("playing", deckA[0].Status);
        Assert.Equal([1, 2], deckA.Select(item => item.Id));
    }

    [Theory]
    [InlineData(SongSources.Local, SongSources.Local)]
    [InlineData(SongSources.Spotify, SongSources.Spotify)]
    [InlineData(SongSources.Local, SongSources.Spotify)]
    [InlineData(SongSources.Spotify, SongSources.Local)]
    public void SameDeckMode_SelectsNextQueuedItemAcrossSources(string currentSource, string nextSource)
    {
        var deckA = Queue(
            "Deck A",
            Item(1, "Deck A", currentSource),
            Item(2, "Deck A", nextSource));

        var decision = Decide(
            DancePilotTransitionModes.SameDeck,
            currentDeckName: "Deck A",
            currentItemId: 1,
            deckA,
            deckB: []);

        Assert.True(decision.HasTarget);
        Assert.Equal("Deck A", decision.ChosenDeckName);
        Assert.Equal(2, decision.ChosenItem?.Id);
        Assert.Equal(nextSource, decision.ChosenItem?.Source);
    }

    [Fact]
    public void PlaybackPath_LocalToLocal_UsesOverlap()
    {
        var current = LocalItem(1, "Deck A");
        var next = LocalItem(2, "Deck B");

        var path = DancePilotTransitionPlaybackPath.Resolve(current, next);

        Assert.Equal(DancePilotTransitionPlaybackPathKind.LocalOverlap, path);
    }

    [Theory]
    [InlineData(SongSources.Spotify, SongSources.Spotify)]
    [InlineData(SongSources.Local, SongSources.Spotify)]
    [InlineData(SongSources.Spotify, SongSources.Local)]
    public void PlaybackPath_WithSpotify_UsesConnectHandoff(string currentSource, string nextSource)
    {
        var current = Item(1, "Deck A", currentSource);
        var next = Item(2, "Deck B", nextSource);

        var path = DancePilotTransitionPlaybackPath.Resolve(current, next);

        Assert.Equal(DancePilotTransitionPlaybackPathKind.SpotifyConnectHandoff, path);
    }

    private static DancePilotTransitionDecision Decide(
        string mode,
        string currentDeckName,
        int currentItemId,
        IReadOnlyList<DancePilotQueueItem> deckA,
        IReadOnlyList<DancePilotQueueItem> deckB) =>
        DancePilotTransitionPlanner.Decide(new DancePilotTransitionRequest
        {
            CurrentDeckName = currentDeckName,
            CurrentItemId = currentItemId,
            RequestedMode = mode,
            DeckAQueue = deckA,
            DeckBQueue = deckB
        });

    private static List<DancePilotQueueItem> Queue(string deckName, params DancePilotQueueItem[] items) =>
        items.Select((item, index) => item with
        {
            DeckName = deckName,
            QueuePosition = index + 1
        }).ToList();

    private static DancePilotQueueItem LocalItem(int id, string deckName, string status = "pending") =>
        Item(id, deckName, SongSources.Local, status);

    private static DancePilotQueueItem SpotifyItem(int id, string deckName, string status = "pending") =>
        Item(id, deckName, SongSources.Spotify, status);

    private static DancePilotQueueItem Item(
        int id,
        string deckName,
        string source,
        string status = "pending") => new()
        {
            Id = id,
            DeckName = deckName,
            Source = source,
            ExternalUri = source == SongSources.Spotify ? $"spotify:track:{id}" : string.Empty,
            LocalPath = source == SongSources.Local ? $@"C:\Music\Track {id}.wav" : null,
            Title = $"Song {id}",
            Artist = "DancePilot",
            QueuePosition = id,
            Status = status
        };
}
