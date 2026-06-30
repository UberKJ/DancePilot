using DancePilot.Core.Models;
using DancePilot.Core.Spotify;

namespace DancePilot.Tests;

public sealed class DancePilotQueueOperationsTests
{
    [Fact]
    public void RandomizeDeckQueue_RandomizesDeckAOnly()
    {
        var deckA = CreateQueue("Deck A", 1, 2, 3, 4, 5);
        var deckB = CreateQueue("Deck B", 11, 12, 13, 14, 15);
        var originalDeckAOrder = deckA.Select(item => item.Id).ToList();
        var originalDeckBOrder = deckB.Select(item => item.Id).ToList();

        var randomized = DancePilotQueueOperations.RandomizeDeckQueue(deckA, "Deck A", random: new Random(7));

        Assert.True(randomized);
        Assert.NotEqual(originalDeckAOrder, deckA.Select(item => item.Id));
        Assert.Equal(originalDeckAOrder.Order(), deckA.Select(item => item.Id).Order());
        Assert.Equal(originalDeckBOrder, deckB.Select(item => item.Id));
        AssertOrdered(deckA, "Deck A");
        AssertOrdered(deckB, "Deck B");
    }

    [Fact]
    public void RandomizeDeckQueue_RandomizesDeckBOnly()
    {
        var deckA = CreateQueue("Deck A", 1, 2, 3, 4, 5);
        var deckB = CreateQueue("Deck B", 11, 12, 13, 14, 15);
        var originalDeckAOrder = deckA.Select(item => item.Id).ToList();
        var originalDeckBOrder = deckB.Select(item => item.Id).ToList();

        var randomized = DancePilotQueueOperations.RandomizeDeckQueue(deckB, "Deck B", random: new Random(11));

        Assert.True(randomized);
        Assert.Equal(originalDeckAOrder, deckA.Select(item => item.Id));
        Assert.NotEqual(originalDeckBOrder, deckB.Select(item => item.Id));
        Assert.Equal(originalDeckBOrder.Order(), deckB.Select(item => item.Id).Order());
        AssertOrdered(deckA, "Deck A");
        AssertOrdered(deckB, "Deck B");
    }

    [Fact]
    public void RandomizeDeckQueue_DoesNotPullFromSelectedPlaylistItems()
    {
        var deck = CreateQueue("Deck A", 1, 2, 3, 4, 5);
        var selectedPlaylistItems = CreateQueue("Source", 101, 102, 103, 104, 105);
        var originalDeckIds = deck.Select(item => item.Id).Order().ToList();
        var selectedPlaylistIds = selectedPlaylistItems.Select(item => item.Id).ToHashSet();

        var randomized = DancePilotQueueOperations.RandomizeDeckQueue(deck, "Deck A", random: new Random(19));

        Assert.True(randomized);
        Assert.Equal(originalDeckIds, deck.Select(item => item.Id).Order());
        Assert.DoesNotContain(deck, item => selectedPlaylistIds.Contains(item.Id));
        AssertOrdered(deck, "Deck A");
    }

    [Fact]
    public void AppendItems_QueuesFullPlaylistWithoutTwentyFourSongLimit()
    {
        var queue = new List<DancePilotQueueItem>();
        var playlistItems = CreateQueue("Source", Enumerable.Range(1, 31).ToArray());

        var added = DancePilotQueueOperations.AppendItems(queue, playlistItems, "Deck A");

        Assert.Equal(31, added);
        Assert.Equal(31, queue.Count);
        Assert.Equal(Enumerable.Range(1, 31), queue.Select(item => item.Id));
        AssertOrdered(queue, "Deck A");
    }

    [Fact]
    public void ReplaceItems_ClearsOldItemsAndLoadsFullPlaylist()
    {
        var queue = CreateQueue("Deck A", 1, 2, 3);
        var playlistItems = CreateQueue("Source", Enumerable.Range(101, 31).ToArray());

        var added = DancePilotQueueOperations.ReplaceItems(queue, playlistItems, "Deck A");

        Assert.Equal(31, added);
        Assert.Equal(31, queue.Count);
        Assert.DoesNotContain(queue, item => item.Id is 1 or 2 or 3);
        Assert.Equal(Enumerable.Range(101, 31), queue.Select(item => item.Id));
        AssertOrdered(queue, "Deck A");
    }

