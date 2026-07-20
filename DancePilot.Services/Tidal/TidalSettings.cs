namespace DancePilot.Services.Tidal;

public sealed record TidalSettings
{
    public string ClientId { get; init; } = string.Empty;

    public string RedirectUri { get; init; } = TidalDefaults.RedirectUri;

    public string CountryCode { get; init; } = "US";

    public bool ExperimentalCatalogEnabled { get; init; }
}
