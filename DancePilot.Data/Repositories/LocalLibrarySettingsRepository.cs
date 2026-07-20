using DancePilot.Core.Models;
using DancePilot.Data.Migrations;
using DancePilot.Data.Storage;
using Microsoft.Data.Sqlite;

namespace DancePilot.Data.Repositories;

public sealed class LocalLibrarySettingsRepository
{
    private const string LibraryFolderPathKey = "local_library_folder_path";
    private const string LastScanStartedAtKey = "local_library_last_scan_started_at";
    private const string LastScanCompletedAtKey = "local_library_last_scan_completed_at";
    private const string TrackCountKey = "local_library_track_count";
    private const string AlbumArtCacheFolderPathKey = "local_library_album_art_cache_folder_path";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DancePilotDatabaseMigrator _migrator;

    public LocalLibrarySettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _migrator = new DancePilotDatabaseMigrator(connectionFactory);
    }

    public async Task<LocalLibrarySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);
        var settings = await ReadAllAsync(cancellationToken);
        var defaults = new LocalLibrarySettings();

        return defaults with
        {
            LibraryFolderPath = FirstNonBlank(settings.GetValueOrDefault(LibraryFolderPathKey), defaults.LibraryFolderPath),
            LastScanStartedAt = ParseDate(settings.GetValueOrDefault(LastScanStartedAtKey)),
            LastScanCompletedAt = ParseDate(settings.GetValueOrDefault(LastScanCompletedAtKey)),
            TrackCount = ParseInt(settings.GetValueOrDefault(TrackCountKey), defaults.TrackCount),
            AlbumArtCacheFolderPath = FirstNonBlank(
                settings.GetValueOrDefault(AlbumArtCacheFolderPathKey),
                defaults.AlbumArtCacheFolderPath)
        };
    }

    public async Task SaveAsync(LocalLibrarySettings settings, CancellationToken cancellationToken = default)
    {
        await _migrator.MigrateAsync(cancellationToken);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await SetAsync(connection, (SqliteTransaction)transaction, LibraryFolderPathKey, settings.LibraryFolderPath, cancellationToken);
        await SetAsync(connection, (SqliteTransaction)transaction, LastScanStartedAtKey, settings.LastScanStartedAt?.ToString("O") ?? string.Empty, cancellationToken);
        await SetAsync(connection, (SqliteTransaction)transaction, LastScanCompletedAtKey, settings.LastScanCompletedAt?.ToString("O") ?? string.Empty, cancellationToken);
        await SetAsync(connection, (SqliteTransaction)transaction, TrackCountKey, settings.TrackCount.ToString(), cancellationToken);
        await SetAsync(connection, (SqliteTransaction)transaction, AlbumArtCacheFolderPathKey, settings.AlbumArtCacheFolderPath, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
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

    private static async Task SetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FirstNonBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;
}
