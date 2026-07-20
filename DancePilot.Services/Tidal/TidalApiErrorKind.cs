namespace DancePilot.Services.Tidal;

public enum TidalApiErrorKind
{
    Unknown,
    Unauthorized,
    ExpiredToken,
    Forbidden,
    NotFound,
    RateLimited,
    InvalidRequest,
    NetworkUnavailable,
    MalformedResponse,
    TemporaryFailure,
    MissingRequiredScope,
    EndpointUnavailable,
    AuthorizationFailed,
    AuthorizationTimedOut,
    RedirectPortConflict,
    UserCancelled
}