    [Fact]
    public void AppendItems_KeepsOldItemsAndAddsPlaylistItems()
    {
        var queue = CreateQueue("Deck B", 1, 2, 3);
        var playlistItems = CreateQueue("Source", Enumerable.Range(101, 31).ToArray());

        var added = DancePilotQueueOperations.AppendItems(queue, playlistItems, "Deck B");

        Assert.Equal(31, added);
        Assert.Equal(34, queue.Count);
        Assert.Equal([1, 2, 3], queue.Take(3).Select(item => item.Id));
        Assert.Equal(Enumerable.Range(101, 31), queue.Skip(3).Select(item => item.Id));
        AssertOrdered(queue, "Deck B");
    }

    [Fact]
    public void RandomizeDeckQueue_RetainsLocalPaths()
    {
        var queue = new List<DancePilotQueueItem>
        {
            CreateLocalItem(1, "Deck A", 1, @"C:\Music\Track 1.wav"),
            CreateLocalItem(2, "Deck A", 2, @"C:\Music\Track 2.wav"),
            CreateLocalItem(3, "Deck A", 3, @"C:\Music\Track 3.wav"),
            CreateLocalItem(4, "Deck A", 4, @"C:\Music\Track 4.wav")
        };
        var localPathsById = queue.ToDictionary(item => item.Id, item => item.LocalPath);

        var randomized = DancePilotQueueOperations.RandomizeDeckQueue(queue, "Deck A", random: new Random(23));

        Assert.True(randomized);
        Assert.All(queue, item => Assert.Equal(localPathsById[item.Id], item.LocalPath));
        AssertOrdered(queue, "Deck A");
    }

    [Fact]
    public void RandomizeDeckQueue_RetainsSpotifyUris()
    {
        var queue = new List<DancePilotQueueItem>
        {
            CreateSpotifyItem(1, "Deck B", 1, "spotify:track:1"),
            CreateSpotifyItem(2, "Deck B", 2, "spotify:track:2"),
            CreateSpotifyItem(3, "Deck B", 3, "spotify:track:3"),
            CreateSpotifyItem(4, "Deck B", 4, "spotify:track:4")
        };
        var spotifyUrisById = queue.ToDictionary(item => item.Id, item => item.ExternalUri);

        var randomized = DancePilotQueueOperations.RandomizeDeckQueue(queue, "Deck B", random: new Random(29));

        Assert.True(randomized);
        Assert.All(queue, item =>
        {
            Assert.Equal(SongSources.Spotify, item.Source);
            Assert.Equal(spotifyUrisById[item.Id], item.ExternalUri);
            Assert.Equal(spotifyUrisById[item.Id], item.SpotifyUri);
        });
        AssertOrdered(queue, "Deck B");
    }

    [Fact]
    public void RandomizeDeckQueue_KeepsCurrentlyPlayingItemInQueueAndPosition()
    {
        var queue = new List<DancePilotQueueItem>
        {
            CreateItem(1, "Deck A", 1),
            CreateItem(2, "Deck A", 2),
            CreateItem(3, "Deck A", 3, "playing"),
            CreateItem(4, "Deck A", 4),
            CreateItem(5, "Deck A", 5)
        };
        var originalIds = queue.Select(item => item.Id).Order().ToList();

        var randomized = DancePilotQueueOperations.RandomizeDeckQueue(
            queue,
            "Deck A",
            protectedItemId: 3,
            random: new Random(31));

        Assert.True(randomized);
        Assert.Equal(3, queue[2].Id);
        Assert.Equal("playing", queue[2].Status);
        Assert.Equal(originalIds, queue.Select(item => item.Id).Order());
        Assert.Contains(queue, item => item.Id == 3);
        Assert.NotNull(queue.Skip(3).FirstOrDefault());
        AssertOrdered(queue, "Deck A");
    }

