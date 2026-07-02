namespace DancePilot.Core.Spotify;

public readonly record struct DancePilotMixLevels(
    double MainOutputVolume,
    double DeckAFader,
    double DeckBFader,
    double CrossfaderPosition)
{
    public double MainOutputScalar => NormalizePercent(MainOutputVolume);

    public double DeckAFaderScalar => NormalizePercent(DeckAFader);

    public double DeckBFaderScalar => NormalizePercent(DeckBFader);

    public double CrossfaderAScalar => NormalizePercent(100 - ClampPercent(CrossfaderPosition));

    public double CrossfaderBScalar => NormalizePercent(CrossfaderPosition);

    public double DeckAEffectiveLevel => DeckAFaderScalar * CrossfaderAScalar;

    public double DeckBEffectiveLevel => DeckBFaderScalar * CrossfaderBScalar;

    public double DeckALocalOutputLevel => MainOutputScalar * DeckAEffectiveLevel;

    public double DeckBLocalOutputLevel => MainOutputScalar * DeckBEffectiveLevel;

    public DancePilotMixLevels WithMainOutputVolume(double value) => this with { MainOutputVolume = ClampPercent(value) };

    public DancePilotMixLevels WithDeckAFader(double value) => this with { DeckAFader = ClampPercent(value) };

    public DancePilotMixLevels WithDeckBFader(double value) => this with { DeckBFader = ClampPercent(value) };

    public DancePilotMixLevels WithCrossfaderPosition(double value) => this with { CrossfaderPosition = ClampPercent(value) };

    public static double ClampPercent(double value) => Math.Clamp(value, 0, 100);

    public static double NormalizePercent(double value) => ClampPercent(value) / 100d;
}
