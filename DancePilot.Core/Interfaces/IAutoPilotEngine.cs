using DancePilot.Core.Models;

namespace DancePilot.Core.Interfaces;

public interface IAutoPilotEngine
{
    bool IsEnabled { get; }

    Song? RecommendNextSong(Song currentSong, IReadOnlyList<Song> queue);
}
