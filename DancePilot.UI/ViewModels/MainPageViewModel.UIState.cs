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

public sealed partial class MainPageViewModel
{
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

    public string LocalLibraryTrackCountDisplay => $"Loaded {_localLibrarySavedTrackCount:N0} saved local tracks";

    public string LocalLibraryLastScanDisplay => _localLibraryLastScanCompletedAt is null
        ? "Last scan: never"
        : $"Last scan: {_localLibraryLastScanCompletedAt:g}";

    public string LocalLibraryAlbumArtCacheDisplay => $"Album art cache: {_localLibraryAlbumArtCacheFolderPath}";

    public string LocalLibraryScanButtonText => _localLibrarySavedTrackCount > 0
        ? "Rescan Local Library"
        : "Scan Local Library";

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
                        _ = LoadSavedLocalLibraryAsync();
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
                OnPropertyChanged(nameof(CurrentDeckHeader));
                OnPropertyChanged(nameof(NextDeckHeader));
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

    public DancePilotDeckState DeckAState => CreateDeckState("Deck A");

    public DancePilotDeckState DeckBState => CreateDeckState("Deck B");

    public string DeckAStatus => DeckAState.StatusText;

    public string DeckBStatus => DeckBState.StatusText;

    public string DeckATitle => ResolveDeckTitle("Deck A");

    public string DeckBTitle => ResolveDeckTitle("Deck B");

    public string DeckAArtist => ResolveDeckArtist("Deck A");

    public string DeckBArtist => ResolveDeckArtist("Deck B");

    public string DeckADetail => ResolveDeckDetail("Deck A");

    public string DeckBDetail => ResolveDeckDetail("Deck B");

    public Brush DeckABorderBrush => DeckPanelBorderBrush("Deck A");

    public Brush DeckBBorderBrush => DeckPanelBorderBrush("Deck B");

    public Brush DeckATitleBrush => DeckTitleBrush("Deck A");

    public Brush DeckBTitleBrush => DeckTitleBrush("Deck B");

    public Brush DeckAStatusBrush => DeckStatusBrush("Deck A");

    public Brush DeckBStatusBrush => DeckStatusBrush("Deck B");

    public Brush DeckAPlayingBadgeBrush => DeckPlayingBadgeBrush("Deck A");

    public Brush DeckBPlayingBadgeBrush => DeckPlayingBadgeBrush("Deck B");

    public Brush DeckAPausedBadgeBrush => DeckPausedBadgeBrush("Deck A");

    public Brush DeckBPausedBadgeBrush => DeckPausedBadgeBrush("Deck B");

    public Brush DeckASelectedBadgeBrush => DeckSelectedBadgeBrush("Deck A");

    public Brush DeckBSelectedBadgeBrush => DeckSelectedBadgeBrush("Deck B");

    public Brush DeckAQueueBadgeBrush => DeckQueueBadgeBrush("Deck A");

    public Brush DeckBQueueBadgeBrush => DeckQueueBadgeBrush("Deck B");

    public Brush DeckAPanelBackgroundBrush => DeckPanelBackgroundBrush("Deck A");

    public Brush DeckBPanelBackgroundBrush => DeckPanelBackgroundBrush("Deck B");

    public double DeckAPanelOpacity => DeckPanelOpacity("Deck A");

    public double DeckBPanelOpacity => DeckPanelOpacity("Deck B");

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
                NotifyDeckStateProperties();
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

