namespace DancePilot.Core.Models;

public static class LiveEventMusicSources
{
    public const string Spotify = "Spotify";
    public const string Tidal = "TIDAL";
    public const string Local = "Local";
    public const string YouTube = "YouTube";

    public static IReadOnlyList<string> All { get; } =
    [
        Spotify,
        Tidal,
        Local,
        YouTube
    ];

    public static string Normalize(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        "tidal" or "tital" => Tidal,
        "local" => Local,
        "youtube" => YouTube,
        _ => Spotify
    };

    public static bool IsTidal(string? source) =>
        Normalize(source) == Tidal;

    public static bool SupportsDeckPlayback(string? source) =>
        Normalize(source) is Spotify or Local;
}
