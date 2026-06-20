using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DancePilot.Core.Models;
using DancePilot.Core.Spotify;
using DancePilot.Data.Migrations;
using DancePilot.Data.Repositories;
using DancePilot.Data.Storage;
using DancePilot.Services.MockData;
using DancePilot.Services.LocalMusic;
using DancePilot.Services.Spotify;
using DancePilot.Services.Spotify.Auth;
using DancePilot.Services.Spotify.Playback;
using DancePilot.UI.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
    private readonly SpotifySettingsStore _spotifySettingsStore;
    private readonly SpotifyService _spotifyService;
    private readonly SpotifyImportRepository _spotifyImportRepository;
    private readonly SpotifyPlaylistImporter _spotifyPlaylistImporter;
    private readonly SpotifyPlayerService _spotifyPlayerService;
    private readonly SpotifyDeviceManager _spotifyDeviceManager;
    private readonly DancePilotPlaybackCoordinator _playbackCoordinator;
    private readonly PlaybackSettingsRepository _playbackSettingsRepository;
    private readonly SpotifyLibraryRepository _spotifyLibraryRepository;
    private readonly LocalMusicLibraryService _localMusicLibraryService;
    private readonly MediaPlayer _localMediaPlayer;
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _analyzerTimer;
    private readonly Dictionary<string, int?> _selectedDeckQueueItemIds = new()
    {
        ["Deck A"] = null,
        ["Deck B"] = null
    };
    private readonly List<LocalMusicTrack> _allLocalMusicTracks = [];
    private readonly List<DancePilotQueueItem> _deckAQueue = [];
    private readonly List<DancePilotQueueItem> _deckBQueue = [];
    private bool _suppressSpotifyPlaylistAutoLoad;
    private int _analyzerFrame;
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
    private DancePilotQueueItem? _selectedActiveDeckQueueItem;
    private string _localMusicSearchQuery = string.Empty;
    private string _localLibraryStatus = "Click LOCAL to scan your Windows Music folder.";
    private string _activeSource = SourceSpotify;
    private string _selectedPlaybackMode = SpotifyPlaybackModes.SpotifyConnect;
    private string _activeDeckName = "Deck A";
    private string _playingDeckName = "Deck A";
    private int? _playingDeckQueueItemId;
    private int? _lastTransitionSourceItemId;
    private int? _lastTransitionTargetItemId;
    private bool _isPlaybackPlaying;
    private bool _spotifyAutopilotEnabled;
    private bool _deckTransitionEnabled = true;
    private double _autoplaySecondsBeforeEnd = 8;
    private double _transitionSecondsBeforeEnd = 8;
    private double _defaultSpotifyVolume = 70;
    private double _seekPositionSeconds;
    private string _currentOutputStatus = "No Spotify device selected";
    private string _spotifyNowPlayingTitle = "Nothing playing";
    private string _spotifyNowPlayingArtist = "Spotify";
    private string _spotifyPlaybackStatus = "Idle";
    private string _spotifyTimeRemaining = "--:--";
    private string _spotifyProgressDisplay = "--:-- / --:--";
    private string _selectedOutputDeviceId = string.Empty;
    private string _selectedOutputDeviceName = "No Spotify device selected";
    private string _currentSpotifyPlaylistName = "No Spotify playlist active";
    private string _nextUpTitle = "No queued recommendation";
    private string _nextUpArtist = "DancePilot queue";
    private string _selectedTransitionMode = TransitionSameDeck;
    private bool _isPlaybackRefreshRunning;
    private bool _isAutoPilotEnabled = true;

    public MainPageViewModel()
    {
        StartupLog.Write("MainPageViewModel constructor start");
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DancePilot");
        Directory.CreateDirectory(appData);

        _spotifySettingsStore = new SpotifySettingsStore();

        var tokenStore = new EncryptedFileSpotifyTokenStore();
        var httpClient = new HttpClient();
        var authService = new SpotifyAuthService(httpClient, tokenStore);
        _spotifyService = new SpotifyService(httpClient, tokenStore, authService);

        var databaseOptions = new DatabaseOptions
        {
            DatabasePath = Path.Combine(appData, "dancepilot.sqlite")
        };
        var connectionFactory = new SqliteConnectionFactory(databaseOptions);
        _spotifyImportRepository = new SpotifyImportRepository(connectionFactory);
        _spotifyPlaylistImporter = new SpotifyPlaylistImporter(_spotifyService, _spotifyImportRepository);
        _spotifyPlayerService = new SpotifyPlayerService(_spotifyService);
        _spotifyDeviceManager = new SpotifyDeviceManager(_spotifyService);
        _playbackSettingsRepository = new PlaybackSettingsRepository(connectionFactory);
        _spotifyLibraryRepository = new SpotifyLibraryRepository(connectionFactory);
        _localMusicLibraryService = new LocalMusicLibraryService();
        _localMediaPlayer = new MediaPlayer();
        var queueRepository = new PlaybackQueueRepository(connectionFactory);
        var historyRepository = new PlaybackHistoryRepository(connectionFactory);
        _playbackCoordinator = new DancePilotPlaybackCoordinator(_spotifyPlayerService, queueRepository, historyRepository);

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
        PauseLocalMusicCommand = new RelayCommand(PauseLocalMusic);
        TogglePlaybackCommand = new AsyncRelayCommand(TogglePlaybackAsync);
        SelectDeckACommand = new RelayCommand(() => SelectDeck("Deck A"));
        SelectDeckBCommand = new RelayCommand(() => SelectDeck("Deck B"));

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _playbackTimer.Tick += async (_, _) => await RefreshPlaybackAndAutopilotAsync();
        _playbackTimer.Start();

        _analyzerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _analyzerTimer.Tick += (_, _) => UpdateDeckAnalyzers();
        _analyzerTimer.Start();

        _ = InitializeAsync(connectionFactory);
        StartupLog.Write("MainPageViewModel constructor complete");
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
        set => SetProperty(ref _spotifySearchQuery, value);
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
        }
    }

    public SpotifyTrackMetadata? SelectedSpotifyTrack
    {
        get => _selectedSpotifyTrack;
        set => SetProperty(ref _selectedSpotifyTrack, value);
    }

    public SpotifyTrackMetadata? SelectedSpotifySearchTrack
    {
        get => _selectedSpotifySearchTrack;
        set => SetProperty(ref _selectedSpotifySearchTrack, value);
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
        set => SetProperty(ref _selectedImportedSpotifyPlaylist, value);
    }

    public SpotifyTrackMetadata? SelectedImportedSpotifyTrack
    {
        get => _selectedImportedSpotifyTrack;
        set => SetProperty(ref _selectedImportedSpotifyTrack, value);
    }

    public LocalMusicTrack? SelectedLocalMusicTrack
    {
        get => _selectedLocalMusicTrack;
        set => SetProperty(ref _selectedLocalMusicTrack, value);
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
            }
        }
    }

    public string LocalMusicSearchQuery
    {
        get => _localMusicSearchQuery;
        set => SetProperty(ref _localMusicSearchQuery, value);
    }

    public string LocalLibraryStatus
    {
        get => _localLibraryStatus;
        private set => SetProperty(ref _localLibraryStatus, value);
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
                    _ = LoadLocalMusicAsync();
                }
                else if (normalized == SourceSpotify && SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
                {
                    SelectedPlaybackMode = SpotifyPlaybackModes.SpotifyConnect;
                }
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

    public GridLength RequestsColumnWidth => new(0);

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
                OnPropertyChanged(nameof(CurrentDeckHeader));
                OnPropertyChanged(nameof(NextDeckHeader));
            }
        }
    }

    public string CurrentDeckHeader => $"NOW PLAYING - {_playingDeckName}";

    public string NextDeckHeader => $"NEXT UP - {ResolveTransitionDeckName()}";

    public bool IsPlaybackPlaying
    {
        get => _isPlaybackPlaying;
        private set
        {
            if (SetProperty(ref _isPlaybackPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseLabel));
            }
        }
    }

    public string PlayPauseLabel => IsPlaybackPlaying ? "PAUSE" : "PLAY";

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

                if (normalizedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
                {
                    _ = LoadLocalMusicAsync();
                }
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

    public double TransitionSecondsBeforeEnd
    {
        get => _transitionSecondsBeforeEnd;
        set
        {
            if (SetProperty(ref _transitionSecondsBeforeEnd, Math.Clamp(value, 1, 45)))
            {
                OnPropertyChanged(nameof(TransitionSecondsDisplay));
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string TransitionSecondsDisplay => $"{TransitionSecondsBeforeEnd:N0}s";

    public IReadOnlyList<string> TransitionModeOptions { get; } =
    [
        TransitionSameDeck,
        TransitionOppositeDeck
    ];

    public string SelectedTransitionMode
    {
        get => _selectedTransitionMode;
        set
        {
            var normalized = TransitionModeOptions.Contains(value)
                ? value
                : TransitionSameDeck;
            if (SetProperty(ref _selectedTransitionMode, normalized))
            {
                OnPropertyChanged(nameof(NextDeckHeader));
                UpdateNextUpFromDecks();
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public double DefaultSpotifyVolume
    {
        get => _defaultSpotifyVolume;
        set
        {
            if (SetProperty(ref _defaultSpotifyVolume, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(DefaultSpotifyVolumeDisplay));
            }
        }
    }

    public string DefaultSpotifyVolumeDisplay => $"{DefaultSpotifyVolume:N0}%";

    public double SeekPositionSeconds
    {
        get => _seekPositionSeconds;
        set => SetProperty(ref _seekPositionSeconds, Math.Max(0, value));
    }

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

    public string SpotifyTransitionNotice => "Spotify transitions are controlled by Spotify settings. DancePilot can choose next track but does not mix Spotify audio.";

    public string EventName => "Saturday Night Dance";

    public string EventTimeRemaining => "2:18";

    public string ClockText => "8:42 PM";

    public string QueueSummary => "1,024 songs";

    public string LibraryDuration => "8.7 days";

    public string AnalysisStatus => "Analyzed";

    public int CrowdEnergy => 7;

    public int VolumeLevel => 78;

    public int CrossfadeSeconds => 10;

    public Song CurrentSong => Songs[0];

    public Song NextSong => Songs[1];

    public IReadOnlyList<Song> Songs { get; } = DancePilotSampleData.Songs;

    public IReadOnlyList<Song> LibrarySongs { get; } = DancePilotSampleData.Songs.Skip(6).Concat(DancePilotSampleData.Songs.Take(4)).ToList();

    public IReadOnlyList<PlaylistSummary> Playlists { get; } = DancePilotSampleData.Playlists;

    public IReadOnlyList<SongRecommendation> Recommendations { get; } = DancePilotSampleData.Recommendations;

    public IReadOnlyList<SongRequest> Requests { get; } = DancePilotSampleData.Requests;

    public ObservableCollection<SpotifyPlaylistSummary> SpotifyPlaylists { get; } = [];

    public ObservableCollection<SpotifyTrackMetadata> SpotifyPreviewTracks { get; } = [];

    public ObservableCollection<SpotifyTrackMetadata> SpotifySearchResults { get; } = [];

    public ObservableCollection<SpotifyDevice> SpotifyDevices { get; } = [];

    public ObservableCollection<SpotifyPlaylistSummary> ImportedSpotifyPlaylists { get; } = [];

    public ObservableCollection<SpotifyTrackMetadata> ImportedSpotifyTracks { get; } = [];

    public ObservableCollection<LocalMusicTrack> LocalMusicTracks { get; } = [];

    public ObservableCollection<DancePilotQueueItem> DancePilotQueue { get; } = [];

    public ObservableCollection<DancePilotQueueItem> ActiveDeckQueue { get; } = [];

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

    public IRelayCommand PauseLocalMusicCommand { get; }

    public IAsyncRelayCommand TogglePlaybackCommand { get; }

    public IRelayCommand SelectDeckACommand { get; }

    public IRelayCommand SelectDeckBCommand { get; }

    public ObservableCollection<WaveBar> CurrentWaveform { get; } = CreateWaveform("#F4B400", "#8E949A");

    public ObservableCollection<WaveBar> NextWaveform { get; } = CreateWaveform("#1EA7FF", "#274357");

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

    private static ObservableCollection<WaveBar> CreateWaveform(string primary, string secondary)
    {
        double[] heights =
        [
            18, 36, 28, 46, 22, 52, 34, 61, 26, 44, 32, 57, 20, 49, 36, 67, 29, 54, 22, 48,
            42, 74, 34, 55, 25, 47, 32, 62, 36, 53, 21, 41, 30, 58, 44, 70, 24, 52, 34, 60,
            28, 49, 39, 64, 30, 56, 22, 43, 35, 51, 26, 46, 33, 59, 41, 68, 25, 50, 32, 44
        ];

        var bars = new ObservableCollection<WaveBar>();
        for (var index = 0; index < heights.Length; index++)
        {
            bars.Add(new WaveBar(heights[index], Brush(index < 32 ? primary : secondary)));
        }

        return bars;
    }

    private void UpdateDeckAnalyzers()
    {
        _analyzerFrame++;
        var currentActive = IsPlaybackPlaying && _playingDeckQueueItemId is not null;
        UpdateAnalyzer(CurrentWaveform, "#F4B400", "#4A5560", currentActive, _analyzerFrame, _playingDeckQueueItemId ?? 0);
        UpdateAnalyzer(NextWaveform, "#1EA7FF", "#274357", active: false, _analyzerFrame + 11, FindTransitionTarget()?.Id ?? 0);
    }

    private static void UpdateAnalyzer(ObservableCollection<WaveBar> bars, string primary, string secondary, bool active, int phase, int seed)
    {
        const int count = 60;
        var seedPhase = (seed % 29) * 0.17;
        bars.Clear();
        for (var index = 0; index < count; index++)
        {
            var band = index / (count - 1d);
            var idleShape = 8 + Math.Abs(Math.Sin((index * 0.42) + seedPhase)) * 11;
            var bass = Math.Pow(Math.Max(0, Math.Sin((phase * 0.34) + seedPhase)), 2) * (1 - band) * 48;
            var mid = Math.Pow(Math.Max(0, Math.Sin((phase * 0.21) + (index * 0.31) + seedPhase)), 2)
                * Math.Max(0, 1 - Math.Abs(band - 0.48) * 1.8)
                * 34;
            var high = (0.5 + Math.Sin((phase * 0.63) + (index * 0.83) + seedPhase) * 0.5) * band * 18;
            var baseHeight = active
                ? 10 + bass + mid + high
                : idleShape;
            var height = Math.Clamp(baseHeight, 8, 76);
            bars.Add(new WaveBar(height, Brush(active || index % 7 == 0 ? primary : secondary)));
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var value = hex.TrimStart('#');
        var r = Convert.ToByte(value[..2], 16);
        var g = Convert.ToByte(value.Substring(2, 2), 16);
        var b = Convert.ToByte(value.Substring(4, 2), 16);
        return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
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
            await RefreshSpotifyConnectionStatusAsync();
            await RefreshPlaybackCollectionsAsync();

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
        TransitionSecondsBeforeEnd = settings.DeckTransitionSecondsBeforeEnd;
        SelectedTransitionMode = TransitionModeOptions.Contains(settings.DeckTransitionMode)
            ? settings.DeckTransitionMode
            : TransitionSameDeck;
        DefaultSpotifyVolume = settings.DefaultVolume;
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
        DefaultVolume = Math.Clamp(Convert.ToInt32(DefaultSpotifyVolume), 0, 100),
        DeckTransitionEnabled = DeckTransitionEnabled,
        DeckTransitionSecondsBeforeEnd = Math.Max(1, Convert.ToInt32(TransitionSecondsBeforeEnd)),
        DeckTransitionMode = SelectedTransitionMode
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
                $"Imported {result.ImportedCount}, updated {result.UpdatedCount}, skipped {result.UnavailableCount} unavailable, likely local matches {result.LikelyLocalMatchCount}.";
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
        });
    }

    private Task PlaySelectedSpotifySearchTrackAsync()
    {
        if (SelectedSpotifySearchTrack is null)
        {
            SpotifyOperationMessage = "Select a Spotify search result first.";
            return Task.CompletedTask;
        }

        QueueSpotifyTrackToActiveDeck(SelectedSpotifySearchTrack);
        return Task.CompletedTask;
    }

    private async Task PlaySpotifyTrackWithFallbackAsync(SpotifyTrackMetadata track, string successMessage)
    {
        if (await TryExternalHandoffTrackAsync(track))
        {
            return;
        }

        await EnsurePlaybackScopesAsync();
        EnsureSpotifyConnectPlaybackMode();
        try
        {
            var deviceId = await ResolveSelectedDeviceIdAsync();
            await _playbackCoordinator.PlayTrackAsync(CurrentSpotifySettings, deviceId, track);
            SpotifyOperationMessage = successMessage;
            await RefreshPlaybackCoreAsync(runAutopilot: false);
        }
        catch (SpotifyApiException ex) when (ex.Kind is SpotifyApiErrorKind.PlaybackForbidden or SpotifyApiErrorKind.NoActiveDevice or SpotifyApiErrorKind.DeviceUnavailable)
        {
            StartupLog.Write($"Spotify Connect play failed ({ex.Kind}); falling back to Spotify app for {track.Title}. status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
            if (!await OpenSpotifyTrackLinkAsync(track))
            {
                throw;
            }

            SelectedPlaybackMode = SpotifyPlaybackModes.ExternalSpotifyAppHandoff;
            SpotifyOperationMessage = $"Spotify refused remote control, so DancePilot opened {track.Title} in Spotify. Press Play in Spotify if it does not start automatically.";
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

    private Task PlaySelectedSpotifyTrackAsync()
    {
        var track = SelectedImportedSpotifyTrack ?? SelectedSpotifyTrack ?? SelectedSpotifySearchTrack;
        if (track is null)
        {
            SpotifyOperationMessage = "Select a Spotify track first.";
            return Task.CompletedTask;
        }

        QueueSpotifyTrackToActiveDeck(track);
        return Task.CompletedTask;
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

    private Task PlayFromSelectedSpotifyTrackAsync()
    {
        if (SelectedImportedSpotifyTrack is null)
        {
            SpotifyOperationMessage = "Select a Spotify playlist track first.";
            return Task.CompletedTask;
        }

        QueueSpotifyTrackToActiveDeck(SelectedImportedSpotifyTrack);
        return Task.CompletedTask;
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
        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.SetVolumeAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync(), Convert.ToInt32(DefaultSpotifyVolume));
            await SavePlaybackSettingsAsync();
            SpotifyOperationMessage = $"Requested Spotify volume {DefaultSpotifyVolumeDisplay}. Device support may vary.";
        });
    }

    private async Task SeekSpotifyAsync()
    {
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
        {
            SeekLocalPlaybackTo(TimeSpan.FromSeconds(SeekPositionSeconds));
            return;
        }

        await RunSpotifyOperationAsync(async () =>
        {
            await EnsurePlaybackScopesAsync();
            EnsureSpotifyConnectPlaybackMode();
            await _spotifyPlayerService.SeekAsync(CurrentSpotifySettings, await ResolveSelectedDeviceIdAsync(), Convert.ToInt32(SeekPositionSeconds * 1000));
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
        });
    }

    private async Task LoadLocalMusicAsync()
    {
        try
        {
            var musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            LocalLibraryStatus = $"Scanning {musicFolder}...";
            var tracks = await _localMusicLibraryService.LoadDefaultMusicLibraryAsync();
            _allLocalMusicTracks.Clear();
            _allLocalMusicTracks.AddRange(tracks);
            ApplyLocalMusicFilter();

            LocalLibraryStatus = tracks.Count == 0
                ? $"No supported audio files found in {musicFolder}."
                : $"Loaded {tracks.Count} local audio file(s) from {musicFolder}.";
            CurrentOutputStatus = "Local file mode is ready.";
            SpotifyOperationMessage = LocalLibraryStatus;
        }
        catch (Exception ex)
        {
            LocalLibraryStatus = $"Local music scan failed: {ex.Message}";
            SpotifyOperationMessage = LocalLibraryStatus;
        }
    }

    private Task SearchLocalMusicAsync()
    {
        ApplyLocalMusicFilter();
        LocalLibraryStatus = LocalMusicTracks.Count == _allLocalMusicTracks.Count
            ? $"Showing all {_allLocalMusicTracks.Count} local audio file(s)."
            : $"Showing {LocalMusicTracks.Count} of {_allLocalMusicTracks.Count} local audio file(s).";
        SpotifyOperationMessage = LocalLibraryStatus;
        return Task.CompletedTask;
    }

    private void ApplyLocalMusicFilter()
    {
        var filteredTracks = _localMusicLibraryService.Search(_allLocalMusicTracks, LocalMusicSearchQuery);
        LocalMusicTracks.Clear();
        foreach (var track in filteredTracks.Take(500))
        {
            LocalMusicTracks.Add(track);
        }

        SelectedLocalMusicTrack = LocalMusicTracks.FirstOrDefault();
    }

    private Task QueueSelectedLocalMusicAsync()
    {
        if (SelectedLocalMusicTrack is null)
        {
            SpotifyOperationMessage = "Select a local music file first.";
            return Task.CompletedTask;
        }

        QueueLocalTrackToActiveDeck(SelectedLocalMusicTrack);
        return Task.CompletedTask;
    }

    private Task StartSelectedLocalMusicAsync()
    {
        if (SelectedLocalMusicTrack is null)
        {
            SpotifyOperationMessage = "Select a local music file first.";
            return Task.CompletedTask;
        }

        if (!File.Exists(SelectedLocalMusicTrack.FilePath))
        {
            SpotifyOperationMessage = $"Local file was not found: {SelectedLocalMusicTrack.FileName}";
            return Task.CompletedTask;
        }

        var fileUri = new Uri(SelectedLocalMusicTrack.FilePath);
        _localMediaPlayer.Source = MediaSource.CreateFromUri(fileUri);
        _localMediaPlayer.Play();

        IsPlaybackPlaying = true;
        SpotifyNowPlayingTitle = SelectedLocalMusicTrack.Title;
        SpotifyNowPlayingArtist = SelectedLocalMusicTrack.DisplayArtist;
        SpotifyPlaybackStatus = "Playing local";
        SpotifyProgressDisplay = SelectedLocalMusicTrack.DurationDisplay;
        CurrentOutputStatus = $"Playing local file: {SelectedLocalMusicTrack.FileName}";
        SpotifyOperationMessage = "Local playback is running from this PC. Spotify commands do not affect local files.";
        return Task.CompletedTask;
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

        session.Position = target;
        SeekPositionSeconds = target.TotalSeconds;
        SpotifyProgressDisplay = session.NaturalDuration > TimeSpan.Zero
            ? $"{target:m\\:ss} / {session.NaturalDuration:m\\:ss}"
            : $"{target:m\\:ss} / --:--";
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

        if (SelectedActiveDeckQueueItem is not null)
        {
            await PlayDeckQueueItemAsync(SelectedActiveDeckQueueItem);
            return;
        }

        if (ActiveDeckQueue.FirstOrDefault() is { } nextDeckItem)
        {
            SelectedActiveDeckQueueItem = nextDeckItem;
            await PlayDeckQueueItemAsync(nextDeckItem);
            return;
        }

        await ResumeSpotifyAsync();
    }

    private void SelectDeck(string deckName)
    {
        ActiveDeckName = deckName;
        RefreshActiveDeckQueue();
        UpdateNextUpFromDecks();
        SpotifyOperationMessage = $"{ActiveDeckName} selected. Cue a song here, then select it for transition.";
    }

    private async Task PlayDeckQueueItemAsync(DancePilotQueueItem queueItem, string? deckName = null, bool isTransition = false)
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
                await StartSelectedLocalMusicAsync();
                MarkDeckItemPlaying(targetDeckName, queueItem);
                return;
            }
        }

        if (queueItem.Source == SongSources.Spotify && !string.IsNullOrWhiteSpace(queueItem.ExternalUri))
        {
            if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture)
            {
                SelectedPlaybackMode = SpotifyPlaybackModes.SpotifyConnect;
            }

            var track = FindSpotifyTrackByUri(queueItem.ExternalUri) ?? new SpotifyTrackMetadata
            {
                SpotifyTrackId = ExtractSpotifyTrackId(queueItem.ExternalUri),
                Title = queueItem.Title,
                Artist = queueItem.Artist,
                DurationMs = 0,
                SpotifyUri = queueItem.ExternalUri
            };

            await RunSpotifyOperationAsync(async () =>
            {
                await PlaySpotifyTrackWithFallbackAsync(track, isTransition
                    ? $"Transitioned to {track.Title} on {targetDeckName}."
                    : $"Playing {track.Title} from {targetDeckName}.");
                MarkDeckItemPlaying(targetDeckName, queueItem);
            });
            return;
        }

        SpotifyOperationMessage = $"Queued item cannot be played yet: {queueItem.Title}.";
    }

    public Task PlaySelectedActiveDeckQueueItemAsync() =>
        SelectedActiveDeckQueueItem is null
            ? Task.CompletedTask
            : PlayDeckQueueItemAsync(SelectedActiveDeckQueueItem);

    private void MarkDeckItemPlaying(string deckName, DancePilotQueueItem queueItem)
    {
        _playingDeckName = deckName;
        _playingDeckQueueItemId = queueItem.Id;
        OnPropertyChanged(nameof(CurrentDeckHeader));
        OnPropertyChanged(nameof(NextDeckHeader));
        UpdateNextUpFromDecks();
    }

    private async Task<bool> MaybeTransitionDeckAsync(SpotifyPlaybackState? currentState)
    {
        if (!DeckTransitionEnabled
            || _playingDeckQueueItemId is null
            || currentState?.IsPlaying != true
            || currentState.DurationMs is null
            || currentState.ProgressMs is null)
        {
            return false;
        }

        var remainingMs = currentState.DurationMs.Value - currentState.ProgressMs.Value;
        if (remainingMs > TransitionSecondsBeforeEnd * 1000)
        {
            return false;
        }

        return await TryStartTransitionTargetAsync();
    }

    private async Task<bool> TryStartTransitionTargetAsync()
    {
        var sourceItemId = _playingDeckQueueItemId;
        var next = FindTransitionTarget();
        if (sourceItemId is null || next is null)
        {
            return false;
        }

        if (_lastTransitionSourceItemId == sourceItemId && _lastTransitionTargetItemId == next.Id)
        {
            return false;
        }

        _lastTransitionSourceItemId = sourceItemId;
        _lastTransitionTargetItemId = next.Id;
        await PlayDeckQueueItemAsync(next, ResolveTransitionDeckName(), isTransition: true);
        return true;
    }

    private DancePilotQueueItem? FindTransitionTarget()
    {
        var deckName = ResolveTransitionDeckName();
        var queue = QueueForDeck(deckName);
        if (queue.Count == 0)
        {
            return null;
        }

        var selected = FindSelectedDeckQueueItem(deckName);
        if (selected is not null && selected.Id != _playingDeckQueueItemId)
        {
            return selected;
        }

        if (SelectedTransitionMode == TransitionOppositeDeck)
        {
            return queue.FirstOrDefault();
        }

        var currentIndex = queue.FindIndex(item => item.Id == _playingDeckQueueItemId);
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

    private static string OppositeDeckName(string deckName) =>
        deckName == "Deck B" ? "Deck A" : "Deck B";

    private List<DancePilotQueueItem> QueueForDeck(string deckName) =>
        deckName == "Deck B" ? _deckBQueue : _deckAQueue;

    private void UpdateNextUpFromDecks()
    {
        var next = FindTransitionTarget() ?? ActiveDeckQueue.FirstOrDefault();
        NextUpTitle = next?.Title ?? "No queued recommendation";
        NextUpArtist = next?.Artist ?? "DancePilot queue";
        OnPropertyChanged(nameof(NextDeckHeader));
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
        QueueToActiveDeck(new DancePilotQueueItem
        {
            Id = NextDeckQueueId(),
            Source = SongSources.Local,
            ExternalUri = song.ExternalUri ?? song.ExternalUrl ?? string.Empty,
            SongId = song.Id,
            Title = song.Title,
            Artist = song.Artist,
            QueuePosition = ActiveQueueList().Count + 1,
            Status = "pending"
        });
    }

    public void QueueSpotifyTrackToActiveDeck(SpotifyTrackMetadata track)
    {
        if (track.IsUnavailable || string.IsNullOrWhiteSpace(track.SpotifyUri))
        {
            SpotifyOperationMessage = $"{track.Title} is not playable through Spotify API controls. Open it in Spotify or choose another track.";
            return;
        }

        QueueToActiveDeck(new DancePilotQueueItem
        {
            Id = NextDeckQueueId(),
            Source = SongSources.Spotify,
            ExternalUri = track.SpotifyUri,
            Title = track.Title,
            Artist = track.Artist,
            QueuePosition = ActiveQueueList().Count + 1,
            Status = "pending"
        });
    }

    public void QueueLocalTrackToActiveDeck(LocalMusicTrack track)
    {
        QueueToActiveDeck(new DancePilotQueueItem
        {
            Id = NextDeckQueueId(),
            Source = SongSources.Local,
            ExternalUri = track.FilePath,
            Title = track.Title,
            Artist = track.DisplayArtist,
            QueuePosition = ActiveQueueList().Count + 1,
            Status = "pending"
        });
    }

    private void QueueToActiveDeck(DancePilotQueueItem item)
    {
        ActiveQueueList().Add(item);
        RefreshActiveDeckQueue();
        SelectedActiveDeckQueueItem = ActiveDeckQueue.LastOrDefault();
        UpdateNextUpFromDecks();
        SpotifyOperationMessage = $"Queued on {ActiveDeckName}: {item.Title}. Select it for transition, or double-click the deck row to start it now.";
    }

    private List<DancePilotQueueItem> ActiveQueueList() => QueueForDeck(ActiveDeckName);

    private int NextDeckQueueId() => _deckAQueue.Count + _deckBQueue.Count + 1;

    private void RefreshActiveDeckQueue()
    {
        ActiveDeckQueue.Clear();
        var position = 1;
        foreach (var item in ActiveQueueList())
        {
            ActiveDeckQueue.Add(item with { QueuePosition = position++ });
        }

        _selectedDeckQueueItemIds.TryGetValue(ActiveDeckName, out var selectedId);
        SelectedActiveDeckQueueItem = ActiveDeckQueue.FirstOrDefault(item => item.Id == selectedId)
            ?? ActiveDeckQueue.FirstOrDefault();
        UpdateNextUpFromDecks();
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
            CurrentOutputStatus = "No active Spotify playback device.";
            return;
        }

        SpotifyPlaybackStatus = state.PlaybackStatus;
        IsPlaybackPlaying = state.IsPlaying;
        SpotifyNowPlayingTitle = state.TrackTitle;
        SpotifyNowPlayingArtist = state.TrackArtist;
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

        if (state.ProgressMs is not null)
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

    private async Task RefreshPlaybackCollectionsAsync()
    {
        await LoadImportedSpotifyPlaylistsCoreAsync();
        await RefreshQueueCoreAsync();
    }

    private async Task LoadImportedSpotifyPlaylistsCoreAsync()
    {
        var playlists = await _spotifyLibraryRepository.GetImportedPlaylistsAsync();
        ImportedSpotifyPlaylists.Clear();
        foreach (var playlist in playlists)
        {
            ImportedSpotifyPlaylists.Add(playlist);
        }

        SelectedImportedSpotifyPlaylist = ImportedSpotifyPlaylists.FirstOrDefault();
        if (SelectedImportedSpotifyPlaylist is not null)
        {
            await LoadImportedPlaylistTracksCoreAsync();
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

    private async Task RunSpotifyOperationAsync(Func<Task> operation)
    {
        if (IsSpotifyBusy)
        {
            return;
        }

        try
        {
            IsSpotifyBusy = true;
            await operation();
        }
        catch (SpotifyApiException ex)
        {
            StartupLog.Write($"Spotify API error kind={ex.Kind} status={ex.StatusCode} uri={ex.RequestUri} body={ex.ErrorBody}");
            SpotifyOperationMessage = ToFriendlySpotifyMessage(ex);
        }
        catch (Exception ex)
        {
            SpotifyOperationMessage = $"Spotify operation failed: {ex.Message}";
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

public sealed record WaveBar(double Height, Brush Fill);

public sealed record EnergySegment(Brush Fill);

public sealed record RemoteAction(string Label, string IconGlyph, Brush Background);