    [Fact]
    public void MoveItemUp_SwapsWithPreviousItemAndRenumbers()
    {
        var queue = CreateQueue("Deck A", 1, 2, 3);

        var moved = DancePilotQueueOperations.MoveItemUp(queue, itemId: 3, deckName: "Deck A");

        Assert.True(moved);
        Assert.Equal([1, 3, 2], queue.Select(item => item.Id));
        AssertOrdered(queue, "Deck A");
    }

    [Fact]
    public void MoveItemDown_SwapsWithNextItemAndRenumbers()
    {
        var queue = CreateQueue("Deck B", 1, 2, 3);

        var moved = DancePilotQueueOperations.MoveItemDown(queue, itemId: 1, deckName: "Deck B");

        Assert.True(moved);
        Assert.Equal([2, 1, 3], queue.Select(item => item.Id));
        AssertOrdered(queue, "Deck B");
    }

    [Fact]
    public void RemoveItem_RemovesSelectedItemAndRenumbers()
    {
        var queue = CreateQueue("Deck A", 1, 2, 3);

        var removed = DancePilotQueueOperations.RemoveItem(queue, itemId: 2, deckName: "Deck A");

        Assert.True(removed);
        Assert.Equal([1, 3], queue.Select(item => item.Id));
        AssertOrdered(queue, "Deck A");
    }

    [Fact]
    public void ClearPending_RemovesPendingItemsAndPreservesProtectedOrPlayingItems()
    {
        var queue = new List<DancePilotQueueItem>
        {
            CreateItem(1, "Deck B", 1, "playing"),
            CreateItem(2, "Deck B", 2),
            CreateItem(3, "Deck B", 3, "played"),
            CreateItem(4, "Deck B", 4)
        };

        var removed = DancePilotQueueOperations.ClearPending(queue, "Deck B", protectedItemId: 4);

        Assert.Equal(1, removed);
        Assert.Equal([1, 3, 4], queue.Select(item => item.Id));
        AssertOrdered(queue, "Deck B");
    }

    private static List<DancePilotQueueItem> CreateQueue(string deckName, params int[] ids) =>
        ids.Select((id, index) => CreateItem(id, deckName, index + 1)).ToList();

    private static DancePilotQueueItem CreateLocalItem(int id, string deckName, int position, string localPath) =>
        CreateItem(id, deckName, position) with
        {
            Source = SongSources.Local,
            ExternalUri = string.Empty,
            LocalPath = localPath,
            Album = "Line Dance",
            AlbumArtUrl = $"file:///album-art/{id}.jpg",
            Duration = TimeSpan.FromSeconds(180 + id)
        };

    private static DancePilotQueueItem CreateSpotifyItem(int id, string deckName, int position, string spotifyUri) =>
        CreateItem(id, deckName, position) with
        {
            Source = SongSources.Spotify,
            ExternalUri = spotifyUri,
            LocalPath = null,
            Album = "Dance Mix",
            AlbumArtUrl = $"https://example.test/{id}.jpg",
            Duration = TimeSpan.FromSeconds(200 + id)
        };

    private static DancePilotQueueItem CreateItem(int id, string deckName, int position, string status = "pending") => new()
    {
        Id = id,
        DeckName = deckName,
        Source = "test",
        ExternalUri = $"test:{id}",
        Title = $"Song {id}",
        Artist = "DancePilot",
        Album = "Test Album",
        AlbumArtUrl = $"https://example.test/art/{id}.jpg",
        Duration = TimeSpan.FromSeconds(180 + id),
        BPM = 120 + id,
        MusicalKey = "8A",
        QueuePosition = position,
        Status = status
    };

    private static void AssertOrdered(IReadOnlyList<DancePilotQueueItem> queue, string deckName)
    {
        Assert.All(queue, item => Assert.Equal(deckName, item.DeckName));
        Assert.Equal(Enumerable.Range(1, queue.Count), queue.Select(item => item.QueuePosition));
    }
}