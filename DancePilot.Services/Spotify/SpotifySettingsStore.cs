using System.Text.Json;

namespace DancePilot.Services.Spotify;

public sealed class SpotifySettingsStore
{
    private readonly string _settingsPath;

    public SpotifySettingsStore(string? settingsPath = null)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DancePilot");
        _settingsPath = settingsPath ?? Path.Combine(appData, "spotify-settings.json");
    }

    public async Task<SpotifySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new SpotifySettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<SpotifySettings>(stream, cancellationToken: cancellationToken)
            ?? new SpotifySettings();
    }

    public async Task SaveAsync(SpotifySettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }
}
