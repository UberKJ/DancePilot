namespace DancePilot.Core.Spotify;

public static class DancePilotQueueOperations
{
    public static bool RandomizeDeckQueue(
        IList<DancePilotQueueItem> queue,
        string deckName,
        int? protectedItemId = null,
        Random? random = null)
    {
        if (queue.Count < 2)
        {
            return false;
        }

        var protectedIndex = protectedItemId is int itemId
            ? FindQueueIndex(queue, itemId)
            : -1;
        var protectedItem = protectedIndex >= 0 ? queue[protectedIndex] : null;
        var movableItems = queue
            .Where((_, index) => index != protectedIndex)
            .ToList();
        if (movableItems.Count < 2)
        {
            return false;
        }

        var shuffledItems = Shuffle(movableItems, random ?? Random.Shared);
        queue.Clear();
        var shuffledIndex = 0;
        for (var index = 0; index < movableItems.Count + (protectedItem is null ? 0 : 1); index++)
        {
            if (index == protectedIndex && protectedItem is not null)
            {
                queue.Add(protectedItem);
                continue;
            }

            queue.Add(shuffledItems[shuffledIndex++]);
        }

        Renumber(queue, deckName);
        return true;
    }

    public static int AppendItems(
        IList<DancePilotQueueItem> queue,
        IEnumerable<DancePilotQueueItem> items,
        string deckName)
    {
        var added = 0;
        foreach (var item in items)
        {
            queue.Add(item with { DeckName = deckName });
            added++;
        }

        if (added > 0)
        {
            Renumber(queue, deckName);
        }

        return added;
    }

    public static int ReplaceItems(
        IList<DancePilotQueueItem> queue,
        IEnumerable<DancePilotQueueItem> items,
        string deckName)
    {
        queue.Clear();
        return AppendItems(queue, items, deckName);
    }

    public static bool MoveItemUp(IList<DancePilotQueueItem> queue, int itemId, string deckName)
    {
        var index = FindQueueIndex(queue, itemId);
        if (index <= 0)
        {
            return false;
        }

        (queue[index - 1], queue[index]) = (queue[index], queue[index - 1]);
        Renumber(queue, deckName);
        return true;
    }

    public static bool MoveItemDown(IList<DancePilotQueueItem> queue, int itemId, string deckName)
    {
        var index = FindQueueIndex(queue, itemId);
        if (index < 0 || index >= queue.Count - 1)
        {
            return false;
        }

        (queue[index], queue[index + 1]) = (queue[index + 1], queue[index]);
        Renumber(queue, deckName);
        return true;
    }

    public static bool RemoveItem(IList<DancePilotQueueItem> queue, int itemId, string deckName)
    {
        var index = FindQueueIndex(queue, itemId);
        if (index < 0)
        {
            return false;
        }

        queue.RemoveAt(index);
        Renumber(queue, deckName);
        return true;
    }

    public static int ClearPending(IList<DancePilotQueueItem> queue, string deckName, int? protectedItemId = null)
    {
        var removedCount = 0;
        for (var index = queue.Count - 1; index >= 0; index--)
        {
            var item = queue[index];
            if (protectedItemId == item.Id || !IsPending(item))
            {
                continue;
            }

            queue.RemoveAt(index);
            removedCount++;
        }

        if (removedCount > 0)
        {
            Renumber(queue, deckName);
        }

        return removedCount;
    }

    public static void Renumber(IList<DancePilotQueueItem> queue, string deckName)
    {
        for (var index = 0; index < queue.Count; index++)
        {
            queue[index] = queue[index] with
            {
                DeckName = deckName,
                QueuePosition = index + 1
            };
        }
    }

    private static int FindQueueIndex(IList<DancePilotQueueItem> queue, int itemId)
    {
        for (var index = 0; index < queue.Count; index++)
        {
            if (queue[index].Id == itemId)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsPending(DancePilotQueueItem item) =>
        string.IsNullOrWhiteSpace(item.Status)
        || string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase);

    private static List<DancePilotQueueItem> Shuffle(IReadOnlyList<DancePilotQueueItem> items, Random random)
    {
        var shuffled = items.ToList();
        var original = shuffled.ToList();
        for (var index = shuffled.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        if (shuffled.SequenceEqual(original))
        {
            var first = shuffled[0];
            shuffled.RemoveAt(0);
            shuffled.Add(first);
        }

        return shuffled;
    }
}
