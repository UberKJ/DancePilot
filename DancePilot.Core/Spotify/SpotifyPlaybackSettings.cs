namespace DancePilot.Core.Spotify;

public sealed record SpotifyPlaybackSettings
{
    public string SelectedDeviceId { get; init; } = string.Empty;

    public string SelectedDeviceName { get; init; } = string.Empty;

    public string PlaybackMode { get; init; } = SpotifyPlaybackModes.SpotifyConnect;

    public bool AutopilotEnabled { get; init; }

    public int AutoplaySecondsBeforeEnd { get; init; } = 8;

    public int DefaultVolume { get; init; } = 70;

    public bool DeckTransitionEnabled { get; init; } = true;

    public int DeckTransitionSecondsBeforeEnd { get; init; } = 8;

    public string DeckTransitionMode { get; init; } = "Same deck next item";

    public int FadeInSeconds { get; init; } = 4;

    public int FadeOutSeconds { get; init; } = 8;

    public bool AlwaysFadeSongs { get; init; } = true;

    public bool StartTransitionOnFade { get; init; } = true;

    public int LowFrequencyGain { get; init; }

    public int MidFrequencyGain { get; init; }

    public int HighFrequencyGain { get; init; }

    public int CrossfaderPosition { get; init; } = 50;
}
