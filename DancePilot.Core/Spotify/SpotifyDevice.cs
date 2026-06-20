namespace DancePilot.Core.Spotify;

public sealed record SpotifyDevice
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool IsActive { get; init; }

    public bool IsRestricted { get; init; }

    public bool SupportsVolume { get; init; }

    public int? VolumePercent { get; init; }

    public string StatusDisplay => IsRestricted
        ? IsActive ? "Active, restricted" : "Restricted"
        : IsActive ? "Active" : "Available";

    public string DisplayName => $"{Name} ({Type})";

    public string DisplaySummary => $"{DisplayName} - {StatusDisplay}";
}
