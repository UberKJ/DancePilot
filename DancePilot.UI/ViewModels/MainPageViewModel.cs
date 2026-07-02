using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using DancePilot.Services.MockData;
using DancePilot.Services.LocalMusic;
using DancePilot.Services.Media;
using DancePilot.Services.Spotify;
using DancePilot.Services.Spotify.Playback;
using DancePilot.UI.Composition;
using DancePilot.UI.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace DancePilot.UI.ViewModels;

public sealed partial class MainPageViewModel : ObservableObject
{
    private const string SourceSpotify = "Spotify";
    private const string SourceYouTube = "YouTube";
    private const string SourceTidal = "Tidal";
    private const string SourceLocal = "Local";
    private const string DefaultCurrentAlbumArtPath = "ms-appx:///Assets/AlbumDanceFloor.png";
    private const string DefaultNextAlbumArtPath = "ms-appx:///Assets/AlbumDanceFloor.png";
    private const string DefaultLocalAlbumArtPath = "ms-appx:///Assets/AlbumDanceFloor.png";
    private const int DeckWaveformBarCount = 240;
    private const int TrackWaveformSliceCount = 1024;
    private const double DeckAnalyzerHalfHeight = 42;
    private const int FrequencyAnalyzerBandCount = 64;
    private const double LateTransitionSkipFadeSeconds = 1.0;
    private const double DefaultTransitionOverlapSeconds = 8;
    private const double DefaultSmoothFadeInSeconds = 6;
    private const double DefaultSmoothFadeOutSeconds = 10;
    private const string LocalSortFolder = "Folder";
    private const string LocalSortTitle = "Title";
    private const string LocalSortArtist = "Artist";
    private const string LocalSortBpm = "BPM";
    private const string LocalSortKey = "Key";
    private const string LocalSortNewest = "Newest";
    private const string LocalSortDuration = "Duration";
    private const string LocalSortFileType = "File type";
    private readonly SpotifySettingsStore _spotifySettingsStore;
    private readonly SpotifyService _spotifyService;
    private readonly SpotifyImportRepository _spotifyImportRepository;
    private readonly SpotifyPlaylistImporter _spotifyPlaylistImporter;
    private readonly SpotifyPlayerService _spotifyPlayerService;
    private readonly SpotifyDeviceManager _spotifyDeviceManager;
    private readonly DancePilotPlaybackCoordinator _playbackCoordinator;
    private readonly PlaybackSettingsRepository _playbackSettingsRepository;
    private readonly SessionStateRepository _sessionStateRepository;
    private readonly SpotifyLibraryRepository _spotifyLibraryRepository;
    private readonly LocalMusicRepository _localMusicRepository;
    private readonly LocalLibrarySettingsRepository _localLibrarySettingsRepository;
    private readonly LocalPlaylistRepository _localPlaylistRepository;
    private readonly LocalMusicLibraryService _localMusicLibraryService;
    private readonly LocalAudioAnalysisService _localAudioAnalysisService;
    private readonly SystemAudioOutputAnalysisService _systemAudioOutputAnalysisService;
    private readonly AlbumArtCacheService _albumArtCacheService;
    private readonly ExternalAlbumArtLookupService _externalAlbumArtLookupService;
    private readonly MediaPlayer _deckALocalMediaPlayer;
    private readonly MediaPlayer _deckBLocalMediaPlayer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _analyzerTimer;
    private readonly Dictionary<string, int?> _selectedDeckQueueItemIds = new()
    {
        ["Deck A"] = null,
        ["Deck B"] = null
    };
    private readonly Dictionary<string, int?> _lastPlayedDeckQueueItemIds = new()
    {
        ["Deck A"] = null,
        ["Deck B"] = null
    };
    private readonly Dictionary<string, RestoredDeckAlbumArt> _restoredDeckAlbumArt = new(StringComparer.Ordinal)
    {
        ["Deck A"] = new(null, null),
        ["Deck B"] = new(null, null)
    };
    private readonly Dictionary<string, LocalDeckPlaybackState> _localDeckPlaybackStates = new(StringComparer.Ordinal)
    {
        ["Deck A"] = new("Deck A"),
        ["Deck B"] = new("Deck B")
    };
    private readonly List<LocalMusicTrack> _allLocalMusicTracks = [];
    private readonly List<DancePilotQueueItem> _deckAQueue = [];
    private readonly List<DancePilotQueueItem> _deckBQueue = [];
    private readonly List<DjWaveformFrame> _deckAWaveformHistory = [];
    private readonly List<DjWaveformFrame> _deckBWaveformHistory = [];
    private readonly Dictionary<string, IReadOnlyList<TrackWaveformSlice>> _trackWaveformCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _realTrackWaveformKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _trackWaveformAnalysisInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeQueueSelectionRequestKeys = new(StringComparer.Ordinal);
    private int _nextDeckQueueItemId = 1;
    private bool _suppressSpotifyPlaylistAutoLoad;
    private string _spotifyClientId = string.Empty;
    private string _spotifyRedirectUri = SpotifyDefaults.RedirectUri;
    private string _spotifyConnectionStatus = "Not connected";
    private string _spotifyOperationMessage = "Spotify tracks are used for planning and metadata. Playback remains inside Spotify or approved Spotify playback devices.";
    private bool _isSpotifyBusy;
    private string _spotifySearchQuery = string.Empty;
    private SpotifyPlaylistSummary? _selectedSpotifyPlaylist;
    private SpotifyTrackMetadata? _selectedSpotifyTrack;
    private SpotifyTrackMetadata? _selectedSpotifySearchTrack;
    private SpotifyDevice? _selectedSpotifyDevice;
    private SpotifyPlaylistSummary? _selectedImportedSpotifyPlaylist;
    private SpotifyTrackMetadata? _selectedImportedSpotifyTrack;
    private LocalMusicTrack? _selectedLocalMusicTrack;
    private LocalMusicPlaylist? _selectedLocalMusicPlaylist;
    private DancePilotQueueItem? _selectedActiveDeckQueueItem;
    private DancePilotQueueItem? _selectedQueueViewDeckQueueItem;
    private string _localMusicSearchQuery = string.Empty;
    private string _localMusicFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    private string _localLibraryStatus = "Loaded 0 saved local tracks. Scan Local Library to build the index.";
    private int _localLibrarySavedTrackCount;
    private DateTimeOffset? _localLibraryLastScanCompletedAt;
    private string _localLibraryAlbumArtCacheFolderPath = new LocalLibrarySettings().AlbumArtCacheFolderPath;
    private string _localPlaylistStatus = "Create or select a local playlist for quick deck adds.";
    private string _selectedLocalMusicSortOption = LocalSortFolder;
    private string _activeSource = SourceSpotify;
    private string _selectedPlaybackMode = SpotifyPlaybackModes.SpotifyConnect;
    private string _activeDeckName = "Deck A";
    private string _queueViewDeckName = "Deck A";
    private string _playingDeckName = "Deck A";
    private int? _playingDeckQueueItemId;
    private int? _lastTransitionSourceItemId;
    private int? _lastTransitionTargetItemId;
    private bool _isPlaybackPlaying;
    private bool _spotifyAutopilotEnabled;
    private bool _deckTransitionEnabled = true;
    private double _autoplaySecondsBeforeEnd = 8;
    private double _transitionOverlapSeconds = DefaultTransitionOverlapSeconds;
    private double _fadeInSeconds = DefaultSmoothFadeInSeconds;
    private double _fadeOutSeconds = DefaultSmoothFadeOutSeconds;
    private bool _alwaysFadeSongs = true;
    private bool _startTransitionOnFade = true;
    private bool _removePlayedQueueItems = true;
    private double _lowFrequencyGain;
    private double _midFrequencyGain;
    private double _highFrequencyGain;
    private double _crossfaderPosition = 50;
    private double _defaultSpotifyVolume = 70;
    private double _deckAVolume = 70;
    private double _deckBVolume = 70;
    private double _seekPositionSeconds;
    private double _seekPositionMaximumSeconds = 1;
    private bool _isSeekPositionChanging;
    private string _currentOutputStatus = "No Spotify device selected";
    private string _spotifyNowPlayingTitle = "Nothing playing";
    private string _spotifyNowPlayingArtist = "Spotify";
    private string _spotifyPlaybackStatus = "Idle";
    private string _spotifyTimeRemaining = "--:--";
    private string _spotifyProgressDisplay = "--:-- / --:--";
    private ImageSource _currentDeckAlbumArt = CreateAlbumArtSource(DefaultCurrentAlbumArtPath, DefaultCurrentAlbumArtPath);
    private ImageSource _nextDeckAlbumArt = CreateAlbumArtSource(DefaultNextAlbumArtPath, DefaultNextAlbumArtPath);
    private string? _currentPlaybackAlbumArtUrl;
    private string _selectedOutputDeviceId = string.Empty;
    private string _selectedOutputDeviceName = "No Spotify device selected";
    private string _currentSpotifyPlaylistName = "No Spotify playlist active";
    private string _nextUpTitle = "No queued recommendation";
    private string _nextUpArtist = "DancePilot queue";
    private string _selectedTransitionMode = DancePilotTransitionModes.Auto;
    private bool _isPlaybackRefreshRunning;
    private bool _isTransitionAdvanceRunning;
    private bool _isAutoPilotEnabled = true;
    private bool _isShuttingDown;
    private bool _isRestoringSessionState;
    private bool _suppressLocalMusicAutoLoad;
    private bool _hasLoadedSessionState;
    private CancellationTokenSource? _sessionStateSaveDebounce;
    private int _analyzerFrame;

