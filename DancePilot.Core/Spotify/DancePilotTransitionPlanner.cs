namespace DancePilot.Core.Spotify;

public sealed record DancePilotTransitionRequest
{
    public string CurrentDeckName { get; init; } = "Deck A";

    public string RequestedMode { get; init; } = DancePilotTransitionModes.Auto;

    public int? CurrentItemId { get; init; }

    public IReadOnlyList<DancePilotQueueItem> DeckAQueue { get; init; } = [];

    public IReadOnlyList<DancePilotQueueItem> DeckBQueue { get; init; } = [];

    public int? SelectedDeckAItemId { get; init; }

    public int? SelectedDeckBItemId { get; init; }

    public int? LastPlayedDeckAItemId { get; init; }

    public int? LastPlayedDeckBItemId { get; init; }

    public Func<DancePilotQueueItem, bool>? IsPlayable { get; init; }
}

public sealed record DancePilotTransitionDecision
{
    public required string CurrentDeckName { get; init; }

    public required string RequestedMode { get; init; }

    public required string ChosenDeckName { get; init; }

    public DancePilotQueueItem? ChosenItem { get; init; }

    public string? FallbackReason { get; init; }

    public required string StatusMessage { get; init; }

    public bool HasTarget => ChosenItem is not null;
}

public static class DancePilotTransitionPlanner
{
    public static DancePilotTransitionDecision Decide(DancePilotTransitionRequest request)
    {
        var currentDeckName = NormalizeDeckName(request.CurrentDeckName);
        var requestedMode = DancePilotTransitionModes.Normalize(request.RequestedMode);
        if (requestedMode == DancePilotTransitionModes.Off)
        {
            return NoTarget(
                currentDeckName,
                requestedMode,
                currentDeckName,
                null,
                "Automatic transition is off.");
        }

        return requestedMode switch
        {
            DancePilotTransitionModes.SameDeck => DecideSameDeck(request, currentDeckName, requestedMode),
            DancePilotTransitionModes.AlternateDecks => DecideAlternateDecks(request, currentDeckName, requestedMode),
            _ => DecideAuto(request, currentDeckName, DancePilotTransitionModes.Auto)
        };
    }

    private static DancePilotTransitionDecision DecideSameDeck(
        DancePilotTransitionRequest request,
        string currentDeckName,
        string requestedMode)
    {
        var next = FindPlayableCandidate(request, currentDeckName, currentDeckName);
        return next is null
            ? NoTarget(
                currentDeckName,
                requestedMode,
                currentDeckName,
                null,
                $"No playable item on {currentDeckName}; transition is waiting.")
            : Target(currentDeckName, requestedMode, currentDeckName, next, null);
    }

    private static DancePilotTransitionDecision DecideAlternateDecks(
        DancePilotTransitionRequest request,
        string currentDeckName,
        string requestedMode)
    {
        var otherDeckName = OppositeDeckName(currentDeckName);
        var next = FindPlayableCandidate(request, otherDeckName, currentDeckName);
        return next is null
            ? NoTarget(
                currentDeckName,
                requestedMode,
                otherDeckName,
                $"No playable item on {otherDeckName}",
                NoPlayableOtherDeckMessage(otherDeckName, currentDeckName))
            : Target(currentDeckName, requestedMode, otherDeckName, next, null);
    }

    private static DancePilotTransitionDecision DecideAuto(
        DancePilotTransitionRequest request,
        string currentDeckName,
        string requestedMode)
    {
        var otherDeckName = OppositeDeckName(currentDeckName);
        var otherDeckNext = FindPlayableCandidate(request, otherDeckName, currentDeckName);
        if (otherDeckNext is not null)
        {
            return Target(currentDeckName, requestedMode, otherDeckName, otherDeckNext, null);
        }

        var fallbackReason = $"No playable item on {otherDeckName}";
        var sameDeckNext = FindPlayableCandidate(request, currentDeckName, currentDeckName);
        return sameDeckNext is null
            ? NoTarget(
                currentDeckName,
                requestedMode,
                currentDeckName,
                fallbackReason,
                $"{fallbackReason}; no playable item on {currentDeckName}.")
            : Target(currentDeckName, requestedMode, currentDeckName, sameDeckNext, fallbackReason);
    }

