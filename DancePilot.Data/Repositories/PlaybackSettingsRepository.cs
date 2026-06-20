using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;

namespace DancePilot.Data.Repositories;

public sealed class PlaybackSettingsRepository
{
    private const string SelectedDeviceIdKey = "spotify_selected_device_id";
    private const string SelectedDeviceNameKey = "spotify_selected_device_name";
    private const string PlaybackModeKey = "spotify_playback_mode";
    private const string AutopilotEnabledKey = "spotify_autopilot_enabled";
    private const string AutoplaySecondsBeforeEndKey = "spotify_autoplay_seconds_before_end";
    private const string DefaultVolumeKey = "spotify_default_volume";
    private const string DeckTransitionEnabledKey = "deck_transition_enabled";
    private const string DeckTransitionSecondsBeforeEndKey = "deck_transition_seconds_before_end";
    private const string DeckTransitionModeKey = "deck_transition_mode";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public PlaybackSettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task<SpotifyPlaybackSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);

        var settings = await ReadAllAsync(cancellationToken);
        return new SpotifyPlaybackSettings
        {
            SelectedDeviceId = settings.GetValueOrDefault(SelectedDeviceIdKey, string.Empty),
            SelectedDeviceName = settings.GetValueOrDefault(SelectedDeviceNameKey, string.Empty),
            PlaybackMode = settings.GetValueOrDefault(PlaybackModeKey, SpotifyPlaybackModes.SpotifyConnect),
            AutopilotEnabled = bool.TryParse(settings.GetValueOrDefault(AutopilotEnabledKey), out var enabled) && enabled,
            AutoplaySecondsBeforeEnd = ParseInt(settings.GetValueOrDefault(AutoplaySecondsBeforeEndKey), 8),
            DefaultVolume = ParseInt(settings.GetValueOrDefault(DefaultVolumeKey), 70),
            DeckTransitionEnabled = !bool.TryParse(settings.GetValueOrDefault(DeckTransitionEnabledKey), out var transitionEnabled) || transitionEnabled,
            DeckTransitionSecondsBeforeEnd = ParseInt(settings.GetValueOrDefault(DeckTransitionSecondsBeforeEndKey), 8),
            DeckTransitionMode = settings.GetValueOrDefault(DeckTransitionModeKey, "Same deck next item")
        };
    }

    public async Task SaveAsync(SpotifyPlaybackSettings settings, CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);
        await SetAsync(SelectedDeviceIdKey, settings.SelectedDeviceId, cancellationToken);
        await SetAsync(SelectedDeviceNameKey, settings.SelectedDeviceName, cancellationToken);
        await SetAsync(PlaybackModeKey, settings.PlaybackMode, cancellationToken);
        await SetAsync(AutopilotEnabledKey, settings.AutopilotEnabled.ToString(), cancellationToken);
        await SetAsync(AutoplaySecondsBeforeEndKey, settings.AutoplaySecondsBeforeEnd.ToString(), cancellationToken);
        await SetAsync(DefaultVolumeKey, settings.DefaultVolume.ToString(), cancellationToken);
        await SetAsync(DeckTransitionEnabledKey, settings.DeckTransitionEnabled.ToString(), cancellationToken);
        await SetAsync(DeckTransitionSecondsBeforeEndKey, settings.DeckTransitionSecondsBeforeEnd.ToString(), cancellationToken);
        await SetAsync(DeckTransitionModeKey, settings.DeckTransitionMode, cancellationToken);
    }

    private async Task<Dictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM app_settings;";

        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            settings[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return settings;
    }

    private async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;
}
