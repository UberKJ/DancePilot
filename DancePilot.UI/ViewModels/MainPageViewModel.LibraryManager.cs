using DancePilot.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DancePilot.UI.ViewModels;

public sealed partial class MainPageViewModel
{
    private const string WorkspaceLiveEvent = "Live Event";
    private const string WorkspaceLibraryManager = "Library Manager";
    private const string LibrarySectionMusicLibrary = "Music Library";
    private const string LibrarySectionPlaylistBuilder = "Playlist Builder";
    private const string LibrarySectionEventTemplates = "Event Templates";
    private const string LibrarySectionCollections = "Collections";
    private const string LibrarySectionLibraryHealth = "Library Health";
    private const string LibrarySectionImportCenter = "Import Center";
    private const string LibrarySectionTidalCatalog = "Experimental TIDAL Catalog";
    private const string LibrarySectionAiAssistant = "AI Playlist Assistant";

    private string _selectedWorkspace = WorkspaceLiveEvent;
    private string _selectedLibraryManagerSection = LibrarySectionMusicLibrary;

    public IReadOnlyList<string> LibraryManagerSections { get; } =
    [
        LibrarySectionMusicLibrary,
        LibrarySectionPlaylistBuilder,
        LibrarySectionEventTemplates,
        LibrarySectionCollections,
        LibrarySectionLibraryHealth,
        LibrarySectionImportCenter,
        LibrarySectionTidalCatalog,
        LibrarySectionAiAssistant
    ];

    public string SelectedWorkspace
    {
        get => _selectedWorkspace;
        set
        {
            var normalized = string.Equals(value, WorkspaceLibraryManager, StringComparison.Ordinal)
                ? WorkspaceLibraryManager
                : WorkspaceLiveEvent;
            if (SetProperty(ref _selectedWorkspace, normalized))
            {
                OnPropertyChanged(nameof(LiveEventWorkspaceVisibility));
                OnPropertyChanged(nameof(LibraryManagerWorkspaceVisibility));
                OnPropertyChanged(nameof(LiveEventWorkspaceButtonBackground));
                OnPropertyChanged(nameof(LiveEventWorkspaceButtonBorderBrush));
                OnPropertyChanged(nameof(LiveEventWorkspaceButtonForeground));
                OnPropertyChanged(nameof(LibraryManagerWorkspaceButtonBackground));
                OnPropertyChanged(nameof(LibraryManagerWorkspaceButtonBorderBrush));
                OnPropertyChanged(nameof(LibraryManagerWorkspaceButtonForeground));
            }
        }
    }

    public Visibility LiveEventWorkspaceVisibility =>
        SelectedWorkspace == WorkspaceLiveEvent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LibraryManagerWorkspaceVisibility =>
        SelectedWorkspace == WorkspaceLibraryManager ? Visibility.Visible : Visibility.Collapsed;

    public Brush LiveEventWorkspaceButtonBackground =>
        WorkspaceButtonBackground(WorkspaceLiveEvent);

    public Brush LiveEventWorkspaceButtonBorderBrush =>
        WorkspaceButtonBorderBrush(WorkspaceLiveEvent);

    public Brush LiveEventWorkspaceButtonForeground =>
        WorkspaceButtonForeground(WorkspaceLiveEvent);

    public Brush LibraryManagerWorkspaceButtonBackground =>
        WorkspaceButtonBackground(WorkspaceLibraryManager);

    public Brush LibraryManagerWorkspaceButtonBorderBrush =>
        WorkspaceButtonBorderBrush(WorkspaceLibraryManager);

    public Brush LibraryManagerWorkspaceButtonForeground =>
        WorkspaceButtonForeground(WorkspaceLibraryManager);

    public string SelectedLibraryManagerSection
    {
        get => _selectedLibraryManagerSection;
        set
        {
            var normalized = LibraryManagerSections.Contains(value) ? value : LibrarySectionMusicLibrary;
            if (SetProperty(ref _selectedLibraryManagerSection, normalized))
            {
                OnPropertyChanged(nameof(MusicLibrarySectionVisibility));
                OnPropertyChanged(nameof(TidalCatalogSectionVisibility));
                OnPropertyChanged(nameof(LibraryManagerPlaceholderVisibility));
                OnPropertyChanged(nameof(LibraryManagerPlaceholderTitle));
                OnPropertyChanged(nameof(LibraryManagerPlaceholderDescription));
                OnPropertyChanged(nameof(LibraryManagerPlaceholderVersionDisplay));
                OnPropertyChanged(nameof(LibraryManagerPlaceholderFeatureHeader));
                OnPropertyChanged(nameof(LibraryManagerPlaceholderItems));
            }
        }
    }

