using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using DancePilot.Services.Tidal;
using DancePilot.Services.Tidal.Auth;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace DancePilot.UI.ViewModels;

public sealed partial class MainPageViewModel
{
    private readonly TidalSettingsStore _tidalSettingsStore;
    private readonly ITidalTokenStore _tidalTokenStore;
    private readonly TidalAuthService _tidalAuthService;
    private readonly TidalCatalogService _tidalCatalogService;
    private string _tidalClientId = string.Empty;
    private string _tidalRedirectUri = TidalDefaults.RedirectUri;
    private string _tidalCountryCode = "US";
    private bool _experimentalTidalCatalogEnabled;
    private string _tidalConnectionStatus = "Not connected";
    private bool _isTidalConnected;
    private bool _isTidalApiAvailable;
    private bool _hasTidalPlaylistScopes;
    private bool _isTidalBusy;
    private string _tidalOperationMessage = "Enable the experimental catalog and configure TIDAL to connect.";
    private string _tidalSearchQuery = string.Empty;
    private TidalTrackMetadata? _selectedTidalTrack;
    private TidalAlbumMetadata? _selectedTidalAlbum;
    private TidalArtistMetadata? _selectedTidalArtist;
    private TidalPlaylistMetadata? _selectedTidalPlaylist;
    private string _tidalGrantedScopesDisplay = "Granted scopes: none";
    private string _tidalPlaylistAvailabilityMessage = TidalCatalogCapabilities.UserPlaylistsUnavailableMessage;
    private string? _lastTidalDiagnostic;

    public string TidalClientId
    {
        get => _tidalClientId;
        set => SetProperty(ref _tidalClientId, value);
    }

    public string TidalRedirectUri
    {
        get => _tidalRedirectUri;
        set => SetProperty(ref _tidalRedirectUri, value);
    }

    public string TidalCountryCode
    {
        get => _tidalCountryCode;
        set => SetProperty(ref _tidalCountryCode, value);
    }

    public bool ExperimentalTidalCatalogEnabled
    {
        get => _experimentalTidalCatalogEnabled;
        set
        {
            if (SetProperty(ref _experimentalTidalCatalogEnabled, value))
            {
                OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
                OnPropertyChanged(nameof(TidalConnectActionVisibility));
                OnPropertyChanged(nameof(TidalDisconnectActionVisibility));
                if (!value)
                {
                    IsTidalConnected = false;
                    _isTidalApiAvailable = false;
                    _hasTidalPlaylistScopes = false;
                    TidalConnectionStatus = string.IsNullOrWhiteSpace(TidalClientId) ? "Not configured" : "Not connected";
                    TidalGrantedScopesDisplay = "Granted scopes: none";
                    TidalOperationMessage = "Experimental TIDAL catalog access is disabled.";
                    ClearTidalSessionContent();
                }
            }
        }
    }

    public string TidalConnectionStatus
    {
        get => _tidalConnectionStatus;
        private set => SetProperty(ref _tidalConnectionStatus, value);
    }

    public bool IsTidalConnected
    {
        get => _isTidalConnected;
        private set
        {
            if (SetProperty(ref _isTidalConnected, value))
            {
                OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
                OnPropertyChanged(nameof(TidalConnectActionVisibility));
                OnPropertyChanged(nameof(TidalDisconnectActionVisibility));
            }
        }
    }

    public bool IsTidalBusy
    {
        get => _isTidalBusy;
        private set => SetProperty(ref _isTidalBusy, value);
    }

    public string TidalGrantedScopesDisplay
    {
        get => _tidalGrantedScopesDisplay;
        private set => SetProperty(ref _tidalGrantedScopesDisplay, value);
    }

    public string TidalOperationMessage
    {
        get => _tidalOperationMessage;
        private set => SetProperty(ref _tidalOperationMessage, value);
    }

    public bool TidalCatalogControlsEnabled =>
        ExperimentalTidalCatalogEnabled && IsTidalConnected && !IsTidalBusy;

