using DancePilot.Core.Models;

namespace DancePilot.Core.Interfaces;

public interface ISongAnalysisService
{
    Task<Song> AnalyzeAsync(Song song, CancellationToken cancellationToken = default);
}
