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

public sealed class MainPageViewModel : ObservableObject
{
    private const string SourceSpotify = "Spotify";
    private const string SourceYouTube = "YouTube";
    private const string SourceTidal = "Tidal";
    private const string SourceLocal = "Local";
    private const string TransitionSameDeck = "Same deck next item";
    private const string TransitionOppositeDeck = "Opposite deck next item";
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
    private readonly LocalPlaylistRepository _localPlaylistRepository;
    private readonly LocalMusicLibraryService _localMusicLibraryService;
    private readonly LocalAudioAnalysisService _localAudioAnalysisService;
    private readonly SystemAudioOutputAnalysisService _systemAudioOutputAnalysisService;
    private readonly AlbumArtCacheService _albumArtCacheService;
    private readonly ExternalAlbumArtLookupService _externalAlbumArtLookupService;
    private readonly MediaPlayer _localMediaPlayer;
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
    private readonly List<LocalMusicTrack> _allLocalMusicTracks = [];
    private readonly List<DancePilotQueueItem> _deckAQueue = [];
    private readonly List<DancePilotQueueItem> _deckBQueue = [];
    private readonly List<DjWaveformFrame> _deckAWaveformHistory = [];
    private readonly List<DjWaveformFrame> _deckBWaveformHistory = [];
    private readonly Dictionary<string, IReadOnlyList<TrackWaveformSlice>> _trackWaveformCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _realTrackWaveformKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _trackWaveformAnalysisInFlight = new(StringComparer.OrdinalIgnoreCase);
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
    private string _localLibraryStatus = "Pick a music folder or click LOCAL to scan your Windows Music folder.";
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
    private string _selectedTransitionMode = TransitionOppositeDeck;
    private bool _isPlaybackRefreshRunning;
    private bool _isTransitionAdvanceRunning;
    private bool _isAutoPilotEnabled = true;
    private bool _isShuttingDown;
    private bool _isRestoringSessionState;
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
        _localPlaylistRepository = services.LocalPlaylistRepository;
        _localMusicLibraryService = services.LocalMusicLibraryService;
        _localAudioAnalysisService = services.LocalAudioAnalysisService;
        _systemAudioOutputAnalysisService = services.SystemAudioOutputAnalysisService;
        _albumArtCacheService = services.AlbumArtCacheService;
        _externalAlbumArtLookupService = services.ExternalAlbumArtLookupService;
        _localMediaPlayer = services.LocalMediaPlayer;
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
        RandomizePlaylistToDeckACommand = new AsyncRelayCommand(() => RandomizeActiveSourceToDeckAsync("Deck A"));
        RandomizePlaylistToDeckBCommand = new AsyncRelayCommand(() => RandomizeActiveSourceToDeckAsync("Deck B"));
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

        _localMediaPlayer.MediaEnded += (_, _) => DispatchAsync(HandleLocalMediaEndedAsync);

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

    public bool IsAutoPilotEnabled
    {
        get => _isAutoPilotEnabled;
        set
        {
            if (SetProperty(ref _isAutoPilotEnabled, value))
            {
                OnPropertyChanged(nameof(AutoPilotStatus));
            }
        }
    }

    public string AutoPilotStatus => IsAutoPilotEnabled ? "ON" : "OFF";

    public string SpotifyClientId
    {
        get => _spotifyClientId;
        set => SetProperty(ref _spotifyClientId, value);
    }

    public string SpotifyRedirectUri
    {
        get => _spotifyRedirectUri;
        set => SetProperty(ref _spotifyRedirectUri, value);
    }

    public string SpotifyConnectionStatus
    {
        get => _spotifyConnectionStatus;
        private set => SetProperty(ref _spotifyConnectionStatus, value);
    }

    public string SpotifyOperationMessage
    {
        get => _spotifyOperationMessage;
        private set => SetProperty(ref _spotifyOperationMessage, value);
    }

    public bool IsSpotifyBusy
    {
        get => _isSpotifyBusy;
        private set => SetProperty(ref _isSpotifyBusy, value);
    }

    public string SpotifySearchQuery
    {
        get => _spotifySearchQuery;
        set
        {
            if (SetProperty(ref _spotifySearchQuery, value))
            {
                QueueSessionStateSave();
            }
        }
    }

    public SpotifyPlaylistSummary? SelectedSpotifyPlaylist
    {
        get => _selectedSpotifyPlaylist;
        set
        {
            if (SetProperty(ref _selectedSpotifyPlaylist, value)
                && value is not null
                && !_suppressSpotifyPlaylistAutoLoad)
            {
                _ = PreviewSpotifyPlaylistFromSelectionAsync(value);
            }

            QueueSessionStateSave();
        }
    }

    public SpotifyTrackMetadata? SelectedSpotifyTrack
    {
        get => _selectedSpotifyTrack;
        set
        {
            if (SetProperty(ref _selectedSpotifyTrack, value))
            {
                QueueSessionStateSave();
            }
        }
    }

    public SpotifyTrackMetadata? SelectedSpotifySearchTrack
    {
        get => _selectedSpotifySearchTrack;
        set
        {
            if (SetProperty(ref _selectedSpotifySearchTrack, value))
            {
                QueueSessionStateSave();
            }
        }
    }

    public SpotifyDevice? SelectedSpotifyDevice
    {
        get => _selectedSpotifyDevice;
        set
        {
            if (SetProperty(ref _selectedSpotifyDevice, value))
            {
                _selectedOutputDeviceId = value?.Id ?? string.Empty;
                SelectedOutputDeviceName = value?.DisplayName ?? "No Spotify device selected";
            }
        }
    }

    public SpotifyPlaylistSummary? SelectedImportedSpotifyPlaylist
    {
        get => _selectedImportedSpotifyPlaylist;
        set
        {
            if (SetProperty(ref _selectedImportedSpotifyPlaylist, value))
            {
                QueueSessionStateSave();
            }
        }
    }

    public SpotifyTrackMetadata? SelectedImportedSpotifyTrack
    {
        get => _selectedImportedSpotifyTrack;
        set
        {
            if (SetProperty(ref _selectedImportedSpotifyTrack, value))
            {
                QueueSessionStateSave();
            }
        }
    }

    public LocalMusicTrack? SelectedLocalMusicTrack
    {
        get => _selectedLocalMusicTrack;
        set
        {
            if (SetProperty(ref _selectedLocalMusicTrack, value))
            {
                QueueSessionStateSave();
            }
        }
    }

    public LocalMusicPlaylist? SelectedLocalMusicPlaylist
    {
        get => _selectedLocalMusicPlaylist;
        set
        {
            if (SetProperty(ref _selectedLocalMusicPlaylist, value))
            {
                LocalPlaylistStatus = value is null
                    ? "Create or select a local playlist for quick deck adds."
                    : $"Selected local playlist: {value.DisplayName}.";
                QueueSessionStateSave();
            }
        }
    }

    public DancePilotQueueItem? SelectedActiveDeckQueueItem
    {
        get => _selectedActiveDeckQueueItem;
        set
        {
            if (SetProperty(ref _selectedActiveDeckQueueItem, value))
            {
                _selectedDeckQueueItemIds[ActiveDeckName] = value?.Id;
                UpdateNextUpFromDecks();
                RefreshDeckDisplayProperties();
                QueueSessionStateSave();
            }
        }
    }

    public DancePilotQueueItem? SelectedQueueViewDeckQueueItem
    {
        get => _selectedQueueViewDeckQueueItem;
        set
        {
            if (SetProperty(ref _selectedQueueViewDeckQueueItem, value))
            {
                _selectedDeckQueueItemIds[QueueViewDeckName] = value?.Id;
                UpdateNextUpFromDecks();
                RefreshDeckDisplayProperties();
                QueueSessionStateSave();
            }
        }
    }

    public string LocalMusicSearchQuery
    {
        get => _localMusicSearchQuery;
        set
        {
            if (SetProperty(ref _localMusicSearchQuery, value))
            {
                QueueSessionStateSave();
            }
        }
    }

    public IReadOnlyList<string> LocalMusicSortOptions { get; } =
    [
        LocalSortFolder,
        LocalSortTitle,
        LocalSortArtist,
        LocalSortBpm,
        LocalSortKey,
        LocalSortNewest,
        LocalSortDuration,
        LocalSortFileType
    ];

    public string SelectedLocalMusicSortOption
    {
        get => _selectedLocalMusicSortOption;
        set
        {
            var normalized = LocalMusicSortOptions.Contains(value) ? value : LocalSortFolder;
            if (SetProperty(ref _selectedLocalMusicSortOption, normalized))
            {
                ApplyLocalMusicFilter();
                QueueSessionStateSave();
            }
        }
    }