                if (normalizedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture
                    && !_isRestoringSessionState
                    && !_suppressLocalMusicAutoLoad)
                {
                    _ = LoadSavedLocalLibraryAsync();
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
                if (!_deckTransitionEnabled && _selectedTransitionMode != DancePilotTransitionModes.Off)
                {
                    _selectedTransitionMode = DancePilotTransitionModes.Off;
                    OnPropertyChanged(nameof(SelectedTransitionMode));
                }
                else if (_deckTransitionEnabled && _selectedTransitionMode == DancePilotTransitionModes.Off)
                {
                    _selectedTransitionMode = DancePilotTransitionModes.Auto;
                    OnPropertyChanged(nameof(SelectedTransitionMode));
                }

                OnPropertyChanged(nameof(DeckTransitionStatus));
                OnPropertyChanged(nameof(TransitionBehaviorNotice));
                UpdateNextUpFromDecks();
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
                ApplyLocalOutputLevels();
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    public string CrossfaderDisplay => $"A {100 - CrossfaderPosition:N0} / B {CrossfaderPosition:N0}";

    public string MixerStateDisplay =>
        $"LOW {LowFrequencyGainDisplay}  MID {MidFrequencyGainDisplay}  HIGH {HighFrequencyGainDisplay}  {CrossfaderDisplay}";

    public string TransitionBehaviorNotice
    {
        get
        {
            var timing = AlwaysFadeSongs
                ? StartTransitionOnFade
                    ? $"Next song starts {TransitionOverlapDisplay} before the end; fade-out begins up to {FadeOutSecondsDisplay} earlier."
                    : $"Next song starts {TransitionOverlapDisplay} before the end without pre-fade automation."
                : $"Next song starts {TransitionOverlapDisplay} before the end.";

            return ResolveRequestedTransitionMode() switch
            {
                DancePilotTransitionModes.Off => "Automatic transition is off.",
                DancePilotTransitionModes.SameDeck => $"Same Deck: continue in the playing deck. Local crossfade is used for local-to-local when a deck player is free; Spotify Connect is handoff only. {timing}",
                DancePilotTransitionModes.AlternateDecks => $"Alternate Decks: use the other deck only when it has a playable item. Local crossfade is used for local-to-local; Spotify Connect is handoff only. {timing}",
                _ => $"Auto: use other deck if ready, otherwise continue same deck. Local crossfade is used for local-to-local; Spotify Connect is handoff only. {timing}"
            };
        }
    }

    public IReadOnlyList<string> TransitionModeOptions { get; } = DancePilotTransitionModes.Options;

    public string SelectedTransitionMode
    {
        get => _selectedTransitionMode;
        set
        {
            var normalized = DancePilotTransitionModes.Normalize(value);
            if (SetProperty(ref _selectedTransitionMode, normalized))
            {
                var shouldEnableTransition = normalized != DancePilotTransitionModes.Off;
                if (_deckTransitionEnabled != shouldEnableTransition)
                {
                    _deckTransitionEnabled = shouldEnableTransition;
                    OnPropertyChanged(nameof(DeckTransitionEnabled));
                    OnPropertyChanged(nameof(DeckTransitionStatus));
                }

                OnPropertyChanged(nameof(TransitionBehaviorNotice));
                OnPropertyChanged(nameof(NextDeckHeader));
                UpdateNextUpFromDecks();
                _ = SavePlaybackSettingsAsync();
            }
        }
    }


    public double DeckAFader
    {
        get => _deckAVolume;
        set
        {
            if (SetProperty(ref _deckAVolume, Math.Clamp(value, 0, 100)))
            {
                OnDeckFaderChanged(
                    "Deck A",
                    nameof(DeckAFaderDisplay),
                    nameof(DeckAVolume),
                    nameof(DeckAVolumeDisplay));
            }
        }
    }

    public string DeckAFaderDisplay => FormatVolume(DeckAFader);

    public double DeckBFader
    {
        get => _deckBVolume;
        set
        {
            if (SetProperty(ref _deckBVolume, Math.Clamp(value, 0, 100)))
            {
                OnDeckFaderChanged(
                    "Deck B",
                    nameof(DeckBFaderDisplay),
                    nameof(DeckBVolume),
                    nameof(DeckBVolumeDisplay));
            }
        }
    }

    public string DeckBFaderDisplay => FormatVolume(DeckBFader);

    // Legacy aliases for persisted settings; deck controls are faders, not output volume.
    public double DeckAVolume
    {
        get => DeckAFader;
        set => DeckAFader = value;
    }

    public string DeckAVolumeDisplay => DeckAFaderDisplay;

    public double DeckBVolume
    {
        get => DeckBFader;
        set => DeckBFader = value;
    }

    public string DeckBVolumeDisplay => DeckBFaderDisplay;

    public double MainOutputVolume
    {
        get => _defaultSpotifyVolume;
        set
        {
            if (SetProperty(ref _defaultSpotifyVolume, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(MainOutputVolumeDisplay));
                OnPropertyChanged(nameof(DefaultSpotifyVolume));
                OnPropertyChanged(nameof(DefaultSpotifyVolumeDisplay));
                ApplyLocalOutputLevels();
                _ = SavePlaybackSettingsAsync();
            }
        }
    }

    // Legacy alias for older bindings; this is the one main output volume.
    public double DefaultSpotifyVolume
    {
        get => MainOutputVolume;
        set => MainOutputVolume = value;
    }

    public string MainOutputVolumeDisplay => FormatVolume(MainOutputVolume);

    public string DefaultSpotifyVolumeDisplay => MainOutputVolumeDisplay;

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
        SetMainOutputVolumeAsync();

    public Task CommitDeckFaderChangeAsync(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        ApplyLocalOutputLevelIfDeckIsLive(normalizedDeckName);
        SpotifyOperationMessage = $"{normalizedDeckName} fader set to {FormatVolume(ResolveDeckFader(normalizedDeckName))}. Main volume unchanged.";
        return SavePlaybackSettingsAsync();
    }

    public Task CommitDeckVolumeChangeAsync(string deckName) =>
        CommitDeckFaderChangeAsync(deckName);

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

    public string SpotifyTransitionNotice => "Local decks can crossfade with DancePilot players. Spotify Connect transition: handoff only; Deck A/B faders do not independently mix Spotify streams.";

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

    public IAsyncRelayCommand ReplaceLoadedPlaylistOnActiveDeckCommand { get; }

    public IAsyncRelayCommand RandomizeDeckAQueueCommand { get; }

    public IAsyncRelayCommand RandomizeDeckBQueueCommand { get; }

    public IRelayCommand MoveSelectedQueueItemUpCommand { get; }

    public IRelayCommand MoveSelectedQueueItemDownCommand { get; }

    public IRelayCommand RemoveSelectedQueueItemCommand { get; }

    public IRelayCommand ClearPendingQueueCommand { get; }

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
        var localPath = displayItem.ResolvedLocalPath;
        if (displayItem.Source != SongSources.Local
            || string.IsNullOrWhiteSpace(localPath)
            || !File.Exists(localPath)
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
                    localPath,
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
            var localState = FindLocalPlaybackState(deckName, displayItem.Id);
            if (localState is null)
            {
                return 0;
            }

            var player = LocalPlayerForPlayerDeck(localState.PlayerDeckName);
            var duration = player.PlaybackSession.NaturalDuration > TimeSpan.Zero
                ? player.PlaybackSession.NaturalDuration
                : localState.Track?.Duration;
            if (duration is not TimeSpan durationValue || durationValue <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Clamp(player.PlaybackSession.Position.TotalSeconds / durationValue.TotalSeconds, 0, 1);
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
        $"{item.Source}|{item.SourceIdentity}|{item.Title}|{item.Artist}".ToLowerInvariant();

    private static IReadOnlyList<TrackWaveformSlice> CreateGeneratedTrackWaveform(DancePilotQueueItem? item)
    {
        var seedText = item is null
            ? "empty"
            : $"{item.Source}|{item.SourceIdentity}|{item.Title}|{item.Artist}|{item.BPM}|{item.MusicalKey}";
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
            filePath = ResolvePlayingDeckItem(_playingDeckName)?.ResolvedLocalPath;
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        if (!TryGetActiveLocalPlayback(out _, out var player))
        {
            return null;
        }

        return _localAudioAnalysisService.Analyze(filePath, player.PlaybackSession.Position, waveformBarCount: DeckWaveformBarCount);
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

    private void NotifyDeckStateProperties()
    {
        OnPropertyChanged(nameof(DeckAState));
        OnPropertyChanged(nameof(DeckBState));
        OnPropertyChanged(nameof(DeckAStatus));
        OnPropertyChanged(nameof(DeckBStatus));
        OnPropertyChanged(nameof(DeckABorderBrush));
        OnPropertyChanged(nameof(DeckBBorderBrush));
        OnPropertyChanged(nameof(DeckATitleBrush));
        OnPropertyChanged(nameof(DeckBTitleBrush));
        OnPropertyChanged(nameof(DeckAStatusBrush));
        OnPropertyChanged(nameof(DeckBStatusBrush));
        OnPropertyChanged(nameof(DeckAPlayingBadgeBrush));
        OnPropertyChanged(nameof(DeckBPlayingBadgeBrush));
        OnPropertyChanged(nameof(DeckAPausedBadgeBrush));
        OnPropertyChanged(nameof(DeckBPausedBadgeBrush));
        OnPropertyChanged(nameof(DeckASelectedBadgeBrush));
        OnPropertyChanged(nameof(DeckBSelectedBadgeBrush));
        OnPropertyChanged(nameof(DeckAQueueBadgeBrush));
        OnPropertyChanged(nameof(DeckBQueueBadgeBrush));
        OnPropertyChanged(nameof(DeckAPanelBackgroundBrush));
        OnPropertyChanged(nameof(DeckBPanelBackgroundBrush));
        OnPropertyChanged(nameof(DeckAPanelOpacity));
        OnPropertyChanged(nameof(DeckBPanelOpacity));
    }

    private Brush DeckPanelBackgroundBrush(string deckName)
    {
        var state = CreateDeckState(deckName);
        var baseColor = ParseColor("#0B1015");
        var accent = ParseColor(DeckAccentHex(deckName));
        if (state.IsPlaying)
        {
            return Brush(Blend(baseColor, Blend(accent, ParseColor("#39E75F"), 0.22), 0.24));
        }

        if (state.IsSelected)
        {
            return Brush(Blend(baseColor, accent, 0.18));
        }

        if (state.IsPaused)
        {
            return Brush(Blend(baseColor, ParseColor("#A9C6D8"), 0.10));
        }

        return Brush(baseColor);
    }

    private double DeckPanelOpacity(string deckName)
    {
        var state = CreateDeckState(deckName);
        if (state.IsPlaying || state.IsSelected)
        {
            return 1.0;
        }

        if (state.IsPaused)
        {
            return 0.97;
        }

        return state.QueueCount > 0 ? 0.94 : 0.90;
    }

    private Brush DeckPanelBorderBrush(string deckName)
    {
        var state = CreateDeckState(deckName);
        if (state.IsPlaying)
        {
            return Brush("#39E75F");
        }

        if (state.IsSelected)
        {
            return Brush(DeckAccentHex(deckName));
        }

        if (state.IsPaused)
        {
            return Brush("#A9C6D8");
        }

        return Brush("#27313B");
    }

    private Brush DeckTitleBrush(string deckName)
    {
        var state = CreateDeckState(deckName);
        if (state.IsPlaying)
        {
            return Brush("#FFFFFF");
        }

        if (state.IsSelected)
        {
            return Brush(DeckAccentSoftHex(deckName));
        }

        if (state.IsPaused)
        {
            return Brush("#DCE7EF");
        }

        return Brush("#9AA6B2");
    }

    private Brush DeckStatusBrush(string deckName)
    {
        var state = CreateDeckState(deckName);
        if (state.IsPlaying)
        {
            return Brush("#39E75F");
        }

        if (state.IsSelected)
        {
            return Brush(DeckAccentSoftHex(deckName));
        }

        if (state.IsPaused)
        {
            return Brush("#FFD35A");
        }

        return Brush("#7E8A95");
    }

    private Brush DeckPlayingBadgeBrush(string deckName)
    {
        var state = CreateDeckState(deckName);
        return state.IsPlaying ? Brush("#39E75F") : Brush("#56616C");
    }

    private Brush DeckPausedBadgeBrush(string deckName)
    {
        var state = CreateDeckState(deckName);
        return state.IsPaused ? Brush("#FFD35A") : Brush("#56616C");
    }

    private Brush DeckSelectedBadgeBrush(string deckName)
    {
        var state = CreateDeckState(deckName);
        return state.IsSelected ? Brush(DeckAccentSoftHex(deckName)) : Brush("#56616C");
    }

    private Brush DeckQueueBadgeBrush(string deckName)
    {
        var state = CreateDeckState(deckName);
        if (state.QueueCount == 0)
        {
            return Brush("#56616C");
        }

        return state.IsPlaying
            ? Brush("#C8FFD7")
            : state.IsSelected
                ? Brush(DeckAccentSoftHex(deckName))
                : Brush("#9AA6B2");
    }

    private static string DeckAccentHex(string deckName) =>
        NormalizeDeckName(deckName) == "Deck B" ? "#1EA7FF" : "#F4B400";

    private static string DeckAccentSoftHex(string deckName) =>
        NormalizeDeckName(deckName) == "Deck B" ? "#8EE6FF" : "#FFD35A";

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

    private void OnDeckFaderChanged(
        string deckName,
        string displayPropertyName,
        string legacyValuePropertyName,
        string legacyDisplayPropertyName)
    {
        OnPropertyChanged(displayPropertyName);
        OnPropertyChanged(legacyValuePropertyName);
        OnPropertyChanged(legacyDisplayPropertyName);
        ApplyLocalOutputLevelIfDeckIsLive(deckName);
        _ = SavePlaybackSettingsAsync();
    }

    private DancePilotMixLevels CurrentMixLevels =>
        new(MainOutputVolume, DeckAFader, DeckBFader, CrossfaderPosition);

    private double ResolveDeckFader(string deckName) =>
        NormalizeDeckName(deckName) == "Deck B" ? DeckBFader : DeckAFader;

    private int ResolveMainOutputVolumePercent() =>
        Math.Clamp(Convert.ToInt32(MainOutputVolume), 0, 100);

    private double ResolveLocalOutputLevel(string deckName)
    {
        var levels = CurrentMixLevels;
        return NormalizeDeckName(deckName) == "Deck B"
            ? levels.DeckBLocalOutputLevel
            : levels.DeckALocalOutputLevel;
    }

    private bool ShouldApplyLocalOutputLevelToDeck(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        return _localDeckPlaybackStates.Values.Any(state =>
                state.IsLoaded
                && string.Equals(state.LogicalDeckName, normalizedDeckName, StringComparison.Ordinal))
            || (_playingDeckQueueItemId is null
                && string.Equals(ActiveDeckName, normalizedDeckName, StringComparison.Ordinal));
    }

    private void ApplyLocalOutputLevelIfDeckIsLive(string deckName)
    {
        var normalizedDeckName = NormalizeDeckName(deckName);
        if (SelectedPlaybackMode == SpotifyPlaybackModes.LocalFilesFuture
            && ShouldApplyLocalOutputLevelToDeck(normalizedDeckName))
        {
            foreach (var state in _localDeckPlaybackStates.Values.Where(state =>
                state.IsLoaded
                && string.Equals(state.LogicalDeckName, normalizedDeckName, StringComparison.Ordinal)))
            {
                LocalPlayerForPlayerDeck(state.PlayerDeckName).Volume = ResolveLocalOutputLevel(normalizedDeckName);
            }
        }
    }

    private void ApplyLocalOutputLevels()
    {
        ApplyLocalOutputLevelIfDeckIsLive("Deck A");
        ApplyLocalOutputLevelIfDeckIsLive("Deck B");
    }
}