    public Visibility TidalConnectActionVisibility =>
        IsTidalConnected ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TidalDisconnectActionVisibility =>
        IsTidalConnected ? Visibility.Visible : Visibility.Collapsed;

    public string TidalSearchQuery
    {
        get => _tidalSearchQuery;
        set => SetProperty(ref _tidalSearchQuery, value);
    }

    public ObservableCollection<TidalTrackMetadata> TidalTrackResults { get; } = [];

    public ObservableCollection<TidalAlbumMetadata> TidalAlbumResults { get; } = [];

    public ObservableCollection<TidalArtistMetadata> TidalArtistResults { get; } = [];

    public ObservableCollection<TidalPlaylistMetadata> TidalPlaylists { get; } = [];

    public ObservableCollection<TidalTrackMetadata> SelectedTidalPlaylistTracks { get; } = [];

    public TidalTrackMetadata? SelectedTidalTrack
    {
        get => _selectedTidalTrack;
        set
        {
            if (SetProperty(ref _selectedTidalTrack, value) && value is not null)
            {
                _selectedTidalAlbum = null;
                _selectedTidalArtist = null;
                OnPropertyChanged(nameof(SelectedTidalAlbum));
                OnPropertyChanged(nameof(SelectedTidalArtist));
                NotifyTidalSelectionChanged();
            }
        }
    }

    public TidalAlbumMetadata? SelectedTidalAlbum
    {
        get => _selectedTidalAlbum;
        set
        {
            if (SetProperty(ref _selectedTidalAlbum, value) && value is not null)
            {
                _selectedTidalTrack = null;
                _selectedTidalArtist = null;
                OnPropertyChanged(nameof(SelectedTidalTrack));
                OnPropertyChanged(nameof(SelectedTidalArtist));
                NotifyTidalSelectionChanged();
            }
        }
    }

    public TidalArtistMetadata? SelectedTidalArtist
    {
        get => _selectedTidalArtist;
        set
        {
            if (SetProperty(ref _selectedTidalArtist, value) && value is not null)
            {
                _selectedTidalTrack = null;
                _selectedTidalAlbum = null;
                OnPropertyChanged(nameof(SelectedTidalTrack));
                OnPropertyChanged(nameof(SelectedTidalAlbum));
                NotifyTidalSelectionChanged();
            }
        }
    }

    public TidalPlaylistMetadata? SelectedTidalPlaylist
    {
        get => _selectedTidalPlaylist;
        set => SetProperty(ref _selectedTidalPlaylist, value);
    }

    public string SelectedTidalItemTitle =>
        SelectedTidalTrack?.Title ?? SelectedTidalAlbum?.Title ?? SelectedTidalArtist?.Name ?? "No TIDAL item selected";

    public string SelectedTidalItemSubtitle =>
        SelectedTidalTrack?.Artist ?? SelectedTidalAlbum?.Artist ?? "Select a catalog result to inspect it.";

    public string? SelectedTidalItemArtwork =>
        SelectedTidalTrack?.ArtworkReference ?? SelectedTidalAlbum?.ArtworkReference ?? SelectedTidalArtist?.ArtworkReference;

    public string SelectedTidalItemMetadata => SelectedTidalTrack is { } track
        ? string.Join(" | ", new[]
        {
            track.Album,
            track.Duration is { } duration ? $"{(int)duration.TotalMinutes}:{duration.Seconds:00}" : null,
            track.IsExplicit == true ? "Explicit" : null,
            track.Availability
        }.Where(value => !string.IsNullOrWhiteSpace(value)))
        : SelectedTidalAlbum is not null ? "Album" : SelectedTidalArtist is not null ? "Artist" : string.Empty;

    public string TidalPlaylistAvailabilityMessage
    {
        get => _tidalPlaylistAvailabilityMessage;
        private set => SetProperty(ref _tidalPlaylistAvailabilityMessage, value);
    }

    public IAsyncRelayCommand ConnectTidalCommand { get; }

    public IAsyncRelayCommand ReconnectTidalCommand { get; }

    public IAsyncRelayCommand DisconnectTidalCommand { get; }

