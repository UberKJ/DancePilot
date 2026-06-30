namespace DancePilot.Core.Spotify;

public sealed record DancePilotDeckPlaySelection(
    DancePilotQueueItem? Item,
    bool ShouldClearSelectedId);

public static class DancePilotDeckPlaySelector
{
    public static DancePilotDeckPlaySelection FindItemForUserPlay(
        IReadOnlyList<DancePilotQueueItem> queue,
        int? selectedId,
        bool isAudiblyPlaying,
        int? lastPlayedId = null)
    {
        if (isAudiblyPlaying)
        {
            return new DancePilotDeckPlaySelection(null, ShouldClearSelectedId: false);
        }

        if (selectedId is int selectedQueueItemId)
        {
            var selectedItem = queue.FirstOrDefault(item => item.Id == selectedQueueItemId);
            return selectedItem is not null
                ? new DancePilotDeckPlaySelection(selectedItem, ShouldClearSelectedId: false)
                : new DancePilotDeckPlaySelection(queue.FirstOrDefault(), ShouldClearSelectedId: true);
        }

        return new DancePilotDeckPlaySelection(queue.FirstOrDefault(), ShouldClearSelectedId: false);
    }
}
