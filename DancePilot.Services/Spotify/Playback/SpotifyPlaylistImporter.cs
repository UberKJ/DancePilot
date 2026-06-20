using DancePilot.Core.Spotify;
using DancePilot.Data.Repositories;

namespace DancePilot.Services.Spotify.Playback;

public sealed class SpotifyPlaylistImporter
{
    private readonly SpotifyService _spotifyService;
    private readonly SpotifyImportRepository _importRepository;

    public SpotifyPlaylistImporter(SpotifyService spotifyService, SpotifyImportRepository importRepository)
    {
        _spotifyService = spotifyService;
        _importRepository = importRepository;
    }

    public Task<IReadOnlyList<SpotifyPlaylistSummary>> GetUserPlaylistsAsync(
        SpotifySettings settings,
        CancellationToken cancellationToken = default) =>
        _spotifyService.GetUserPlaylistsAsync(settings, cancellationToken);

    public Task<IReadOnlyList<SpotifyTrackMetadata>> PreviewPlaylistTracksAsync(
        SpotifySettings settings,
        string playlistId,
        CancellationToken cancellationToken = default) =>
        _spotifyService.GetPlaylistTracksAsync(settings, playlistId, cancellationToken);

    public async Task<SpotifyImportResult> ImportPlaylistAsync(
        SpotifySettings settings,
        SpotifyPlaylistSummary playlist,
        CancellationToken cancellationToken = default)
    {
        var tracks = await _spotifyService.GetPlaylistTracksAsync(settings, playlist.SpotifyPlaylistId, cancellationToken);
        return await _importRepository.ImportPlaylistAsync(playlist, tracks, cancellationToken);
    }
}
