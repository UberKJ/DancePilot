namespace DancePilot.Services.Spotify;

public enum SpotifyApiErrorKind
{
    Unknown,
    ExpiredToken,
    RefreshTokenFailed,
    MissingScopes,
    RateLimited,
    NetworkOffline,
    UnavailableTrack,
    InvalidRequest,
    NoPremiumAccount,
    NoActiveDevice,
    DeviceUnavailable,
    PlaylistAccessForbidden,
    PlaybackForbidden,
    WebPlaybackSdkUnsupported
}
