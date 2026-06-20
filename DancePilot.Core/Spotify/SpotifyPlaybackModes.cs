namespace DancePilot.Core.Spotify;

public static class SpotifyPlaybackModes
{
    public const string SpotifyConnect = "Spotify Web API / Spotify Connect";
    public const string LegacySpotifyConnect = "Spotify Connect";
    public const string WebPlaybackSdk = "Spotify Web Playback SDK";
    public const string ExternalSpotifyAppHandoff = "Open in Spotify App";
    public const string LegacyExternalSpotifyAppHandoff = "External Spotify App Handoff";
    public const string LocalFilesFuture = "Local Music Files";

    public static readonly IReadOnlyList<string> All =
    [
        SpotifyConnect,
        ExternalSpotifyAppHandoff,
        LocalFilesFuture
    ];
}
