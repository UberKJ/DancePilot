using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using DancePilot.Services.LocalMusic;
using DancePilot.Services.Media;
using DancePilot.Services.Spotify;
using DancePilot.Services.Spotify.Auth;
using DancePilot.Services.Spotify.Playback;
using Windows.Media.Playback;

namespace DancePilot.UI.Composition;

internal static class AppServicesFactory
{
    public static AppServices CreateDefault()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DancePilot");
        Directory.CreateDirectory(appData);
        var albumArtCacheFolder = Path.Combine(appData, "AlbumArt");
        Directory.CreateDirectory(albumArtCacheFolder);

        var spotifySettingsStore = new SpotifySettingsStore();
        var tokenStore = new EncryptedFileSpotifyTokenStore();
        var httpClient = new HttpClient();
        var authService = new SpotifyAuthService(httpClient, tokenStore);
        var spotifyService = new SpotifyService(httpClient, tokenStore, authService);

        var connectionFactory = new SqliteConnectionFactory(new DatabaseOptions
        {
            DatabasePath = Path.Combine(appData, "dancepilot.sqlite")
        });

        var spotifyImportRepository = new SpotifyImportRepository(connectionFactory);
        var spotifyPlaylistImporter = new SpotifyPlaylistImporter(spotifyService, spotifyImportRepository);
        var spotifyPlayerService = new SpotifyPlayerService(spotifyService);
        var queueRepository = new PlaybackQueueRepository(connectionFactory);
        var historyRepository = new PlaybackHistoryRepository(connectionFactory);

        return new AppServices
        {
            SpotifySettingsStore = spotifySettingsStore,
            SpotifyService = spotifyService,
            SpotifyImportRepository = spotifyImportRepository,
            SpotifyPlaylistImporter = spotifyPlaylistImporter,
            SpotifyPlayerService = spotifyPlayerService,
            SpotifyDeviceManager = new SpotifyDeviceManager(spotifyService),
            PlaybackCoordinator = new DancePilotPlaybackCoordinator(spotifyPlayerService, queueRepository, historyRepository),
            PlaybackSettingsRepository = new PlaybackSettingsRepository(connectionFactory),
            SessionStateRepository = new SessionStateRepository(connectionFactory),
            SpotifyLibraryRepository = new SpotifyLibraryRepository(connectionFactory),
            LocalMusicRepository = new LocalMusicRepository(connectionFactory),
            LocalLibrarySettingsRepository = new LocalLibrarySettingsRepository(connectionFactory),
            LocalPlaylistRepository = new LocalPlaylistRepository(connectionFactory),
            LocalMusicLibraryService = new LocalMusicLibraryService(),
            LocalAudioAnalysisService = new LocalAudioAnalysisService(),
            SystemAudioOutputAnalysisService = new SystemAudioOutputAnalysisService(),
            AlbumArtCacheService = new AlbumArtCacheService(
                new AlbumArtCacheRepository(connectionFactory),
                httpClient,
                albumArtCacheFolder),
            ExternalAlbumArtLookupService = new ExternalAlbumArtLookupService(httpClient),
            LocalMediaPlayer = new MediaPlayer(),
            DeckBLocalMediaPlayer = new MediaPlayer(),
            ConnectionFactory = connectionFactory
        };
    }
}