    public MainPageViewModel()
        : this(AppServicesFactory.CreateDefault())
    {
    }

    internal MainPageViewModel(AppServices services)
    {
        StartupLog.Write("MainPageViewModel constructor start");
        _spotifySettingsStore = services.SpotifySettingsStore;
        _spotifyService = services.SpotifyService;
        _spotifyImportRepository = services.SpotifyImportRepository;
        _spotifyPlaylistImporter = services.SpotifyPlaylistImporter;
        _spotifyPlayerService = services.SpotifyPlayerService;
        _spotifyDeviceManager = services.SpotifyDeviceManager;
        _playbackSettingsRepository = services.PlaybackSettingsRepository;
        _sessionStateRepository = services.SessionStateRepository;
        _spotifyLibraryRepository = services.SpotifyLibraryRepository;
        _localMusicRepository = services.LocalMusicRepository;
        _localLibrarySettingsRepository = services.LocalLibrarySettingsRepository;
        _localPlaylistRepository = services.LocalPlaylistRepository;
        _localMusicLibraryService = services.LocalMusicLibraryService;
        _localAudioAnalysisService = services.LocalAudioAnalysisService;
        _systemAudioOutputAnalysisService = services.SystemAudioOutputAnalysisService;
        _albumArtCacheService = services.AlbumArtCacheService;
        _externalAlbumArtLookupService = services.ExternalAlbumArtLookupService;
        _deckALocalMediaPlayer = services.LocalMediaPlayer;
        _deckBLocalMediaPlayer = services.DeckBLocalMediaPlayer;
        _playbackCoordinator = services.PlaybackCoordinator;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        SpotifyLoginCommand = new AsyncRelayCommand(LoginSpotifyAsync);
        SpotifyLogoutCommand = new AsyncRelayCommand(LogoutSpotifyAsync);
        LoadSpotifyPlaylistsCommand = new AsyncRelayCommand(LoadSpotifyPlaylistsAsync);
        PreviewSpotifyPlaylistCommand = new AsyncRelayCommand(PreviewSpotifyPlaylistAsync);
        ImportSpotifyPlaylistCommand = new AsyncRelayCommand(ImportSpotifyPlaylistAsync);
        OpenSelectedSpotifyTrackCommand = new AsyncRelayCommand(OpenSelectedSpotifyTrackAsync);
        SearchSpotifyTracksCommand = new AsyncRelayCommand(SearchSpotifyTracksAsync);
        PlaySelectedSpotifySearchTrackCommand = new AsyncRelayCommand(PlaySelectedSpotifySearchTrackAsync);
        RefreshSpotifyDevicesCommand = new AsyncRelayCommand(RefreshSpotifyDevicesAsync);
        TransferSpotifyPlaybackCommand = new AsyncRelayCommand(TransferSpotifyPlaybackAsync);
        RefreshSpotifyPlaybackCommand = new AsyncRelayCommand(RefreshSpotifyPlaybackAsync);
        PlaySelectedSpotifyTrackCommand = new AsyncRelayCommand(PlaySelectedSpotifyTrackAsync);
        PlayImportedSpotifyPlaylistCommand = new AsyncRelayCommand(PlayImportedSpotifyPlaylistAsync);
        PlayFromSelectedSpotifyTrackCommand = new AsyncRelayCommand(PlayFromSelectedSpotifyTrackAsync);
        AddSelectedTrackToQueueCommand = new AsyncRelayCommand(AddSelectedTrackToQueueAsync);
        RecommendNextFromPlaylistCommand = new AsyncRelayCommand(RecommendNextFromPlaylistAsync);
        PauseSpotifyCommand = new AsyncRelayCommand(PauseSpotifyAsync);
        ResumeSpotifyCommand = new AsyncRelayCommand(ResumeSpotifyAsync);
        SkipNextSpotifyCommand = new AsyncRelayCommand(SkipNextSpotifyAsync);
        SkipPreviousSpotifyCommand = new AsyncRelayCommand(SkipPreviousSpotifyAsync);
        SetSpotifyVolumeCommand = new AsyncRelayCommand(SetSpotifyVolumeAsync);
        SeekSpotifyCommand = new AsyncRelayCommand(SeekSpotifyAsync);
        RewindPlaybackCommand = new AsyncRelayCommand(() => SeekRelativePlaybackAsync(-15));
        FastForwardPlaybackCommand = new AsyncRelayCommand(() => SeekRelativePlaybackAsync(15));
        EmergencyStopCommand = new AsyncRelayCommand(EmergencyStopAsync);
        DisableSpotifyAutopilotCommand = new AsyncRelayCommand(DisableSpotifyAutopilotAsync);
        LoadImportedSpotifyPlaylistsCommand = new AsyncRelayCommand(LoadImportedSpotifyPlaylistsAsync);
        LoadImportedPlaylistTracksCommand = new AsyncRelayCommand(LoadImportedPlaylistTracksAsync);
        LoadLocalMusicCommand = new AsyncRelayCommand(LoadLocalMusicAsync);
        SearchLocalMusicCommand = new AsyncRelayCommand(SearchLocalMusicAsync);
        PlaySelectedLocalMusicCommand = new AsyncRelayCommand(QueueSelectedLocalMusicAsync);
        LoadLocalPlaylistsCommand = new AsyncRelayCommand(LoadLocalPlaylistsAsync);
        AddFilteredLocalMusicToPlaylistCommand = new AsyncRelayCommand(AddFilteredLocalMusicToSelectedPlaylistAsync);
        QueueSelectedLocalPlaylistToDeckACommand = new AsyncRelayCommand(() => QueueSelectedLocalPlaylistToDeckAsync("Deck A"));
        QueueSelectedLocalPlaylistToDeckBCommand = new AsyncRelayCommand(() => QueueSelectedLocalPlaylistToDeckAsync("Deck B"));
        DeleteSelectedLocalPlaylistCommand = new AsyncRelayCommand(DeleteSelectedLocalPlaylistAsync);
        PauseLocalMusicCommand = new RelayCommand(PauseLocalMusic);
        TogglePlaybackCommand = new AsyncRelayCommand(TogglePlaybackAsync);
        PlayPauseDeckACommand = new AsyncRelayCommand(() => ToggleDeckPlaybackAsync("Deck A"));
        PlayPauseDeckBCommand = new AsyncRelayCommand(() => ToggleDeckPlaybackAsync("Deck B"));
        SelectDeckACommand = new RelayCommand(() => SelectDeck("Deck A"));
        SelectDeckBCommand = new RelayCommand(() => SelectDeck("Deck B"));
        ShowDeckAQueueCommand = new RelayCommand(() => ShowDeckQueue("Deck A"));
        ShowDeckBQueueCommand = new RelayCommand(() => ShowDeckQueue("Deck B"));
        AddSelectedSourceToDeckACommand = new AsyncRelayCommand(() => QueueSelectedSourceToDeckAsync("Deck A"));
        AddSelectedSourceToDeckBCommand = new AsyncRelayCommand(() => QueueSelectedSourceToDeckAsync("Deck B"));
        AddLoadedPlaylistToActiveDeckCommand = new AsyncRelayCommand(AddLoadedPlaylistToActiveDeckAsync);
        ReplaceLoadedPlaylistOnActiveDeckCommand = new AsyncRelayCommand(ReplaceLoadedPlaylistOnActiveDeckAsync);
        RandomizeDeckAQueueCommand = new AsyncRelayCommand(() => RandomizeDeckQueueAsync("Deck A"));
        RandomizeDeckBQueueCommand = new AsyncRelayCommand(() => RandomizeDeckQueueAsync("Deck B"));
        MoveSelectedQueueItemUpCommand = new RelayCommand(MoveSelectedQueueItemUp);
        MoveSelectedQueueItemDownCommand = new RelayCommand(MoveSelectedQueueItemDown);
        RemoveSelectedQueueItemCommand = new RelayCommand(RemoveSelectedQueueItem);
        ClearPendingQueueCommand = new RelayCommand(() => ClearPendingQueue(QueueViewDeckName));
        ClearDeckAQueueCommand = new RelayCommand(() => ClearDeckQueue("Deck A"));
        ClearDeckBQueueCommand = new RelayCommand(() => ClearDeckQueue("Deck B"));

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _playbackTimer.Tick += async (_, _) => await RefreshPlaybackAndAutopilotAsync();
        _playbackTimer.Start();

        _analyzerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _analyzerTimer.Tick += (_, _) => UpdateDeckAnalyzers();
        _analyzerTimer.Start();

        _deckALocalMediaPlayer.MediaEnded += (_, _) => DispatchAsync(() => HandleLocalMediaEndedAsync("Deck A"));
        _deckBLocalMediaPlayer.MediaEnded += (_, _) => DispatchAsync(() => HandleLocalMediaEndedAsync("Deck B"));

        _ = InitializeAsync(services.ConnectionFactory);
        StartupLog.Write("MainPageViewModel constructor complete");
    }

