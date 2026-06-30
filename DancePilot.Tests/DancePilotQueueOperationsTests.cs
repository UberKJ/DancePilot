using DancePilot.Core.Spotify;

namespace DancePilot.Tests;

public sealed class DancePilotQueueOperationsTests
{
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

    private static DancePilotQueueItem CreateItem(int id, string deckName, int position, string status = "pending") => new()
    {
        Id = id,
        DeckName = deckName,
        Source = "test",
        ExternalUri = $"test:{id}",
        Title = $"Song {id}",
        Artist = "DancePilot",
        QueuePosition = position,
        Status = status
    };

    private static void AssertOrdered(IReadOnlyList<DancePilotQueueItem> queue, string deckName)
    {
        Assert.All(queue, item => Assert.Equal(deckName, item.DeckName));
        Assert.Equal(Enumerable.Range(1, queue.Count), queue.Select(item => item.QueuePosition));
    }
}
