namespace DancePilot.Core.Spotify;

public sealed record DancePilotDeckState
{
    public required string DeckName { get; init; }

    public bool IsPlaying { get; init; }

    public bool IsPaused { get; init; }

    public bool IsSelected { get; init; }

    public int QueueCount { get; init; }

    public DancePilotQueueItem? CurrentTrack { get; init; }

    public string Source { get; init; } = string.Empty;

    public string StatusText { get; init; } = "Ready";

    public string PlayingBadgeText => "Playing";

    public string PausedBadgeText => "Paused";

    public string SelectedBadgeText => "Selected";

    public string QueueCountText => QueueCount == 1 ? "1 queued" : $"{QueueCount} queued";

    public static DancePilotDeckState Create(
        string deckName,
        bool isPlaying,
        bool isLoaded,
        bool isSelected,
        int queueCount,
        DancePilotQueueItem? currentTrack)
    {
        var isPaused = isLoaded && !isPlaying;
        var stateParts = new List<string>();
        if (isPlaying)
        {
            stateParts.Add("Playing");
        }
        else if (isPaused)
        {
            stateParts.Add("Paused");
        }

        if (isSelected)
        {
            stateParts.Add("Selected");
        }

        stateParts.Add(queueCount == 1 ? "1 queued" : $"{queueCount} queued");

        return new DancePilotDeckState
        {
            DeckName = deckName,
            IsPlaying = isPlaying,
            IsPaused = isPaused,
            IsSelected = isSelected,
            QueueCount = queueCount,
            CurrentTrack = currentTrack,
            Source = currentTrack?.Source ?? string.Empty,
            StatusText = string.Join(" / ", stateParts)
        };
    }
}