    private static DancePilotQueueItem? FindPlayableCandidate(
        DancePilotTransitionRequest request,
        string deckName,
        string currentDeckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(request, normalizedDeckName);
        if (queue.Count == 0)
        {
            return null;
        }

        var selectedId = SelectedItemId(request, normalizedDeckName);
        if (selectedId is int selectedItemId)
        {
            var selectedItem = queue.FirstOrDefault(item => item.Id == selectedItemId);
            if (selectedItem is not null
                && !IsCurrentItem(request, normalizedDeckName, currentDeckName, selectedItem.Id)
                && LastPlayedItemId(request, normalizedDeckName) != selectedItem.Id
                && IsPlayable(request, selectedItem))
            {
                return selectedItem;
            }
        }

        var cursorId = LastPlayedItemId(request, normalizedDeckName);
        if (cursorId is null
            && string.Equals(normalizedDeckName, currentDeckName, StringComparison.Ordinal)
            && request.CurrentItemId is not null)
        {
            cursorId = request.CurrentItemId;
        }

        if (cursorId is null)
        {
            return queue.FirstOrDefault(item =>
                !IsCurrentItem(request, normalizedDeckName, currentDeckName, item.Id)
                && IsPlayable(request, item));
        }

        var cursorIndex = queue.ToList().FindIndex(item => item.Id == cursorId.Value);
        if (cursorIndex < 0)
        {
            return queue.FirstOrDefault(item =>
                !IsCurrentItem(request, normalizedDeckName, currentDeckName, item.Id)
                && IsPlayable(request, item));
        }

        for (var index = cursorIndex + 1; index < queue.Count; index++)
        {
            var item = queue[index];
            if (!IsCurrentItem(request, normalizedDeckName, currentDeckName, item.Id)
                && IsPlayable(request, item))
            {
                return item;
            }
        }

        return null;
    }

    private static DancePilotTransitionDecision Target(
        string currentDeckName,
        string requestedMode,
        string chosenDeckName,
        DancePilotQueueItem chosenItem,
        string? fallbackReason) => new()
        {
            CurrentDeckName = currentDeckName,
            RequestedMode = requestedMode,
            ChosenDeckName = chosenDeckName,
            ChosenItem = chosenItem,
            FallbackReason = fallbackReason,
            StatusMessage = fallbackReason is null
                ? $"Transition ready: {currentDeckName} to {chosenDeckName}."
                : $"{fallbackReason}; continuing {currentDeckName}."
        };

    private static DancePilotTransitionDecision NoTarget(
        string currentDeckName,
        string requestedMode,
        string chosenDeckName,
        string? fallbackReason,
        string statusMessage) => new()
        {
            CurrentDeckName = currentDeckName,
            RequestedMode = requestedMode,
            ChosenDeckName = chosenDeckName,
            FallbackReason = fallbackReason,
            StatusMessage = statusMessage
        };

    private static IReadOnlyList<DancePilotQueueItem> QueueForDeck(
        DancePilotTransitionRequest request,
        string deckName) =>
        deckName == "Deck B" ? request.DeckBQueue : request.DeckAQueue;

    private static int? SelectedItemId(DancePilotTransitionRequest request, string deckName) =>
        deckName == "Deck B" ? request.SelectedDeckBItemId : request.SelectedDeckAItemId;

    private static int? LastPlayedItemId(DancePilotTransitionRequest request, string deckName) =>
        deckName == "Deck B" ? request.LastPlayedDeckBItemId : request.LastPlayedDeckAItemId;

    private static bool IsCurrentItem(
        DancePilotTransitionRequest request,
        string deckName,
        string currentDeckName,
        int itemId) =>
        string.Equals(deckName, currentDeckName, StringComparison.Ordinal)
        && request.CurrentItemId == itemId;

    private static bool IsPlayable(DancePilotTransitionRequest request, DancePilotQueueItem item) =>
        request.IsPlayable?.Invoke(item) ?? DancePilotPlaybackRouter.Resolve(item).IsPlayable;

    private static string NoPlayableOtherDeckMessage(string otherDeckName, string currentDeckName) =>
        $"No playable item on {otherDeckName}; continuing {currentDeckName}.";

    private static string OppositeDeckName(string deckName) =>
        deckName == "Deck B" ? "Deck A" : "Deck B";

    private static string NormalizeDeckName(string deckName) =>
        string.Equals(deckName, "Deck B", StringComparison.OrdinalIgnoreCase) ? "Deck B" : "Deck A";
}
