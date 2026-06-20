namespace DancePilot.Services.Spotify;

public static class SpotifyScopes
{
    public const string UserReadPrivate = "user-read-private";
    public const string PlaylistReadPrivate = "playlist-read-private";
    public const string PlaylistReadCollaborative = "playlist-read-collaborative";
    public const string UserReadPlaybackState = "user-read-playback-state";
    public const string UserModifyPlaybackState = "user-modify-playback-state";
    public const string UserReadCurrentlyPlaying = "user-read-currently-playing";

    public static readonly IReadOnlyList<string> PlanningMetadata =
    [
        UserReadPrivate,
        PlaylistReadPrivate,
        PlaylistReadCollaborative
    ];

    public static readonly IReadOnlyList<string> PlaybackControl =
    [
        UserReadPrivate,
        PlaylistReadPrivate,
        PlaylistReadCollaborative,
        UserReadPlaybackState,
        UserModifyPlaybackState,
        UserReadCurrentlyPlaying
    ];
}
