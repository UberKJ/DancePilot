using System.Text.Json;

namespace DancePilot.Services.Tidal;

public sealed class TidalSettingsStore
{
    private readonly string _settingsPath;

    public TidalSettingsStore(string? settingsPath = null)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DancePilot");
        _settingsPath = settingsPath ?? Path.Combine(appData, "tidal-settings.json");
    }

    public async Task<TidalSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new TidalSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<TidalSettings>(stream, cancellationToken: cancellationToken)
            ?? new TidalSettings();
    }

    public async Task SaveAsync(TidalSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(
            stream,
            settings,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
    }
}