    public IAsyncRelayCommand RefreshTidalConnectionCommand { get; }

    public IAsyncRelayCommand CheckTidalApiCommand { get; }

    public IRelayCommand CopyTidalDiagnosticCommand { get; }

    public IAsyncRelayCommand SearchTidalCommand { get; }

    public IAsyncRelayCommand LoadTidalPlaylistsCommand { get; }

    public IAsyncRelayCommand LoadSelectedTidalPlaylistTracksCommand { get; }

    public IAsyncRelayCommand OpenSelectedTidalTrackCommand { get; }

    public IAsyncRelayCommand OpenSelectedTidalAlbumCommand { get; }

    public IAsyncRelayCommand OpenSelectedTidalArtistCommand { get; }

    public IAsyncRelayCommand OpenSelectedTidalPlaylistCommand { get; }

    public IAsyncRelayCommand OpenSelectedTidalItemCommand { get; }

    public IRelayCommand OpenTidalCatalogCommand { get; }

    private TidalSettings CurrentTidalSettings => new()
    {
        ClientId = TidalClientId.Trim(),
        RedirectUri = string.IsNullOrWhiteSpace(TidalRedirectUri)
            ? TidalDefaults.RedirectUri
            : TidalRedirectUri.Trim(),
        CountryCode = string.IsNullOrWhiteSpace(TidalCountryCode) ? "US" : TidalCountryCode.Trim().ToUpperInvariant(),
        ExperimentalCatalogEnabled = ExperimentalTidalCatalogEnabled
    };

    public Task SaveTidalSettingsAsync(CancellationToken cancellationToken = default) =>
        _tidalSettingsStore.SaveAsync(CurrentTidalSettings, cancellationToken);

    private async Task ConnectTidalAsync()
    {
        if (IsTidalBusy)
        {
            return;
        }

        ExperimentalTidalCatalogEnabled = true;
        IsTidalBusy = true;
        TidalConnectionStatus = "Connecting";
        TidalOperationMessage = "Complete authorization in the system browser. Never enter your TIDAL password into DancePilot.";
        try
        {
            await SaveTidalSettingsAsync();
            await _tidalAuthService.LoginAsync(CurrentTidalSettings, TidalScopes.Requested);
            await RefreshTidalConnectionAsync();
            TidalOperationMessage = "TIDAL authorization completed. Check the public catalog API before searching.";
        }
        catch (TidalApiException ex)
        {
            IsTidalConnected = false;
            TidalConnectionStatus = string.IsNullOrWhiteSpace(TidalClientId) ? "Not configured" : "Not connected";
            RecordTidalError(ex);
        }
        catch (OperationCanceledException)
        {
            IsTidalConnected = false;
            TidalConnectionStatus = "Not connected";
            TidalOperationMessage = "TIDAL authorization was cancelled.";
        }
        finally
        {
            IsTidalBusy = false;
            OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        }
    }

    private async Task ReconnectTidalAsync()
    {
        await _tidalAuthService.LogoutAsync();
        IsTidalConnected = false;
        _isTidalApiAvailable = false;
        _hasTidalPlaylistScopes = false;
        ClearTidalSessionContent();
        await ConnectTidalAsync();
    }

    private async Task DisconnectTidalAsync()
    {
        await _tidalAuthService.LogoutAsync();
        IsTidalConnected = false;
        _isTidalApiAvailable = false;
        TidalConnectionStatus = "Not connected";
        TidalGrantedScopesDisplay = "Granted scopes: none";
        TidalOperationMessage = "TIDAL disconnected. Encrypted tokens were deleted.";
        ClearTidalSessionContent();
    }

