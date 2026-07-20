using DancePilot.Core.Models;

namespace DancePilot.Services.Tidal;

public static class TidalTrackDisplayItemAdapter
{
    public static TrackDisplayItem ToTrackDisplayItem(this TidalTrackMetadata track) => new()
    {
        Source = SongSources.Tidal,
        Title = track.Title,
        Artist = track.Artist,
        Album = track.Album,
        AlbumArt = track.ArtworkReference,
        Duration = track.Duration,
        ProviderTrackId = track.ProviderTrackId,
        PlaybackHint = "TIDAL catalog item; DancePilot playback is not enabled",
        IsPlayable = false
    };
}
