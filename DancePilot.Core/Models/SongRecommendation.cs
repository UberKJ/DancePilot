namespace DancePilot.Core.Models;

public sealed record SongRecommendation
{
    public required int Rank { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public required int BPM { get; init; }

    public required string Key { get; init; }

    public required int MatchPercent { get; init; }

    public string MixDisplay => $"{BPM} BPM / {Key}";

    public string MatchDisplay => $"{MatchPercent}%";
}
