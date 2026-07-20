namespace DancePilot.Services.Tidal;

public static class TidalLink
{
    public static bool TryCreateOfficialUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps
            || !(string.Equals(candidate.Host, "tidal.com", StringComparison.OrdinalIgnoreCase)
                || candidate.Host.EndsWith(".tidal.com", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}
