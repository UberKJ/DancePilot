using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DancePilot.Services.Tidal.Auth;

[SupportedOSPlatform("windows")]
public sealed class EncryptedFileTidalTokenStore : ITidalTokenStore
{
    private readonly string _tokenPath;

    public EncryptedFileTidalTokenStore(string? tokenPath = null)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DancePilot");
        _tokenPath = tokenPath ?? Path.Combine(appData, "tidal-token.dat");
    }

    public async Task<TidalTokenSet?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_tokenPath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_tokenPath, cancellationToken);
        var jsonBytes = ProtectedData.Unprotect(
            protectedBytes,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<TidalTokenSet>(Encoding.UTF8.GetString(jsonBytes));
    }

    public async Task SaveAsync(TidalTokenSet tokenSet, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tokenPath)!);
        var json = JsonSerializer.Serialize(tokenSet);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_tokenPath, protectedBytes, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_tokenPath))
        {
            File.Delete(_tokenPath);
        }

        return Task.CompletedTask;
    }
}
