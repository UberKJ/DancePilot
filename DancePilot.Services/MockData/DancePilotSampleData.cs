using DancePilot.Core.Models;

namespace DancePilot.Services.MockData;

public static class DancePilotSampleData
{
    public static IReadOnlyList<Song> Songs { get; } =
    [
        new Song { Id = 1, Title = "Blue Suede Shoes", Artist = "Elvis Presley", Duration = TimeSpan.FromSeconds(128), BPM = 122, Key = "A", EnergyScore = 7, Genre = "50s Rock & Roll", Tags = "Classic, Swing", Rating = 4.8, AlbumArtPath = "ms-appx:///Assets/AlbumBlueSuede.png" },
        new Song { Id = 2, Title = "Rockin' Robin", Artist = "Bobby Day", Duration = TimeSpan.FromSeconds(155), BPM = 121, Key = "A", EnergyScore = 7, Genre = "50s Rock & Roll", Tags = "Classic, Floor Filler", Rating = 4.6, AlbumArtPath = "ms-appx:///Assets/AlbumRockinRobin.png" },
        new Song { Id = 3, Title = "Rock Around The Clock", Artist = "Bill Haley & His Comets", Duration = TimeSpan.FromSeconds(132), BPM = 120, Key = "A", EnergyScore = 8, Genre = "50s Rock & Roll", Tags = "Swing, Classic", Rating = 4.9, AlbumArtPath = "ms-appx:///Assets/AlbumBlueSuede.png" },
        new Song { Id = 4, Title = "Jailhouse Rock", Artist = "Elvis Presley", Duration = TimeSpan.FromSeconds(149), BPM = 126, Key = "A", EnergyScore = 8, Genre = "50s Rock & Roll", Tags = "Classic, Upbeat", Rating = 4.7, AlbumArtPath = "ms-appx:///Assets/AlbumBlueSuede.png" },
        new Song { Id = 5, Title = "The Twist", Artist = "Chubby Checker", Duration = TimeSpan.FromSeconds(156), BPM = 157, Key = "C", EnergyScore = 9, Genre = "60s Pop & Rock", Tags = "Line Dance", Rating = 4.5, AlbumArtPath = "ms-appx:///Assets/AlbumRockinRobin.png" },
        new Song { Id = 6, Title = "Great Balls of Fire", Artist = "Jerry Lee Lewis", Duration = TimeSpan.FromSeconds(111), BPM = 117, Key = "G", EnergyScore = 8, Genre = "50s Rock & Roll", Tags = "Piano, Classic", Rating = 4.4, AlbumArtPath = "ms-appx:///Assets/AlbumBlueSuede.png" },
        new Song { Id = 7, Title = "Copperhead Road", Artist = "Steve Earle", Duration = TimeSpan.FromSeconds(269), BPM = 128, Key = "A", EnergyScore = 7, Genre = "Country Favorites", Tags = "Country, Line Dance", Rating = 4.7, AlbumArtPath = "ms-appx:///Assets/AlbumCountryRoad.png" },
        new Song { Id = 8, Title = "Boot Scootin' Boogie", Artist = "Brooks & Dunn", Duration = TimeSpan.FromSeconds(198), BPM = 136, Key = "A", EnergyScore = 8, Genre = "Country Favorites", Tags = "Country, Line Dance", Rating = 4.8, AlbumArtPath = "ms-appx:///Assets/AlbumCountryRoad.png" },
        new Song { Id = 9, Title = "Cupid Shuffle", Artist = "Cupid", Duration = TimeSpan.FromSeconds(229), BPM = 128, Key = "A", EnergyScore = 7, Genre = "Line Dance Party", Tags = "Line Dance", Rating = 4.6, AlbumArtPath = "ms-appx:///Assets/AlbumDanceFloor.png" },
        new Song { Id = 10, Title = "Electric Slide", Artist = "Marcia Griffiths", Duration = TimeSpan.FromSeconds(229), BPM = 125, Key = "A", EnergyScore = 7, Genre = "Line Dance Party", Tags = "Line Dance", Rating = 4.5, AlbumArtPath = "ms-appx:///Assets/AlbumDanceFloor.png" },
        new Song { Id = 11, Title = "Cha Cha Slide", Artist = "DJ Casper", Duration = TimeSpan.FromSeconds(223), BPM = 124, Key = "A", EnergyScore = 8, Genre = "Line Dance Party", Tags = "Line Dance", Rating = 4.7, AlbumArtPath = "ms-appx:///Assets/AlbumDanceFloor.png" },
        new Song { Id = 12, Title = "September", Artist = "Earth, Wind & Fire", Duration = TimeSpan.FromSeconds(216), BPM = 125, Key = "Ab", EnergyScore = 9, Genre = "70s Classics", Tags = "Dance, Classic", Rating = 4.9, AlbumArtPath = "ms-appx:///Assets/AlbumDanceFloor.png" }
    ];

    public static IReadOnlyList<PlaylistSummary> Playlists { get; } =
    [
        new PlaylistSummary { Name = "50s Rock & Roll", SongCount = 125 },
        new PlaylistSummary { Name = "60s Pop & Rock", SongCount = 142 },
        new PlaylistSummary { Name = "70s Classics", SongCount = 130 },
        new PlaylistSummary { Name = "80s Hits", SongCount = 118 },
        new PlaylistSummary { Name = "Country Favorites", SongCount = 96 },
        new PlaylistSummary { Name = "Line Dance Party", SongCount = 75, IsSelected = true },
        new PlaylistSummary { Name = "Slow Dance", SongCount = 60 },
        new PlaylistSummary { Name = "Wedding Songs", SongCount = 88 },
        new PlaylistSummary { Name = "Karaoke Night", SongCount = 70 },
        new PlaylistSummary { Name = "Christmas", SongCount = 55 }
    ];

    public static IReadOnlyList<SongRecommendation> Recommendations { get; } =
    [
        new SongRecommendation { Rank = 1, Title = "Rock Around The Clock", Artist = "Bill Haley & His Comets", BPM = 120, Key = "A", MatchPercent = 96 },
        new SongRecommendation { Rank = 2, Title = "Jailhouse Rock", Artist = "Elvis Presley", BPM = 126, Key = "A", MatchPercent = 94 },
        new SongRecommendation { Rank = 3, Title = "The Twist", Artist = "Chubby Checker", BPM = 157, Key = "C", MatchPercent = 91 },
        new SongRecommendation { Rank = 4, Title = "Great Balls of Fire", Artist = "Jerry Lee Lewis", BPM = 117, Key = "G", MatchPercent = 89 }
    ];

    public static IReadOnlyList<SongRequest> Requests { get; } =
    [
        new SongRequest { Position = 1, Title = "Sweet Caroline", Artist = "Neil Diamond" },
        new SongRequest { Position = 2, Title = "Brown Eyed Girl", Artist = "Van Morrison" },
        new SongRequest { Position = 3, Title = "Friends in Low Places", Artist = "Garth Brooks" },
        new SongRequest { Position = 4, Title = "Margaritaville", Artist = "Jimmy Buffett" },
        new SongRequest { Position = 5, Title = "Take Me Home Country Roads", Artist = "John Denver" }
    ];
}