    public Visibility MusicLibrarySectionVisibility =>
        SelectedLibraryManagerSection == LibrarySectionMusicLibrary ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TidalCatalogSectionVisibility =>
        SelectedLibraryManagerSection == LibrarySectionTidalCatalog ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LibraryManagerPlaceholderVisibility =>
        SelectedLibraryManagerSection is LibrarySectionMusicLibrary or LibrarySectionTidalCatalog
            ? Visibility.Collapsed
            : Visibility.Visible;

    public string LibraryManagerPlaceholderTitle => SelectedLibraryManagerSection;

    public string LibraryManagerPlaceholderDescription => SelectedLibraryManagerSection switch
    {
        LibrarySectionPlaylistBuilder => "Create DancePilot playlists.",
        LibrarySectionEventTemplates => "Reusable event plans.",
        LibrarySectionCollections => "Permanent music groupings.",
        LibrarySectionLibraryHealth => "Library maintenance tools.",
        LibrarySectionImportCenter => "Music import tools.",
        LibrarySectionAiAssistant => "Future AI planning assistant.",
        _ => "Planning workspace section."
    };

    public string LibraryManagerPlaceholderVersionDisplay => SelectedLibraryManagerSection switch
    {
        LibrarySectionPlaylistBuilder => "Coming in Version 0.5.2",
        LibrarySectionEventTemplates => "Coming in Version 0.5.3",
        LibrarySectionCollections => "Coming in Version 0.5.4",
        LibrarySectionLibraryHealth => "Coming in Version 0.5.5",
        LibrarySectionImportCenter => "Coming in Version 0.5.6",
        LibrarySectionAiAssistant => "Coming in Version 0.5.7",
        _ => "Coming in Version 0.5.x"
    };

    public string LibraryManagerPlaceholderFeatureHeader => SelectedLibraryManagerSection switch
    {
        LibrarySectionCollections => "Examples",
        LibrarySectionAiAssistant => "Example requests",
        _ => "Future features"
    };

    public IReadOnlyList<string> LibraryManagerPlaceholderItems => SelectedLibraryManagerSection switch
    {
        LibrarySectionPlaylistBuilder =>
        [
            "Create playlist",
            "Save playlist",
            "Load playlist to Deck A",
            "Load playlist to Deck B",
            "Estimated duration",
            "AI playlist suggestions"
        ],
        LibrarySectionEventTemplates =>
        [
            "Friday Night Dance",
            "Wedding",
            "Cornhole",
            "Holiday",
            "RV Park"
        ],
        LibrarySectionCollections =>
        [
            "Country",
            "Classic Rock",
            "Favorites",
            "Christmas",
            "Background Music"
        ],
        LibrarySectionLibraryHealth =>
        [
            "Duplicate songs",
            "Missing files",
            "Broken paths",
            "Missing artwork",
            "Metadata repair"
        ],
        LibrarySectionImportCenter =>
        [
            "Add Folder",
            "Import Playlist",
            "Import M3U",
            "Import ZIP",
            "Rescan Library"
        ],
        LibrarySectionAiAssistant =>
        [
            "Build a 3-hour country dance.",
            "Create a cornhole playlist.",
            "Improve this playlist.",
            "Avoid duplicate artists."
        ],
        _ => []
    };

    public string LibraryManagerCurrentSourcePath => LocalMusicFolderPath;

    public string LibraryManagerCurrentSourceStatus => LocalLibraryTrackCountDisplay;

    public string LibraryManagerSongCountDisplay => $"{_localLibrarySavedTrackCount:N0} Songs";

    public string LibraryManagerArtistCountDisplay
    {
        get
        {
            var count = LocalMusicTracks
                .Select(track => track.DisplayArtist)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return $"{count:N0} Artists";
        }
    }

    public string LibraryManagerAlbumCountDisplay
    {
        get
        {
            var count = LocalMusicTracks
                .Select(track => track.Album)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return $"{count:N0} Albums";
        }
    }

