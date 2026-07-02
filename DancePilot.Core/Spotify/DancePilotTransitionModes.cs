namespace DancePilot.Core.Spotify;

public static class DancePilotTransitionModes
{
    public const string Off = "Off";
    public const string SameDeck = "Same Deck";
    public const string AlternateDecks = "Alternate Decks";
    public const string Auto = "Auto";

    private const string LegacySameDeck = "Same deck next item";
    private const string LegacyOppositeDeck = "Opposite deck next item";

    public static IReadOnlyList<string> Options { get; } =
    [
        Off,
        SameDeck,
        AlternateDecks,
        Auto
    ];

    public static string Normalize(string? mode, string fallback = Auto)
    {
        var fallbackMode = Options.Contains(fallback) ? fallback : Auto;
        if (string.IsNullOrWhiteSpace(mode))
        {
            return fallbackMode;
        }

        var trimmed = mode.Trim();
        foreach (var option in Options)
        {
            if (string.Equals(trimmed, option, StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        return trimmed.ToLowerInvariant() switch
        {
            "none" or "disabled" => Off,
            "same deck advance" or "same deck next" or "same-deck" => SameDeck,
            var value when string.Equals(value, LegacySameDeck, StringComparison.OrdinalIgnoreCase) => SameDeck,
            "opposite deck" or "opposite deck next" or "alternate" or "alternate deck" => AlternateDecks,
            var value when string.Equals(value, LegacyOppositeDeck, StringComparison.OrdinalIgnoreCase) => AlternateDecks,
            "automatic" => Auto,
            _ => fallbackMode
        };
    }
}