    private async Task RefreshTidalConnectionAsync()
    {
        if (!ExperimentalTidalCatalogEnabled)
        {
            IsTidalConnected = false;
            TidalConnectionStatus = string.IsNullOrWhiteSpace(TidalClientId) ? "Not configured" : "Not connected";
            return;
        }

        try
        {
            var token = await _tidalTokenStore.GetAsync();
            if (token is null)
            {
                IsTidalConnected = false;
                TidalConnectionStatus = string.IsNullOrWhiteSpace(TidalClientId) ? "Not configured" : "Not connected";
                TidalGrantedScopesDisplay = "Granted scopes: none";
                return;
            }

            if (token.IsExpired)
            {
                token = await _tidalAuthService.RefreshAsync(token);
            }

            IsTidalConnected = true;
            _isTidalApiAvailable = false;
            TidalConnectionStatus = "Connected; API unchecked";
            UpdateTidalScopeState(token);
        }
        catch (TidalApiException ex)
        {
            IsTidalConnected = false;
            TidalConnectionStatus = "Refresh required";
            TidalOperationMessage = FormatTidalError(ex);
        }
    }

    private async Task CheckTidalApiAsync()
    {
        if (IsTidalBusy)
        {
            return;
        }

        IsTidalBusy = true;
        OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        TidalOperationMessage = "Checking the official TIDAL catalog API...";
        try
        {
            var health = await _tidalCatalogService.CheckApiAsync(CurrentTidalSettings);
            _isTidalApiAvailable = true;
            TidalConnectionStatus = HasTidalPlaylistScopes()
                ? "Connected; catalog API available"
                : "Connected; catalog available; playlist scope missing";
            TidalOperationMessage = $"TIDAL public catalog API is available (album {health.AlbumId}: {health.AlbumTitle}).";
        }
        catch (TidalApiException ex)
        {
            _isTidalApiAvailable = false;
            if (ex.Kind is TidalApiErrorKind.Unauthorized or TidalApiErrorKind.ExpiredToken)
            {
                IsTidalConnected = false;
                TidalConnectionStatus = "Reconnect required";
            }
            else
            {
                TidalConnectionStatus = "Connected; API check failed";
            }
            RecordTidalError(ex);
        }
        finally
        {
            IsTidalBusy = false;
            OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        }
    }

    private async Task SearchTidalAsync()
    {
        if (IsTidalBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TidalSearchQuery))
        {
            TidalOperationMessage = "Enter a track, album, or artist to search the TIDAL catalog.";
            return;
        }

