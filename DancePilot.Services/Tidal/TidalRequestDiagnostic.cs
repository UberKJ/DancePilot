using System.Text;

namespace DancePilot.Services.Tidal;

public sealed record TidalRequestDiagnostic
{
    public required string OperationName { get; init; }

    public required string Method { get; init; }

    public required string RequestUri { get; init; }

    public int? StatusCode { get; init; }

    public string? ContentType { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorTitle { get; init; }

    public string? ErrorDetail { get; init; }

    public TimeSpan? RetryAfter { get; init; }

    public string GrantedScopes { get; init; } = string.Empty;

    public string ToDisplayString()
    {
        var status = StatusCode is { } code ? $"HTTP {code}" : "no HTTP response";
        var detail = ErrorDetail ?? ErrorTitle ?? ErrorCode;
        return string.IsNullOrWhiteSpace(detail)
            ? $"{OperationName} failed: {status}."
            : $"{OperationName} failed: {status} - {detail}.";
    }

    public string ToLogString()
    {
        var fields = new StringBuilder("TIDAL request diagnostic");
        Append(fields, "operation", OperationName);
        Append(fields, "method", Method);
        Append(fields, "uri", RequestUri);
        Append(fields, "status", StatusCode?.ToString());
        Append(fields, "contentType", ContentType);
        Append(fields, "errorCode", ErrorCode);
        Append(fields, "errorTitle", ErrorTitle);
        Append(fields, "errorDetail", ErrorDetail);
        Append(fields, "retryAfterSeconds", RetryAfter?.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        Append(fields, "grantedScopes", GrantedScopes);
        return fields.ToString();
    }

    private static void Append(StringBuilder target, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Append("; ").Append(name).Append('=').Append(value.ReplaceLineEndings(" "));
        }
    }
}

public sealed class TidalRequestDiagnosticWriter
{
    private readonly string _logPath;
    private readonly bool _disabled;

    public static TidalRequestDiagnosticWriter Disabled { get; } = new(disabled: true);

    public TidalRequestDiagnosticWriter(string? logPath = null)
        : this(logPath, disabled: false)
    {
    }

    private TidalRequestDiagnosticWriter(string? logPath = null, bool disabled = false)
    {
        _disabled = disabled;
        _logPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DancePilot",
            "startup.log");
    }

    public void Write(TidalRequestDiagnostic diagnostic)
    {
        if (_disabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(
                _logPath,
                $"{DateTimeOffset.Now:O} {diagnostic.ToLogString()}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never turn an API failure into an application failure.
        }
    }

    public static string SanitizeUri(Uri uri)
    {
        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !IsSensitiveQueryKey(part.Split('=', 2)[0]));
        builder.Query = string.Join('&', query);
        return builder.Uri.AbsoluteUri;
    }

    private static bool IsSensitiveQueryKey(string encodedKey)
    {
        var key = Uri.UnescapeDataString(encodedKey);
        return key.Equals("access_token", StringComparison.OrdinalIgnoreCase)
            || key.Equals("refresh_token", StringComparison.OrdinalIgnoreCase)
            || key.Equals("authorization_code", StringComparison.OrdinalIgnoreCase)
            || key.Equals("code", StringComparison.OrdinalIgnoreCase)
            || key.Equals("client_secret", StringComparison.OrdinalIgnoreCase)
            || key.Equals("code_verifier", StringComparison.OrdinalIgnoreCase);
    }
}
