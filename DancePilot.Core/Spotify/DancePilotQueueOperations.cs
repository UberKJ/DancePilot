namespace DancePilot.Core.Spotify;

public static class DancePilotQueueOperations
{
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
}
