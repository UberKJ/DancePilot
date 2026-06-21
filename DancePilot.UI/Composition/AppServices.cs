using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using DancePilot.Services.LocalMusic;
using DancePilot.Services.Media;
using DancePilot.Services.Spotify;
using DancePilot.Services.Spotify.Playback;
using Windows.Media.Playback;

namespace DancePilot.UI.Composition;

internal sealed class AppServices
{
    public required SpotifySettingsStore SpotifySettingsStore { get; init; }

    public required SpotifyService SpotifyService { get; init; }

    public required SpotifyImportRepository SpotifyImportRepository { get; init; }

    public required SpotifyPlaylistImporter SpotifyPlaylistImporter { get; init; }

    public required SpotifyPlayerService SpotifyPlayerService { get; init; }

    public required SpotifyDeviceManager SpotifyDeviceManager { get; init; }

    public required DancePilotPlaybackCoordinator PlaybackCoordinator { get; init; }

    public required PlaybackSettingsRepository PlaybackSettingsRepository { get; init; }

    public required SessionStateRepository SessionStateRepository { get; init; }

    public required SpotifyLibraryRepository SpotifyLibraryRepository { get; init; }

    public required LocalPlaylistRepository LocalPlaylistRepository { get; init; }

    public required LocalMusicLibraryService LocalMusicLibraryService { get; init; }

    public required LocalAudioAnalysisService LocalAudioAnalysisService { get; init; }

    public required SystemAudioOutputAnalysisService SystemAudioOutputAnalysisService { get; init; }

    public required AlbumArtCacheService AlbumArtCacheService { get; init; }

    public required MediaPlayer LocalMediaPlayer { get; init; }

    public required SqliteConnectionFactory ConnectionFactory { get; init; }
}