    public string LibraryManagerLastScanDisplay => LocalLibraryLastScanDisplay;

    public ImageSource SelectedLibrarySongAlbumArt =>
        SelectedLocalMusicTrack is null
            ? CreateAlbumArtSource(null, DefaultLocalAlbumArtPath)
            : CreateAlbumArtSource(ResolveLocalTrackAlbumArt(SelectedLocalMusicTrack), DefaultLocalAlbumArtPath);

    public string SelectedLibrarySongTitle =>
        SelectedLocalMusicTrack?.Title ?? "No song selected";

    public string SelectedLibrarySongArtist =>
        SelectedLocalMusicTrack?.DisplayArtist ?? "Select a song from the library.";

    public string SelectedLibrarySongAlbum =>
        FormatSelectedSongValue("Album", SelectedLocalMusicTrack?.Album);

    public string SelectedLibrarySongGenre =>
        FormatSelectedSongValue("Genre", SelectedLocalMusicTrack?.Genre);

    public string SelectedLibrarySongYear =>
        FormatSelectedSongValue("Year", SelectedLocalMusicTrack?.Year?.ToString());

    public string SelectedLibrarySongDuration =>
        SelectedLocalMusicTrack is null ? "Duration: -" : $"Duration: {SelectedLocalMusicTrack.DurationDisplay}";

    public string SelectedLibrarySongFileLocation =>
        FormatSelectedSongValue("File Location", SelectedLocalMusicTrack?.FilePath);

    public string SelectedLibrarySongProvider =>
        SelectedLocalMusicTrack is null ? "Provider: -" : "Provider: Local files";

    public string SelectedLibrarySongFavorite => "Favorite: placeholder";

    public string SelectedLibrarySongRating => "Rating: placeholder";

    public string SelectedLibrarySongCollections => "Collections: placeholder";

    public void ShowLiveEventWorkspace()
    {
        SelectedWorkspace = WorkspaceLiveEvent;
    }

    public void ShowLibraryManagerWorkspace()
    {
        SelectedWorkspace = WorkspaceLibraryManager;
    }

    private Brush WorkspaceButtonBackground(string workspace) =>
        SelectedWorkspace == workspace ? Brush("#123047") : Brush("#111922");

    private Brush WorkspaceButtonBorderBrush(string workspace) =>
        SelectedWorkspace == workspace ? Brush("#1EA7FF") : Brush("#27313B");

    private Brush WorkspaceButtonForeground(string workspace) =>
        SelectedWorkspace == workspace ? Brush("#F5F8FA") : Brush("#B5C0C9");

    private static string FormatSelectedSongValue(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? $"{label}: -" : $"{label}: {value.Trim()}";

    private void NotifyLibraryManagerLocalLibraryChanged()
    {
        OnPropertyChanged(nameof(LibraryManagerCurrentSourcePath));
        OnPropertyChanged(nameof(LibraryManagerCurrentSourceStatus));
        OnPropertyChanged(nameof(LibraryManagerSongCountDisplay));
        OnPropertyChanged(nameof(LibraryManagerArtistCountDisplay));
        OnPropertyChanged(nameof(LibraryManagerAlbumCountDisplay));
        OnPropertyChanged(nameof(LibraryManagerLastScanDisplay));
    }

    private void NotifyLibraryManagerSelectedSongChanged()
    {
        OnPropertyChanged(nameof(SelectedLibrarySongAlbumArt));
        OnPropertyChanged(nameof(SelectedLibrarySongTitle));
        OnPropertyChanged(nameof(SelectedLibrarySongArtist));
        OnPropertyChanged(nameof(SelectedLibrarySongAlbum));
        OnPropertyChanged(nameof(SelectedLibrarySongGenre));
        OnPropertyChanged(nameof(SelectedLibrarySongYear));
        OnPropertyChanged(nameof(SelectedLibrarySongDuration));
        OnPropertyChanged(nameof(SelectedLibrarySongFileLocation));
        OnPropertyChanged(nameof(SelectedLibrarySongProvider));
        OnPropertyChanged(nameof(SelectedLibrarySongFavorite));
        OnPropertyChanged(nameof(SelectedLibrarySongRating));
        OnPropertyChanged(nameof(SelectedLibrarySongCollections));
    }
}