    private void DispatchAsync(Func<Task> action)
    {
        if (_dispatcherQueue is null)
        {
            _ = RunDispatchedAsync(action);
            return;
        }

        _dispatcherQueue.TryEnqueue(async () => await RunDispatchedAsync(action));
    }

    private static async Task RunDispatchedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Dispatched view model action failed");
        }
    }

    private async Task InitializeAsync(SqliteConnectionFactory connectionFactory)
    {
        try
        {
            StartupLog.Write("MainPageViewModel async initialization start");

            var spotifySettings = await _spotifySettingsStore.LoadAsync();
            SpotifyClientId = spotifySettings.ClientId;
            SpotifyRedirectUri = spotifySettings.RedirectUri;

            await new DancePilotDatabaseMigrator(connectionFactory).MigrateAsync();
            await LoadPlaybackSettingsAsync();
            await RefreshPlaybackCollectionsAsync(loadImportedTracks: false);
            await LoadLocalPlaylistsCoreAsync();
            await RestoreSessionStateAsync();
            await LoadSavedLocalLibraryAsync();
            await RefreshSpotifyConnectionStatusAsync();

            StartupLog.Write("MainPageViewModel async initialization complete");
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "MainPageViewModel async initialization failed");
            SpotifyOperationMessage = $"DancePilot startup warning: {ex.Message}";
        }
    }

    private async Task LoadPlaybackSettingsAsync()
    {
        var settings = await _playbackSettingsRepository.LoadAsync();
        var savedPlaybackMode = settings.PlaybackMode;
        var normalizedSavedPlaybackMode = NormalizePlaybackMode(savedPlaybackMode);
        var correctedPlaybackMode = IsSupportedPlaybackMode(normalizedSavedPlaybackMode)
            ? normalizedSavedPlaybackMode
            : SpotifyPlaybackModes.SpotifyConnect;
        SelectedPlaybackMode = correctedPlaybackMode;
        SpotifyAutopilotEnabled = settings.AutopilotEnabled;
        AutoplaySecondsBeforeEnd = settings.AutoplaySecondsBeforeEnd;
        DeckTransitionEnabled = settings.DeckTransitionEnabled;
        TransitionOverlapSeconds = settings.DeckTransitionOverlapSeconds;
        SelectedTransitionMode = settings.DeckTransitionEnabled
            ? DancePilotTransitionModes.Normalize(settings.DeckTransitionMode)
            : DancePilotTransitionModes.Off;
        FadeInSeconds = Math.Max(settings.FadeInSeconds, DefaultSmoothFadeInSeconds);
        FadeOutSeconds = Math.Max(settings.FadeOutSeconds, DefaultSmoothFadeOutSeconds);
        AlwaysFadeSongs = settings.AlwaysFadeSongs;
        StartTransitionOnFade = settings.StartTransitionOnFade;
        RemovePlayedQueueItems = settings.RemovePlayedQueueItems;
        LowFrequencyGain = settings.LowFrequencyGain;
        MidFrequencyGain = settings.MidFrequencyGain;
        HighFrequencyGain = settings.HighFrequencyGain;
        CrossfaderPosition = settings.CrossfaderPosition;
        _defaultSpotifyVolume = Math.Clamp(settings.DefaultVolume, 0, 100);
        OnPropertyChanged(nameof(MainOutputVolume));
        OnPropertyChanged(nameof(MainOutputVolumeDisplay));
        OnPropertyChanged(nameof(DefaultSpotifyVolume));
        OnPropertyChanged(nameof(DefaultSpotifyVolumeDisplay));
        DeckAFader = settings.DeckAVolume;
        DeckBFader = settings.DeckBVolume;
        _selectedOutputDeviceId = settings.SelectedDeviceId;
        SelectedOutputDeviceName = string.IsNullOrWhiteSpace(settings.SelectedDeviceName)
            ? "No Spotify device selected"
            : settings.SelectedDeviceName;
        CurrentOutputStatus = $"Playback mode: {SelectedPlaybackMode}";
        if (savedPlaybackMode != correctedPlaybackMode)
        {
            await SavePlaybackSettingsAsync();
            SpotifyOperationMessage = savedPlaybackMode == SpotifyPlaybackModes.WebPlaybackSdk
                ? "Spotify Web Playback SDK is browser/WebView playback. DancePilot switched to Spotify Web API / Spotify Connect."
                : "That saved playback mode is not available yet. DancePilot switched to Spotify Web API / Spotify Connect.";
            OnPropertyChanged(nameof(SelectedPlaybackMode));
        }
    }

    private SpotifyPlaybackSettings CurrentPlaybackSettings => new()
    {
        SelectedDeviceId = SelectedSpotifyDevice?.Id ?? _selectedOutputDeviceId,
        SelectedDeviceName = SelectedSpotifyDevice?.Name ?? SelectedOutputDeviceName,
        PlaybackMode = SelectedPlaybackMode,
        AutopilotEnabled = SpotifyAutopilotEnabled,
        AutoplaySecondsBeforeEnd = Math.Max(1, Convert.ToInt32(AutoplaySecondsBeforeEnd)),
        DefaultVolume = Math.Clamp(Convert.ToInt32(MainOutputVolume), 0, 100),
        DeckAVolume = Math.Clamp(Convert.ToInt32(DeckAFader), 0, 100),
        DeckBVolume = Math.Clamp(Convert.ToInt32(DeckBFader), 0, 100),
        DeckTransitionEnabled = DeckTransitionEnabled,
        DeckTransitionSecondsBeforeEnd = Math.Max(0, Convert.ToInt32(TransitionOverlapSeconds)),
        DeckTransitionOverlapSeconds = Math.Max(0, Convert.ToInt32(TransitionOverlapSeconds)),
        DeckTransitionMode = SelectedTransitionMode,
        FadeInSeconds = Math.Clamp(Convert.ToInt32(FadeInSeconds), 1, 20),
        FadeOutSeconds = Math.Clamp(Convert.ToInt32(FadeOutSeconds), 1, 30),
        AlwaysFadeSongs = AlwaysFadeSongs,
        StartTransitionOnFade = StartTransitionOnFade,
        RemovePlayedQueueItems = RemovePlayedQueueItems,
        LowFrequencyGain = Math.Clamp(Convert.ToInt32(LowFrequencyGain), -12, 12),
        MidFrequencyGain = Math.Clamp(Convert.ToInt32(MidFrequencyGain), -12, 12),
        HighFrequencyGain = Math.Clamp(Convert.ToInt32(HighFrequencyGain), -12, 12),
        CrossfaderPosition = Math.Clamp(Convert.ToInt32(CrossfaderPosition), 0, 100)
    };

    private SpotifySettings CurrentSpotifySettings => new()
    {
        ClientId = SpotifyClientId.Trim(),
        RedirectUri = string.IsNullOrWhiteSpace(SpotifyRedirectUri)
            ? SpotifyDefaults.RedirectUri
            : SpotifyRedirectUri.Trim()
    };

    private SpotifySettings CurrentSpotifyPlaybackSettings =>
        CurrentSpotifySettings with { IncludePlaybackControlScopes = true };

    private async Task RestoreSessionStateAsync()
    {
        var state = await _sessionStateRepository.LoadAsync();
        if (state is null)
        {
            _hasLoadedSessionState = true;
            return;
        }

        _isRestoringSessionState = true;
        var restored = false;
        try
        {
            SpotifySearchQuery = state.SpotifySearchQuery;
            LocalMusicSearchQuery = state.LocalMusicSearchQuery;
            if (!string.IsNullOrWhiteSpace(state.LocalMusicFolderPath))
            {
                LocalMusicFolderPath = state.LocalMusicFolderPath;
            }

            SelectedLocalMusicSortOption = LocalMusicSortOptions.Contains(state.LocalMusicSortOption)
                ? state.LocalMusicSortOption
                : LocalSortFolder;

            _deckAQueue.Clear();
            _deckAQueue.AddRange(NormalizeQueueItems(state.DeckAQueue, "Deck A"));
            _deckBQueue.Clear();
            _deckBQueue.AddRange(NormalizeQueueItems(state.DeckBQueue, "Deck B"));

            var maxQueueItemId = _deckAQueue
                .Concat(_deckBQueue)
                .Select(item => item.Id)
                .DefaultIfEmpty(0)
                .Max();
            _nextDeckQueueItemId = Math.Max(Math.Max(1, state.NextDeckQueueItemId), maxQueueItemId + 1);

            _selectedDeckQueueItemIds["Deck A"] = FindQueueItemId("Deck A", state.SelectedDeckAQueueItemId);
            _selectedDeckQueueItemIds["Deck B"] = FindQueueItemId("Deck B", state.SelectedDeckBQueueItemId);
            _lastPlayedDeckQueueItemIds["Deck A"] = FindQueueItemId("Deck A", state.LastPlayedDeckAQueueItemId);
            _lastPlayedDeckQueueItemIds["Deck B"] = FindQueueItemId("Deck B", state.LastPlayedDeckBQueueItemId);
            _restoredDeckAlbumArt["Deck A"] = CreateRestoredDeckAlbumArt("Deck A", state.DeckAAlbumArtQueueItemId, state.DeckAAlbumArtSource);
            _restoredDeckAlbumArt["Deck B"] = CreateRestoredDeckAlbumArt("Deck B", state.DeckBAlbumArtQueueItemId, state.DeckBAlbumArtSource);
            _playingDeckName = NormalizeDeckName(state.PlayingDeckName);
            _playingDeckQueueItemId = FindQueueItemId(_playingDeckName, state.PlayingDeckQueueItemId);

            _suppressSpotifyPlaylistAutoLoad = true;
            try
            {
                SelectedSpotifyPlaylist = state.SelectedSpotifyPlaylist;
            }
            finally
            {
                _suppressSpotifyPlaylistAutoLoad = false;
            }

            ReplaceCollection(SpotifyPreviewTracks, state.SpotifyPreviewTracks);
            ReplaceCollection(SpotifySearchResults, state.SpotifySearchResults);
            SelectedSpotifyTrack = FindTrackByKey(SpotifyPreviewTracks, state.SelectedSpotifyTrackKey)
                ?? SpotifyPreviewTracks.FirstOrDefault();
            SelectedSpotifySearchTrack = FindTrackByKey(SpotifySearchResults, state.SelectedSpotifySearchTrackKey)
                ?? SpotifySearchResults.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(state.SelectedImportedSpotifyPlaylistId))
            {
                var importedPlaylist = ImportedSpotifyPlaylists.FirstOrDefault(playlist =>
                    string.Equals(playlist.SpotifyPlaylistId, state.SelectedImportedSpotifyPlaylistId, StringComparison.OrdinalIgnoreCase));
                if (importedPlaylist is not null)
                {
                    SelectedImportedSpotifyPlaylist = importedPlaylist;
                }
            }

            ActiveSource = NormalizeSource(state.ActiveSource);
            SelectedPlaybackMode = IsSupportedPlaybackMode(NormalizePlaybackMode(state.SelectedPlaybackMode))
                ? NormalizePlaybackMode(state.SelectedPlaybackMode)
                : SelectedPlaybackMode;
            ActiveDeckName = NormalizeDeckName(state.ActiveDeckName);
            RetainRestoredLocalPlaybackCursor();
            SelectedLocalMusicPlaylist = LocalMusicPlaylists.FirstOrDefault(playlist => playlist.Id == state.SelectedLocalPlaylistId)
                ?? LocalMusicPlaylists.FirstOrDefault();

            if (ActiveSource == SourceLocal)
            {
                LocalLibraryStatus = "Local source restored. Click LOCAL to scan your music folder when ready.";
            }

            await HydrateRestoredDeckAlbumArtAsync("Deck A");
            await HydrateRestoredDeckAlbumArtAsync("Deck B");

            RefreshActiveDeckQueue();
            RefreshDeckDisplayProperties();
            UpdateNextUpFromDecks();
            SpotifyOperationMessage = state.SavedAt == default
                ? "Restored your last DancePilot session."
                : $"Restored your last DancePilot session from {state.SavedAt:g}.";
            restored = true;
            StartupLog.Write($"Session restored: deckA={_deckAQueue.Count}; deckB={_deckBQueue.Count}; preview={SpotifyPreviewTracks.Count}; search={SpotifySearchResults.Count}");
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Session restore failed");
            SpotifyOperationMessage = $"DancePilot could not restore the previous session: {ex.Message}";
        }
        finally
        {
            _isRestoringSessionState = false;
            _hasLoadedSessionState = true;
            if (restored)
            {
                CacheDeckAlbumArtForCurrentQueues();
                await SaveSessionStateAsync();
            }
        }
    }

    public async Task SaveSessionStateNowAsync()
    {
        _sessionStateSaveDebounce?.Cancel();
        await SaveSessionStateAsync();
    }

    public async Task ShutdownAsync()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _playbackTimer.Stop();
        _analyzerTimer.Stop();

        try
        {
            PauseLocalPlaybackForShutdown();
            await PauseSpotifyPlaybackForShutdownAsync();
        }
        finally
        {
            await SaveSessionStateNowAsync();
        }
    }

    private void PauseLocalPlaybackForShutdown()
    {
        try
        {
            _deckALocalMediaPlayer.Pause();
            _deckBLocalMediaPlayer.Pause();
            foreach (var state in _localDeckPlaybackStates.Values)
            {
                state.Clear();
            }

            IsPlaybackPlaying = false;
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                SpotifyPlaybackStatus = "Local paused";
                CurrentOutputStatus = "Local playback paused because DancePilot closed.";
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Local playback shutdown pause failed");
        }
    }

    private async Task PauseSpotifyPlaybackForShutdownAsync()
    {
        try
        {
            if (!ShouldPauseSpotifyOnShutdown())
            {
                return;
            }

            if (!await _spotifyService.IsConnectedAsync())
            {
                return;
            }

            var missingModifyScopes = await _spotifyService.GetMissingScopesAsync([SpotifyScopes.UserModifyPlaybackState]);
            if (missingModifyScopes.Count > 0)
            {
                StartupLog.Write("Spotify shutdown pause skipped; missing scope(s): " + string.Join(", ", missingModifyScopes));
                return;
            }

            var deviceId = SelectedSpotifyDevice?.Id ?? _selectedOutputDeviceId;
            var missingReadScopes = await _spotifyService.GetMissingScopesAsync([SpotifyScopes.UserReadPlaybackState]);
            if (missingReadScopes.Count == 0)
            {
                var state = await _spotifyPlayerService.GetPlaybackStateAsync(CurrentSpotifySettings);
                if (state?.IsPlaying != true)
                {
                    return;
                }

                deviceId = state.Device?.Id ?? deviceId;
            }

            await _spotifyPlayerService.PauseAsync(CurrentSpotifySettings, deviceId ?? string.Empty);
            SpotifyOperationMessage = "Spotify playback paused because DancePilot closed.";
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Spotify shutdown pause failed");
        }
    }

    private bool ShouldPauseSpotifyOnShutdown()
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            return false;
        }

        if (IsPlaybackPlaying)
        {
            return true;
        }

        return ResolvePlayingDeckItem(_playingDeckName)?.Source == SongSources.Spotify;
    }

    private void QueueSessionStateSave()
    {
        if (_isRestoringSessionState || !_hasLoadedSessionState)
        {
            return;
        }

        _sessionStateSaveDebounce?.Cancel();
        var debounce = new CancellationTokenSource();
        _sessionStateSaveDebounce = debounce;
        _ = SaveSessionStateDebouncedAsync(debounce.Token);
    }

    private async Task SaveSessionStateDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await SaveSessionStateAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveSessionStateAsync(CancellationToken cancellationToken = default)
    {
        if (_isRestoringSessionState)
        {
            return;
        }

        try
        {
            await _sessionStateRepository.SaveAsync(CreateSessionState(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "Session save failed");
        }
    }

    private DancePilotSessionState CreateSessionState()
    {
        var deckAAlbumArt = CreateDeckAlbumArtSnapshot("Deck A");
        var deckBAlbumArt = CreateDeckAlbumArtSnapshot("Deck B");

        return new DancePilotSessionState
        {
            SavedAt = DateTimeOffset.UtcNow,
            ActiveSource = ActiveSource,
            ActiveDeckName = ActiveDeckName,
            SelectedPlaybackMode = SelectedPlaybackMode,
            PlayingDeckName = _playingDeckName,
            PlayingDeckQueueItemId = _playingDeckQueueItemId,
            SelectedDeckAQueueItemId = _selectedDeckQueueItemIds.GetValueOrDefault("Deck A"),
            SelectedDeckBQueueItemId = _selectedDeckQueueItemIds.GetValueOrDefault("Deck B"),
            LastPlayedDeckAQueueItemId = _lastPlayedDeckQueueItemIds.GetValueOrDefault("Deck A"),
            LastPlayedDeckBQueueItemId = _lastPlayedDeckQueueItemIds.GetValueOrDefault("Deck B"),
            DeckAAlbumArtQueueItemId = deckAAlbumArt.QueueItemId,
            DeckAAlbumArtSource = deckAAlbumArt.AlbumArtSource,
            DeckBAlbumArtQueueItemId = deckBAlbumArt.QueueItemId,
            DeckBAlbumArtSource = deckBAlbumArt.AlbumArtSource,
            NextDeckQueueItemId = _nextDeckQueueItemId,
            DeckAQueue = CreateDeckQueueStateSnapshot("Deck A", deckAAlbumArt),
            DeckBQueue = CreateDeckQueueStateSnapshot("Deck B", deckBAlbumArt),
            SelectedSpotifyPlaylist = SelectedSpotifyPlaylist,
            SelectedSpotifyTrackKey = TrackKey(SelectedSpotifyTrack),
            SpotifySearchQuery = SpotifySearchQuery,
            SelectedSpotifySearchTrackKey = TrackKey(SelectedSpotifySearchTrack),
            SpotifyPreviewTracks = SpotifyPreviewTracks.ToList(),
            SpotifySearchResults = SpotifySearchResults.ToList(),
            SelectedImportedSpotifyPlaylistId = SelectedImportedSpotifyPlaylist?.SpotifyPlaylistId,
            SelectedImportedSpotifyTrackKey = TrackKey(SelectedImportedSpotifyTrack),
            LocalMusicSearchQuery = LocalMusicSearchQuery,
            LocalMusicFolderPath = LocalMusicFolderPath,
            LocalMusicSortOption = SelectedLocalMusicSortOption,
            SelectedLocalPlaylistId = SelectedLocalMusicPlaylist?.Id,
            SelectedLocalFilePath = SelectedLocalMusicTrack?.FilePath
        };
    }

    private DeckAlbumArtSnapshot CreateDeckAlbumArtSnapshot(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var displayItem = ResolveDeckDisplayItem(normalizedDeckName);
        if (displayItem is null)
        {
            return new DeckAlbumArtSnapshot(null, null);
        }

        var source = ResolveDeckAlbumArtSource(normalizedDeckName);
        if (!HasUsableAlbumArtSource(source))
        {
            source = normalizedDeckName == "Deck B"
                ? _nextDeckAlbumArtSource
                : _currentDeckAlbumArtSource;
        }

        return HasUsableAlbumArtSource(source)
            ? new DeckAlbumArtSnapshot(displayItem.Id, source)
            : new DeckAlbumArtSnapshot(displayItem.Id, null);
    }

    private List<DancePilotQueueItem> CreateDeckQueueStateSnapshot(
        string deckName,
        DeckAlbumArtSnapshot albumArtSnapshot)
    {
        return QueueForDeck(NormalizeDeckName(deckName))
            .Select(item => albumArtSnapshot.QueueItemId == item.Id
                && HasUsableAlbumArtSource(albumArtSnapshot.AlbumArtSource)
                    ? item with { AlbumArtUrl = albumArtSnapshot.AlbumArtSource }
                    : item)
            .ToList();
    }

    private RestoredDeckAlbumArt CreateRestoredDeckAlbumArt(
        string deckName,
        int? queueItemId,
        string? albumArtSource)
    {
        if (queueItemId is null || !HasUsableAlbumArtSource(albumArtSource))
        {
            return new RestoredDeckAlbumArt(null, null);
        }

        var normalizedDeckName = NormalizeDeckName(deckName);
        return QueueForDeck(normalizedDeckName).Any(item => item.Id == queueItemId.Value)
            ? new RestoredDeckAlbumArt(queueItemId, albumArtSource)
            : new RestoredDeckAlbumArt(null, null);
    }

    private sealed record DeckAlbumArtSnapshot(int? QueueItemId, string? AlbumArtSource);

    private sealed record RestoredDeckAlbumArt(int? QueueItemId, string? AlbumArtSource);

    private IEnumerable<DancePilotQueueItem> NormalizeQueueItems(IEnumerable<DancePilotQueueItem> items, string deckName)
    {
        var position = 1;
        var seenSourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.Title)))
        {
            var normalizedItem = NormalizeQueueItemForDeck(item, deckName, position);
            var sourceKey = CreateExactQueueSourceKey(normalizedItem);
            if (sourceKey is not null && !seenSourceKeys.Add(sourceKey))
            {
                StartupLog.Write($"Removed duplicate restored queue source from {deckName}: {item.Title}");
                continue;
            }

            yield return normalizedItem;
            position++;
        }
    }

    private static DancePilotQueueItem NormalizeQueueItemForDeck(
        DancePilotQueueItem item,
        string deckName,
        int position)
    {
        var source = NormalizeQueueItemSource(item.Source);
        var localPath = source == SongSources.Local
            ? item.LocalPath ?? (string.IsNullOrWhiteSpace(item.ExternalUri) ? null : item.ExternalUri)
            : item.LocalPath;
        var externalUri = source == SongSources.Local
            ? string.Empty
            : item.ExternalUri;

        return item with
        {
            DeckName = NormalizeDeckName(deckName),
            Source = source,
            ExternalUri = externalUri,
            LocalPath = localPath,
            QueuePosition = position
        };
    }

    private static string NormalizeQueueItemSource(string source) =>
        source?.Trim().ToLowerInvariant() switch
        {
            SongSources.Local => SongSources.Local,
            SongSources.Tidal => SongSources.Tidal,
            SongSources.YouTube => SongSources.YouTube,
            SongSources.Manual => SongSources.Manual,
            _ => SongSources.Spotify
        };

    private int? FindQueueItemId(string deckName, int? itemId)
    {
        if (itemId is null)
        {
            return QueueForDeck(deckName).FirstOrDefault()?.Id;
        }

        return QueueForDeck(deckName).Any(item => item.Id == itemId.Value)
            ? itemId.Value
            : QueueForDeck(deckName).FirstOrDefault()?.Id;
    }

    private static SpotifyTrackMetadata? FindTrackByKey(IEnumerable<SpotifyTrackMetadata> tracks, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return tracks.FirstOrDefault(track =>
            string.Equals(TrackKey(track), key, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TrackKey(SpotifyTrackMetadata? track) =>
        track is null
            ? null
            : !string.IsNullOrWhiteSpace(track.SpotifyUri)
                ? track.SpotifyUri
                : track.SpotifyTrackId;

    private sealed class LocalDeckPlaybackState(string playerDeckName)
    {
        public string PlayerDeckName { get; } = playerDeckName;

        public string LogicalDeckName { get; set; } = playerDeckName;

        public int? QueueItemId { get; set; }

        public DateTimeOffset? RequestedAt { get; set; }

        public int? PreviousLastPlayedQueueItemId { get; set; }

        public bool ProgressObserved { get; set; }

        public LocalMusicTrack? Track { get; set; }

        public bool IsLoaded => QueueItemId is not null;

        public void Clear()
        {
            LogicalDeckName = PlayerDeckName;
            QueueItemId = null;
            RequestedAt = null;
            PreviousLastPlayedQueueItemId = null;
            ProgressObserved = false;
            Track = null;
        }
    }
}

public sealed record WaveBar(double Height, Brush Fill)
{
    public double UpperHeight { get; init; } = Height;

    public double LowerHeight { get; init; } = Height;

    public Brush UpperFill { get; init; } = Fill;

    public Brush LowerFill { get; init; } = Fill;

    public double PeakHeight { get; init; } = Height;

    public Brush PeakFill { get; init; } = Fill;

    public Thickness PeakMargin { get; init; } = new(0);

    public double LowHeight { get; init; } = Height;

    public double MidHeight { get; init; } = 0;

    public double HighHeight { get; init; } = 0;

    public double BeatHeight { get; init; } = 0;

    public Brush LowFill { get; init; } = Fill;

    public Brush MidFill { get; init; } = Fill;

    public Brush HighFill { get; init; } = Fill;

    public Brush BeatFill { get; init; } = Fill;
}

public sealed record DjWaveformFrame(double Low, double Mid, double High, double Beat);

public sealed record FrequencyBand(
    string Label,
    string Range,
    double DeckALevel,
    double DeckBLevel,
    double PeakLevel,
    Brush DeckAFill,
    Brush DeckBFill,
    Brush PeakFill,
    Brush GridFill);

public sealed record EnergySegment(Brush Fill);

public sealed record RemoteAction(string Label, string IconGlyph, Brush Background);
