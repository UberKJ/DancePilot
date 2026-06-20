using DancePilot.Core.Models;

namespace DancePilot.Core.Interfaces;

public interface IMusicProvider
{
    Task<IReadOnlyList<Song>> GetSongsAsync(CancellationToken cancellationToken = default);

    Task<Song?> GetCurrentSongAsync(CancellationToken cancellationToken = default);

    Task QueueSongAsync(Song song, CancellationToken cancellationToken = default);
}