    public string LocalMusicFolderPath
    {
        get => _localMusicFolderPath;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                : value.Trim();
            if (SetProperty(ref _localMusicFolderPath, normalized))
            {
                QueueSessionStateSave();
            }
        }
    }

    public string LocalLibraryStatus
    {
        get => _localLibraryStatus;
        private set => SetProperty(ref _localLibraryStatus, value);
    }

    public string LocalPlaylistStatus
    {
        get => _localPlaylistStatus;
        private set => SetProperty(ref _localPlaylistStatus, value);
    }

    public string ActiveSource
    {
        get => _activeSource;
        set
        {
            var normalized = NormalizeSource(value);
            if (SetProperty(ref _activeSource, normalized))
            {
                OnPropertyChanged(nameof(SpotifySourceVisibility));
                OnPropertyChanged(nameof(YouTubeSourceVisibility));
                OnPropertyChanged(nameof(TidalSourceVisibility));
                OnPropertyChanged(nameof(LocalSourceVisibility));
                OnPropertyChanged(nameof(SpotifySourceColumnWidth));
                OnPropertyChanged(nameof(RequestsColumnWidth));
                OnPropertyChanged(nameof(SpotifySearchRowHeight));
                OnPropertyChanged(nameof(SpotifyResultsRowHeight));
                OnPropertyChanged(nameof(LocalSearchRowHeight));
                OnPropertyChanged(nameof(LocalResultsRowHeight));
                OnPropertyChanged(nameof(SourceInstruction));

                if (normalized == SourceLocal)
                {
                    SelectedPlaybackMode = SpotifyPlaybackModes.LocalFilesFuture;
                    if (!_isRestoringSessionState)
                    {
                        _ = LoadLocalMusicAsync();
                    }
                }
                else if (normalized == SourceSpotify && SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
                {
                    SelectedPlaybackMode = SpotifyPlaybackModes.SpotifyConnect;
                }

                QueueSessionStateSave();
            }
        }
    }

    public IReadOnlyList<string> SourceOptions { get; } =
    [
        SourceSpotify,
        SourceYouTube,
        SourceTidal,
        SourceLocal
    ];

    public Visibility SpotifySourceVisibility => ActiveSource == SourceSpotify ? Visibility.Visible : Visibility.Collapsed;

    public Visibility YouTubeSourceVisibility => ActiveSource == SourceYouTube ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TidalSourceVisibility => ActiveSource == SourceTidal ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LocalSourceVisibility => ActiveSource == SourceLocal ? Visibility.Visible : Visibility.Collapsed;

    public GridLength SpotifySourceColumnWidth => ActiveSource == SourceSpotify
        ? new GridLength(310)
        : new GridLength(0);

    public GridLength SourceSetupColumnWidth => new(360);

    public GridLength RequestsColumnWidth => SourceSetupColumnWidth;

    public GridLength SpotifySearchRowHeight => ActiveSource == SourceSpotify
        ? new GridLength(1, GridUnitType.Auto)
        : new GridLength(0);

    public GridLength SpotifyResultsRowHeight => ActiveSource == SourceSpotify
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);

    public GridLength LocalSearchRowHeight => ActiveSource == SourceLocal
        ? new GridLength(1, GridUnitType.Auto)
        : new GridLength(0);

    public GridLength LocalResultsRowHeight => ActiveSource == SourceLocal
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);

    public string SourceInstruction => ActiveSource switch
    {
        SourceLocal => "Local files from this Windows PC.",
        SourceYouTube => "YouTube connector view.",
        SourceTidal => "Tidal connector view.",
        _ => "Spotify playlists, search, and Connect playback."
    };

    public string ActiveDeckName
    {
        get => _activeDeckName;
        private set
        {
            if (SetProperty(ref _activeDeckName, value))
            {
                _defaultSpotifyVolume = ResolveDeckVolume(_activeDeckName);
                OnPropertyChanged(nameof(CurrentDeckHeader));
                OnPropertyChanged(nameof(NextDeckHeader));
                OnPropertyChanged(nameof(DefaultSpotifyVolume));
                OnPropertyChanged(nameof(DefaultSpotifyVolumeDisplay));
                RefreshDeckDisplayProperties();
                QueueSessionStateSave();
            }
        }
    }

    public string QueueViewDeckName
    {
        get => _queueViewDeckName;
        private set
        {
            var normalizedDeckName = NormalizeDeckName(value);
            if (SetProperty(ref _queueViewDeckName, normalizedDeckName))
            {
                RefreshQueueViewDeckQueue();
                OnPropertyChanged(nameof(QueueViewDeckHeader));
                OnPropertyChanged(nameof(QueueViewDeckCountDisplay));
                OnPropertyChanged(nameof(QueueViewDeckBrush));
                OnPropertyChanged(nameof(QueueViewDeckAButtonBackground));
                OnPropertyChanged(nameof(QueueViewDeckAButtonBorderBrush));
                OnPropertyChanged(nameof(QueueViewDeckAButtonForeground));
                OnPropertyChanged(nameof(QueueViewDeckBButtonBackground));
                OnPropertyChanged(nameof(QueueViewDeckBButtonBorderBrush));
                OnPropertyChanged(nameof(QueueViewDeckBButtonForeground));
            }
        }
    }

    public string QueueViewDeckHeader => $"{QueueViewDeckName.ToUpperInvariant()} QUEUE";

    public string QueueViewDeckCountDisplay
    {
        get
        {
            var count = QueueForDeck(QueueViewDeckName).Count;
            return count == 1 ? "1 song" : $"{count} songs";
        }
    }

    public Brush QueueViewDeckBrush => IsQueueViewDeck("Deck B") ? Brush("#1EA7FF") : Brush("#F4B400");

    public Brush QueueViewDeckAButtonBackground => QueueViewButtonBackground("Deck A");

    public Brush QueueViewDeckAButtonBorderBrush => QueueViewButtonBorderBrush("Deck A");

    public Brush QueueViewDeckAButtonForeground => QueueViewButtonForeground("Deck A");

    public Brush QueueViewDeckBButtonBackground => QueueViewButtonBackground("Deck B");

    public Brush QueueViewDeckBButtonBorderBrush => QueueViewButtonBorderBrush("Deck B");

    public Brush QueueViewDeckBButtonForeground => QueueViewButtonForeground("Deck B");

    public string CurrentDeckHeader => "DECK A";

    public string NextDeckHeader => "DECK B";

    public string DeckAStatus => ResolveDeckStatus("Deck A");

    public string DeckBStatus => ResolveDeckStatus("Deck B");

    public string DeckATitle => ResolveDeckTitle("Deck A");

    public string DeckBTitle => ResolveDeckTitle("Deck B");

    public string DeckAArtist => ResolveDeckArtist("Deck A");

    public string DeckBArtist => ResolveDeckArtist("Deck B");

    public string DeckADetail => ResolveDeckDetail("Deck A");

    public string DeckBDetail => ResolveDeckDetail("Deck B");

    public bool IsPlaybackPlaying
    {
        get => _isPlaybackPlaying;
        private set
        {
            if (SetProperty(ref _isPlaybackPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseLabel));
                OnPropertyChanged(nameof(DeckAPlayPauseLabel));
                OnPropertyChanged(nameof(DeckBPlayPauseLabel));
            }
        }
    }

    public string PlayPauseLabel => IsPlaybackPlaying ? "PAUSE" : "PLAY";

    public string DeckAPlayPauseLabel => IsDeckPlaying("Deck A") ? "PAUSE" : "PLAY";

    public string DeckBPlayPauseLabel => IsDeckPlaying("Deck B") ? "PAUSE" : "PLAY";

    public string SelectedPlaybackMode
    {
        get => _selectedPlaybackMode;
        set
        {
            var requestedPlaybackMode = string.IsNullOrWhiteSpace(value)
                ? SpotifyPlaybackModes.SpotifyConnect
                : value.Trim();
            var normalizedPlaybackMode = NormalizePlaybackMode(requestedPlaybackMode);
            var wasUnsupportedMode = !IsSupportedPlaybackMode(normalizedPlaybackMode);

            if (wasUnsupportedMode)
            {
                SpotifyOperationMessage = requestedPlaybackMode == SpotifyPlaybackModes.WebPlaybackSdk
                    ? "Spotify Web Playback SDK is browser/WebView playback. DancePilot switched to Spotify Web API / Spotify Connect for the native Windows app."
                    : "That playback mode is reserved for a future DancePilot release. DancePilot switched to Spotify Web API / Spotify Connect.";
                normalizedPlaybackMode = SpotifyPlaybackModes.SpotifyConnect;
            }

            if (SetProperty(ref _selectedPlaybackMode, normalizedPlaybackMode))
            {
                CurrentOutputStatus = normalizedPlaybackMode == SpotifyPlaybackModes.ExternalSpotifyAppHandoff
                    ? "Open in Spotify App mode launches Spotify links instead of controlling a device."
                    : normalizedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture
                        ? "Local file mode plays audio files from this Windows PC."
                    : $"Playback mode: {normalizedPlaybackMode}";

                if (normalizedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture && !_isRestoringSessionState)
                {
                    _ = LoadLocalMusicAsync();
                }

                QueueSessionStateSave();
            }
            else if (wasUnsupportedMode)
            {
                OnPropertyChanged(nameof(SelectedPlaybackMode));
                CurrentOutputStatus = $"Playback mode: {normalizedPlaybackMode}";
            }
        }
    }

    public bool SpotifyAutopilotEnabled
    {
        get => _spotifyAutopilotEnabled;
        set
        {
            if (SetProperty(ref _spotifyAutopilotEnabled, value))
            {
                OnPropertyChanged(nameof(SpotifyAutopilotStatus));
            }
        }
    }

    public string SpotifyAutopilotStatus => SpotifyAutopilotEnabled ? "ON" : "OFF";

    public bool DeckTransitionEnabled
    {
        get => _deckTransitionEnabled;
        set
        {
            if (SetProperty(ref _deckTransitionEnabled, value))
            {
                OnPropertyChanged(nameof(DeckTransitionStatus));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string DeckTransitionStatus => DeckTransitionEnabled ? "ON" : "OFF";

    public double AutoplaySecondsBeforeEnd
    {
        get => _autoplaySecondsBeforeEnd;
        set => SetProperty(ref _autoplaySecondsBeforeEnd, Math.Clamp(value, 1, 60));
    }

    public double TransitionOverlapSeconds
    {
        get => _transitionOverlapSeconds;
        set
        {
            if (SetProperty(ref _transitionOverlapSeconds, Math.Clamp(value, 0, 30)))
            {
                OnPropertyChanged(nameof(TransitionOverlapDisplay));
                OnPropertyChanged(nameof(TransitionSecondsDisplay));
                OnPropertyChanged(nameof(TransitionBehaviorNotice));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string TransitionOverlapDisplay => $"{TransitionOverlapSeconds:N0}s";

    public double TransitionSecondsBeforeEnd
    {
        get => TransitionOverlapSeconds;
        set => TransitionOverlapSeconds = value;
    }

    public string TransitionSecondsDisplay => TransitionOverlapDisplay;

    public double FadeInSeconds
    {
        get => _fadeInSeconds;
        set
        {
            if (SetProperty(ref _fadeInSeconds, Math.Clamp(value, 1, 20)))
            {
                OnPropertyChanged(nameof(FadeInSecondsDisplay));
                OnPropertyChanged(nameof(TransitionBehaviorNotice));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string FadeInSecondsDisplay => $"{FadeInSeconds:N0}s";

    public double FadeOutSeconds
    {
        get => _fadeOutSeconds;
        set
        {
            if (SetProperty(ref _fadeOutSeconds, Math.Clamp(value, 1, 30)))
            {
                OnPropertyChanged(nameof(FadeOutSecondsDisplay));
                OnPropertyChanged(nameof(TransitionBehaviorNotice));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string FadeOutSecondsDisplay => $"{FadeOutSeconds:N0}s";

    public bool AlwaysFadeSongs
    {
        get => _alwaysFadeSongs;
        set
        {
            if (SetProperty(ref _alwaysFadeSongs, value))
            {
                OnPropertyChanged(nameof(AlwaysFadeSongsStatus));
                OnPropertyChanged(nameof(TransitionBehaviorNotice));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string AlwaysFadeSongsStatus => AlwaysFadeSongs ? "AUTO FADE ON" : "AUTO FADE OFF";

    public bool StartTransitionOnFade
    {
        get => _startTransitionOnFade;
        set
        {
            if (SetProperty(ref _startTransitionOnFade, value))
            {
                OnPropertyChanged(nameof(StartTransitionOnFadeStatus));
                OnPropertyChanged(nameof(TransitionBehaviorNotice));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string StartTransitionOnFadeStatus => StartTransitionOnFade ? "FADE START" : "TIMER START";

    public bool RemovePlayedQueueItems
    {
        get => _removePlayedQueueItems;
        set
        {
            if (SetProperty(ref _removePlayedQueueItems, value))
            {
                OnPropertyChanged(nameof(RemovePlayedQueueItemsStatus));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string RemovePlayedQueueItemsStatus => RemovePlayedQueueItems ? "REMOVE PLAYED" : "LOOP QUEUE";

    public double LowFrequencyGain
    {
        get => _lowFrequencyGain;
        set
        {
            if (SetProperty(ref _lowFrequencyGain, Math.Clamp(value, -12, 12)))
            {
                OnPropertyChanged(nameof(LowFrequencyGainDisplay));
                OnPropertyChanged(nameof(MixerStateDisplay));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string LowFrequencyGainDisplay => FormatGain(LowFrequencyGain);

    public double MidFrequencyGain
    {
        get => _midFrequencyGain;
        set
        {
            if (SetProperty(ref _midFrequencyGain, Math.Clamp(value, -12, 12)))
            {
                OnPropertyChanged(nameof(MidFrequencyGainDisplay));
                OnPropertyChanged(nameof(MixerStateDisplay));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string MidFrequencyGainDisplay => FormatGain(MidFrequencyGain);

    public double HighFrequencyGain
    {
        get => _highFrequencyGain;
        set
        {
            if (SetProperty(ref _highFrequencyGain, Math.Clamp(value, -12, 12)))
            {
                OnPropertyChanged(nameof(HighFrequencyGainDisplay));
                OnPropertyChanged(nameof(MixerStateDisplay));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string HighFrequencyGainDisplay => FormatGain(HighFrequencyGain);

    public double CrossfaderPosition
    {
        get => _crossfaderPosition;
        set
        {
            if (SetProperty(ref _crossfaderPosition, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(CrossfaderDisplay));
                OnPropertyChanged(nameof(MixerStateDisplay));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string CrossfaderDisplay => $"A {100 - CrossfaderPosition:N0} / B {CrossfaderPosition:N0}";

    public string MixerStateDisplay =>
        $"LOW {LowFrequencyGainDisplay}  MID {MidFrequencyGainDisplay}  HIGH {HighFrequencyGainDisplay}  {CrossfaderDisplay}";

    public string TransitionBehaviorNotice => AlwaysFadeSongs
        ? StartTransitionOnFade
            ? $"Next song starts {TransitionOverlapDisplay} before the end; fade-out begins up to {FadeOutSecondsDisplay} earlier."
            : $"Next song starts {TransitionOverlapDisplay} before the end without pre-fade automation."
        : $"Next song starts {TransitionOverlapDisplay} before the end.";

    public IReadOnlyList<string> TransitionModeOptions { get; } =
    [
        TransitionOppositeDeck,
        TransitionSameDeck
    ];

    public string SelectedTransitionMode
    {
        get => _selectedTransitionMode;
        set
        {
            var normalized = TransitionModeOptions.Contains(value)
                ? value
                : TransitionOppositeDeck;
            if (SetProperty(ref _selectedTransitionMode, normalized))
            {
                OnPropertyChanged(nameof(NextDeckHeader));
                UpdateNextUpFromDecks();
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public double DeckAVolume
    {
        get => _deckAVolume;
        set
        {
            if (SetProperty(ref _deckAVolume, Math.Clamp(value, 0, 100)))
            {
                OnDeckVolumeChanged("Deck A", nameof(DeckAVolumeDisplay));
            }
        }
    }

    public string DeckAVolumeDisplay => FormatVolume(DeckAVolume);

    public double DeckBVolume
    {
        get => _deckBVolume;
        set
        {
            if (SetProperty(ref _deckBVolume, Math.Clamp(value, 0, 100)))
            {
                OnDeckVolumeChanged("Deck B", nameof(DeckBVolumeDisplay));
            }
        }
    }

    public string DeckBVolumeDisplay => FormatVolume(DeckBVolume);

    public double DefaultSpotifyVolume
    {
        get => ResolveDeckVolume(ActiveDeckName);
        set => SetDeckVolume(ActiveDeckName, value);
    }

    public string DefaultSpotifyVolumeDisplay => FormatVolume(DefaultSpotifyVolume);

    public double SeekPositionSeconds
    {
        get => _seekPositionSeconds;
        set
        {
            var maximum = Math.Max(0, SeekPositionMaximumSeconds);
            SetProperty(ref _seekPositionSeconds, Math.Clamp(value, 0, maximum));
        }
    }

    public double SeekPositionMaximumSeconds
    {
        get => _seekPositionMaximumSeconds;
        private set
        {
            var normalized = Math.Max(1, value);
            if (SetProperty(ref _seekPositionMaximumSeconds, normalized)
                && SeekPositionSeconds > normalized)
            {
                SeekPositionSeconds = normalized;
            }
        }
    }

    public void BeginSeekPositionChange() => _isSeekPositionChanging = true;

    public async Task CommitSeekPositionChangeAsync()
    {
        _isSeekPositionChanging = false;
        await SeekSpotifyCommand.ExecuteAsync(null);
    }

    public Task CommitVolumeChangeAsync() =>
        CommitDeckVolumeChangeAsync(ActiveDeckName);

    public Task CommitDeckVolumeChangeAsync(string deckName) =>
        SetDeckOutputVolumeAsync(deckName);

    public string CurrentOutputStatus
    {
        get => _currentOutputStatus;
        private set => SetProperty(ref _currentOutputStatus, value);
    }

    public string SpotifyNowPlayingTitle
    {
        get => _spotifyNowPlayingTitle;
        private set => SetProperty(ref _spotifyNowPlayingTitle, value);
    }

    public string SpotifyNowPlayingArtist
    {
        get => _spotifyNowPlayingArtist;
        private set => SetProperty(ref _spotifyNowPlayingArtist, value);
    }

    public string SpotifyPlaybackStatus
    {
        get => _spotifyPlaybackStatus;
        private set => SetProperty(ref _spotifyPlaybackStatus, value);
    }

    public string SpotifyTimeRemaining
    {
        get => _spotifyTimeRemaining;
        private set => SetProperty(ref _spotifyTimeRemaining, value);
    }

    public string SpotifyProgressDisplay
    {
        get => _spotifyProgressDisplay;
        private set => SetProperty(ref _spotifyProgressDisplay, value);
    }

    public ImageSource CurrentDeckAlbumArt
    {
        get => _currentDeckAlbumArt;
        private set => SetProperty(ref _currentDeckAlbumArt, value);
    }

    public ImageSource NextDeckAlbumArt
    {
        get => _nextDeckAlbumArt;
        private set => SetProperty(ref _nextDeckAlbumArt, value);
    }

    public string SelectedOutputDeviceName
    {
        get => _selectedOutputDeviceName;
        private set => SetProperty(ref _selectedOutputDeviceName, value);
    }

    public string CurrentSpotifyPlaylistName
    {
        get => _currentSpotifyPlaylistName;
        private set => SetProperty(ref _currentSpotifyPlaylistName, value);
    }

    public string NextUpTitle
    {
        get => _nextUpTitle;
        private set => SetProperty(ref _nextUpTitle, value);
    }

    public string NextUpArtist
    {
        get => _nextUpArtist;
        private set => SetProperty(ref _nextUpArtist, value);
    }

    public string SpotifyTransitionNotice => "Deck A/B plan Spotify Connect handoffs. DancePilot controls one Spotify output at a time and does not mix two Spotify streams.";

    public string EventName => "Saturday Night Dance";

    public string EventTimeRemaining => "2:18";

    public string ClockText => "8:42 PM";

    public string QueueSummary => "1,024 songs";

    public string LibraryDuration => "8.7 days";

    public string AnalysisStatus => "Analyzed";

    public int CrowdEnergy => 7;

    public int VolumeLevel => 78;

    public int CrossfadeSeconds => 10;

    public IReadOnlyList<SongRecommendation> Recommendations { get; } = [];

    public Visibility RecommendationsVisibility => Recommendations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<SpotifyPlaylistSummary> SpotifyPlaylists { get; } = [];

    public ObservableCollection<SpotifyTrackMetadata> SpotifyPreviewTracks { get; } = [];

    public ObservableCollection<SpotifyTrackMetadata> SpotifySearchResults { get; } = [];

    public ObservableCollection<SpotifyDevice> SpotifyDevices { get; } = [];

    public ObservableCollection<SpotifyPlaylistSummary> ImportedSpotifyPlaylists { get; } = [];

    public ObservableCollection<SpotifyTrackMetadata> ImportedSpotifyTracks { get; } = [];

    public ObservableCollection<LocalMusicTrack> LocalMusicTracks { get; } = [];

    public ObservableCollection<LocalMusicPlaylist> LocalMusicPlaylists { get; } = [];

    public ObservableCollection<DancePilotQueueItem> DancePilotQueue { get; } = [];

    public ObservableCollection<DancePilotQueueItem> DeckAQueueItems { get; } = [];

    public ObservableCollection<DancePilotQueueItem> DeckBQueueItems { get; } = [];

    public ObservableCollection<DancePilotQueueItem> ActiveDeckQueue { get; } = [];

    public ObservableCollection<DancePilotQueueItem> QueueViewDeckQueue { get; } = [];

    public IReadOnlyList<string> PlaybackModeOptions { get; } =
    [
        SpotifyPlaybackModes.SpotifyConnect,
        SpotifyPlaybackModes.ExternalSpotifyAppHandoff,
        SpotifyPlaybackModes.LocalFilesFuture
    ];

    public IAsyncRelayCommand SpotifyLoginCommand { get; }

    public IAsyncRelayCommand SpotifyLogoutCommand { get; }

    public IAsyncRelayCommand LoadSpotifyPlaylistsCommand { get; }

    public IAsyncRelayCommand PreviewSpotifyPlaylistCommand { get; }

    public IAsyncRelayCommand ImportSpotifyPlaylistCommand { get; }

    public IAsyncRelayCommand OpenSelectedSpotifyTrackCommand { get; }

    public IAsyncRelayCommand SearchSpotifyTracksCommand { get; }

    public IAsyncRelayCommand PlaySelectedSpotifySearchTrackCommand { get; }

    public IAsyncRelayCommand RefreshSpotifyDevicesCommand { get; }

    public IAsyncRelayCommand TransferSpotifyPlaybackCommand { get; }

    public IAsyncRelayCommand RefreshSpotifyPlaybackCommand { get; }

    public IAsyncRelayCommand PlaySelectedSpotifyTrackCommand { get; }

    public IAsyncRelayCommand PlayImportedSpotifyPlaylistCommand { get; }

    public IAsyncRelayCommand PlayFromSelectedSpotifyTrackCommand { get; }

    public IAsyncRelayCommand AddSelectedTrackToQueueCommand { get; }

    public IAsyncRelayCommand RecommendNextFromPlaylistCommand { get; }

    public IAsyncRelayCommand PauseSpotifyCommand { get; }

    public IAsyncRelayCommand ResumeSpotifyCommand { get; }

    public IAsyncRelayCommand SkipNextSpotifyCommand { get; }

    public IAsyncRelayCommand SkipPreviousSpotifyCommand { get; }

    public IAsyncRelayCommand SetSpotifyVolumeCommand { get; }

    public IAsyncRelayCommand SeekSpotifyCommand { get; }

    public IAsyncRelayCommand RewindPlaybackCommand { get; }

    public IAsyncRelayCommand FastForwardPlaybackCommand { get; }

    public IAsyncRelayCommand EmergencyStopCommand { get; }

    public IAsyncRelayCommand DisableSpotifyAutopilotCommand { get; }

    public IAsyncRelayCommand LoadImportedSpotifyPlaylistsCommand { get; }

    public IAsyncRelayCommand LoadImportedPlaylistTracksCommand { get; }

    public IAsyncRelayCommand LoadLocalMusicCommand { get; }

    public IAsyncRelayCommand SearchLocalMusicCommand { get; }

    public IAsyncRelayCommand PlaySelectedLocalMusicCommand { get; }

    public IAsyncRelayCommand LoadLocalPlaylistsCommand { get; }

    public IAsyncRelayCommand AddFilteredLocalMusicToPlaylistCommand { get; }

    public IAsyncRelayCommand QueueSelectedLocalPlaylistToDeckACommand { get; }

    public IAsyncRelayCommand QueueSelectedLocalPlaylistToDeckBCommand { get; }

    public IAsyncRelayCommand DeleteSelectedLocalPlaylistCommand { get; }

    public IRelayCommand PauseLocalMusicCommand { get; }

    public IAsyncRelayCommand TogglePlaybackCommand { get; }

    public IAsyncRelayCommand PlayPauseDeckACommand { get; }

    public IAsyncRelayCommand PlayPauseDeckBCommand { get; }

    public IRelayCommand SelectDeckACommand { get; }

    public IRelayCommand SelectDeckBCommand { get; }

    public IRelayCommand ShowDeckAQueueCommand { get; }

    public IRelayCommand ShowDeckBQueueCommand { get; }

    public IAsyncRelayCommand AddSelectedSourceToDeckACommand { get; }

    public IAsyncRelayCommand AddSelectedSourceToDeckBCommand { get; }

    public IAsyncRelayCommand AddLoadedPlaylistToActiveDeckCommand { get; }

    public IAsyncRelayCommand RandomizePlaylistToDeckACommand { get; }

    public IAsyncRelayCommand RandomizePlaylistToDeckBCommand { get; }

    public IRelayCommand ClearDeckAQueueCommand { get; }

    public IRelayCommand ClearDeckBQueueCommand { get; }

    public ObservableCollection<WaveBar> CurrentWaveform { get; } = CreateWaveform("Deck A");

    public ObservableCollection<WaveBar> NextWaveform { get; } = CreateWaveform("Deck B");

    public ObservableCollection<FrequencyBand> FrequencyAnalyzer { get; } = CreateFrequencyBands();

    public IReadOnlyList<EnergySegment> EnergySegments { get; } =
    [
        new EnergySegment(Brush("#18C84F")),
        new EnergySegment(Brush("#18C84F")),
        new EnergySegment(Brush("#18C84F")),
        new EnergySegment(Brush("#31C944")),
        new EnergySegment(Brush("#91D01B")),
        new EnergySegment(Brush("#D6C71C")),
        new EnergySegment(Brush("#F0B422")),
        new EnergySegment(Brush("#343B44")),
        new EnergySegment(Brush("#343B44")),
        new EnergySegment(Brush("#343B44"))
    ];

    public IReadOnlyList<RemoteAction> CrowdActions { get; } =
    [
        new RemoteAction("Floor is Full", "\uE716", Brush("#1D7C32")),
        new RemoteAction("Floor Thinning", "\uE902", Brush("#876D14")),
        new RemoteAction("Need Upbeat", "\uE768", Brush("#12599C")),
        new RemoteAction("Need Slowdown", "\uE7C5", Brush("#63318D")),
        new RemoteAction("Line Dance Time", "\uE7C3", Brush("#A44717")),
        new RemoteAction("Country Time", "\uE774", Brush("#087786")),
        new RemoteAction("Wedding Classics", "\uEB51", Brush("#8A2364")),
        new RemoteAction("Cool Down", "\uE9CA", Brush("#2D333C"))
    ];

    public IReadOnlyList<RemoteAction> PhoneActions { get; } =
    [
        new RemoteAction("Floor Full", "\uE716", Brush("#25853A")),
        new RemoteAction("Floor Thinning", "\uE902", Brush("#967715")),
        new RemoteAction("Upbeat", "\uE768", Brush("#126CB8")),
        new RemoteAction("Slow Down", "\uE7C5", Brush("#6231A2")),
        new RemoteAction("Line Dance", "\uE7C3", Brush("#B34B19")),
        new RemoteAction("Country", "\uE774", Brush("#0A8495")),
        new RemoteAction("Request Song", "\uE8F1", Brush("#A22B78")),
        new RemoteAction("Skip / Next", "\uE8AD", Brush("#303743"))
    ];

    private static ObservableCollection<WaveBar> CreateWaveform(string deckName)
    {
        var bars = new ObservableCollection<WaveBar>();
        for (var index = 0; index < DeckWaveformBarCount; index++)
        {
            var bandPosition = index / Math.Max(1d, DeckWaveformBarCount - 1d);
            var lowWeight = Math.Max(0, 1 - bandPosition * 1.55);
            var midWeight = Math.Max(0, 1 - Math.Abs(bandPosition - 0.48) * 1.9);
            var highWeight = Math.Pow(bandPosition, 1.45);
            var motion = (Math.Sin(index * 0.47) + 1) * 0.5;
            var energy = Math.Clamp((lowWeight * 0.32) + (midWeight * 0.24) + (highWeight * 0.18) + (motion * 0.22), 0, 1);
            bars.Add(CreateDeckWaveBar(deckName, bandPosition, energy * 0.36, active: false, queued: true));
        }

        return bars;
    }

    private static ObservableCollection<FrequencyBand> CreateFrequencyBands()
    {
        var bands = new ObservableCollection<FrequencyBand>();
        for (var index = 0; index < FrequencyAnalyzerBandCount; index++)
        {
            bands.Add(CreateFrequencyBand(index, FrequencyAnalyzerBandCount, 0.08, 0.08, 0.12));
        }

        return bands;
    }

    private void UpdateDeckAnalyzers()
    {
        _analyzerFrame++;
        var deckADisplayItem = ResolveDeckDisplayItem("Deck A");
        var deckBDisplayItem = ResolveDeckDisplayItem("Deck B");
        var deckASeed = deckADisplayItem?.Id ?? _playingDeckQueueItemId ?? 0;
        var deckBSeed = deckBDisplayItem?.Id ?? FindTransitionTarget()?.Id ?? 0;
        var liveSnapshot = TryAnalyzeSystemOutput();
        var snapshot = liveSnapshot ?? TryAnalyzeLocalPlayback();
        var activeAnalyzerDeckName = NormalizeDeckName(_playingDeckQueueItemId is null ? ActiveDeckName : _playingDeckName);
        if (snapshot is not null)
        {
            AppendDjWaveformFrame(activeAnalyzerDeckName, snapshot);
        }

        UpdateDeckWaveform(
            CurrentWaveform,
            deckADisplayItem,
            "Deck A",
            ResolveDeckPlaybackProgress("Deck A", deckADisplayItem),
            IsDeckAudiblyPlaying("Deck A"),
            activeAnalyzerDeckName == "Deck A" ? snapshot : null);

        UpdateDeckWaveform(
            NextWaveform,
            deckBDisplayItem,
            "Deck B",
            ResolveDeckPlaybackProgress("Deck B", deckBDisplayItem),
            IsDeckAudiblyPlaying("Deck B"),
            activeAnalyzerDeckName == "Deck B" ? snapshot : null);

        UpdateFrequencyAnalyzer(snapshot, activeAnalyzerDeckName, deckASeed, deckBSeed);
    }

    private LocalAudioSpectrumSnapshot? TryAnalyzeSystemOutput() =>
        _systemAudioOutputAnalysisService.Analyze(waveformBarCount: DeckWaveformBarCount);

    private void UpdateDeckWaveform(
        ObservableCollection<WaveBar> bars,
        DancePilotQueueItem? displayItem,
        string deckName,
        double progress,
        bool active,
        LocalAudioSpectrumSnapshot? liveSnapshot)
    {
        const int visibleCount = DeckWaveformBarCount;
        var sourceWaveform = ResolveDisplayTrackWaveform(displayItem);
        var centerIndex = visibleCount / 2;
        var sourceCenter = Convert.ToInt32(Math.Round(Math.Clamp(progress, 0, 1) * Math.Max(0, sourceWaveform.Count - 1)));
        var queued = displayItem is not null;

        bars.Clear();
        for (var index = 0; index < visibleCount; index++)
        {
            var sourceIndex = sourceCenter + index - centerIndex;
            var slice = ReadTrackWaveformSlice(sourceWaveform, sourceIndex, queued);
            var centerDistance = Math.Abs(index - centerIndex) / 12d;
            var liveLift = liveSnapshot is null
                ? 0
                : Math.Max(0, 1 - centerDistance) * ReadLevel(liveSnapshot.Waveform, index, visibleCount) * 0.20;
            bars.Add(CreateDeckWaveBar(deckName, slice, index, visibleCount, active, queued, liveLift));
        }
    }

    private IReadOnlyList<TrackWaveformSlice> ResolveDisplayTrackWaveform(DancePilotQueueItem? displayItem)
    {
        if (displayItem is null)
        {
            return CreateGeneratedTrackWaveform(null);
        }

        var cacheKey = CreateTrackWaveformCacheKey(displayItem);
        if (_trackWaveformCache.TryGetValue(cacheKey, out var cached))
        {
            EnsureTrackWaveformAnalysis(displayItem, cacheKey);
            return cached;
        }

        var generated = CreateGeneratedTrackWaveform(displayItem);
        _trackWaveformCache[cacheKey] = generated;
        EnsureTrackWaveformAnalysis(displayItem, cacheKey);
        return generated;
    }

    private void EnsureTrackWaveformAnalysis(DancePilotQueueItem displayItem, string cacheKey)
    {
        if (displayItem.Source != SongSources.Local
            || string.IsNullOrWhiteSpace(displayItem.ExternalUri)
            || !File.Exists(displayItem.ExternalUri)
            || _realTrackWaveformKeys.Contains(cacheKey)
            || !_trackWaveformAnalysisInFlight.Add(cacheKey))
        {
            return;
        }

        DispatchAsync(async () =>
        {
            try
            {
                var slices = await _localAudioAnalysisService.GetOrCreateTrackWaveformAsync(
                    displayItem.ExternalUri,
                    TrackWaveformSliceCount);
                if (slices.Count == 0)
                {
                    return;
                }

                _trackWaveformCache[cacheKey] = slices;
                _realTrackWaveformKeys.Add(cacheKey);
                if (IsDeckDisplayItem(displayItem.DeckName, displayItem.Id))
                {
                    UpdateDeckAnalyzers();
                }
            }
            finally
            {
                _trackWaveformAnalysisInFlight.Remove(cacheKey);
            }
        });
    }

    private double ResolveDeckPlaybackProgress(string deckName, DancePilotQueueItem? displayItem)
    {
        if (displayItem is null)
        {
            return 0;
        }

        var loadedItem = ResolveLoadedDeckItem(deckName);
        if (loadedItem?.Id != displayItem.Id)
        {
            return 0;
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            var duration = _localMediaPlayer.PlaybackSession.NaturalDuration > TimeSpan.Zero
                ? _localMediaPlayer.PlaybackSession.NaturalDuration
                : SelectedLocalMusicTrack?.Duration;
            if (duration is not TimeSpan durationValue || durationValue <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Clamp(_localMediaPlayer.PlaybackSession.Position.TotalSeconds / durationValue.TotalSeconds, 0, 1);
        }

        if (SeekPositionMaximumSeconds <= 1)
        {
            return 0;
        }

        return Math.Clamp(SeekPositionSeconds / SeekPositionMaximumSeconds, 0, 1);
    }

    private static TrackWaveformSlice ReadTrackWaveformSlice(
        IReadOnlyList<TrackWaveformSlice> waveform,
        int index,
        bool queued)
    {
        if (waveform.Count == 0 || index < 0 || index >= waveform.Count)
        {
            var floor = queued ? 0.035 : 0.012;
            return new TrackWaveformSlice(floor, floor, floor, floor, floor);
        }

        return waveform[index];
    }

    private static string CreateTrackWaveformCacheKey(DancePilotQueueItem item) =>
        $"{item.Source}|{item.ExternalUri}|{item.Title}|{item.Artist}".ToLowerInvariant();

    private static IReadOnlyList<TrackWaveformSlice> CreateGeneratedTrackWaveform(DancePilotQueueItem? item)
    {
        var seedText = item is null
            ? "empty"
            : $"{item.Source}|{item.ExternalUri}|{item.Title}|{item.Artist}|{item.BPM}|{item.MusicalKey}";
        var seed = CreateDeterministicSeed(seedText);
        var slices = new TrackWaveformSlice[TrackWaveformSliceCount];
        var empty = item is null;

        var breakPosition = 0.42 + ((seed >> 8) % 22) / 100d;
        var buildPosition = Math.Min(0.78, breakPosition + 0.10 + ((seed >> 16) % 14) / 100d);
        var bassPhase = (seed & 0xFF) / 255d * Math.PI * 2;
        var midPhase = ((seed >> 8) & 0xFF) / 255d * Math.PI * 2;
        var highPhase = ((seed >> 16) & 0xFF) / 255d * Math.PI * 2;

        for (var index = 0; index < slices.Length; index++)
        {
            var position = index / Math.Max(1d, slices.Length - 1d);
            var intro = SmoothStep(0.02, 0.16, position);
            var outro = 1 - SmoothStep(0.84, 0.99, position);
            var body = Math.Clamp(intro * outro, 0, 1);
            var breakDip = 1 - (0.42 * Bell(position, breakPosition, 0.050));
            var buildLift = 0.18 * Bell(position, buildPosition, 0.085);
            var phrase = 0.78
                + Math.Sin((position * Math.PI * 8.0) + bassPhase) * 0.10
                + Math.Sin((position * Math.PI * 19.0) + midPhase) * 0.055
                + Math.Sin((position * Math.PI * 43.0) + highPhase) * 0.025;
            var total = empty
                ? 0.025
                : Math.Clamp((0.10 + body * (0.62 * breakDip + buildLift)) * phrase, 0.035, 0.95);
            var low = empty
                ? 0.020
                : Math.Clamp(total * (0.70 + Math.Sin(position * Math.PI * 12.0 + bassPhase) * 0.22), 0.025, 1);
            var mid = empty
                ? 0.018
                : Math.Clamp(total * (0.78 + Math.Sin(position * Math.PI * 22.0 + midPhase) * 0.17), 0.025, 1);
            var high = empty
                ? 0.016
                : Math.Clamp(total * (0.52 + Math.Sin(position * Math.PI * 56.0 + highPhase) * 0.24), 0.018, 1);
            var transient = empty
                ? 0.015
                : Math.Clamp(total + Bell(position, buildPosition, 0.025) * 0.20 + Math.Sin(position * Math.PI * 96.0 + highPhase) * 0.035, 0.02, 1);
            slices[index] = new TrackWaveformSlice(low, mid, high, transient, total);
        }

        return slices;
    }

    private static int CreateDeterministicSeed(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / Math.Max(0.000001, edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Bell(double value, double center, double width)
    {
        var distance = (value - center) / Math.Max(0.000001, width);
        return Math.Exp(-distance * distance * 0.5);
    }

    private void AppendDjWaveformFrame(string deckName, LocalAudioSpectrumSnapshot snapshot)
    {
        var history = WaveformHistoryForDeck(deckName);
        history.Add(new DjWaveformFrame(
            Low: Math.Clamp(snapshot.Low, 0, 1),
            Mid: Math.Clamp(snapshot.Mid, 0, 1),
            High: Math.Clamp(snapshot.High, 0, 1),
            Beat: Math.Clamp(snapshot.BeatPulse, 0, 1)));

        while (history.Count > DeckWaveformBarCount)
        {
            history.RemoveAt(0);
        }
    }

    private List<DjWaveformFrame> WaveformHistoryForDeck(string deckName) =>
        NormalizeDeckName(deckName) == "Deck B" ? _deckBWaveformHistory : _deckAWaveformHistory;

    private LocalAudioSpectrumSnapshot? TryAnalyzeLocalPlayback()
    {
        if (SelectedPlaybackMode != SpotifyPlaybackModes.LocalFilesFuture || !IsPlaybackPlaying)
        {
            return null;
        }

        var filePath = SelectedLocalMusicTrack?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) && _playingDeckQueueItemId is not null)
        {
            filePath = ResolvePlayingDeckItem(_playingDeckName)?.ExternalUri;
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        return _localAudioAnalysisService.Analyze(filePath, _localMediaPlayer.PlaybackSession.Position, waveformBarCount: DeckWaveformBarCount);
    }

    private void UpdateLiveDeckAnalyzer(ObservableCollection<WaveBar> bars, LocalAudioSpectrumSnapshot snapshot, string deckName)
    {
        const int count = DeckWaveformBarCount;
        bars.Clear();
        for (var index = 0; index < count; index++)
        {
            var band = index / (count - 1d);
            var raw = ReadLevel(snapshot.Bands, index, count);
            var lowWeight = Math.Max(0, 1 - band * 1.50);
            var midWeight = Math.Max(0, 1 - Math.Abs(band - 0.47) * 1.75);
            var highWeight = Math.Pow(band, 1.45);
            var beatLift = snapshot.BeatPulse * (lowWeight * 0.28 + midWeight * 0.11 + highWeight * 0.04);
            var curve = 0.56 + (lowWeight * 0.20) + (midWeight * 0.12) - (highWeight * 0.10);
            var level = Math.Clamp((raw * curve + beatLift) * GainForWaveBand(band), 0.04, 1);
            bars.Add(CreateDeckWaveBar(deckName, band, level, active: true, queued: true));
        }
    }

    private static DjWaveformFrame ReadDjWaveformFrame(IReadOnlyList<DjWaveformFrame> history, int index, int targetCount)
    {
        if (history.Count == 0)
        {
            return new DjWaveformFrame(0, 0, 0, 0);
        }

        var historyOffset = history.Count - targetCount;
        var sourceIndex = historyOffset + index;
        if (sourceIndex < 0)
        {
            return new DjWaveformFrame(0, 0, 0, 0);
        }

        return history[Math.Clamp(sourceIndex, 0, history.Count - 1)];
    }

    private static SolidColorBrush DjWaveformBlend(double low, double mid, double high, double beat, bool active)
    {
        var lowColor = ParseColor("#FF3B30");
        var midColor = ParseColor("#39E75F");
        var highColor = ParseColor("#28A8FF");
        var total = Math.Max(0.001, low + mid + high);
        var r = (lowColor.R * low + midColor.R * mid + highColor.R * high) / total;
        var g = (lowColor.G * low + midColor.G * mid + highColor.G * high) / total;
        var b = (lowColor.B * low + midColor.B * mid + highColor.B * high) / total;
        var color = ColorHelper.FromArgb(255, Convert.ToByte(r), Convert.ToByte(g), Convert.ToByte(b));
        var brightness = active ? 0.54 + Math.Clamp(total / 3d + beat * 0.25, 0, 0.72) : 0.38;
        color = ScaleBrightness(color, brightness);
        if (high > 0.62)
        {
            color = Blend(color, ParseColor("#FFFFFF"), Math.Clamp((high - 0.62) * 0.34, 0, 0.20));
        }

        return Brush(color);
    }

    private void UpdateIdleAnalyzer(ObservableCollection<WaveBar> bars, int seed, string deckName, bool queued)
    {
        const int count = DeckWaveformBarCount;
        var seedPhase = (seed % 97) * 0.071;
        bars.Clear();
        for (var index = 0; index < count; index++)
        {
            var band = index / (count - 1d);
            var lowWeight = Math.Max(0, 1 - band * 1.62);
            var midWeight = Math.Max(0, 1 - Math.Abs(band - 0.46) * 1.85);
            var highWeight = Math.Pow(band, 1.50);
            var bass = (Math.Sin((_analyzerFrame * 0.045) + (index * 0.17) + seedPhase) + 1) * 0.5;
            var mid = (Math.Sin((_analyzerFrame * 0.085) + (index * 0.31) + seedPhase * 1.7) + 1) * 0.5;
            var high = (Math.Sin((_analyzerFrame * 0.145) + (index * 0.57) + seedPhase * 2.3) + 1) * 0.5;
            var shaped = lowWeight * (0.52 + bass * 0.48)
                + midWeight * (0.26 + mid * 0.48)
                + highWeight * (0.08 + high * 0.24);
            var level = queued
                ? Math.Clamp(0.045 + shaped * 0.22, 0.035, 0.30)
                : Math.Clamp(0.025 + shaped * 0.045, 0.02, 0.08);
            bars.Add(CreateDeckWaveBar(deckName, band, level, active: false, queued));
        }
    }

    private static WaveBar CreateDeckWaveBar(string deckName, double band, double level, bool active, bool queued)
    {
        var clampedLevel = Math.Clamp(level, 0, 1);
        var upperMotion = 0.86 + Math.Sin(band * Math.PI * 6.0) * 0.10;
        var lowerMotion = 0.68 + Math.Cos((band * Math.PI * 5.0) + 0.4) * 0.12;
        var floor = queued ? 1.6 : 0.8;
        var upperHeight = Math.Clamp(DeckAnalyzerHalfHeight * clampedLevel * upperMotion, floor, DeckAnalyzerHalfHeight);
        var lowerHeight = Math.Clamp(DeckAnalyzerHalfHeight * clampedLevel * lowerMotion, floor, DeckAnalyzerHalfHeight);
        var height = Math.Max(upperHeight, lowerHeight);
        var peakHeight = Math.Clamp(height + (active ? 4.5 : 2.5), floor, DeckAnalyzerHalfHeight);
        var fill = DeckAnalyzerBrush(deckName, band, clampedLevel, active, queued);
        var lowerFill = Brush(ScaleBrightness(fill.Color, active ? 0.84 : 0.72));
        var peakFill = DeckAnalyzerPeakBrush(deckName, clampedLevel, active, queued);
        return new WaveBar(height, fill)
        {
            UpperHeight = upperHeight,
            LowerHeight = lowerHeight,
            UpperFill = fill,
            LowerFill = lowerFill,
            PeakHeight = peakHeight,
            PeakFill = peakFill
        };
    }

    private static WaveBar CreateDeckWaveBar(
        string deckName,
        TrackWaveformSlice slice,
        int index,
        int count,
        bool active,
        bool queued,
        double liveLift)
    {
        var position = index / Math.Max(1d, count - 1d);
        var totalEnergy = Math.Clamp((slice.TotalEnergy * 0.66) + (slice.PeakEnergy * 0.20) + liveLift, queued ? 0.018 : 0.008, 1);
        var upperShape = 0.82 + Math.Sin(position * Math.PI * 18.0) * 0.08 + slice.HighEnergy * 0.06;
        var lowerShape = 0.70 + Math.Cos(position * Math.PI * 13.0) * 0.08 + slice.LowEnergy * 0.10;
        var floor = queued ? 1.4 : 0.7;
        var upperHeight = Math.Clamp(DeckAnalyzerHalfHeight * totalEnergy * upperShape, floor, DeckAnalyzerHalfHeight);
        var lowerHeight = Math.Clamp(DeckAnalyzerHalfHeight * totalEnergy * lowerShape, floor, DeckAnalyzerHalfHeight);
        var peakHeight = Math.Clamp(Math.Max(upperHeight, lowerHeight) + slice.PeakEnergy * (active ? 5.0 : 2.8), floor, DeckAnalyzerHalfHeight);
        var fill = DjWaveformSliceBrush(deckName, slice, active, queued);
        var lowerFill = Brush(ScaleBrightness(fill.Color, active ? 0.82 : 0.70));
        var peakFill = DeckAnalyzerPeakBrush(deckName, slice.PeakEnergy, active, queued);

        return new WaveBar(Math.Max(upperHeight, lowerHeight), fill)
        {
            UpperHeight = upperHeight,
            LowerHeight = lowerHeight,
            UpperFill = fill,
            LowerFill = lowerFill,
            PeakHeight = peakHeight,
            PeakFill = peakFill
        };
    }

    private static SolidColorBrush DjWaveformSliceBrush(
        string deckName,
        TrackWaveformSlice slice,
        bool active,
        bool queued)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var deckAccent = normalizedDeckName == "Deck B"
            ? ParseColor("#1EA7FF")
            : ParseColor("#F4B400");
        var lowColor = normalizedDeckName == "Deck B" ? ParseColor("#4CB8FF") : ParseColor("#F4B400");
        var midColor = ParseColor("#2FD889");
        var highColor = ParseColor("#8EE6FF");
        var low = Math.Clamp(slice.LowEnergy, 0, 1);
        var mid = Math.Clamp(slice.MidEnergy, 0, 1);
        var high = Math.Clamp(slice.HighEnergy, 0, 1);
        var total = Math.Max(0.001, low + mid + high);
        var r = (lowColor.R * low + midColor.R * mid + highColor.R * high) / total;
        var g = (lowColor.G * low + midColor.G * mid + highColor.G * high) / total;
        var b = (lowColor.B * low + midColor.B * mid + highColor.B * high) / total;
        var color = ColorHelper.FromArgb(255, Convert.ToByte(r), Convert.ToByte(g), Convert.ToByte(b));
        color = Blend(color, deckAccent, queued ? 0.16 : 0.34);
        var brightness = active
            ? 0.70 + slice.TotalEnergy * 0.52
            : queued ? 0.42 + slice.TotalEnergy * 0.40 : 0.20;
        return Brush(ScaleBrightness(color, Math.Clamp(brightness, 0.18, 1.25)));
    }

    private static SolidColorBrush DeckAnalyzerBrush(string deckName, double band, double level, bool active, bool queued)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var deckAccent = normalizedDeckName == "Deck B"
            ? ParseColor("#1EA7FF")
            : ParseColor("#F4B400");
        var lowHeat = ParseColor("#E74A36");
        var midEnergy = ParseColor("#39D96A");
        var air = normalizedDeckName == "Deck B" ? ParseColor("#8EE6FF") : ParseColor("#FFD35A");
        var color = band switch
        {
            < 0.16 => Blend(deckAccent, lowHeat, Math.Clamp(level * 0.42, 0, 0.34)),
            < 0.58 => Blend(deckAccent, midEnergy, Math.Clamp(0.12 + level * 0.36, 0, 0.42)),
            _ => Blend(deckAccent, air, Math.Clamp(0.10 + level * 0.28, 0, 0.36))
        };

        var brightness = active
            ? 0.54 + level * 0.78
            : queued ? 0.30 + level * 0.58 : 0.18;
        color = ScaleBrightness(color, Math.Clamp(brightness, 0.16, 1.34));
        if (!queued)
        {
            color = Blend(color, ParseColor("#43505B"), 0.55);
        }

        return Brush(color);
    }

    private static SolidColorBrush DeckAnalyzerPeakBrush(string deckName, double level, bool active, bool queued)
    {
        var accent = NormalizeDeckName(deckName) == "Deck B"
            ? ParseColor("#8EE6FF")
            : ParseColor("#FFD35A");
        var brightness = active ? 0.56 + level * 0.72 : queued ? 0.32 + level * 0.38 : 0.20;
        return Brush(ScaleBrightness(accent, Math.Clamp(brightness, 0.18, 1.15)));
    }

    private static SolidColorBrush WaveformBrush(double bandPosition, double energy, double highPresence, bool active)
    {
        var baseColor = bandPosition switch
        {
            < 0.14 => ParseColor("#F25D4D"),
            < 0.28 => ParseColor("#FF8A1C"),
            < 0.42 => ParseColor("#F4B400"),
            < 0.56 => ParseColor("#32E76A"),
            < 0.70 => ParseColor("#1EA7FF"),
            < 0.84 => ParseColor("#B875FF"),
            _ => ParseColor("#FF7AB6")
        };

        var brightness = active
            ? 0.58 + energy * 0.52
            : 0.24 + energy * 0.34;
        if (bandPosition >= 0.66)
        {
            brightness += highPresence * (active ? 0.38 : 0.18);
        }

        var color = ScaleBrightness(baseColor, Math.Clamp(brightness, 0.20, 1.32));
        if (bandPosition >= 0.74 && highPresence > 0.08)
        {
            color = Blend(color, ParseColor("#FFFFFF"), Math.Clamp(highPresence * 0.24, 0, 0.28));
        }

        if (!active)
        {
            color = Blend(color, ParseColor("#4A5560"), 0.42);
        }

        return Brush(color);
    }

    private void UpdateFrequencyAnalyzer(
        LocalAudioSpectrumSnapshot? snapshot,
        string activeAnalyzerDeckName,
        int deckASeed,
        int deckBSeed)
    {
        FrequencyAnalyzer.Clear();
        for (var index = 0; index < FrequencyAnalyzerBandCount; index++)
        {
            var deckALevel = CreateDeckAnalyzerLevel(index, FrequencyAnalyzerBandCount, "Deck A", activeAnalyzerDeckName, deckASeed, snapshot);
            var deckBLevel = CreateDeckAnalyzerLevel(index, FrequencyAnalyzerBandCount, "Deck B", activeAnalyzerDeckName, deckBSeed, snapshot);
            var peakLevel = Math.Clamp(Math.Max(deckALevel, deckBLevel) + 0.08, 0.10, 1);
            FrequencyAnalyzer.Add(CreateFrequencyBand(index, FrequencyAnalyzerBandCount, deckALevel, deckBLevel, peakLevel));
        }
    }

    private double CreateDeckAnalyzerLevel(
        int index,
        int count,
        string deckName,
        string activeAnalyzerDeckName,
        int seed,
        LocalAudioSpectrumSnapshot? snapshot)
    {
        var band = index / Math.Max(1d, count - 1d);
        var gain = GainForWaveBand(band);
        if (snapshot is not null && string.Equals(activeAnalyzerDeckName, NormalizeDeckName(deckName), StringComparison.Ordinal))
        {
            var live = ReadLevel(snapshot.Bands, index, count);
            var beatWeight = band < 0.18 ? 0.26 : band < 0.38 ? 0.16 : 0.05;
            return Math.Clamp((live + snapshot.BeatPulse * beatWeight) * gain, 0.04, 1);
        }

        var deckIsPlaying = IsDeckPlaying(deckName);
        return CreateSimulatedAnalyzerLevel(index, count, seed, deckIsPlaying, gain);
    }

    private double CreateSimulatedAnalyzerLevel(int index, int count, int seed, bool active, double gain)
    {
        var band = index / Math.Max(1d, count - 1d);
        var seedPhase = (seed % 97) * 0.071;
        var frame = _analyzerFrame;
        var lowWeight = Math.Max(0, 1 - band * 1.62);
        var midWeight = Math.Max(0, 1 - Math.Abs(band - 0.45) * 1.85);
        var highWeight = Math.Pow(band, 1.35);
        var slowBass = (Math.Sin((frame * 0.055) + (index * 0.21) + seedPhase) + 1) * 0.5;
        var midMotion = (Math.Sin((frame * 0.095) + (index * 0.39) + seedPhase * 1.7) + 1) * 0.5;
        var highMotion = (Math.Sin((frame * 0.17) + (index * 0.73) + seedPhase * 2.3) + 1) * 0.5;
        var shaped = (lowWeight * (0.42 + slowBass * 0.58))
            + (midWeight * (0.24 + midMotion * 0.48))
            + (highWeight * (0.10 + highMotion * 0.28));
        var scale = active ? 0.58 : 0.18;
        var floor = active ? 0.08 : 0.035;
        return Math.Clamp((floor + shaped * scale) * gain, 0.035, active ? 0.92 : 0.30);
    }

    private static FrequencyBand CreateFrequencyBand(
        int index,
        int count,
        double deckALevel,
        double deckBLevel,
        double peakLevel)
    {
        var band = index / Math.Max(1d, count - 1d);
        var label = FrequencyLabelForBand(band);
        var range = FrequencyRangeForBand(band);
        var deckAColor = ScaleBrightness(ParseColor("#F4B400"), 0.42 + deckALevel * 0.86);
        var deckBColor = ScaleBrightness(ParseColor("#1EA7FF"), 0.40 + deckBLevel * 0.82);
        if (deckALevel > 0.78)
        {
            deckAColor = Blend(deckAColor, ParseColor("#FFEE88"), Math.Clamp((deckALevel - 0.78) * 0.65, 0, 0.18));
        }

        if (deckBLevel > 0.78)
        {
            deckBColor = Blend(deckBColor, ParseColor("#9BE8FF"), Math.Clamp((deckBLevel - 0.78) * 0.65, 0, 0.18));
        }

        return new FrequencyBand(
            label,
            range,
            Math.Clamp(4 + deckALevel * 42, 3, 46),
            Math.Clamp(4 + deckBLevel * 42, 3, 46),
            Math.Clamp(2 + peakLevel * 42, 3, 46),
            Brush(deckAColor),
            Brush(deckBColor),
            Brush(ScaleBrightness(ParseColor("#8FA4B8"), 0.26 + peakLevel * 0.32)),
            Brush("#1A242E"));
    }

    private static string FrequencyLabelForBand(double band) =>
        band switch
        {
            < 0.08 => "SUB",
            < 0.20 => "BASS",
            < 0.33 => "LOW",
            < 0.54 => "MID",
            < 0.72 => "HIGH",
            < 0.88 => "PRES",
            _ => "AIR"
        };

    private static string FrequencyRangeForBand(double band) =>
        band switch
        {
            < 0.08 => "30-60",
            < 0.20 => "60-120",
            < 0.33 => "120-250",
            < 0.54 => "250-750",
            < 0.72 => "750-2K",
            < 0.88 => "2K-6K",
            _ => "6K-12K"
        };

    private double GainForWaveBand(double bandPosition)
    {
        var lowWeight = Math.Max(0, 1 - bandPosition * 1.45);
        var midWeight = Math.Max(0, 1 - Math.Abs(bandPosition - 0.48) * 1.8);
        var highWeight = Math.Pow(bandPosition, 1.35);
        var total = Math.Max(0.001, lowWeight + midWeight + highWeight);
        return ((lowWeight * GainForFrequencyBand(1))
            + (midWeight * GainForFrequencyBand(3))
            + (highWeight * GainForFrequencyBand(5))) / total;
    }

    private double GainForFrequencyBand(int bandIndex)
    {
        var gainDb = bandIndex switch
        {
            <= 2 => LowFrequencyGain,
            3 => MidFrequencyGain,
            _ => HighFrequencyGain
        };
        return Math.Clamp(Math.Pow(10, gainDb / 24d), 0.35, 1.85);
    }

    private static double ReadLevel(IReadOnlyList<double> levels, int index, int targetCount)
    {
        if (levels.Count == 0)
        {
            return 0;
        }

        var sourceIndex = Math.Clamp(
            Convert.ToInt32(Math.Round(index * (levels.Count - 1d) / Math.Max(1, targetCount - 1d))),
            0,
            levels.Count - 1);
        return Math.Clamp(levels[sourceIndex], 0, 1);
    }

    private static SolidColorBrush Brush(string hex)
    {
        return Brush(ParseColor(hex));
    }

    private static SolidColorBrush Brush(Windows.UI.Color color)
    {
        return new SolidColorBrush(color);
    }

    private bool IsQueueViewDeck(string deckName) =>
        string.Equals(QueueViewDeckName, NormalizeDeckName(deckName), StringComparison.Ordinal);

    private Brush QueueViewButtonBackground(string deckName)
    {
        if (!IsQueueViewDeck(deckName))
        {
            return Brush("#141C25");
        }

        return NormalizeDeckName(deckName) == "Deck B"
            ? Brush("#08253A")
            : Brush("#342706");
    }

    private Brush QueueViewButtonBorderBrush(string deckName) =>
        IsQueueViewDeck(deckName)
            ? NormalizeDeckName(deckName) == "Deck B" ? Brush("#1EA7FF") : Brush("#F4B400")
            : Brush("#293642");

    private Brush QueueViewButtonForeground(string deckName) =>
        IsQueueViewDeck(deckName)
            ? NormalizeDeckName(deckName) == "Deck B" ? Brush("#8EE6FF") : Brush("#FFD35A")
            : Brush("#F5F8FA");

    private static Windows.UI.Color ParseColor(string hex)
    {
        var value = hex.TrimStart('#');
        var r = Convert.ToByte(value[..2], 16);
        var g = Convert.ToByte(value.Substring(2, 2), 16);
        var b = Convert.ToByte(value.Substring(4, 2), 16);
        return ColorHelper.FromArgb(255, r, g, b);
    }

    private static Windows.UI.Color ScaleBrightness(Windows.UI.Color color, double factor)
    {
        static byte Scale(byte channel, double factor) =>
            Convert.ToByte(Math.Clamp(channel * factor, 0, 255));

        return ColorHelper.FromArgb(color.A, Scale(color.R, factor), Scale(color.G, factor), Scale(color.B, factor));
    }

    private static Windows.UI.Color Blend(Windows.UI.Color source, Windows.UI.Color target, double amount)
    {
        var clamped = Math.Clamp(amount, 0, 1);
        static byte Mix(byte source, byte target, double amount) =>
            Convert.ToByte(Math.Clamp(source + ((target - source) * amount), 0, 255));

        return ColorHelper.FromArgb(
            source.A,
            Mix(source.R, target.R, clamped),
            Mix(source.G, target.G, clamped),
            Mix(source.B, target.B, clamped));
    }

    private static string FormatGain(double value) =>
        $"{(value > 0 ? "+" : string.Empty)}{value:N0} dB";

    private static string FormatVolume(double value) =>
        $"{value:N0}%";

    private void OnDeckVolumeChanged(string deckName, string displayPropertyName)
    {
        _defaultSpotifyVolume = ResolveDeckVolume(ActiveDeckName);
        OnPropertyChanged(displayPropertyName);
        OnPropertyChanged(nameof(DefaultSpotifyVolume));
        OnPropertyChanged(nameof(DefaultSpotifyVolumeDisplay));
        ApplyLocalVolumeIfDeckIsLive(deckName);
        _ = SavePlaybackSettingsAsync();
    }

    private void SetDeckVolume(string deckName, double volume)
    {
        if (NormalizeDeckName(deckName) == "Deck B")
        {
            DeckBVolume = volume;
        }
        else
        {
            DeckAVolume = volume;
        }
    }

    private double ResolveDeckVolume(string deckName) =>
        NormalizeDeckName(deckName) == "Deck B" ? DeckBVolume : DeckAVolume;

    private int ResolveDeckVolumePercent(string deckName) =>
        Math.Clamp(Convert.ToInt32(ResolveDeckVolume(deckName)), 0, 100);

    private double ResolveDeckVolumeScalar(string deckName) =>
        Math.Clamp(ResolveDeckVolume(deckName) / 100d, 0, 1);

    private bool ShouldApplyVolumeToLiveOutput(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        return IsDeckPlaying(normalizedDeckName)
            || (_playingDeckQueueItemId is null
                && string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal));
    }

    private void ApplyLocalVolumeIfDeckIsLive(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture
            && ShouldApplyVolumeToLiveOutput(normalizedDeckName))
        {
            _localMediaPlayer.Volume = ResolveDeckVolumeScalar(normalizedDeckName);
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
        SelectedTransitionMode = TransitionModeOptions.Contains(settings.DeckTransitionMode)
            ? settings.DeckTransitionMode
            : TransitionOppositeDeck;
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
        DeckAVolume = settings.DeckAVolume;
        DeckBVolume = settings.DeckBVolume;
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
        DefaultVolume = Math.Clamp(Convert.ToInt32(_defaultSpotifyVolume), 0, 100),
        DeckAVolume = Math.Clamp(Convert.ToInt32(DeckAVolume), 0, 100),
        DeckBVolume = Math.Clamp(Convert.ToInt32(DeckBVolume), 0, 100),
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
                QueueSessionStateSave();
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
            _localMediaPlayer.Pause();
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
            DeckAQueue = _deckAQueue.ToList(),
            DeckBQueue = _deckBQueue.ToList(),
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
        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.Title)))
        {
            yield return item with
            {
                DeckName = NormalizeDeckName(deckName),
                QueuePosition = position++
            };
        }
    }

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

    private async Task LoginSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await SaveSpotifySettingsAsync();
            await _spotifyService.LoginAsync(CurrentSpotifySettings);
            var profile = await _spotifyService.GetCurrentUserProfileAsync(CurrentSpotifySettings);
            SpotifyConnectionStatus = $"Connected as {profile.DisplayName}";
            SpotifyOperationMessage = "Spotify connected for profile, playlists, search, and metadata. Playback control scopes are requested only when you use Spotify Connect controls.";
        });
    }

    private async Task LogoutSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await _spotifyService.LogoutAsync();
            SpotifyPlaylists.Clear();
            SpotifyPreviewTracks.Clear();
            SpotifySearchResults.Clear();
            SpotifyDevices.Clear();
            SelectedSpotifyPlaylist = null;
            SelectedSpotifyTrack = null;
            SelectedSpotifySearchTrack = null;
            SelectedSpotifyDevice = null;
            SpotifyConnectionStatus = "Not connected";
            SpotifyOperationMessage = "Spotify tokens removed from this Windows profile.";
        });
    }

    private async Task LoadSpotifyPlaylistsAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await SaveSpotifySettingsAsync();
            var playlists = await _spotifyPlaylistImporter.GetUserPlaylistsAsync(CurrentSpotifySettings);
            SpotifyPlaylists.Clear();
            foreach (var playlist in playlists.OrderBy(playlist => playlist.Name))
            {
                SpotifyPlaylists.Add(playlist);
            }

            StartupLog.Write("Spotify playlists returned: " + string.Join("; ", SpotifyPlaylists.Select(playlist =>
                $"{playlist.Name}/tracks={playlist.TrackCount}/id={playlist.SpotifyPlaylistId}")));

            _suppressSpotifyPlaylistAutoLoad = true;
            try
            {
                SelectedSpotifyPlaylist = null;
            }
            finally
            {
                _suppressSpotifyPlaylistAutoLoad = false;
            }

            SpotifyOperationMessage = SpotifyPlaylists.Count == 0
                ? "No Spotify playlists were returned for this account."
                : $"Loaded {SpotifyPlaylists.Count} Spotify playlist(s). Select a playlist to load its songs.";
        });
    }

    private async Task PreviewSpotifyPlaylistAsync()
    {
        if (SelectedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select a Spotify playlist first.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await PreviewSpotifyPlaylistCoreAsync();
        });
    }

    private async Task PreviewSpotifyPlaylistFromSelectionAsync(SpotifyPlaylistSummary playlist)
    {
        await RunSpotifyOperationAsync(async () =>
        {
            if (!string.Equals(SelectedSpotifyPlaylist?.SpotifyPlaylistId, playlist.SpotifyPlaylistId, StringComparison.Ordinal))
            {
                return;
            }

            await PreviewSpotifyPlaylistCoreAsync();
        });
    }

    private async Task PreviewSpotifyPlaylistCoreAsync()
    {
        if (SelectedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select a Spotify playlist first.";
            return;
        }

        SpotifyPreviewTracks.Clear();
        SelectedSpotifyTrack = null;
        SpotifyOperationMessage = $"Loading tracks from {SelectedSpotifyPlaylist.Name}...";
        var tracks = await _spotifyPlaylistImporter.PreviewPlaylistTracksAsync(CurrentSpotifySettings, SelectedSpotifyPlaylist.SpotifyPlaylistId);
        foreach (var track in tracks)
        {
            SpotifyPreviewTracks.Add(track);
        }

        StartupLog.Write($"Spotify playlist preview loaded: {SelectedSpotifyPlaylist.Name}/summaryTracks={SelectedSpotifyPlaylist.TrackCount}/loadedTracks={tracks.Count}/id={SelectedSpotifyPlaylist.SpotifyPlaylistId}");
        UpdateSelectedPlaylistTrackCount(tracks.Count);
        SelectedSpotifyTrack = SpotifyPreviewTracks.FirstOrDefault();
        var unavailableCount = SpotifyPreviewTracks.Count(track => track.IsUnavailable);
        SpotifyOperationMessage = tracks.Count == 0
            ? $"Spotify returned no track items for {SelectedSpotifyPlaylist.Name}."
            : unavailableCount == 0
                ? $"Loaded {SpotifyPreviewTracks.Count} track(s) from {SelectedSpotifyPlaylist.Name}."
                : $"Loaded {SpotifyPreviewTracks.Count} track(s) from {SelectedSpotifyPlaylist.Name}; {unavailableCount} are Spotify-local or unavailable for Spotify API playback.";
        QueueSessionStateSave();
    }

    private void UpdateSelectedPlaylistTrackCount(int trackCount)
    {
        if (SelectedSpotifyPlaylist is null)
        {
            return;
        }

        var index = SpotifyPlaylists.IndexOf(SelectedSpotifyPlaylist);
        var displayTrackCount = SelectedSpotifyPlaylist.TrackCount > 0
            ? SelectedSpotifyPlaylist.TrackCount
            : trackCount;
        var updated = SelectedSpotifyPlaylist with { TrackCount = displayTrackCount };
        if (index >= 0)
        {
            SpotifyPlaylists[index] = updated;
        }

        _suppressSpotifyPlaylistAutoLoad = true;
        try
        {
            SelectedSpotifyPlaylist = updated;
        }
        finally
        {
            _suppressSpotifyPlaylistAutoLoad = false;
        }
    }

    private async Task ImportSpotifyPlaylistAsync()
    {
        if (SelectedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select and preview a Spotify playlist first.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            var tracks = SpotifyPreviewTracks.Count > 0
                ? SpotifyPreviewTracks.ToList()
                : (await _spotifyService.GetPlaylistTracksAsync(CurrentSpotifySettings, SelectedSpotifyPlaylist.SpotifyPlaylistId)).ToList();

            var result = await _spotifyImportRepository.ImportPlaylistAsync(SelectedSpotifyPlaylist, tracks);
            await LoadImportedSpotifyPlaylistsCoreAsync();
            SpotifyOperationMessage =
                $"Saved locally: {result.ImportedCount} imported, {result.UpdatedCount} updated, {result.UnavailableCount} unavailable skipped, {result.LikelyLocalMatchCount} likely local matches.";
        });
    }

    private async Task OpenSelectedSpotifyTrackAsync()
    {
        var track = SelectedSpotifyTrack ?? SelectedSpotifySearchTrack ?? SelectedImportedSpotifyTrack;
        if (track?.ExternalUrl is null)
        {
            SpotifyOperationMessage = "Select a Spotify track with an external URL first.";
            return;
        }

        await Launcher.LaunchUriAsync(new Uri(track.ExternalUrl));
    }

    private async Task SearchSpotifyTracksAsync()
    {
        if (string.IsNullOrWhiteSpace(SpotifySearchQuery))
        {
            SpotifyOperationMessage = "Enter a song or artist to search Spotify.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            var tracks = await _spotifyService.SearchTracksAsync(CurrentSpotifySettings, SpotifySearchQuery.Trim(), limit: 10);
            SpotifySearchResults.Clear();
            foreach (var track in tracks)
            {
                SpotifySearchResults.Add(track);
            }

            SelectedSpotifySearchTrack = SpotifySearchResults.FirstOrDefault();
            SpotifyOperationMessage = $"Found {SpotifySearchResults.Count} Spotify search result(s) for \"{SpotifySearchQuery.Trim()}\".";
            QueueSessionStateSave();
        });
    }

    private async Task PlaySelectedSpotifySearchTrackAsync()
    {
        if (SelectedSpotifySearchTrack is null)
        {
            SpotifyOperationMessage = "Select a Spotify search result first.";
            return;
        }

        await QueueSpotifyTrackToDeckAsync(SelectedSpotifySearchTrack, ActiveDeckName);
    }

    private async Task<bool> PlaySpotifyTrackWithFallbackAsync(
        SpotifyTrackMetadata track,
        string successMessage,
        bool allowExternalFallback = true)
    {
        if (allowExternalFallback && await TryExternalHandoffTrackAsync(track))
        {
            return false;
        }

        await EnsurePlaybackScopesAsync();
        EnsureSpotifyConnectPlaybackMode();
        try
        {
            var deviceId = await ResolveSelectedDeviceIdAsync();
            await _playbackCoordinator.PlayTrackAsync(CurrentSpotifySettings, deviceId, track);
            SpotifyOperationMessage = successMessage;
            await RefreshPlaybackCoreAsync(runAutopilot: false);
            return true;
        }
        catch (SpotifyApiException ex) when (ex.Kind is SpotifyApiErrorKind.PlaybackForbidden or SpotifyApiErrorKind.NoActiveDevice or SpotifyApiErrorKind.DeviceUnavailable)
        {
            if (!allowExternalFallback)
            {
                throw;
            }

            StartupLog.Write($"Spotify Connect play failed ({ex.Kind}); falling back to Spotify app for {track.Title}. status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
            if (!await OpenSpotifyTrackLinkAsync(track))
            {
                throw;
            }

            SelectedPlaybackMode = SpotifyPlaybackModes.ExternalSpotifyAppHandoff;
            SpotifyOperationMessage = $"Spotify refused remote control, so DancePilot opened {track.Title} in Spotify. Press Play in Spotify if it does not start automatically.";
            return false;
        }
    }

    private async Task RefreshSpotifyDevicesAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            await RefreshSpotifyDevicesCoreAsync();
        });
    }

    private async Task TransferSpotifyPlaybackAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            var deviceId = await ResolveSelectedDeviceIdAsync();
            await _spotifyDeviceManager.TransferPlaybackAsync(CurrentSpotifySettings, deviceId);
            await SavePlaybackSettingsAsync();
            CurrentOutputStatus = $"Transferred playback to {SelectedSpotifyDevice?.Name ?? "selected Spotify device"}.";
            SpotifyOperationMessage = "Spotify output transferred. Audio remains controlled by Spotify, the selected device, and Windows.";
        });
    }

    private async Task RefreshSpotifyPlaybackAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task PlaySelectedSpotifyTrackAsync()
    {
        var track = SelectedImportedSpotifyTrack ?? SelectedSpotifyTrack ?? SelectedSpotifySearchTrack;
        if (track is null)
        {
            SpotifyOperationMessage = "Select a Spotify track first.";
            return;
        }

        await QueueSpotifyTrackToDeckAsync(track, ActiveDeckName);
    }

    private async Task PlayImportedSpotifyPlaylistAsync()
    {
        if (SelectedImportedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select an imported Spotify playlist first.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            if (await TryExternalHandoffPlaylistAsync(SelectedImportedSpotifyPlaylist))
            {
                return;
            }

            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            try
            {
                var deviceId = await ResolveSelectedDeviceIdAsync();
                await _spotifyPlayerService.PlayPlaylistAsync(CurrentSpotifySettings, deviceId, SelectedImportedSpotifyPlaylist.SpotifyPlaylistId);
            }
            catch (SpotifyApiException ex) when (ex.Kind == SpotifyApiErrorKind.PlaybackForbidden
                && SelectedImportedSpotifyPlaylist is not null
                && HasSpotifyPlaylistLink(SelectedImportedSpotifyPlaylist))
            {
                StartupLog.Write($"Spotify Connect playlist play refused; falling back to Spotify app for {SelectedImportedSpotifyPlaylist.Name}. status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
                SelectedPlaybackMode = SpotifyPlaybackModes.ExternalSpotifyAppHandoff;
                await OpenSpotifyPlaylistLinkAsync(SelectedImportedSpotifyPlaylist);
                SpotifyOperationMessage = $"Spotify refused remote control, so DancePilot opened {SelectedImportedSpotifyPlaylist.Name} in Spotify. Press Play in Spotify if it does not start automatically.";
                return;
            }

            CurrentSpotifyPlaylistName = SelectedImportedSpotifyPlaylist.Name;
            SpotifyOperationMessage = $"Started Spotify playlist: {SelectedImportedSpotifyPlaylist.Name}.";
            await SavePlaybackSettingsAsync();
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task PlayFromSelectedSpotifyTrackAsync()
    {
        if (SelectedImportedSpotifyTrack is null)
        {
            SpotifyOperationMessage = "Select a Spotify playlist track first.";
            return;
        }

        await QueueSpotifyTrackToDeckAsync(SelectedImportedSpotifyTrack, ActiveDeckName);
    }

    private async Task AddSelectedTrackToQueueAsync()
    {
        var track = SelectedImportedSpotifyTrack ?? SelectedSpotifyTrack;
        if (track is null)
        {
            SpotifyOperationMessage = "Select a Spotify track before adding to the DancePilot queue.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            var item = await _playbackCoordinator.AddToQueueAsync(track);
            await RefreshQueueCoreAsync();
            SpotifyOperationMessage = $"Added to DancePilot queue: {item.Title}.";
        });
    }

    private async Task RecommendNextFromPlaylistAsync()
    {
        if (ImportedSpotifyTracks.Count == 0)
        {
            SpotifyOperationMessage = "Load an imported playlist before asking DancePilot to recommend a next Spotify track.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            var candidate = ImportedSpotifyTracks
                .FirstOrDefault(track => !string.Equals(track.SpotifyUri, SelectedImportedSpotifyTrack?.SpotifyUri, StringComparison.OrdinalIgnoreCase))
                ?? ImportedSpotifyTracks.First();

            SelectedImportedSpotifyTrack = candidate;
            var item = await _playbackCoordinator.AddToQueueAsync(candidate);
            await RefreshQueueCoreAsync();
            SpotifyOperationMessage = $"Recommended next from playlist: {item.Title}.";
        });
    }

    private async Task PauseSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.PauseAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            SpotifyOperationMessage = "Spotify playback paused.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task ResumeSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.ResumeAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            SpotifyOperationMessage = "Spotify playback resumed.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task SkipNextSpotifyAsync()
    {
        var hadLoadedDeckItem = _playingDeckQueueItemId is not null;
        if (await TryPlayNextDeckQueueItemAsync())
        {
            return;
        }

        if (hadLoadedDeckItem)
        {
            var targetDeckName = ResolveTransitionDeckName();
            SpotifyOperationMessage = $"No next queued song on {targetDeckName} for {SelectedTransitionMode}.";
            return;
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SpotifyOperationMessage = $"No next queued song on {NormalizeDeckName(_playingDeckQueueItemId is null ? ActiveDeckName : _playingDeckName)}.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.SkipNextAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            SpotifyOperationMessage = "Skipped to the next Spotify track.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task SkipPreviousSpotifyAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.SkipPreviousAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            SpotifyOperationMessage = "Returned to the previous Spotify track.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task SetSpotifyVolumeAsync()
    {
        await SetDeckOutputVolumeAsync(ActiveDeckName);
    }

    private async Task SetDeckOutputVolumeAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var volumePercent = ResolveDeckVolumePercent(normalizedDeckName);
        await SavePlaybackSettingsAsync();

        if (!ShouldApplyVolumeToLiveOutput(normalizedDeckName))
        {
            SpotifyOperationMessage = $"{normalizedDeckName} volume saved at {FormatVolume(volumePercent)}. It will apply when that deck plays.";
            return;
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            _localMediaPlayer.Volume = ResolveDeckVolumeScalar(normalizedDeckName);
            SpotifyOperationMessage = $"{normalizedDeckName} local volume set to {FormatVolume(volumePercent)}.";
            return;
        }

        if (SelectedPlaybackMode != SpotifyPlaybackModes.SpotifyConnect)
        {
            SpotifyOperationMessage = $"{normalizedDeckName} volume saved at {FormatVolume(volumePercent)}. Open in Spotify App mode uses Spotify or Windows volume controls.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.SetVolumeAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync(), volumePercent);
            await SavePlaybackSettingsAsync();
            SpotifyOperationMessage = $"Requested {normalizedDeckName} Spotify volume {FormatVolume(volumePercent)}. Device support may vary.";
        });
    }

    private async Task SeekSpotifyAsync()
    {
        SeekPositionSeconds = Math.Clamp(SeekPositionSeconds, 0, SeekPositionMaximumSeconds);

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SeekLocalPlaybackTo(TimeSpan.FromSeconds(SeekPositionSeconds));
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            var targetMs = Convert.ToInt32(SeekPositionSeconds * 1000);
            await _spotifyPlayerService.SeekAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync(), targetMs);
            SpotifyOperationMessage = $"Spotify seek requested at {SeekPositionSeconds:N0} seconds.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task SeekRelativePlaybackAsync(int seconds)
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SeekLocalPlaybackTo(_localMediaPlayer.PlaybackSession.Position + TimeSpan.FromSeconds(seconds));
            SpotifyOperationMessage = seconds < 0
                ? $"Rewound local playback {Math.Abs(seconds)} seconds."
                : $"Fast-forwarded local playback {seconds} seconds.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            var state = await _spotifyPlayerService.GetPlaybackStateAsync(CurrentSpotifySettings);
            UpdateSeekPositionMaximum(state?.DurationMs);
            var currentMs = state?.ProgressMs ?? Convert.ToInt32(SeekPositionSeconds * 1000);
            var targetMs = Math.Max(0, currentMs + seconds * 1000);
            if (state?.DurationMs is int durationMs)
            {
                targetMs = Math.Min(durationMs, targetMs);
            }

            await _spotifyPlayerService.SeekAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync(), targetMs);
            SeekPositionSeconds = targetMs / 1000d;
            SpotifyOperationMessage = seconds < 0
                ? $"Rewound {Math.Abs(seconds)} seconds."
                : $"Fast-forwarded {seconds} seconds.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task EmergencyStopAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            SpotifyAutopilotEnabled = false;
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.PauseAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync());
            await SavePlaybackSettingsAsync();
            SpotifyOperationMessage = "Emergency stop sent: Spotify paused and DancePilot Spotify Autopilot disabled.";
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        });
    }

    private async Task DisableSpotifyAutopilotAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            SpotifyAutopilotEnabled = false;
            await SavePlaybackSettingsAsync();
            SpotifyOperationMessage = "DancePilot Spotify Autopilot disabled.";
        });
    }

    private async Task LoadImportedSpotifyPlaylistsAsync()
    {
        await RunSpotifyOperationAsync(async () =>
        {
            await LoadImportedSpotifyPlaylistsCoreAsync();
            SpotifyOperationMessage = $"Loaded {ImportedSpotifyPlaylists.Count} imported Spotify playlists from local SQLite.";
            QueueSessionStateSave();
        });
    }

    private async Task LoadImportedPlaylistTracksAsync()
    {
        if (SelectedImportedSpotifyPlaylist is null)
        {
            SpotifyOperationMessage = "Select an imported Spotify playlist first.";
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await LoadImportedPlaylistTracksCoreAsync();
            SpotifyOperationMessage = $"Loaded {ImportedSpotifyTracks.Count} imported tracks from {SelectedImportedSpotifyPlaylist.Name}.";
            QueueSessionStateSave();
        });
    }

    private async Task LoadLocalMusicAsync()
    {
        try
        {
            var musicFolder = string.IsNullOrWhiteSpace(LocalMusicFolderPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                : LocalMusicFolderPath;
            LocalMusicFolderPath = musicFolder;
            if (!Directory.Exists(musicFolder))
            {
                _allLocalMusicTracks.Clear();
                LocalMusicTracks.Clear();
                SelectedLocalMusicTrack = null;
                LocalLibraryStatus = $"Local music folder not found: {musicFolder}";
                SpotifyOperationMessage = LocalLibraryStatus;
                QueueSessionStateSave();
                return;
            }

            LocalLibraryStatus = $"Scanning {musicFolder}...";
            var tracks = await _localMusicLibraryService.LoadFromFolderAsync(musicFolder);
            _allLocalMusicTracks.Clear();
            _allLocalMusicTracks.AddRange(tracks);
            ApplyLocalMusicFilter();

            LocalLibraryStatus = tracks.Count == 0
                ? $"No supported music files found in {musicFolder}. MP3, MP4, WAV, M4A, AAC, WMA, FLAC, AIFF, OGG, and OPUS are included."
                : $"Loaded {tracks.Count} local audio file(s) from {musicFolder}.";
            CurrentOutputStatus = "Local file mode is ready.";
            SpotifyOperationMessage = LocalLibraryStatus;
            QueueSessionStateSave();
        }
        catch (Exception ex)
        {
            LocalLibraryStatus = $"Local music scan failed: {ex.Message}";
            SpotifyOperationMessage = LocalLibraryStatus;
        }
    }

    public async Task LoadLocalMusicFromFolderAsync(string folderPath)
    {
        LocalMusicFolderPath = folderPath;
        await LoadLocalMusicAsync();
    }

    private async Task LoadLocalPlaylistsAsync()
    {
        await LoadLocalPlaylistsCoreAsync();
        LocalPlaylistStatus = LocalMusicPlaylists.Count == 0
            ? "No saved local playlists yet."
            : $"Loaded {LocalMusicPlaylists.Count} saved local playlist(s).";
    }

    private async Task LoadLocalPlaylistsCoreAsync()
    {
        var selectedId = SelectedLocalMusicPlaylist?.Id;
        var playlists = await _localPlaylistRepository.GetPlaylistsAsync();
        ReplaceCollection(LocalMusicPlaylists, playlists);
        SelectedLocalMusicPlaylist = LocalMusicPlaylists.FirstOrDefault(playlist => playlist.Id == selectedId)
            ?? LocalMusicPlaylists.FirstOrDefault();
    }

    public async Task<LocalMusicPlaylist?> CreateLocalPlaylistAsync(string playlistName)
    {
        try
        {
            var playlist = await _localPlaylistRepository.CreatePlaylistAsync(playlistName);
            await LoadLocalPlaylistsCoreAsync();
            SelectedLocalMusicPlaylist = LocalMusicPlaylists.FirstOrDefault(existing => existing.Id == playlist.Id)
                ?? playlist;
            LocalPlaylistStatus = $"Saved local playlist: {SelectedLocalMusicPlaylist.Name}.";
            QueueSessionStateSave();
            return SelectedLocalMusicPlaylist;
        }
        catch (Exception ex)
        {
            LocalPlaylistStatus = $"Local playlist could not be saved: {ex.Message}";
            SpotifyOperationMessage = LocalPlaylistStatus;
            return null;
        }
    }

    public async Task AddLocalTracksToSelectedPlaylistAsync(IEnumerable<LocalMusicTrack> tracks)
    {
        if (SelectedLocalMusicPlaylist is null)
        {
            LocalPlaylistStatus = "Create or select a local playlist first.";
            SpotifyOperationMessage = LocalPlaylistStatus;
            return;
        }

        var filePaths = tracks
            .Where(track => File.Exists(track.FilePath))
            .Select(track => track.FilePath)
            .ToList();
        if (filePaths.Count == 0)
        {
            LocalPlaylistStatus = "Select local songs before adding to a playlist.";
            SpotifyOperationMessage = LocalPlaylistStatus;
            return;
        }

        var playlistId = SelectedLocalMusicPlaylist.Id;
        var added = await _localPlaylistRepository.AddTracksAsync(playlistId, filePaths);
        await LoadLocalPlaylistsCoreAsync();
        SelectedLocalMusicPlaylist = LocalMusicPlaylists.FirstOrDefault(playlist => playlist.Id == playlistId)
            ?? SelectedLocalMusicPlaylist;
        LocalPlaylistStatus = added == 0
            ? $"Those songs were already in {SelectedLocalMusicPlaylist.Name}."
            : $"Added {added} song(s) to {SelectedLocalMusicPlaylist.Name}.";
        SpotifyOperationMessage = LocalPlaylistStatus;
        QueueSessionStateSave();
    }

    private async Task AddFilteredLocalMusicToSelectedPlaylistAsync()
    {
        await AddLocalTracksToSelectedPlaylistAsync(LocalMusicTracks);
    }

    public async Task QueueSelectedLocalPlaylistToDeckAsync(string deckName)
    {
        if (SelectedLocalMusicPlaylist is null)
        {
            LocalPlaylistStatus = "Select a saved local playlist first.";
            SpotifyOperationMessage = LocalPlaylistStatus;
            return;
        }

        var playlist = SelectedLocalMusicPlaylist;
        var added = await QueueLocalPlaylistsToDeckAsync([playlist], deckName);
        if (added == 0)
        {
            LocalPlaylistStatus = $"{playlist.Name} has no available songs. Scan or reattach the folder that contains them.";
            SpotifyOperationMessage = LocalPlaylistStatus;
        }
    }

    private async Task DeleteSelectedLocalPlaylistAsync()
    {
        if (SelectedLocalMusicPlaylist is null)
        {
            LocalPlaylistStatus = "Select a saved local playlist to delete.";
            return;
        }

        var name = SelectedLocalMusicPlaylist.Name;
        await _localPlaylistRepository.DeletePlaylistAsync(SelectedLocalMusicPlaylist.Id);
        await LoadLocalPlaylistsCoreAsync();
        LocalPlaylistStatus = $"Deleted local playlist: {name}.";
        SpotifyOperationMessage = LocalPlaylistStatus;
        QueueSessionStateSave();
    }

    private async Task<IReadOnlyList<LocalMusicTrack>> LoadSelectedLocalPlaylistTracksAsync()
    {
        if (SelectedLocalMusicPlaylist is null)
        {
            return [];
        }

        return await LoadLocalPlaylistTracksAsync(SelectedLocalMusicPlaylist);
    }

    private async Task<IReadOnlyList<LocalMusicTrack>> LoadLocalPlaylistTracksAsync(LocalMusicPlaylist playlist)
    {
        var paths = await _localPlaylistRepository.GetTrackFilePathsAsync(playlist.Id);
        var tracks = await _localMusicLibraryService.LoadFromFilePathsAsync(paths);
        var order = paths
            .Select((path, index) => new { Path = path, Index = index })
            .ToDictionary(pair => pair.Path, pair => pair.Index, StringComparer.OrdinalIgnoreCase);
        return tracks
            .OrderBy(track => order.GetValueOrDefault(track.FilePath, int.MaxValue))
            .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Task SearchLocalMusicAsync()
    {
        ApplyLocalMusicFilter();
        LocalLibraryStatus = LocalMusicTracks.Count == _allLocalMusicTracks.Count
            ? $"Showing all {_allLocalMusicTracks.Count} local audio file(s)."
            : $"Showing {LocalMusicTracks.Count} of {_allLocalMusicTracks.Count} local audio file(s).";
        SpotifyOperationMessage = LocalLibraryStatus;
        QueueSessionStateSave();
        return Task.CompletedTask;
    }

    private void ApplyLocalMusicFilter()
    {
        var selectedFilePath = SelectedLocalMusicTrack?.FilePath;
        var filteredTracks = SortLocalMusicTracks(_localMusicLibraryService.Search(_allLocalMusicTracks, LocalMusicSearchQuery));
        LocalMusicTracks.Clear();
        foreach (var track in filteredTracks.Take(500))
        {
            LocalMusicTracks.Add(track);
        }

        SelectedLocalMusicTrack = LocalMusicTracks.FirstOrDefault(track =>
            string.Equals(track.FilePath, selectedFilePath, StringComparison.OrdinalIgnoreCase))
            ?? LocalMusicTracks.FirstOrDefault();
    }

    private IEnumerable<LocalMusicTrack> SortLocalMusicTracks(IEnumerable<LocalMusicTrack> tracks) =>
        SelectedLocalMusicSortOption switch
        {
            LocalSortTitle => tracks
                .OrderBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Folder, StringComparer.OrdinalIgnoreCase),
            LocalSortArtist => tracks
                .OrderBy(track => track.DisplayArtist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Folder, StringComparer.OrdinalIgnoreCase),
            LocalSortBpm => tracks
                .OrderBy(track => track.BPM is null)
                .ThenBy(track => track.BPM)
                .ThenBy(track => track.MusicalKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase),
            LocalSortKey => tracks
                .OrderBy(track => string.IsNullOrWhiteSpace(track.MusicalKey))
                .ThenBy(track => track.MusicalKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.BPM is null)
                .ThenBy(track => track.BPM)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase),
            LocalSortNewest => tracks
                .OrderByDescending(track => track.LastModifiedAt)
                .ThenBy(track => track.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase),
            LocalSortDuration => tracks
                .OrderBy(track => track.Duration is null)
                .ThenBy(track => track.Duration)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase),
            LocalSortFileType => tracks
                .OrderBy(track => track.Extension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase),
            _ => tracks
                .OrderBy(track => track.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
        };

    private async Task QueueSelectedLocalMusicAsync()
    {
        if (SelectedLocalMusicTrack is null)
        {
            SpotifyOperationMessage = "Select a local music file first.";
            return;
        }

        await QueueLocalTrackToDeckAsync(SelectedLocalMusicTrack, ActiveDeckName);
    }

    private Task<bool> StartSelectedLocalMusicAsync(string? deckName = null)
    {
        if (SelectedLocalMusicTrack is null)
        {
            SpotifyOperationMessage = "Select a local music file first.";
            return Task.FromResult(false);
        }

        if (!File.Exists(SelectedLocalMusicTrack.FilePath))
        {
            SpotifyOperationMessage = $"Local file was not found: {SelectedLocalMusicTrack.FileName}";
            return Task.FromResult(false);
        }

        var fileUri = new Uri(SelectedLocalMusicTrack.FilePath);
        var playbackDeckName = NormalizeDeckName(deckName ?? ActiveDeckName);
        _localMediaPlayer.Source = MediaSource.CreateFromUri(fileUri);
        _localMediaPlayer.Volume = AlwaysFadeSongs
            ? 0
            : ResolveDeckVolumeScalar(playbackDeckName);
        _localMediaPlayer.Play();

        SeekPositionMaximumSeconds = SelectedLocalMusicTrack.Duration?.TotalSeconds ?? 1;
        SeekPositionSeconds = 0;
        IsPlaybackPlaying = true;
        SpotifyNowPlayingTitle = SelectedLocalMusicTrack.Title;
        SpotifyNowPlayingArtist = SelectedLocalMusicTrack.DisplayArtist;
        _currentPlaybackAlbumArtUrl = ResolveLocalTrackAlbumArt(SelectedLocalMusicTrack);
        SpotifyPlaybackStatus = "Playing local";
        SpotifyTimeRemaining = SelectedLocalMusicTrack.DurationDisplay;
        SpotifyProgressDisplay = SelectedLocalMusicTrack.Duration is TimeSpan duration
            ? $"0:00 / {duration:m\\:ss}"
            : "0:00 / --:--";
        CurrentOutputStatus = $"Playing local file: {SelectedLocalMusicTrack.FileName}";
        SpotifyOperationMessage = "Local playback is running from this PC. Spotify commands do not affect local files.";
        return Task.FromResult(true);
    }

    private void SeekLocalPlaybackTo(TimeSpan requestedPosition)
    {
        var session = _localMediaPlayer.PlaybackSession;
        var target = requestedPosition < TimeSpan.Zero
            ? TimeSpan.Zero
            : requestedPosition;
        if (session.NaturalDuration > TimeSpan.Zero && target > session.NaturalDuration)
        {
            target = session.NaturalDuration;
        }

        if (session.NaturalDuration > TimeSpan.Zero)
        {
            SeekPositionMaximumSeconds = session.NaturalDuration.TotalSeconds;
        }

        session.Position = target;
        SeekPositionSeconds = target.TotalSeconds;
        SpotifyProgressDisplay = session.NaturalDuration > TimeSpan.Zero
            ? $"{target:m\\:ss} / {session.NaturalDuration:m\\:ss}"
            : $"{target:m\\:ss} / --:--";
        SpotifyTimeRemaining = session.NaturalDuration > TimeSpan.Zero
            ? $"{(session.NaturalDuration - target):m\\:ss}"
            : "--:--";
        CurrentOutputStatus = $"Local file position: {target:m\\:ss}.";
    }

    private void PauseLocalMusic()
    {
        _localMediaPlayer.Pause();
        IsPlaybackPlaying = false;
        SpotifyPlaybackStatus = "Local paused";
        CurrentOutputStatus = "Local file playback paused.";
        SpotifyOperationMessage = "Local playback paused.";
    }

    private void ResumeLocalMusic()
    {
        _localMediaPlayer.Volume = ResolveDeckVolumeScalar(_playingDeckName);
        _localMediaPlayer.Play();
        IsPlaybackPlaying = true;
        SpotifyPlaybackStatus = "Playing local";
        CurrentOutputStatus = $"Resumed local file on {_playingDeckName}.";
        SpotifyOperationMessage = "Local playback resumed.";
        RefreshDeckDisplayProperties();
    }

    private async Task TogglePlaybackAsync()
    {
        if (IsPlaybackPlaying)
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                PauseLocalMusic();
            }
            else
            {
                await PauseSpotifyAsync();
            }

            return;
        }

        if (_playingDeckQueueItemId is not null)
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                ResumeLocalMusic();
            }
            else
            {
                await ResumeSpotifyAsync();
            }

            return;
        }

        var nextDeckItem = SelectedActiveDeckQueueItem
            ?? GetNextDeckQueueItem(ActiveDeckName, includeFirstIfNoLastPlayed: true);
        if (nextDeckItem is not null)
        {
            await PlayDeckQueueItemAsync(nextDeckItem);
            return;
        }

        await ResumeSpotifyAsync();
    }

    private async Task ToggleDeckPlaybackAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (IsDeckPlaying(normalizedDeckName))
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                PauseLocalMusic();
            }
            else
            {
                await PauseSpotifyAsync();
            }

            return;
        }

        if (!IsPlaybackPlaying
            && _playingDeckQueueItemId is not null
            && string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                ResumeLocalMusic();
            }
            else
            {
                await ResumeSpotifyAsync();
            }

            return;
        }

        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        var item = FindSelectedDeckQueueItem(normalizedDeckName)
            ?? GetNextDeckQueueItem(normalizedDeckName, includeFirstIfNoLastPlayed: true)
            ?? QueueForDeck(normalizedDeckName).FirstOrDefault();
        if (item is null)
        {
            SpotifyOperationMessage = $"{normalizedDeckName} has no queued song to play.";
            return;
        }

        _selectedDeckQueueItemIds[normalizedDeckName] = item.Id;
        SelectedActiveDeckQueueItem = ActiveDeckQueue.FirstOrDefault(queueItem => queueItem.Id == item.Id)
            ?? item;
        await PlayDeckQueueItemAsync(item, normalizedDeckName);
    }

    private void SelectDeck(string deckName)
    {
        ActiveDeckName = deckName;
        RefreshActiveDeckQueue();
        UpdateNextUpFromDecks();
        SpotifyOperationMessage = $"{ActiveDeckName} selected. Cue a song here, then select it for transition.";
    }

    private void ShowDeckQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        QueueViewDeckName = normalizedDeckName;
        CacheDeckAlbumArtForQueue(normalizedDeckName);
        SpotifyOperationMessage = $"Viewing {normalizedDeckName} queue. Playback and transition cursor are unchanged.";
    }

    private async Task<bool> PlayDeckQueueItemAsync(DancePilotQueueItem queueItem, string? deckName = null, bool isTransition = false)
    {
        var targetDeckName = deckName ?? ActiveDeckName;
        if (!string.Equals(ActiveDeckName, targetDeckName, StringComparison.Ordinal))
        {
            ActiveDeckName = targetDeckName;
            RefreshActiveDeckQueue();
        }

        if (queueItem.Source == SongSources.Local)
        {
            var localTrack = _allLocalMusicTracks.FirstOrDefault(track =>
                string.Equals(track.FilePath, queueItem.ExternalUri, StringComparison.OrdinalIgnoreCase));
            if (localTrack is not null)
            {
                SelectedLocalMusicTrack = localTrack;
                SelectedPlaybackMode = SpotifyPlaybackModes.LocalFilesFuture;
                var started = await StartSelectedLocalMusicAsync(targetDeckName);
                if (!started)
                {
                    return false;
                }

                MarkDeckItemPlaying(targetDeckName, queueItem);
                if (!isTransition)
                {
                    await FadeIncomingPlaybackInAsync();
                }

                return true;
            }
        }

        if (queueItem.Source == SongSources.Spotify && !string.IsNullOrWhiteSpace(queueItem.ExternalUri))
        {
            if (SelectedPlaybackMode != SpotifyPlaybackModes.SpotifyConnect)
            {
                SelectedPlaybackMode = SpotifyPlaybackModes.SpotifyConnect;
            }

            var track = FindSpotifyTrackByUri(queueItem.ExternalUri) ?? new SpotifyTrackMetadata
            {
                SpotifyTrackId = ExtractSpotifyTrackId(queueItem.ExternalUri),
                Title = queueItem.Title,
                Artist = queueItem.Artist,
                AlbumArtUrl = queueItem.AlbumArtUrl,
                DurationMs = 0,
                SpotifyUri = queueItem.ExternalUri
            };

            var started = false;
            var operationSucceeded = await RunSpotifyOperationAsync(async () =>
            {
                started = await PlaySpotifyTrackWithFallbackAsync(track, isTransition
                    ? $"Transitioned to {track.Title} on {targetDeckName}."
                    : $"Playing {track.Title} from {targetDeckName}.",
                    allowExternalFallback: false);
                if (!started)
                {
                    return;
                }

                MarkDeckItemPlaying(targetDeckName, queueItem);
                if (!isTransition)
                {
                    await FadeIncomingPlaybackInAsync();
                }
            });

            if (!operationSucceeded || !started)
            {
                return false;
            }

            return true;
        }

        SpotifyOperationMessage = $"Queued item cannot be played yet: {queueItem.Title}.";
        return false;
    }

    public async Task PlaySelectedActiveDeckQueueItemAsync()
    {
        if (SelectedActiveDeckQueueItem is not null)
        {
            await PlayDeckQueueItemAsync(SelectedActiveDeckQueueItem);
        }
    }

    public Task PlayQueueItemAsync(DancePilotQueueItem queueItem) =>
        PlayDeckQueueItemAsync(queueItem, queueItem.DeckName);

    private bool TryValidateDeckQueueItemForPlayback(DancePilotQueueItem queueItem, out string? validationMessage)
    {
        validationMessage = null;
        if (queueItem.Source == SongSources.Local)
        {
            var localTrack = _allLocalMusicTracks.FirstOrDefault(track =>
                string.Equals(track.FilePath, queueItem.ExternalUri, StringComparison.OrdinalIgnoreCase));
            if (localTrack is null)
            {
                validationMessage = $"Local track is no longer loaded for {queueItem.Title}.";
                return false;
            }

            if (!File.Exists(localTrack.FilePath))
            {
                validationMessage = $"Local file was not found: {localTrack.FileName}.";
                return false;
            }

            return true;
        }

        if (queueItem.Source == SongSources.Spotify)
        {
            if (string.IsNullOrWhiteSpace(queueItem.ExternalUri))
            {
                validationMessage = $"{queueItem.Title} does not have a playable Spotify URI.";
                return false;
            }

            return true;
        }

        validationMessage = $"Queued item cannot be played yet: {queueItem.Title}.";
        return false;
    }

    private void MarkDeckItemPlaying(string deckName, DancePilotQueueItem queueItem)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (_playingDeckQueueItemId != queueItem.Id || !string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            WaveformHistoryForDeck(normalizedDeckName).Clear();
        }

        _playingDeckName = normalizedDeckName;
        _playingDeckQueueItemId = queueItem.Id;
        _lastPlayedDeckQueueItemIds[normalizedDeckName] = queueItem.Id;
        _selectedDeckQueueItemIds[normalizedDeckName] = queueItem.Id;
        if (!string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            ActiveDeckName = normalizedDeckName;
        }

        RefreshActiveDeckQueue();
        OnPropertyChanged(nameof(CurrentDeckHeader));
        OnPropertyChanged(nameof(NextDeckHeader));
        OnPropertyChanged(nameof(DeckAPlayPauseLabel));
        OnPropertyChanged(nameof(DeckBPlayPauseLabel));
        OnPropertyChanged(nameof(DefaultSpotifyVolume));
        OnPropertyChanged(nameof(DefaultSpotifyVolumeDisplay));
        UpdateNextUpFromDecks();
        QueueSessionStateSave();
    }

    private void MarkDeckItemCompleted(string deckName, int itemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        _lastPlayedDeckQueueItemIds[normalizedDeckName] = itemId;
        _selectedDeckQueueItemIds[normalizedDeckName] = GetQueueItemAfter(normalizedDeckName, itemId)?.Id;

        if (string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal))
        {
            RefreshActiveDeckQueue();
        }
        else
        {
            UpdateNextUpFromDecks();
        }

        QueueSessionStateSave();
    }

    private void ClearEndedPlayingDeckItemIfCurrent(string deckName, int itemId)
    {
        if (!string.Equals(_playingDeckName, NormalizeDeckName(deckName), StringComparison.Ordinal)
            || _playingDeckQueueItemId != itemId)
        {
            return;
        }

        _playingDeckQueueItemId = null;
        _currentPlaybackAlbumArtUrl = null;
        OnPropertyChanged(nameof(DeckAPlayPauseLabel));
        OnPropertyChanged(nameof(DeckBPlayPauseLabel));
        RefreshActiveDeckQueue();
        RefreshDeckDisplayProperties();
        QueueSessionStateSave();
    }

    private async Task<bool> TryPlayNextDeckQueueItemAsync()
    {
        if (_playingDeckQueueItemId is not null)
        {
            return await TryStartTransitionTargetAsync(skipFadeOut: !IsPlaybackPlaying);
        }

        var deckName = NormalizeDeckName(ActiveDeckName);
        var sourceDeckName = _playingDeckName;
        var sourceItemId = _playingDeckQueueItemId;
        var sourcePlaybackMode = SelectedPlaybackMode;
        var nextItem = FindNextDeckQueueItemForManualAdvance(deckName);
        if (nextItem is null)
        {
            return false;
        }

        if (IsPlaybackPlaying)
        {
            await FadeCurrentPlaybackOutAsync();
        }

        var started = await PlayDeckQueueItemAsync(nextItem, deckName);
        if (!started)
        {
            await RestoreDeckOutputVolumeAsync(sourceDeckName, sourcePlaybackMode);
            return false;
        }

        if (RemovePlayedQueueItems && sourceItemId is not null && nextItem.Id != sourceItemId.Value)
        {
            RemovePlayedQueueItem(sourceDeckName, sourceItemId.Value);
        }

        return true;
    }

    private DancePilotQueueItem? FindNextDeckQueueItemForManualAdvance(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count == 0)
        {
            return null;
        }

        if (string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal)
            && _playingDeckQueueItemId is not null)
        {
            var currentIndex = queue.FindIndex(item => item.Id == _playingDeckQueueItemId);
            if (currentIndex >= 0)
            {
                if (currentIndex + 1 < queue.Count)
                {
                    return queue[currentIndex + 1];
                }

                return RemovePlayedQueueItems ? null : queue.FirstOrDefault();
            }
        }

        return FindSelectedDeckQueueItem(normalizedDeckName) ?? queue.FirstOrDefault();
    }

    private async Task<bool> MaybeTransitionDeckAsync(SpotifyPlaybackState? currentState)
    {
        if (!DeckTransitionEnabled
            || _isTransitionAdvanceRunning
            || _playingDeckQueueItemId is null
            || currentState is null
            || currentState.DurationMs is null
            || currentState.DurationMs <= 0
            || currentState.ProgressMs is null
            || currentState.ProgressMs < 0)
        {
            return false;
        }

        var remainingMs = currentState.DurationMs.Value - currentState.ProgressMs.Value;
        var remainingSeconds = Math.Max(0, remainingMs / 1000d);
        var transitionTriggerSeconds = AlwaysFadeSongs && StartTransitionOnFade
            ? FadeOutSeconds + TransitionOverlapSeconds
            : TransitionOverlapSeconds;
        if (currentState.IsPlaying && remainingSeconds > transitionTriggerSeconds)
        {
            return false;
        }

        if (!currentState.IsPlaying && remainingSeconds > LateTransitionSkipFadeSeconds)
        {
            return false;
        }

        return await TryStartTransitionTargetAsync(
            skipFadeOut: !currentState.IsPlaying || remainingSeconds <= LateTransitionSkipFadeSeconds,
            fadeOutSeconds: CreateTransitionFadeOutDuration(remainingSeconds));
    }

    private async Task<bool> TryStartTransitionTargetAsync(bool skipFadeOut = false, double? fadeOutSeconds = null)
    {
        if (_isTransitionAdvanceRunning)
        {
            return false;
        }

        var sourceDeckName = _playingDeckName;
        var sourcePlaybackMode = SelectedPlaybackMode;
        _isTransitionAdvanceRunning = true;
        try
        {
            var sourceItemId = _playingDeckQueueItemId;
            var targetDeckName = ResolveTransitionDeckName();
            var next = FindTransitionTarget(targetDeckName);
            if (sourceItemId is null || next is null)
            {
                return false;
            }

            if (next.Id == sourceItemId.Value)
            {
                return false;
            }

            if (_lastTransitionSourceItemId == sourceItemId && _lastTransitionTargetItemId == next.Id)
            {
                return false;
            }

            if (!TryValidateDeckQueueItemForPlayback(next, out var validationMessage))
            {
                SpotifyOperationMessage = validationMessage ?? $"The next song on {targetDeckName} is not playable.";
                return false;
            }

            if (skipFadeOut)
            {
                await SilenceCurrentPlaybackForImmediateTransitionAsync();
            }
            else
            {
                await FadeCurrentPlaybackOutAsync(fadeOutSeconds);
            }

            var started = await PlayDeckQueueItemAsync(next, targetDeckName, isTransition: true);
            if (!started || _playingDeckQueueItemId != next.Id)
            {
                await RestoreDeckOutputVolumeAsync(sourceDeckName, sourcePlaybackMode);
                SpotifyOperationMessage = $"Deck transition could not start {next.Title} on {targetDeckName}. Volume was restored.";
                return false;
            }

            _lastTransitionSourceItemId = sourceItemId;
            _lastTransitionTargetItemId = next.Id;
            MarkDeckItemCompleted(sourceDeckName, sourceItemId.Value);
            if (RemovePlayedQueueItems && next.Id != sourceItemId.Value)
            {
                RemovePlayedQueueItem(sourceDeckName, sourceItemId.Value);
            }

            await FadeIncomingPlaybackInAsync();
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Deck transition failed: {ex.Message}");
            await RestoreDeckOutputVolumeAsync(sourceDeckName, sourcePlaybackMode);
            SpotifyOperationMessage = $"Deck transition failed: {ex.Message}. Volume was restored.";
            return false;
        }
        finally
        {
            _isTransitionAdvanceRunning = false;
        }
    }

    private double CreateTransitionFadeOutDuration(double remainingSeconds)
    {
        if (remainingSeconds <= LateTransitionSkipFadeSeconds)
        {
            return 0;
        }

        return Math.Clamp(
            remainingSeconds - TransitionOverlapSeconds,
            0,
            FadeOutSeconds);
    }

    private async Task FadeCurrentPlaybackOutAsync(double? fadeOutSeconds = null)
    {
        if (!AlwaysFadeSongs)
        {
            return;
        }

        var seconds = Math.Max(0, fadeOutSeconds ?? FadeOutSeconds);
        if (seconds <= 0.05)
        {
            await SilenceCurrentPlaybackForImmediateTransitionAsync();
            return;
        }

        try
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                await FadeLocalVolumeAsync(_localMediaPlayer.Volume, 0, seconds);
                return;
            }

            await FadeSpotifyVolumeAsync(ResolveDeckVolumePercent(_playingDeckName), 0, seconds);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Fade-out skipped: {ex.Message}");
        }
    }

    private async Task SilenceCurrentPlaybackForImmediateTransitionAsync()
    {
        if (!AlwaysFadeSongs)
        {
            return;
        }

        try
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                _localMediaPlayer.Volume = 0;
                return;
            }

            await SetSpotifyVolumeImmediateAsync(0);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Immediate transition volume prep skipped: {ex.Message}");
        }
    }

    private async Task FadeIncomingPlaybackInAsync()
    {
        var targetDeckName = _playingDeckName;
        var targetPlaybackMode = SelectedPlaybackMode;
        if (!AlwaysFadeSongs)
        {
            await RestoreDeckOutputVolumeAsync(targetDeckName, targetPlaybackMode);
            return;
        }

        if (targetPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            var targetVolume = ResolveDeckVolumeScalar(targetDeckName);
            try
            {
                await FadeLocalVolumeAsync(_localMediaPlayer.Volume, targetVolume, FadeInSeconds);
            }
            catch (Exception ex)
            {
                StartupLog.Write($"Local fade-in skipped: {ex.Message}");
            }
            finally
            {
                _localMediaPlayer.Volume = targetVolume;
            }

            return;
        }

        await FadeSpotifyVolumeAsync(0, ResolveDeckVolumePercent(targetDeckName), FadeInSeconds);
        await RestoreDeckOutputVolumeAsync(targetDeckName, targetPlaybackMode);
    }

    private async Task RestoreDeckOutputVolumeAsync(string deckName, string playbackMode)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (playbackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            _localMediaPlayer.Volume = ResolveDeckVolumeScalar(normalizedDeckName);
            return;
        }

        if (playbackMode == SpotifyPlaybackModes.SpotifyConnect)
        {
            await TrySetSpotifyVolumeImmediateAsync(ResolveDeckVolumePercent(normalizedDeckName));
        }
    }

    private async Task TrySetSpotifyVolumeImmediateAsync(int volume)
    {
        try
        {
            await SetSpotifyVolumeImmediateAsync(volume);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Spotify volume restore skipped: {ex.Message}");
        }
    }

    private async Task FadeSpotifyVolumeAsync(int fromVolume, int toVolume, double seconds)
    {
        if (SelectedPlaybackMode != SpotifyPlaybackModes.SpotifyConnect)
        {
            return;
        }

        string deviceId;
        try
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            deviceId = await ResolveSelectedDeviceIdAsync();
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Spotify fade skipped: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            StartupLog.Write("Spotify fade skipped: no Spotify Connect device was resolved.");
            return;
        }

        var steps = Math.Clamp(Convert.ToInt32(Math.Ceiling(seconds * 3)), 6, 30);
        var delay = TimeSpan.FromMilliseconds(Math.Max(120, seconds * 1000 / steps));

        for (var step = 0; step <= steps; step++)
        {
            var percent = step / (double)steps;
            var easedPercent = SmoothFadeEase(percent);
            var volume = Convert.ToInt32(fromVolume + (toVolume - fromVolume) * easedPercent);
            try
            {
                await _spotifyPlayerService.SetVolumeAsync(CurrentSpotifySettings, deviceId, Math.Clamp(volume, 0, 100));
            }
            catch (Exception ex)
            {
                StartupLog.Write($"Spotify fade volume command skipped: {ex.Message}");
                return;
            }

            if (step < steps)
            {
                await Task.Delay(delay);
            }
        }
    }

    private async Task SetSpotifyVolumeImmediateAsync(int volume)
    {
        if (SelectedPlaybackMode != SpotifyPlaybackModes.SpotifyConnect)
        {
            return;
        }

        await EnsurePlaybackScopesAsync();
        EnsureSpotifyConnectPlaybackMode();
        await _spotifyPlayerService.SetVolumeAsync(
            CurrentSpotifySettings,
            await ResolveSelectedDeviceIdAsync(),
            Math.Clamp(volume, 0, 100));
    }

    private async Task FadeLocalVolumeAsync(double fromVolume, double toVolume, double seconds)
    {
        var steps = Math.Clamp(Convert.ToInt32(Math.Ceiling(seconds * 12)), 12, 160);
        var delay = TimeSpan.FromMilliseconds(Math.Max(25, seconds * 1000 / steps));

        for (var step = 0; step <= steps; step++)
        {
            var percent = step / (double)steps;
            var easedPercent = SmoothFadeEase(percent);
            _localMediaPlayer.Volume = Math.Clamp(fromVolume + (toVolume - fromVolume) * easedPercent, 0, 1);
            if (step < steps)
            {
                await Task.Delay(delay);
            }
        }
    }

    private static double SmoothFadeEase(double percent)
    {
        var clamped = Math.Clamp(percent, 0, 1);
        return clamped * clamped * (3 - (2 * clamped));
    }

    private DancePilotQueueItem? FindTransitionTarget(string? deckName = null)
    {
        var resolvedDeckName = NormalizeDeckName(deckName ?? ResolveTransitionDeckName());
        return GetNextDeckQueueItem(resolvedDeckName, includeFirstIfNoLastPlayed: true);
    }

    private DancePilotQueueItem? GetNextDeckQueueItem(string deckName, bool includeFirstIfNoLastPlayed = false)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count == 0)
        {
            return null;
        }

        var lastPlayedId = _lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
        if (lastPlayedId is null
            && string.Equals(_playingDeckName, normalizedDeckName, StringComparison.Ordinal)
            && _playingDeckQueueItemId is not null)
        {
            lastPlayedId = _playingDeckQueueItemId;
        }

        if (lastPlayedId is null)
        {
            return includeFirstIfNoLastPlayed ? queue.FirstOrDefault() : null;
        }

        var currentIndex = queue.FindIndex(item => item.Id == lastPlayedId.Value);
        if (currentIndex < 0)
        {
            return includeFirstIfNoLastPlayed ? queue.FirstOrDefault() : null;
        }

        if (currentIndex + 1 >= queue.Count)
        {
            return null;
        }

        return queue[currentIndex + 1];
    }

    private DancePilotQueueItem? GetQueueItemAfter(string deckName, int itemId)
    {
        var queue = QueueForDeck(NormalizeDeckName(deckName));
        var currentIndex = queue.FindIndex(item => item.Id == itemId);
        return currentIndex >= 0 && currentIndex + 1 < queue.Count
            ? queue[currentIndex + 1]
            : null;
    }

    private string ResolveTransitionDeckName() =>
        SelectedTransitionMode == TransitionOppositeDeck
            ? OppositeDeckName(_playingDeckName)
            : _playingDeckName;

    private DancePilotQueueItem? FindSelectedDeckQueueItem(string deckName)
    {
        if (!_selectedDeckQueueItemIds.TryGetValue(deckName, out var selectedId) || selectedId is null)
        {
            return null;
        }

        return QueueForDeck(deckName).FirstOrDefault(item => item.Id == selectedId.Value);
    }

    private int? GetLastPlayedDeckQueueItemId(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        return _lastPlayedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
    }

    private static string OppositeDeckName(string deckName) =>
        deckName == "Deck B" ? "Deck A" : "Deck B";

    private List<DancePilotQueueItem> QueueForDeck(string deckName) =>
        deckName == "Deck B" ? _deckBQueue : _deckAQueue;

    private bool IsDeckPlaying(string deckName) =>
        IsDeckAudiblyPlaying(deckName);

    private bool IsDeckLoaded(string deckName) =>
        string.Equals(_playingDeckName, NormalizeDeckName(deckName), StringComparison.Ordinal)
        && _playingDeckQueueItemId is not null;

    private bool IsDeckAudiblyPlaying(string deckName) =>
        IsDeckLoaded(deckName) && IsPlaybackPlaying;

    private DancePilotQueueItem? ResolveNextDeckDisplayItem(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count == 0)
        {
            return null;
        }

        var next = GetNextDeckQueueItem(normalizedDeckName, includeFirstIfNoLastPlayed: true);
        if (next is not null)
        {
            return next;
        }

        return RemovePlayedQueueItems ? null : queue.FirstOrDefault();
    }

    private DancePilotQueueItem? ResolveDeckDisplayItem(string deckName) =>
        ResolveLoadedDeckItem(deckName) ?? ResolveNextDeckDisplayItem(deckName);

    private DancePilotQueueItem? ResolvePlayingDeckItem(string deckName) =>
        ResolveLoadedDeckItem(deckName);

    private DancePilotQueueItem? ResolveLoadedDeckItem(string deckName) =>
        IsDeckLoaded(deckName)
            ? QueueForDeck(NormalizeDeckName(deckName)).FirstOrDefault(item => item.Id == _playingDeckQueueItemId)
            : null;

    private string ResolveDeckStatus(string deckName)
    {
        if (IsDeckLoaded(deckName))
        {
            var state = IsDeckAudiblyPlaying(deckName) ? "Playing" : "Paused";
            return string.Equals(ActiveDeckName, deckName, StringComparison.Ordinal)
                ? $"{state} / Selected"
                : state;
        }

        if (string.Equals(ActiveDeckName, deckName, StringComparison.Ordinal))
        {
            return "Selected";
        }

        var count = QueueForDeck(deckName).Count;
        return count == 0 ? "Ready" : $"{count} queued";
    }

    private string ResolveDeckTitle(string deckName)
    {
        return ResolveDeckDisplayItem(deckName)?.Title ?? "No song queued";
    }

    private string ResolveDeckArtist(string deckName)
    {
        return ResolveDeckDisplayItem(deckName)?.Artist ?? "Drag songs or playlists here";
    }

    private string ResolveDeckDetail(string deckName)
    {
        var item = ResolveDeckDisplayItem(deckName);
        if (item is null)
        {
            return "Queue is empty";
        }

        if (ResolveLoadedDeckItem(deckName)?.Id == item.Id)
        {
            return !string.IsNullOrWhiteSpace(item.MixDisplay)
                ? $"{item.MixDisplay} | {SpotifyProgressDisplay}"
                : SpotifyProgressDisplay;
        }

        return item.MixDisplay;
    }

    private string? ResolveDeckAlbumArtSource(string deckName)
    {
        var displayItem = ResolveDeckDisplayItem(deckName);
        if (displayItem is null)
        {
            return null;
        }

        var itemArtSource = ResolveQueueItemAlbumArtSource(displayItem);
        if (HasUsableAlbumArtSource(itemArtSource))
        {
            return itemArtSource;
        }

        if (displayItem.Source == SongSources.Spotify
            && PlaybackStateMatchesQueueItem(displayItem)
            && !string.IsNullOrWhiteSpace(_currentPlaybackAlbumArtUrl))
        {
            return _currentPlaybackAlbumArtUrl;
        }

        var restoredArtSource = ResolveRestoredDeckAlbumArtSource(deckName, displayItem.Id);
        if (HasUsableAlbumArtSource(restoredArtSource))
        {
            return restoredArtSource;
        }

        return itemArtSource;
    }

    private string? ResolveRestoredDeckAlbumArtSource(string deckName, int queueItemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        return _restoredDeckAlbumArt.TryGetValue(normalizedDeckName, out var restored)
            && restored.QueueItemId == queueItemId
            ? restored.AlbumArtSource
            : null;
    }

    private string? ResolveQueueItemAlbumArtSource(DancePilotQueueItem item)
    {
        if (HasUsableAlbumArtSource(item.AlbumArtUrl))
        {
            return item.AlbumArtUrl;
        }

        if (item.Source == SongSources.Spotify)
        {
            var track = FindSpotifyTrackByUri(item.ExternalUri);
            if (HasUsableAlbumArtSource(track?.AlbumArtUrl))
            {
                return track!.AlbumArtUrl;
            }
        }

        if (item.Source == SongSources.Local)
        {
            var localTrack = _allLocalMusicTracks.FirstOrDefault(track =>
                string.Equals(track.FilePath, item.ExternalUri, StringComparison.OrdinalIgnoreCase));
            if (HasUsableAlbumArtSource(localTrack?.AlbumArtUrl))
            {
                return localTrack!.AlbumArtUrl;
            }
        }

        return item.AlbumArtUrl;
    }

    private bool PlaybackStateMatchesQueueItem(DancePilotQueueItem item) =>
        string.Equals(SpotifyNowPlayingTitle, item.Title, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(item.Artist)
            || string.Equals(SpotifyNowPlayingArtist, item.Artist, StringComparison.OrdinalIgnoreCase));

    private static bool IsDefaultAlbumArtSource(string? albumArtSource)
    {
        if (string.IsNullOrWhiteSpace(albumArtSource))
        {
            return true;
        }

        var normalized = NormalizeAlbumArtSource(albumArtSource, DefaultLocalAlbumArtPath);
        return string.Equals(normalized, DefaultLocalAlbumArtPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableAlbumArtSource(string? albumArtSource) =>
        !string.IsNullOrWhiteSpace(albumArtSource)
        && !IsDefaultAlbumArtSource(albumArtSource);

    private bool IsDeckDisplayItem(string deckName, int itemId) =>
        ResolveDeckDisplayItem(NormalizeDeckName(deckName))?.Id == itemId;

    private bool IsDeckNextPlayableItem(string deckName, int itemId) =>
        ResolveNextDeckDisplayItem(NormalizeDeckName(deckName))?.Id == itemId;

    private string? _currentDeckAlbumArtSource = DefaultCurrentAlbumArtPath;
    private string? _nextDeckAlbumArtSource = DefaultNextAlbumArtPath;

    private void RefreshDeckDisplayProperties()
    {
        var currentDeckSource = ResolveDeckAlbumArtSource("Deck A");
        if (!string.Equals(currentDeckSource, _currentDeckAlbumArtSource, StringComparison.OrdinalIgnoreCase))
        {
            CurrentDeckAlbumArt = CreateAlbumArtSource(currentDeckSource, DefaultCurrentAlbumArtPath);
            _currentDeckAlbumArtSource = currentDeckSource;
        }

        var nextDeckSource = ResolveDeckAlbumArtSource("Deck B");
        if (!string.Equals(nextDeckSource, _nextDeckAlbumArtSource, StringComparison.OrdinalIgnoreCase))
        {
            NextDeckAlbumArt = CreateAlbumArtSource(nextDeckSource, DefaultNextAlbumArtPath);
            _nextDeckAlbumArtSource = nextDeckSource;
        }

        OnPropertyChanged(nameof(DeckAStatus));
        OnPropertyChanged(nameof(DeckBStatus));
        OnPropertyChanged(nameof(DeckATitle));
        OnPropertyChanged(nameof(DeckBTitle));
        OnPropertyChanged(nameof(DeckAArtist));
        OnPropertyChanged(nameof(DeckBArtist));
        OnPropertyChanged(nameof(DeckADetail));
        OnPropertyChanged(nameof(DeckBDetail));
    }

    private void UpdateNextUpFromDecks()
    {
        var next = FindTransitionTarget() ?? ActiveDeckQueue.FirstOrDefault();
        NextUpTitle = next?.Title ?? "No queued recommendation";
        NextUpArtist = next?.Artist ?? "DancePilot queue";
        OnPropertyChanged(nameof(NextDeckHeader));
        RefreshDeckDisplayProperties();
    }

    private SpotifyTrackMetadata? FindSpotifyTrackByUri(string spotifyUri) =>
        SpotifyPreviewTracks
            .Concat(SpotifySearchResults)
            .Concat(ImportedSpotifyTracks)
            .FirstOrDefault(track => string.Equals(track.SpotifyUri, spotifyUri, StringComparison.OrdinalIgnoreCase));

    private static string ExtractSpotifyTrackId(string spotifyUri)
    {
        const string prefix = "spotify:track:";
        return spotifyUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? spotifyUri[prefix.Length..]
            : spotifyUri;
    }

    public void QueueSongToActiveDeck(Song song)
    {
        QueueSongToDeck(song, ActiveDeckName);
    }

    private void QueueSongToDeck(Song song, string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        QueueToDeck(new DancePilotQueueItem
        {
            Id = NextDeckQueueId(),
            DeckName = normalizedDeckName,
            Source = SongSources.Local,
            ExternalUri = song.ExternalUri ?? song.ExternalUrl ?? string.Empty,
            SongId = song.Id,
            Title = song.Title,
            Artist = song.Artist,
            AlbumArtUrl = song.AlbumArtPath,
            BPM = song.BPM,
            MusicalKey = song.Key,
            Status = "pending"
        }, normalizedDeckName);
    }

    public void QueueSpotifyTrackToActiveDeck(SpotifyTrackMetadata track)
    {
        DispatchAsync(() => QueueSpotifyTrackToDeckAsync(track, ActiveDeckName));
    }

    public void QueueSpotifyTracksToActiveDeck(IEnumerable<SpotifyTrackMetadata> tracks)
    {
        DispatchAsync(() => QueueSpotifyTracksToDeckAsync(tracks, ActiveDeckName));
    }

    private async Task<int> QueueSpotifyTracksToDeckAsync(IEnumerable<SpotifyTrackMetadata> tracks, string deckName, int? beforeItemId = null)
    {
        var added = 0;
        var normalizedDeckName = NormalizeDeckName(deckName);
        foreach (var track in tracks)
        {
            if (await QueueSpotifyTrackToDeckAsync(track, normalizedDeckName, announce: false, selectQueuedItem: added == 0, beforeItemId: beforeItemId))
            {
                added++;
            }
        }

        if (added > 0)
        {
            SpotifyOperationMessage = $"Queued {added} Spotify track(s) on {normalizedDeckName}.";
            QueueSessionStateSave();
        }

        return added;
    }

    private Task<bool> QueueSpotifyTrackToDeckAsync(
        SpotifyTrackMetadata track,
        string deckName,
        bool announce = true,
        bool selectQueuedItem = true,
        int? beforeItemId = null)
    {
        if (track.IsUnavailable || string.IsNullOrWhiteSpace(track.SpotifyUri))
        {
            SpotifyOperationMessage = $"{track.Title} is not playable through Spotify API controls. Open it in Spotify or choose another track.";
            return Task.FromResult(false);
        }

        var normalizedDeckName = NormalizeDeckName(deckName);
        var queueItem = new DancePilotQueueItem
        {
            Id = NextDeckQueueId(),
            DeckName = normalizedDeckName,
            Source = SongSources.Spotify,
            ExternalUri = track.SpotifyUri,
            Title = track.Title,
            Artist = track.Artist,
            AlbumArtUrl = track.AlbumArtUrl,
            BPM = track.BPM,
            MusicalKey = track.MusicalKey,
            Status = "pending"
        };

        QueueToDeck(queueItem, normalizedDeckName, announce, selectQueuedItem, beforeItemId);
        return Task.FromResult(true);
    }

    public void QueueLocalTrackToActiveDeck(LocalMusicTrack track)
    {
        DispatchAsync(() => QueueLocalTrackToDeckAsync(track, ActiveDeckName));
    }

    public void QueueLocalTracksToActiveDeck(IEnumerable<LocalMusicTrack> tracks)
    {
        DispatchAsync(() => QueueLocalTracksToDeckAsync(tracks, ActiveDeckName));
    }

    private async Task<int> QueueLocalTracksToDeckAsync(IEnumerable<LocalMusicTrack> tracks, string deckName, int? beforeItemId = null)
    {
        var added = 0;
        var normalizedDeckName = NormalizeDeckName(deckName);
        foreach (var track in tracks)
        {
            await QueueLocalTrackToDeckAsync(track, normalizedDeckName, announce: false, selectQueuedItem: added == 0, beforeItemId: beforeItemId);
            added++;
        }

        if (added > 0)
        {
            SpotifyOperationMessage = $"Queued {added} local track(s) on {normalizedDeckName}.";
            QueueSessionStateSave();
        }

        return added;
    }

    private Task QueueLocalTrackToDeckAsync(
        LocalMusicTrack track,
        string deckName,
        bool announce = true,
        bool selectQueuedItem = true,
        int? beforeItemId = null)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queueItem = new DancePilotQueueItem
        {
            Id = NextDeckQueueId(),
            DeckName = normalizedDeckName,
            Source = SongSources.Local,
            ExternalUri = track.FilePath,
            Title = track.Title,
            Artist = track.DisplayArtist,
            AlbumArtUrl = ResolveLocalTrackAlbumArt(track),
            BPM = track.BPM,
            MusicalKey = track.MusicalKey,
            Status = "pending"
        };

        QueueToDeck(queueItem, normalizedDeckName, announce, selectQueuedItem, beforeItemId);
        return Task.CompletedTask;
    }

    private static string ResolveLocalTrackAlbumArt(LocalMusicTrack track) =>
        string.IsNullOrWhiteSpace(track.AlbumArtUrl)
            ? DefaultLocalAlbumArtPath
            : track.AlbumArtUrl.Trim();

    private async Task<DancePilotQueueItem> ResolveDisplayQueueItemAlbumArtAsync(DancePilotQueueItem queueItem)
    {
        var resolvedItem = await ResolveQueueItemAlbumArtAsync(queueItem);
        if (!HasUsableAlbumArtSource(resolvedItem.AlbumArtUrl))
        {
            return resolvedItem;
        }

        try
        {
            var cachedAlbumArtUri = await _albumArtCacheService.CacheAlbumArtAsync(resolvedItem);
            return string.IsNullOrWhiteSpace(cachedAlbumArtUri)
                ? resolvedItem
                : resolvedItem with { AlbumArtUrl = cachedAlbumArtUri };
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Album art could not be cached for {queueItem.Title}: {ex.Message}");
            return resolvedItem;
        }
    }

    public async Task QueueSourceSelectionToDeckAsync(
        string deckName,
        IEnumerable<SpotifyTrackMetadata> spotifyTracks,
        IEnumerable<LocalMusicTrack> localTracks,
        IEnumerable<SpotifyPlaylistSummary> spotifyPlaylists,
        int? insertBeforeItemId = null,
        bool insertAtTop = false,
        IEnumerable<LocalMusicPlaylist>? localPlaylists = null)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var beforeItemId = insertAtTop && QueueForDeck(normalizedDeckName).Count > 0
            ? QueueForDeck(normalizedDeckName).First().Id
            : insertBeforeItemId;

        var selectedSpotifyTracks = spotifyTracks
            .DistinctBy(track => track.SpotifyUri ?? track.SpotifyTrackId)
            .ToList();
        var selectedLocalTracks = localTracks
            .DistinctBy(track => track.FilePath)
            .ToList();
        var selectedPlaylists = spotifyPlaylists
            .DistinctBy(playlist => playlist.SpotifyPlaylistId)
            .ToList();
        var selectedLocalPlaylists = (localPlaylists ?? [])
            .DistinctBy(playlist => playlist.Id)
            .ToList();
        var hasExplicitSelection = selectedSpotifyTracks.Count > 0
            || selectedLocalTracks.Count > 0
            || selectedPlaylists.Count > 0
            || selectedLocalPlaylists.Count > 0;

        var queuedCount = 0;
        if (selectedLocalTracks.Count > 0)
        {
            queuedCount += await QueueLocalTracksToDeckAsync(selectedLocalTracks, normalizedDeckName, beforeItemId);
        }

        if (selectedSpotifyTracks.Count > 0)
        {
            queuedCount += await QueueSpotifyTracksToDeckAsync(selectedSpotifyTracks, normalizedDeckName, beforeItemId);
        }

        if (selectedPlaylists.Count > 0)
        {
            queuedCount += await QueueSpotifyPlaylistsToDeckAsync(selectedPlaylists, normalizedDeckName, beforeItemId);
        }

        if (selectedLocalPlaylists.Count > 0)
        {
            queuedCount += await QueueLocalPlaylistsToDeckAsync(selectedLocalPlaylists, normalizedDeckName, beforeItemId);
        }

        if (queuedCount == 0 && !hasExplicitSelection)
        {
            await QueueSelectedSourceToDeckAsync(normalizedDeckName, beforeItemId);
            return;
        }

        if (queuedCount == 0)
        {
            SpotifyOperationMessage = "No playable songs were found in that drop selection.";
            return;
        }

        SpotifyOperationMessage = $"Queued {queuedCount} item(s) on {normalizedDeckName}.";
        QueueSessionStateSave();
    }

    private async Task<int> QueueSpotifyPlaylistsToDeckAsync(IReadOnlyList<SpotifyPlaylistSummary> playlists, string deckName, int? beforeItemId = null)
    {
        var added = 0;
        var wasBusy = IsSpotifyBusy;
        try
        {
            if (!wasBusy)
            {
                IsSpotifyBusy = true;
            }

            foreach (var playlist in playlists)
            {
                var tracks = SelectedSpotifyPlaylist is not null
                    && string.Equals(SelectedSpotifyPlaylist.SpotifyPlaylistId, playlist.SpotifyPlaylistId, StringComparison.Ordinal)
                    && SpotifyPreviewTracks.Count > 0
                        ? SpotifyPreviewTracks.ToList()
                        : (await _spotifyPlaylistImporter.PreviewPlaylistTracksAsync(CurrentSpotifySettings, playlist.SpotifyPlaylistId)).ToList();

                added += await QueueSpotifyTracksToDeckAsync(
                    tracks.Where(track => !track.IsUnavailable && !string.IsNullOrWhiteSpace(track.SpotifyUri)),
                    deckName,
                    beforeItemId);
            }
        }
        catch (SpotifyApiException ex)
        {
            StartupLog.Write($"Spotify playlist queue failed kind={ex.Kind} status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
            SpotifyOperationMessage = ToFriendlySpotifyMessage(ex);
        }
        catch (Exception ex)
        {
            SpotifyOperationMessage = $"Spotify playlist could not be queued: {ex.Message}";
        }
        finally
        {
            if (!wasBusy)
            {
                IsSpotifyBusy = false;
            }
        }

        if (added > 0)
        {
            SpotifyOperationMessage = playlists.Count == 1
                ? $"Queued {added} track(s) from {playlists[0].Name} on {NormalizeDeckName(deckName)}."
                : $"Queued {added} track(s) from {playlists.Count} playlist(s) on {NormalizeDeckName(deckName)}.";
            QueueSessionStateSave();
        }

        return added;
    }

    private async Task<int> QueueLocalPlaylistsToDeckAsync(IReadOnlyList<LocalMusicPlaylist> playlists, string deckName, int? beforeItemId = null)
    {
        var added = 0;
        foreach (var playlist in playlists)
        {
            var tracks = await LoadLocalPlaylistTracksAsync(playlist);
            if (tracks.Count == 0)
            {
                continue;
            }

            added += await QueueLocalTracksToDeckAsync(tracks, deckName, beforeItemId);
        }

        if (added > 0)
        {
            LocalPlaylistStatus = playlists.Count == 1
                ? $"Queued {added} song(s) from {playlists[0].Name} on {NormalizeDeckName(deckName)}."
                : $"Queued {added} song(s) from {playlists.Count} local playlist(s) on {NormalizeDeckName(deckName)}.";
            SpotifyOperationMessage = LocalPlaylistStatus;
            QueueSessionStateSave();
        }
        else if (playlists.Count > 0)
        {
            LocalPlaylistStatus = "Those local playlist songs are not available in the selected folder anymore.";
            SpotifyOperationMessage = LocalPlaylistStatus;
        }

        return added;
    }

    private async Task QueueSelectedSourceToDeckAsync(string deckName, int? insertBeforeItemId = null)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);

        if (ActiveSource == SourceLocal)
        {
            if (SelectedLocalMusicTrack is null)
            {
                SpotifyOperationMessage = "Select a local music file first.";
                return;
            }

            await QueueLocalTrackToDeckAsync(SelectedLocalMusicTrack, normalizedDeckName, beforeItemId: insertBeforeItemId);
            return;
        }

        var track = SelectedSpotifyTrack ?? SelectedSpotifySearchTrack ?? SelectedImportedSpotifyTrack;
        if (track is not null)
        {
            await QueueSpotifyTrackToDeckAsync(track, normalizedDeckName, beforeItemId: insertBeforeItemId);
            return;
        }

        if (ActiveSource is SourceYouTube or SourceTidal)
        {
            SpotifyOperationMessage = $"{ActiveSource} source setup is visible, but queueing from that provider is not implemented yet.";
            return;
        }

        await Task.CompletedTask;
        SpotifyOperationMessage = "Select a playlist track, search result, imported track, or local file first.";
    }

    private async Task AddLoadedPlaylistToActiveDeckAsync()
    {
        if (ActiveSource != SourceSpotify)
        {
            SpotifyOperationMessage = "Switch to Spotify playlists and load a playlist window before adding loaded tracks.";
            return;
        }

        var tracks = GetLoadedPlayableSpotifyPreviewTracks();
        if (tracks.Count == 0)
        {
            SpotifyOperationMessage = "Load a Spotify playlist into the playlist tracks window before adding it to the selected deck.";
            return;
        }

        var normalizedDeckName = NormalizeDeckName(ActiveDeckName);
        var added = await QueueSpotifyTracksToDeckAsync(tracks, normalizedDeckName);
        SpotifyOperationMessage = added > 0
            ? $"Added {added} loaded playlist track(s) to {normalizedDeckName}."
            : "No playable loaded playlist tracks were added.";
    }

    private async Task RandomizeActiveSourceToDeckAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);

        if (ActiveSource == SourceLocal)
        {
            var tracks = LocalMusicTracks.Count > 0
                ? LocalMusicTracks.ToList()
                : _allLocalMusicTracks.ToList();
            if (tracks.Count > 0)
            {
                await RandomizeLocalTracksToDeckAsync(tracks, normalizedDeckName);
            }
            else if (!TryRandomizeDeckQueue(normalizedDeckName))
            {
                SpotifyOperationMessage = "Scan local music or add at least two songs to the deck before randomizing.";
            }

            return;
        }

        if (ActiveSource is SourceYouTube or SourceTidal)
        {
            if (!TryRandomizeDeckQueue(normalizedDeckName))
            {
                SpotifyOperationMessage = $"{ActiveSource} randomizer will be enabled when that provider connector can return playable tracks.";
            }

            return;
        }

        var loadedTracks = SpotifyPreviewTracks.ToList();
        if (loadedTracks.Count > 0)
        {
            await RandomizeSpotifyPlaylistToDeckAsync(loadedTracks, normalizedDeckName);
            return;
        }

        if (TryRandomizeDeckQueue(normalizedDeckName))
        {
            return;
        }

        SpotifyOperationMessage = "Load a Spotify playlist into the playlist tracks window, or add at least two songs to the deck before randomizing.";
    }

    private async Task RandomizeSpotifyPlaylistToDeckAsync(IReadOnlyList<SpotifyTrackMetadata> tracks, string deckName)
    {
        var randomizedTracks = Shuffle(tracks);
        var playableTracks = randomizedTracks
            .Where(IsPlayableSpotifyTrack)
            .Take(24)
            .ToList();
        if (playableTracks.Count == 0)
        {
            SpotifyOperationMessage = "The loaded playlist was randomized, but no playable tracks were available for the deck.";
            ReplaceCollection(SpotifyPreviewTracks, randomizedTracks);
            SelectedSpotifyTrack = SpotifyPreviewTracks.FirstOrDefault();
            QueueSessionStateSave();
            return;
        }

        ReplaceCollection(SpotifyPreviewTracks, randomizedTracks);
        SelectedSpotifyTrack = SpotifyPreviewTracks.FirstOrDefault();

        PrepareDeckForRandomizedLoad(deckName);
        var added = await QueueSpotifyTracksToDeckAsync(playableTracks, deckName);
        SpotifyOperationMessage = added > 0
            ? $"Randomized the loaded playlist and loaded {added} track(s) onto {NormalizeDeckName(deckName)}."
            : "The loaded playlist was randomized, but no playable tracks were added.";
        CacheDeckAlbumArtForQueue(deckName);
        QueueSessionStateSave();
    }

    private List<SpotifyTrackMetadata> GetLoadedPlayableSpotifyPreviewTracks() =>
        SpotifyPreviewTracks
            .Where(IsPlayableSpotifyTrack)
            .ToList();

    private static bool IsPlayableSpotifyTrack(SpotifyTrackMetadata track) =>
        !track.IsUnavailable && !string.IsNullOrWhiteSpace(track.SpotifyUri);

    private async Task RandomizeLocalTracksToDeckAsync(IReadOnlyList<LocalMusicTrack> tracks, string deckName)
    {
        if (tracks.Count == 0)
        {
            SpotifyOperationMessage = "Scan local music before randomizing local tracks.";
            return;
        }

        var randomizedTracks = Shuffle(tracks);
        if (LocalMusicTracks.Count > 0)
        {
            ReplaceCollection(LocalMusicTracks, randomizedTracks);
            SelectedLocalMusicTrack = LocalMusicTracks.FirstOrDefault();
        }

        var deckTracks = randomizedTracks.Take(24).ToList();
        PrepareDeckForRandomizedLoad(deckName);
        var added = await QueueLocalTracksToDeckAsync(deckTracks, deckName);
        SpotifyOperationMessage = $"Randomized {added} local track(s) onto {NormalizeDeckName(deckName)}.";
        CacheDeckAlbumArtForQueue(deckName);
        QueueSessionStateSave();
    }

    private bool TryRandomizeDeckQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        if (queue.Count < 2)
        {
            return false;
        }

        var playingItem = ResolveLoadedDeckItem(normalizedDeckName);
        var shuffledItems = Shuffle(queue.Where(item => playingItem is null || item.Id != playingItem.Id));
        if (shuffledItems.Count < 2)
        {
            return false;
        }

        var selectedId = _selectedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName);
        queue.Clear();
        if (playingItem is not null)
        {
            queue.Add(playingItem);
        }

        queue.AddRange(shuffledItems);
        RenumberQueue(queue, normalizedDeckName);
        _selectedDeckQueueItemIds[normalizedDeckName] =
            selectedId is not null && queue.Any(item => item.Id == selectedId.Value)
                ? selectedId
                : queue.FirstOrDefault(item => playingItem is null || item.Id != playingItem.Id)?.Id
                    ?? queue.FirstOrDefault()?.Id;

        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        RefreshDeckDisplayProperties();
        CacheDeckAlbumArtForQueue(normalizedDeckName);
        SpotifyOperationMessage = $"Randomized {shuffledItems.Count} queued track(s) on {normalizedDeckName}.";
        QueueSessionStateSave();
        return true;
    }

    private void PrepareDeckForRandomizedLoad(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var playingItem = ResolveLoadedDeckItem(normalizedDeckName);

        queue.Clear();
        if (playingItem is not null)
        {
            queue.Add(playingItem);
        }

        RenumberQueue(queue, normalizedDeckName);
        _selectedDeckQueueItemIds[normalizedDeckName] = playingItem?.Id;
        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
    }

    private void QueueToDeck(
        DancePilotQueueItem item,
        string deckName,
        bool announce = true,
        bool selectQueuedItem = true,
        int? beforeItemId = null)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var queuedItem = item with { DeckName = normalizedDeckName };

        if (beforeItemId is int insertBeforeId)
        {
            var insertIndex = queue.FindIndex(existing => existing.Id == insertBeforeId);
            if (insertIndex < 0)
            {
                queue.Add(queuedItem);
            }
            else
            {
                queue.Insert(insertIndex, queuedItem);
            }
        }
        else
        {
            queue.Add(queuedItem);
        }

        RenumberQueue(queue, normalizedDeckName);
        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        if (selectQueuedItem)
        {
            SelectedActiveDeckQueueItem = ActiveDeckQueue.FirstOrDefault(queueItem => queueItem.Id == queuedItem.Id)
                ?? queuedItem;
        }

        UpdateNextUpFromDecks();
        if (announce)
        {
            SpotifyOperationMessage = $"Queued on {normalizedDeckName}: {queuedItem.Title}. Select it for transition, or double-click the deck row to start it now.";
        }

        var isVisibleDisplayItem = IsDeckDisplayItem(normalizedDeckName, queuedItem.Id);
        if (isVisibleDisplayItem)
        {
            RefreshDeckDisplayProperties();
        }

        if (isVisibleDisplayItem || IsDeckNextPlayableItem(normalizedDeckName, queuedItem.Id))
        {
            DispatchAsync(() => CacheQueuedAlbumArtAsync(normalizedDeckName, queuedItem.Id));
        }

        QueueSessionStateSave();
    }

    private async Task CacheQueuedAlbumArtAsync(string deckName, int itemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var queueIndex = queue.FindIndex(item => item.Id == itemId);
        if (queueIndex < 0)
        {
            return;
        }

        var item = queue[queueIndex];
        item = await ResolveQueueItemAlbumArtAsync(item);
        queueIndex = queue.FindIndex(existing => existing.Id == itemId);
        if (queueIndex < 0)
        {
            return;
        }

        if (!string.Equals(queue[queueIndex].AlbumArtUrl, item.AlbumArtUrl, StringComparison.OrdinalIgnoreCase))
        {
            queue[queueIndex] = item;
            StartupLog.Write($"Album art resolved for {normalizedDeckName} item {itemId}: {item.Title} -> {item.AlbumArtUrl}");
            RefreshDeckAfterAlbumArtUpdate(normalizedDeckName, itemId);
            QueueSessionStateSave();
        }

        var itemForCache = queue[queueIndex];
        var cachedAlbumArtUri = await _albumArtCacheService.CacheAlbumArtAsync(itemForCache);
        if (string.IsNullOrWhiteSpace(cachedAlbumArtUri))
        {
            StartupLog.Write($"Album art cache returned no image for {normalizedDeckName} item {itemId}: {itemForCache.Title}");
            return;
        }

        queueIndex = queue.FindIndex(existing => existing.Id == itemId);
        if (queueIndex < 0
            || string.Equals(queue[queueIndex].AlbumArtUrl, cachedAlbumArtUri, StringComparison.OrdinalIgnoreCase)
            || !ShouldReplaceQueueAlbumArtWithCachedUri(queue[queueIndex]))
        {
            return;
        }

        queue[queueIndex] = queue[queueIndex] with { AlbumArtUrl = cachedAlbumArtUri };
        StartupLog.Write($"Album art cached for {normalizedDeckName} item {itemId}: {queue[queueIndex].Title} -> {cachedAlbumArtUri}");
        RefreshDeckAfterAlbumArtUpdate(normalizedDeckName, itemId);
        QueueSessionStateSave();
    }

    private async Task<DancePilotQueueItem> ResolveQueueItemAlbumArtAsync(DancePilotQueueItem item)
    {
        if (HasUsableAlbumArtSource(item.AlbumArtUrl))
        {
            return item;
        }

        if (item.Source == SongSources.Spotify)
        {
            var spotifyItem = await ResolveSpotifyQueueItemAlbumArtAsync(item);
            if (HasUsableAlbumArtSource(spotifyItem.AlbumArtUrl))
            {
                return spotifyItem;
            }

            var externalItem = await ResolveExternalQueueItemAlbumArtAsync(spotifyItem);
            return HasUsableAlbumArtSource(externalItem.AlbumArtUrl)
                ? externalItem
                : spotifyItem;
        }

        if (item.Source == SongSources.Local)
        {
            var localTrack = _allLocalMusicTracks.FirstOrDefault(track =>
                string.Equals(track.FilePath, item.ExternalUri, StringComparison.OrdinalIgnoreCase));
            if (HasUsableAlbumArtSource(localTrack?.AlbumArtUrl))
            {
                return item with { AlbumArtUrl = localTrack!.AlbumArtUrl };
            }

            return await ResolveExternalQueueItemAlbumArtAsync(item);
        }

        return await ResolveExternalQueueItemAlbumArtAsync(item);
    }

    private async Task<DancePilotQueueItem> ResolveSpotifyQueueItemAlbumArtAsync(DancePilotQueueItem item)
    {
        if (item.Source != SongSources.Spotify || HasUsableAlbumArtSource(item.AlbumArtUrl))
        {
            return item;
        }

        var inMemoryTrack = FindSpotifyTrackByUri(item.ExternalUri);
        if (HasUsableAlbumArtSource(inMemoryTrack?.AlbumArtUrl))
        {
            return item with { AlbumArtUrl = inMemoryTrack!.AlbumArtUrl };
        }

        var trackId = ExtractSpotifyTrackId(item.ExternalUri);
        var importedTrack = await FindImportedSpotifyTrackByUriAsync(item.ExternalUri, trackId);
        if (HasUsableAlbumArtSource(importedTrack?.AlbumArtUrl))
        {
            return item with { AlbumArtUrl = importedTrack!.AlbumArtUrl };
        }

        return item;
    }

    private async Task<SpotifyTrackMetadata?> FindImportedSpotifyTrackByUriAsync(string spotifyUri, string spotifyTrackId)
    {
        try
        {
            return await _spotifyLibraryRepository.GetImportedTrackByUriAsync(spotifyUri, spotifyTrackId);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Imported Spotify album art lookup skipped for {spotifyUri}: {ex.Message}");
            return null;
        }
    }

    private async Task<DancePilotQueueItem> ResolveExternalQueueItemAlbumArtAsync(DancePilotQueueItem item)
    {
        try
        {
            var albumArtUrl = await _externalAlbumArtLookupService.FindAlbumArtAsync(item.Title, item.Artist);
            if (HasUsableAlbumArtSource(albumArtUrl))
            {
                StartupLog.Write($"External album art resolved for {item.Title} - {item.Artist}: {albumArtUrl}");
            }

            return HasUsableAlbumArtSource(albumArtUrl)
                ? item with { AlbumArtUrl = albumArtUrl }
                : item;
        }
        catch (Exception ex)
        {
            StartupLog.Write($"External album art lookup skipped for {item.Title}: {ex.Message}");
            return item;
        }
    }

    private static bool ShouldReplaceQueueAlbumArtWithCachedUri(DancePilotQueueItem _) =>
        true;

    private void RefreshDeckAfterAlbumArtUpdate(string deckName, int itemId)
    {
        if (IsDeckDisplayItem(deckName, itemId))
        {
            RefreshActiveDeckQueue();
            RefreshDeckDisplayProperties();
        }
    }

    private void CacheDeckAlbumArtForCurrentQueues()
    {
        CacheDeckAlbumArtForQueue("Deck A");
        CacheDeckAlbumArtForQueue("Deck B");
    }

    private async Task HydrateRestoredDeckAlbumArtAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var displayItemId = ResolveDeckDisplayItem(normalizedDeckName)?.Id;
        if (displayItemId is null)
        {
            return;
        }

        await CacheQueuedAlbumArtAsync(normalizedDeckName, displayItemId.Value);
    }

    private void CacheDeckAlbumArtForQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var displayItemId = ResolveDeckDisplayItem(normalizedDeckName)?.Id;
        if (displayItemId is not null)
        {
            DispatchAsync(() => CacheQueuedAlbumArtAsync(normalizedDeckName, displayItemId.Value));
        }
    }

    private List<DancePilotQueueItem> ActiveQueueList() => QueueForDeck(ActiveDeckName);

    private List<DancePilotQueueItem> QueueViewQueueList() => QueueForDeck(QueueViewDeckName);

    private static string NormalizeDeckName(string deckName) =>
        string.Equals(deckName, "Deck B", StringComparison.OrdinalIgnoreCase) ? "Deck B" : "Deck A";

    private static List<T> Shuffle<T>(IEnumerable<T> items)
    {
        var shuffled = items.ToList();
        if (shuffled.Count < 2)
        {
            return shuffled;
        }

        var original = shuffled.ToList();
        for (var index = shuffled.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        if (shuffled.SequenceEqual(original))
        {
            var first = shuffled[0];
            shuffled.RemoveAt(0);
            shuffled.Add(first);
        }

        return shuffled;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private int NextDeckQueueId() => _nextDeckQueueItemId++;

    public void MoveQueueItemsToDeck(IEnumerable<DancePilotQueueItem> items, string targetDeckName, int? beforeItemId = null)
    {
        var normalizedTargetDeckName = NormalizeDeckName(targetDeckName);
        var itemIds = items
            .Select(item => item.Id)
            .Distinct()
            .ToList();
        if (itemIds.Count == 0)
        {
            return;
        }

        var moving = _deckAQueue
            .Concat(_deckBQueue)
            .Where(item => itemIds.Contains(item.Id))
            .OrderBy(item => item.DeckName)
            .ThenBy(item => item.QueuePosition)
            .ToList();
        if (moving.Count == 0)
        {
            return;
        }

        _deckAQueue.RemoveAll(item => itemIds.Contains(item.Id));
        _deckBQueue.RemoveAll(item => itemIds.Contains(item.Id));

        var targetQueue = QueueForDeck(normalizedTargetDeckName);
        var insertIndex = beforeItemId is null
            ? targetQueue.Count
            : targetQueue.FindIndex(item => item.Id == beforeItemId.Value);
        if (insertIndex < 0)
        {
            insertIndex = targetQueue.Count;
        }

        targetQueue.InsertRange(insertIndex, moving.Select(item => item with { DeckName = normalizedTargetDeckName }));
        RenumberQueue(_deckAQueue, "Deck A");
        RenumberQueue(_deckBQueue, "Deck B");

        ActiveDeckName = normalizedTargetDeckName;
        RefreshActiveDeckQueue();
        SpotifyOperationMessage = moving.Count == 1
            ? $"Moved {moving[0].Title} on {normalizedTargetDeckName}."
            : $"Moved {moving.Count} queued songs on {normalizedTargetDeckName}.";
        QueueSessionStateSave();
    }

    public void RemoveQueueItem(DancePilotQueueItem item)
    {
        var deckName = NormalizeDeckName(item.DeckName);
        var queue = QueueForDeck(deckName);
        var removed = queue.RemoveAll(existing => existing.Id == item.Id);
        if (removed == 0)
        {
            SpotifyOperationMessage = $"{item.Title} is no longer in the queue.";
            return;
        }

        RenumberQueue(queue, deckName);
        if (_selectedDeckQueueItemIds.GetValueOrDefault(deckName) == item.Id)
        {
            _selectedDeckQueueItemIds[deckName] = null;
        }

        ActiveDeckName = deckName;
        RefreshActiveDeckQueue();
        SpotifyOperationMessage = $"Removed {item.Title} from {deckName}.";
        QueueSessionStateSave();
    }

    private void RemovePlayedQueueItem(string deckName, int itemId)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var removed = queue.RemoveAll(existing => existing.Id == itemId);
        if (removed == 0)
        {
            return;
        }

        RenumberQueue(queue, normalizedDeckName);
        if (_selectedDeckQueueItemIds.GetValueOrDefault(normalizedDeckName) == itemId)
        {
            _selectedDeckQueueItemIds[normalizedDeckName] = queue.FirstOrDefault(item => item.Id == _playingDeckQueueItemId)?.Id
                ?? queue.FirstOrDefault()?.Id;
        }

        RefreshActiveDeckQueue();
        QueueSessionStateSave();
    }

    public void ClearDeckQueue(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        var queue = QueueForDeck(normalizedDeckName);
        var removedCount = queue.Count;
        if (removedCount == 0)
        {
            ActiveDeckName = normalizedDeckName;
            RefreshActiveDeckQueue();
            SpotifyOperationMessage = $"{normalizedDeckName} queue is already empty.";
            QueueSessionStateSave();
            return;
        }

        queue.Clear();
        _selectedDeckQueueItemIds[normalizedDeckName] = null;
        ActiveDeckName = normalizedDeckName;
        RefreshActiveDeckQueue();
        SpotifyOperationMessage = $"Cleared {removedCount} song(s) from {normalizedDeckName}. Add different tracks to this deck when ready.";
        QueueSessionStateSave();
    }

    private static void RenumberQueue(List<DancePilotQueueItem> queue, string deckName)
    {
        for (var index = 0; index < queue.Count; index++)
        {
            queue[index] = queue[index] with { DeckName = deckName, QueuePosition = index + 1 };
        }
    }

    private void RefreshActiveDeckQueue()
    {
        RefreshDeckQueueCollection(DeckAQueueItems, _deckAQueue);
        RefreshDeckQueueCollection(DeckBQueueItems, _deckBQueue);
        ActiveDeckQueue.Clear();
        var position = 1;
        foreach (var item in ActiveQueueList())
        {
            ActiveDeckQueue.Add(item with { DeckName = ActiveDeckName, QueuePosition = position++ });
        }

        _selectedDeckQueueItemIds.TryGetValue(ActiveDeckName, out var selectedId);
        SelectedActiveDeckQueueItem = selectedId is not null
            ? ActiveDeckQueue.FirstOrDefault(item => item.Id == selectedId)
            : null;
        RefreshQueueViewDeckQueue();
        UpdateNextUpFromDecks();
    }

    private void RefreshQueueViewDeckQueue()
    {
        QueueViewDeckQueue.Clear();
        var position = 1;
        foreach (var item in QueueViewQueueList().OrderBy(item => item.QueuePosition))
        {
            QueueViewDeckQueue.Add(item with { DeckName = QueueViewDeckName, QueuePosition = position++ });
        }

        _selectedDeckQueueItemIds.TryGetValue(QueueViewDeckName, out var selectedId);
        SelectedQueueViewDeckQueueItem = selectedId is not null
            ? QueueViewDeckQueue.FirstOrDefault(item => item.Id == selectedId)
            : null;
        OnPropertyChanged(nameof(QueueViewDeckCountDisplay));
    }

    private static void RefreshDeckQueueCollection(
        ObservableCollection<DancePilotQueueItem> target,
        IReadOnlyList<DancePilotQueueItem> source)
    {
        target.Clear();
        foreach (var item in source.OrderBy(item => item.QueuePosition))
        {
            target.Add(item);
        }
    }

    private async Task RefreshSpotifyDevicesCoreAsync()
    {
        var devices = await _spotifyDeviceManager.GetAvailableDevicesAsync(CurrentSpotifySettings);
        StartupLog.Write("Spotify devices: " + string.Join("; ", devices.Select(device =>
            $"{device.Name}/{device.Type}/active={device.IsActive}/restricted={device.IsRestricted}/volume={device.SupportsVolume}")));
        SpotifyDevices.Clear();
        foreach (var device in devices.OrderByDescending(device => device.IsActive).ThenBy(device => device.Name))
        {
            SpotifyDevices.Add(device);
        }

        var saved = await _playbackSettingsRepository.LoadAsync();
        SelectedSpotifyDevice = SpotifyDevices.FirstOrDefault(device => device.Id == saved.SelectedDeviceId && !device.IsRestricted)
            ?? SpotifyDevices.FirstOrDefault(device => device.IsActive && !device.IsRestricted)
            ?? SpotifyDevices.FirstOrDefault(device => !device.IsRestricted)
            ?? SpotifyDevices.FirstOrDefault();

        CurrentOutputStatus = SpotifyDevices.Count == 0
            ? "No Spotify devices found. Open Spotify on a phone, browser, or desktop app, then refresh."
            : $"Found {SpotifyDevices.Count} Spotify device(s).";
        SpotifyOperationMessage = CurrentOutputStatus;
    }

    private async Task RefreshPlaybackCoreAsync(bool runAutopilot)
    {
        var state = await _spotifyPlayerService.GetPlaybackStateAsync(CurrentSpotifySettings);
        ApplyPlaybackState(state);
        await RefreshQueueCoreAsync();

        if (runAutopilot)
        {
            var transitioned = await MaybeTransitionDeckAsync(state);
            if (transitioned)
            {
                SpotifyOperationMessage = $"Deck transition started the next track on {ActiveDeckName}.";
            }
        }
    }

    private async Task RefreshPlaybackAndAutopilotAsync()
    {
        if (_isPlaybackRefreshRunning || IsSpotifyBusy)
        {
            return;
        }

        try
        {
            _isPlaybackRefreshRunning = true;
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                await RefreshLocalPlaybackCoreAsync(runAutopilot: true);
                return;
            }

            if (!await _spotifyService.IsConnectedAsync())
            {
                return;
            }

            if (!await HasPlaybackScopesAsync())
            {
                return;
            }

            await RefreshPlaybackCoreAsync(runAutopilot: true);
        }
        catch (SpotifyApiException ex)
        {
            CurrentOutputStatus = ToFriendlySpotifyMessage(ex);
        }
        catch
        {
            CurrentOutputStatus = "Spotify playback state is temporarily unavailable.";
        }
        finally
        {
            _isPlaybackRefreshRunning = false;
        }
    }

    private async Task HandleLocalMediaEndedAsync()
    {
        var completedDeckName = _playingDeckName;
        var completedItemId = _playingDeckQueueItemId;
        IsPlaybackPlaying = false;
        SpotifyPlaybackStatus = "Local ended";
        SpotifyProgressDisplay = SelectedLocalMusicTrack?.Duration is TimeSpan duration
            ? $"{duration:m\\:ss} / {duration:m\\:ss}"
            : SpotifyProgressDisplay;
        SpotifyTimeRemaining = "0:00";
        if (completedItemId is not null)
        {
            MarkDeckItemCompleted(completedDeckName, completedItemId.Value);
        }
        else
        {
            RefreshDeckDisplayProperties();
        }

        if (!DeckTransitionEnabled || _isTransitionAdvanceRunning || completedItemId is null)
        {
            if (completedItemId is not null)
            {
                ClearEndedPlayingDeckItemIfCurrent(completedDeckName, completedItemId.Value);
            }

            return;
        }

        var transitioned = await TryStartTransitionTargetAsync(skipFadeOut: true);
        if (!transitioned)
        {
            ClearEndedPlayingDeckItemIfCurrent(completedDeckName, completedItemId.Value);
        }

        SpotifyOperationMessage = transitioned
            ? $"Deck transition started the next track on {ActiveDeckName}."
            : RemovePlayedQueueItems
                ? "Local song ended. No next queued song is available on this deck."
                : "Local song ended. Queue loop is ready, but no playable track was found.";
    }

    private async Task RefreshLocalPlaybackCoreAsync(bool runAutopilot)
    {
        var session = _localMediaPlayer.PlaybackSession;
        var duration = session.NaturalDuration > TimeSpan.Zero
            ? session.NaturalDuration
            : SelectedLocalMusicTrack?.Duration;
        var position = session.Position < TimeSpan.Zero ? TimeSpan.Zero : session.Position;

        IsPlaybackPlaying = session.PlaybackState is MediaPlaybackState.Playing
            or MediaPlaybackState.Buffering
            or MediaPlaybackState.Opening;

        if (duration is TimeSpan durationValue && durationValue > TimeSpan.Zero)
        {
            SeekPositionMaximumSeconds = durationValue.TotalSeconds;
            if (!_isSeekPositionChanging)
            {
                SeekPositionSeconds = Math.Clamp(position.TotalSeconds, 0, SeekPositionMaximumSeconds);
            }

            SpotifyProgressDisplay = $"{position:m\\:ss} / {durationValue:m\\:ss}";
            SpotifyTimeRemaining = $"{(durationValue - position > TimeSpan.Zero ? durationValue - position : TimeSpan.Zero):m\\:ss}";
        }
        else
        {
            if (!_isSeekPositionChanging)
            {
                SeekPositionSeconds = Math.Max(0, position.TotalSeconds);
            }

            SpotifyProgressDisplay = $"{position:m\\:ss} / --:--";
            SpotifyTimeRemaining = "--:--";
        }

        RefreshDeckDisplayProperties();

        if (!runAutopilot
            || !DeckTransitionEnabled
            || _isTransitionAdvanceRunning
            || _playingDeckQueueItemId is null
            || duration is not TimeSpan knownDuration
            || knownDuration <= TimeSpan.Zero)
        {
            return;
        }

        var remainingSeconds = Math.Max(0, (knownDuration - position).TotalSeconds);
        var transitionTriggerSeconds = AlwaysFadeSongs && StartTransitionOnFade
            ? FadeOutSeconds + TransitionOverlapSeconds
            : TransitionOverlapSeconds;
        var isNearEnd = remainingSeconds <= transitionTriggerSeconds;
        var hasEnded = session.PlaybackState is MediaPlaybackState.None or MediaPlaybackState.Paused
            && remainingSeconds <= LateTransitionSkipFadeSeconds;

        if (isNearEnd || hasEnded)
        {
            var transitioned = await TryStartTransitionTargetAsync(
                skipFadeOut: hasEnded || remainingSeconds <= LateTransitionSkipFadeSeconds,
                fadeOutSeconds: CreateTransitionFadeOutDuration(remainingSeconds));
            if (transitioned)
            {
                SpotifyOperationMessage = $"Deck transition started the next track on {ActiveDeckName}.";
            }
        }
    }

    private void ApplyPlaybackState(SpotifyPlaybackState? state)
    {
        if (state is null)
        {
            SpotifyPlaybackStatus = "No active playback";
            IsPlaybackPlaying = false;
            SpotifyNowPlayingTitle = "Nothing playing";
            SpotifyNowPlayingArtist = "Open Spotify on a device, then refresh devices.";
            SpotifyTimeRemaining = "--:--";
            SpotifyProgressDisplay = "--:-- / --:--";
            if (ResolveLoadedDeckItem(_playingDeckName) is null)
            {
                _currentPlaybackAlbumArtUrl = null;
            }

            RefreshDeckDisplayProperties();
            CurrentOutputStatus = "No active Spotify playback device.";
            ResetSeekPositionRange();
            return;
        }

        SpotifyPlaybackStatus = state.PlaybackStatus;
        IsPlaybackPlaying = state.IsPlaying;
        SpotifyNowPlayingTitle = state.TrackTitle;
        SpotifyNowPlayingArtist = state.TrackArtist;
        if (!string.IsNullOrWhiteSpace(state.Track?.AlbumArtUrl))
        {
            _currentPlaybackAlbumArtUrl = state.Track.AlbumArtUrl;
            ApplyPlaybackAlbumArtToLoadedQueueItem(state.Track);
        }

        RefreshDeckDisplayProperties();
        SpotifyTimeRemaining = state.TimeRemainingDisplay;
        SpotifyProgressDisplay = state.ProgressDisplay;
        CurrentOutputStatus = state.Device is null
            ? "Spotify playback active, but no output device was returned."
            : $"{state.PlaybackStatus} on {state.Device.DisplayName}";

        if (state.IsPlaying
            && SpotifyOperationMessage.StartsWith("Spotify refused that playback command", StringComparison.OrdinalIgnoreCase))
        {
            SpotifyOperationMessage = state.Device is null
                ? "Spotify is playing. DancePilot can see playback state from your account."
                : $"Spotify is playing on {state.Device.DisplayName}.";
        }

        UpdateSeekPositionMaximum(state.DurationMs);

        if (state.ProgressMs is not null && !_isSeekPositionChanging)
        {
            SeekPositionSeconds = Math.Max(0, state.ProgressMs.Value / 1000d);
        }

        if (state.Device is not null)
        {
            SelectedOutputDeviceName = state.Device.DisplayName;
            SelectedSpotifyDevice = SpotifyDevices.FirstOrDefault(device => device.Id == state.Device.Id) ?? SelectedSpotifyDevice;
        }

        if (!string.IsNullOrWhiteSpace(state.ContextUri)
            && state.ContextUri.StartsWith("spotify:playlist:", StringComparison.OrdinalIgnoreCase))
        {
            var playlistId = state.ContextUri.Split(':').Last();
            CurrentSpotifyPlaylistName = ImportedSpotifyPlaylists.FirstOrDefault(playlist => playlist.SpotifyPlaylistId == playlistId)?.Name
                ?? state.ContextUri;
        }
    }

    private void ApplyPlaybackAlbumArtToLoadedQueueItem(SpotifyTrackMetadata track)
    {
        if (!HasUsableAlbumArtSource(track.AlbumArtUrl)
            || _playingDeckQueueItemId is null)
        {
            return;
        }

        var normalizedDeckName = NormalizeDeckName(_playingDeckName);
        var queue = QueueForDeck(normalizedDeckName);
        var queueIndex = queue.FindIndex(item => item.Id == _playingDeckQueueItemId);
        if (queueIndex < 0)
        {
            return;
        }

        var item = queue[queueIndex];
        if (item.Source != SongSources.Spotify
            || !PlaybackTrackMatchesQueueItem(track, item)
            || string.Equals(item.AlbumArtUrl, track.AlbumArtUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        queue[queueIndex] = item with { AlbumArtUrl = track.AlbumArtUrl };
        RefreshActiveDeckQueue();
        QueueSessionStateSave();
    }

    private static bool PlaybackTrackMatchesQueueItem(SpotifyTrackMetadata track, DancePilotQueueItem item)
    {
        if (!string.IsNullOrWhiteSpace(track.SpotifyUri)
            && string.Equals(track.SpotifyUri, item.ExternalUri, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trackId = ExtractSpotifyTrackId(item.ExternalUri);
        if (!string.IsNullOrWhiteSpace(trackId)
            && string.Equals(trackId, track.SpotifyTrackId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(track.Title, item.Title, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(item.Artist)
                || string.Equals(track.Artist, item.Artist, StringComparison.OrdinalIgnoreCase));
    }

    private void ResetSeekPositionRange()
    {
        SeekPositionMaximumSeconds = 1;
        if (!_isSeekPositionChanging)
        {
            SeekPositionSeconds = 0;
        }
    }

    private void UpdateSeekPositionMaximum(int? durationMs)
    {
        if (durationMs is int value && value > 0)
        {
            SeekPositionMaximumSeconds = value / 1000d;
        }
    }

    private async Task RefreshPlaybackCollectionsAsync(bool loadImportedTracks = true)
    {
        await LoadImportedSpotifyPlaylistsCoreAsync(loadTracksForSelection: loadImportedTracks);
        await RefreshQueueCoreAsync();
    }

    private async Task LoadImportedSpotifyPlaylistsCoreAsync(bool loadTracksForSelection = true)
    {
        var playlists = await _spotifyLibraryRepository.GetImportedPlaylistsAsync();
        ImportedSpotifyPlaylists.Clear();
        foreach (var playlist in playlists)
        {
            ImportedSpotifyPlaylists.Add(playlist);
        }

        SelectedImportedSpotifyPlaylist = ImportedSpotifyPlaylists.FirstOrDefault();
        if (loadTracksForSelection && SelectedImportedSpotifyPlaylist is not null)
        {
            await LoadImportedPlaylistTracksCoreAsync();
        }
        else
        {
            ImportedSpotifyTracks.Clear();
            SelectedImportedSpotifyTrack = null;
        }
    }

    private async Task LoadImportedPlaylistTracksCoreAsync()
    {
        ImportedSpotifyTracks.Clear();
        if (SelectedImportedSpotifyPlaylist is null)
        {
            return;
        }

        var tracks = await _spotifyLibraryRepository.GetImportedPlaylistTracksAsync(SelectedImportedSpotifyPlaylist.SpotifyPlaylistId);
        foreach (var track in tracks)
        {
            ImportedSpotifyTracks.Add(track);
        }

        SelectedImportedSpotifyTrack = ImportedSpotifyTracks.FirstOrDefault();
    }

    private async Task RefreshQueueCoreAsync()
    {
        var queuedItems = await _playbackCoordinator.GetPendingQueueAsync();
        DancePilotQueue.Clear();
        foreach (var item in queuedItems)
        {
            DancePilotQueue.Add(item);
        }

        var next = DancePilotQueue.FirstOrDefault();
        NextUpTitle = next?.Title ?? "No queued recommendation";
        NextUpArtist = next?.Artist ?? "DancePilot queue";
    }

    private async Task<bool> TryExternalHandoffTrackAsync(SpotifyTrackMetadata track)
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SpotifyOperationMessage = "Local Music Files mode plays files from this PC. Select a local file and use PLAY FILE, or switch to Spotify Web API / Spotify Connect for Spotify tracks.";
            return true;
        }

        if (SelectedPlaybackMode != SpotifyPlaybackModes.ExternalSpotifyAppHandoff)
        {
            return false;
        }

        if (!HasSpotifyTrackLink(track))
        {
            SpotifyOperationMessage = "This Spotify track does not have a Spotify link to open.";
            return true;
        }

        await OpenSpotifyTrackLinkAsync(track);
        SpotifyOperationMessage = $"Opened {track.Title} in Spotify.";
        return true;
    }

    private async Task<bool> TryExternalHandoffPlaylistAsync(SpotifyPlaylistSummary playlist)
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SpotifyOperationMessage = "Local Music Files mode plays files from this PC. Switch to Spotify Web API / Spotify Connect or Open in Spotify App for Spotify playlists.";
            return true;
        }

        if (SelectedPlaybackMode != SpotifyPlaybackModes.ExternalSpotifyAppHandoff)
        {
            return false;
        }

        if (!HasSpotifyPlaylistLink(playlist))
        {
            SpotifyOperationMessage = "This Spotify playlist does not have a Spotify link to open.";
            return true;
        }

        await OpenSpotifyPlaylistLinkAsync(playlist);
        SpotifyOperationMessage = $"Opened {playlist.Name} in Spotify.";
        return true;
    }

    private static bool HasSpotifyTrackLink(SpotifyTrackMetadata track) =>
        !string.IsNullOrWhiteSpace(track.SpotifyUri)
        || !string.IsNullOrWhiteSpace(track.SpotifyTrackId)
        || !string.IsNullOrWhiteSpace(track.ExternalUrl);

    private static bool HasSpotifyPlaylistLink(SpotifyPlaylistSummary playlist) =>
        !string.IsNullOrWhiteSpace(playlist.SpotifyPlaylistId)
        || !string.IsNullOrWhiteSpace(playlist.ExternalUrl);

    private static async Task<bool> OpenSpotifyTrackLinkAsync(SpotifyTrackMetadata track)
    {
        if (!string.IsNullOrWhiteSpace(track.SpotifyUri)
            && await TryLaunchUriAsync(track.SpotifyUri))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(track.SpotifyTrackId)
            && await TryLaunchUriAsync($"spotify:track:{track.SpotifyTrackId}"))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(track.ExternalUrl)
            && await TryLaunchUriAsync(track.ExternalUrl);
    }

    private static async Task<bool> OpenSpotifyPlaylistLinkAsync(SpotifyPlaylistSummary playlist)
    {
        if (!string.IsNullOrWhiteSpace(playlist.SpotifyPlaylistId)
            && await TryLaunchUriAsync($"spotify:playlist:{playlist.SpotifyPlaylistId}"))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(playlist.ExternalUrl)
            && await TryLaunchUriAsync(playlist.ExternalUrl);
    }

    private static async Task<bool> TryLaunchUriAsync(string uriText)
    {
        return Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
            && await Launcher.LaunchUriAsync(uri);
    }

    private async Task<string> ResolveSelectedDeviceIdAsync()
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Local Music Files mode is active. Use PLAY FILE for local tracks or switch to Spotify Web API / Spotify Connect for Spotify controls.");
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.WebPlaybackSdk)
        {
            throw new SpotifyApiException(
                SpotifyApiErrorKind.WebPlaybackSdkUnsupported,
                "The Spotify Web Playback SDK is a browser/WebView player path. Use Spotify Web API / Spotify Connect or Open in Spotify App for this native Windows build.");
        }

        if (SelectedSpotifyDevice?.IsRestricted == true)
        {
            throw new SpotifyApiException(
                SpotifyApiErrorKind.DeviceUnavailable,
                $"{SelectedSpotifyDevice.DisplayName} is marked restricted by Spotify and cannot accept remote playback commands.");
        }

        if (!string.IsNullOrWhiteSpace(SelectedSpotifyDevice?.Id))
        {
            return SelectedSpotifyDevice.Id;
        }

        if (SpotifyDevices.Count == 0)
        {
            await RefreshSpotifyDevicesCoreAsync();
        }

        if (!string.IsNullOrWhiteSpace(SelectedSpotifyDevice?.Id))
        {
            return SelectedSpotifyDevice.Id;
        }

        if (!string.IsNullOrWhiteSpace(_selectedOutputDeviceId))
        {
            return _selectedOutputDeviceId;
        }

        var saved = await _playbackSettingsRepository.LoadAsync();
        if (!string.IsNullOrWhiteSpace(saved.SelectedDeviceId))
        {
            _selectedOutputDeviceId = saved.SelectedDeviceId;
            SelectedOutputDeviceName = string.IsNullOrWhiteSpace(saved.SelectedDeviceName)
                ? SelectedOutputDeviceName
                : saved.SelectedDeviceName;
            return saved.SelectedDeviceId;
        }

        throw new SpotifyApiException(SpotifyApiErrorKind.NoActiveDevice, "Select a Spotify output device first.");
    }

    private void EnsureSpotifyConnectPlaybackMode()
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.WebPlaybackSdk)
        {
            throw new SpotifyApiException(
                SpotifyApiErrorKind.WebPlaybackSdkUnsupported,
                "Spotify Web Playback SDK is not the native WinUI playback path. Select Spotify Web API / Spotify Connect and use your HAASLAPTOP Spotify device.");
        }

        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            throw new SpotifyApiException(SpotifyApiErrorKind.InvalidRequest, "Local Music Files mode is active. Use PLAY FILE for local tracks or switch to Spotify Web API / Spotify Connect for Spotify controls.");
        }
    }

    private static string NormalizePlaybackMode(string playbackMode) =>
        playbackMode switch
        {
            SpotifyPlaybackModes.LegacySpotifyConnect => SpotifyPlaybackModes.SpotifyConnect,
            SpotifyPlaybackModes.LegacyExternalSpotifyAppHandoff => SpotifyPlaybackModes.ExternalSpotifyAppHandoff,
            _ => playbackMode
        };

    private static ImageSource CreateAlbumArtSource(string? albumArtSource, string fallbackSource)
    {
        var normalized = NormalizeAlbumArtSource(albumArtSource, fallbackSource);
        try
        {
            return new BitmapImage(new Uri(normalized));
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Album art source rejected: {albumArtSource ?? "<empty>"}; {ex.Message}");
            return new BitmapImage(new Uri(NormalizeAlbumArtSource(fallbackSource, DefaultLocalAlbumArtPath)));
        }
    }

    private static ImageSource CreateAlbumArtSource(string? albumArtSource, ImageSource existingSource, string fallbackSource) =>
        string.IsNullOrWhiteSpace(albumArtSource)
            ? existingSource
            : CreateAlbumArtSource(albumArtSource, fallbackSource);

    private static string NormalizeAlbumArtSource(string? albumArtSource, string fallbackSource)
    {
        var fallback = NormalizeAlbumArtSourceCore(fallbackSource, null) ?? DefaultLocalAlbumArtPath;
        return NormalizeAlbumArtSourceCore(albumArtSource, fallback) ?? fallback;
    }

    private static string? NormalizeAlbumArtSourceCore(string? albumArtSource, string? fallbackSource)
    {
        if (string.IsNullOrWhiteSpace(albumArtSource))
        {
            return fallbackSource;
        }

        var source = albumArtSource.Trim();
        if (source.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(@"Assets\", StringComparison.OrdinalIgnoreCase))
        {
            return $"ms-appx:///{source.Replace('\\', '/')}";
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https" or "ms-appx")
            {
                return uri.AbsoluteUri;
            }

            if (uri.Scheme == "file")
            {
                return File.Exists(uri.LocalPath) ? uri.AbsoluteUri : fallbackSource;
            }
        }

        if (Path.IsPathFullyQualified(source) && File.Exists(source))
        {
            return new Uri(source).AbsoluteUri;
        }

        return fallbackSource;
    }

    private static string NormalizeSource(string source) =>
        source?.Trim().ToLowerInvariant() switch
        {
            "youtube" => SourceYouTube,
            "tidal" => SourceTidal,
            "tital" => SourceTidal,
            "local" => SourceLocal,
            _ => SourceSpotify
        };

    private static bool IsSupportedPlaybackMode(string playbackMode)
    {
        var normalizedPlaybackMode = NormalizePlaybackMode(playbackMode);
        return normalizedPlaybackMode is SpotifyPlaybackModes.SpotifyConnect
            or SpotifyPlaybackModes.ExternalSpotifyAppHandoff
            or SpotifyPlaybackModes.LocalFilesFuture;
    }

    private async Task RefreshSpotifyConnectionStatusAsync()
    {
        try
        {
            if (!await _spotifyService.IsConnectedAsync())
            {
                SpotifyConnectionStatus = "Not connected";
                return;
            }

            var missingPlanningScopes = await _spotifyService.GetMissingScopesAsync(SpotifyScopes.PlanningMetadata);
            if (missingPlanningScopes.Count > 0)
            {
                SpotifyConnectionStatus = "Token stored, metadata scopes missing";
                SpotifyOperationMessage = $"Log out and log back in so DancePilot can request: {string.Join(", ", missingPlanningScopes)}.";
                return;
            }

            var missingPlaybackScopes = await _spotifyService.GetMissingScopesAsync(SpotifyScopes.PlaybackControl);
            SpotifyConnectionStatus = missingPlaybackScopes.Count == 0
                ? "Connected, playback scopes ready"
                : "Connected for Spotify metadata";
        }
        catch
        {
            SpotifyConnectionStatus = "Token unreadable";
        }
    }

    private async Task SaveSpotifySettingsAsync()
    {
        await _spotifySettingsStore.SaveAsync(CurrentSpotifySettings);
    }

    private async Task SavePlaybackSettingsAsync()
    {
        await _playbackSettingsRepository.SaveAsync(CurrentPlaybackSettings);
    }

    private async Task EnsurePlaybackScopesAsync()
    {
        var missingScopes = await _spotifyService.GetMissingScopesAsync(SpotifyScopes.PlaybackControl);
        if (missingScopes.Count == 0)
        {
            return;
        }

        SpotifyOperationMessage = $"Spotify playback needs approval for: {string.Join(", ", missingScopes)}.";
        await SaveSpotifySettingsAsync();
        await _spotifyService.LoginAsync(CurrentSpotifyPlaybackSettings);
        var profile = await _spotifyService.GetCurrentUserProfileAsync(CurrentSpotifySettings);
        SpotifyConnectionStatus = $"Connected as {profile.DisplayName}; playback scopes ready";
        SpotifyOperationMessage = "Spotify playback scopes are ready for Spotify Web API / Spotify Connect controls.";
    }

    private async Task<bool> HasPlaybackScopesAsync() =>
        (await _spotifyService.GetMissingScopesAsync(SpotifyScopes.PlaybackControl)).Count == 0;

    private async Task<bool> RunSpotifyOperationAsync(Func<Task> operation)
    {
        if (IsSpotifyBusy)
        {
            return false;
        }

        try
        {
            IsSpotifyBusy = true;
            await operation();
            return true;
        }
        catch (SpotifyApiException ex)
        {
            StartupLog.Write($"Spotify API error kind={ex.Kind} status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
            SpotifyOperationMessage = ToFriendlySpotifyMessage(ex);
            return false;
        }
        catch (Exception ex)
        {
            SpotifyOperationMessage = $"Spotify operation failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsSpotifyBusy = false;
        }
    }

    private static string ToFriendlySpotifyMessage(SpotifyApiException ex)
    {
        return ex.Kind switch
        {
            SpotifyApiErrorKind.RateLimited when ex.RetryAfter is not null =>
                $"Spotify rate limited this request. Try again in {ex.RetryAfter.Value.TotalSeconds:N0} seconds.",
            SpotifyApiErrorKind.NoPremiumAccount =>
                "Spotify Premium is required for Spotify Web API playback controls such as transfer, play, pause, seek, and volume.",
            SpotifyApiErrorKind.NoActiveDevice =>
                "No active Spotify device was found. Open Spotify on a phone, browser, or desktop app, then refresh devices.",
            SpotifyApiErrorKind.DeviceUnavailable =>
                "The selected Spotify device is unavailable. Refresh devices or choose another output.",
            SpotifyApiErrorKind.MissingScopes =>
                $"{ex.Message} Log out and log back in so DancePilot can request the playback scopes.",
            SpotifyApiErrorKind.RefreshTokenFailed =>
                "Spotify session refresh failed. Log out and log back in.",
            SpotifyApiErrorKind.NetworkOffline =>
                "Spotify is unreachable. Check your network connection.",
            SpotifyApiErrorKind.PlaybackForbidden =>
                "Spotify refused that playback command. Use Spotify Web API / Spotify Connect mode, open Spotify on the selected device, start or pause any song once, then Refresh and Transfer again.",
            SpotifyApiErrorKind.PlaylistAccessForbidden =>
                "Spotify refused access to that playlist's items. It may be private, unavailable, or not readable by this app even though it appears in your playlist list.",
            SpotifyApiErrorKind.UnavailableTrack =>
                "This Spotify track is unavailable or does not have a playable Spotify URI.",
            SpotifyApiErrorKind.WebPlaybackSdkUnsupported =>
                ex.Message,
            _ => ex.Message
        };
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
