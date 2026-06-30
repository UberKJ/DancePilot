namespace DancePilot.Core.Models;

public sealed record LocalLibrarySettings
{
    public string LibraryFolderPath { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    public DateTimeOffset? LastScanStartedAt { get; init; }

    public DateTimeOffset? LastScanCompletedAt { get; init; }

    public int TrackCount { get; init; }

    public string AlbumArtCacheFolderPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DancePilot",
        "AlbumArt");
}
