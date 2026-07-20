using DancePilot.Core.Spotify;

namespace DancePilot.Tests;

public sealed class DancePilotMixLevelsTests
{
    [Fact]
    public void WithDeckAFader_DoesNotChangeMainOutputVolume()
    {
        var levels = new DancePilotMixLevels(80, 70, 60, 50);

        var changed = levels.WithDeckAFader(35);

        Assert.Equal(80, changed.MainOutputVolume);
        Assert.Equal(35, changed.DeckAFader);
        Assert.Equal(60, changed.DeckBFader);
    }

    [Fact]
    public void WithDeckBFader_DoesNotChangeMainOutputVolume()
    {
        var levels = new DancePilotMixLevels(80, 70, 60, 50);

        var changed = levels.WithDeckBFader(25);

        Assert.Equal(80, changed.MainOutputVolume);
        Assert.Equal(70, changed.DeckAFader);
        Assert.Equal(25, changed.DeckBFader);
    }

    [Fact]
    public void WithMainOutputVolume_DoesNotChangeDeckFaders()
    {
        var levels = new DancePilotMixLevels(80, 70, 60, 50);

        var changed = levels.WithMainOutputVolume(45);

        Assert.Equal(45, changed.MainOutputVolume);
        Assert.Equal(70, changed.DeckAFader);
        Assert.Equal(60, changed.DeckBFader);
    }

    [Fact]
    public void EffectiveLevels_RespectFaderAndCrossfader()
    {
        var levels = new DancePilotMixLevels(
            MainOutputVolume: 80,
            DeckAFader: 50,
            DeckBFader: 75,
            CrossfaderPosition: 25);

        Assert.Equal(0.375, levels.DeckAEffectiveLevel, precision: 3);
        Assert.Equal(0.188, levels.DeckBEffectiveLevel, precision: 3);
        Assert.Equal(0.300, levels.DeckALocalOutputLevel, precision: 3);
        Assert.Equal(0.150, levels.DeckBLocalOutputLevel, precision: 3);
    }
}
