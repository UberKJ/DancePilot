using DancePilot.Services.Tidal;

namespace DancePilot.Tests;

public sealed class TidalLinkTests
{
    [Theory]
    [InlineData("https://tidal.com/browse/track/123")]
    [InlineData("https://listen.tidal.com/album/456")]
    public void TryCreateOfficialUri_AcceptsOfficialHttpsLinks(string value)
    {
        Assert.True(TidalLink.TryCreateOfficialUri(value, out var uri));
        Assert.Equal(value, uri!.AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("http://tidal.com/browse/track/123")]
    [InlineData("https://tidal.com.example.test/track/123")]
    [InlineData("https://example.test/track/123")]
    [InlineData("")]
    public void TryCreateOfficialUri_RejectsUnofficialOrUnsafeLinks(string value)
    {
        Assert.False(TidalLink.TryCreateOfficialUri(value, out var uri));
        Assert.Null(uri);
    }
}