        IsTidalBusy = true;
        OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        TidalOperationMessage = "Searching the TIDAL catalog...";
        try
        {
            if (!_isTidalApiAvailable)
            {
                await _tidalCatalogService.CheckApiAsync(CurrentTidalSettings);
                _isTidalApiAvailable = true;
                TidalConnectionStatus = HasTidalPlaylistScopes()
                    ? "Connected; catalog API available"
                    : "Connected; catalog available; playlist scope missing";
            }

            var results = await _tidalCatalogService.SearchAsync(CurrentTidalSettings, TidalSearchQuery);
            ReplaceTidalCollection(TidalTrackResults, results.Tracks);
            ReplaceTidalCollection(TidalAlbumResults, results.Albums);
            ReplaceTidalCollection(TidalArtistResults, results.Artists);
            if (TidalTrackResults.Count + TidalAlbumResults.Count + TidalArtistResults.Count == 0)
            {
                TidalConnectionStatus = "Connected; catalog API available";
                TidalOperationMessage = "TIDAL search returned no results.";
            }
            else
            {
                TidalConnectionStatus = "Connected; catalog API available";
                TidalOperationMessage = $"Found {TidalTrackResults.Count} tracks, {TidalAlbumResults.Count} albums, and {TidalArtistResults.Count} artists.";
            }
        }
        catch (TidalApiException ex)
        {
            RecordTidalError(ex);
            if (ex.Kind != TidalApiErrorKind.MissingRequiredScope)
            {
                TidalConnectionStatus = "Connected; search failed with API detail";
            }
            if (ex.Kind is TidalApiErrorKind.Unauthorized or TidalApiErrorKind.ExpiredToken)
            {
                IsTidalConnected = false;
                TidalConnectionStatus = "Reconnect required";
            }
        }
        finally
        {
            IsTidalBusy = false;
            OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        }
    }

    private async Task LoadTidalPlaylistsAsync()
    {
        if (IsTidalBusy)
        {
            return;
        }

        IsTidalBusy = true;
        OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        TidalOperationMessage = "Loading TIDAL playlist collection...";
        try
        {
            var playlists = await _tidalCatalogService.LoadUserPlaylistsAsync(CurrentTidalSettings);
            ReplaceTidalCollection(TidalPlaylists, playlists);
            TidalPlaylistAvailabilityMessage = playlists.Count == 0
                ? "No playlists were returned from My Collection or the owned-playlist query."
                : $"Loaded {playlists.Count} playlists. Labels distinguish My Collection from playlists owned by this account.";
            TidalConnectionStatus = "Connected; playlists loaded";
            TidalOperationMessage = TidalPlaylistAvailabilityMessage;
        }
        catch (TidalApiException ex)
        {
            TidalConnectionStatus = ex.Kind == TidalApiErrorKind.MissingRequiredScope
                ? "Connected; catalog available; playlist scope missing"
                : "Connected; playlist collection unavailable";
            TidalPlaylistAvailabilityMessage = FormatTidalError(ex);
            RecordTidalError(ex);
        }
        finally
        {
            IsTidalBusy = false;
            OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        }
    }

    private async Task LoadSelectedTidalPlaylistTracksAsync()
    {
        if (SelectedTidalPlaylist is null)
        {
            TidalOperationMessage = "Select a TIDAL playlist before loading its tracks.";
            return;
        }

        IsTidalBusy = true;
        OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        TidalOperationMessage = $"Loading tracks from {SelectedTidalPlaylist.Title}...";
        try
        {
            var tracks = await _tidalCatalogService.LoadPlaylistTracksAsync(
                CurrentTidalSettings,
                SelectedTidalPlaylist.ProviderPlaylistId);
            ReplaceTidalCollection(SelectedTidalPlaylistTracks, tracks);
            TidalOperationMessage = $"Loaded {tracks.Count} music tracks in playlist order. Video items were skipped.";
        }
        catch (TidalApiException ex)
        {
            RecordTidalError(ex);
        }
        finally
        {
            IsTidalBusy = false;
            OnPropertyChanged(nameof(TidalCatalogControlsEnabled));
        }
    }

    private Task OpenSelectedTidalTrackAsync() => OpenTidalUriAsync(SelectedTidalTrack?.TidalPageUrl);

    private Task OpenSelectedTidalAlbumAsync() => OpenTidalUriAsync(SelectedTidalAlbum?.TidalPageUrl);

    private Task OpenSelectedTidalArtistAsync() => OpenTidalUriAsync(SelectedTidalArtist?.TidalPageUrl);

    private Task OpenSelectedTidalPlaylistAsync() => OpenTidalUriAsync(SelectedTidalPlaylist?.TidalPageUrl);

    private Task OpenSelectedTidalItemAsync()
    {
        if (SelectedTidalTrack is not null)
        {
            return OpenSelectedTidalTrackAsync();
        }

        if (SelectedTidalAlbum is not null)
        {
            return OpenSelectedTidalAlbumAsync();
        }

        return OpenSelectedTidalArtistAsync();
    }

    private async Task OpenTidalUriAsync(string? value)
    {
        if (!TidalLink.TryCreateOfficialUri(value, out var uri) || uri is null)
        {
            TidalOperationMessage = "This item does not include a valid official TIDAL link.";
            return;
        }

        if (!await Launcher.LaunchUriAsync(uri))
        {
            TidalOperationMessage = "Windows could not open this item in TIDAL.";
        }
    }

    private void OpenTidalCatalog()
    {
        ShowLibraryManagerWorkspace();
        SelectedLibraryManagerSection = LibrarySectionTidalCatalog;
    }

    private void ClearTidalSessionContent()
    {
        TidalTrackResults.Clear();
        TidalAlbumResults.Clear();
        TidalArtistResults.Clear();
        TidalPlaylists.Clear();
        SelectedTidalPlaylistTracks.Clear();
        _selectedTidalTrack = null;
        _selectedTidalAlbum = null;
        _selectedTidalArtist = null;
        _selectedTidalPlaylist = null;
        OnPropertyChanged(nameof(SelectedTidalTrack));
        OnPropertyChanged(nameof(SelectedTidalAlbum));
        OnPropertyChanged(nameof(SelectedTidalArtist));
        OnPropertyChanged(nameof(SelectedTidalPlaylist));
        NotifyTidalSelectionChanged();
    }

    private void UpdateTidalScopeState(TidalTokenSet token)
    {
        var scopes = TidalScopes.Parse(token.Scope).Order(StringComparer.Ordinal).ToList();
        TidalGrantedScopesDisplay = scopes.Count == 0
            ? "Granted scopes: none"
            : $"Granted scopes: {string.Join(", ", scopes)}";
        var missing = TidalScopes.Missing(token.Scope, TidalScopes.UserPlaylists);
        _hasTidalPlaylistScopes = missing.Count == 0;
        TidalPlaylistAvailabilityMessage = missing.Count == 0
            ? "My Collection and owned playlists are available through the documented public API."
            : $"Missing playlist scope: {string.Join(", ", missing)}. {TidalCatalogCapabilities.ReconnectForScopesMessage}";
    }

    private bool HasTidalPlaylistScopes() => _hasTidalPlaylistScopes;

    private void RecordTidalError(TidalApiException exception)
    {
        _lastTidalDiagnostic = exception.Diagnostic?.ToLogString();
        TidalOperationMessage = FormatTidalError(exception);
    }

    private void CopyTidalDiagnostic()
    {
        if (string.IsNullOrWhiteSpace(_lastTidalDiagnostic))
        {
            TidalOperationMessage = "No TIDAL request diagnostic is available to copy.";
            return;
        }

        var package = new DataPackage();
        package.SetText(_lastTidalDiagnostic);
        Clipboard.SetContent(package);
        TidalOperationMessage = "Sanitized TIDAL request diagnostic copied.";
    }

    private void NotifyTidalSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedTidalItemTitle));
        OnPropertyChanged(nameof(SelectedTidalItemSubtitle));
        OnPropertyChanged(nameof(SelectedTidalItemArtwork));
        OnPropertyChanged(nameof(SelectedTidalItemMetadata));
    }

    private static void ReplaceTidalCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static string FormatTidalError(TidalApiException exception) => exception.Kind switch
    {
        TidalApiErrorKind.InvalidRequest => exception.Message,
        TidalApiErrorKind.RedirectPortConflict => exception.Message,
        TidalApiErrorKind.UserCancelled => "TIDAL authorization was declined or cancelled.",
        TidalApiErrorKind.AuthorizationFailed => exception.Message,
        TidalApiErrorKind.AuthorizationTimedOut => exception.Message,
        TidalApiErrorKind.Unauthorized when exception.Diagnostic is not null => exception.Message,
        TidalApiErrorKind.Unauthorized => "TIDAL rejected the authorization. Reconnect and grant the required access.",
        TidalApiErrorKind.ExpiredToken => "TIDAL access expired and could not be refreshed. Reconnect TIDAL.",
        TidalApiErrorKind.MissingRequiredScope => exception.Message,
        TidalApiErrorKind.Forbidden => exception.Message,
        TidalApiErrorKind.NotFound => exception.Message,
        TidalApiErrorKind.RateLimited => exception.RetryAfter is { } delay
            ? $"TIDAL rate limited this request (429). Try again in {Math.Ceiling(delay.TotalSeconds):N0} seconds."
            : "TIDAL rate limited this request (429). Try again shortly.",
        TidalApiErrorKind.NetworkUnavailable => "TIDAL is unreachable. Check the network connection and try again.",
        TidalApiErrorKind.EndpointUnavailable => TidalCatalogCapabilities.UserPlaylistsUnavailableMessage,
        TidalApiErrorKind.MalformedResponse => exception.Message,
        TidalApiErrorKind.TemporaryFailure => exception.Message,
        _ => exception.Message
    };
}
