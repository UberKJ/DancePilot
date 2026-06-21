using DancePilot.Services.Media;
using System.Net;

namespace DancePilot.Tests;

public sealed class ExternalAlbumArtLookupServiceTests
{
    [Fact]
    public async Task FindAlbumArtAsync_ReturnsUpgradedArtworkUrlForMatchingSong()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler("""
            {
              "resultCount": 1,
              "results": [
                {
                  "artistName": "Oasis",
                  "trackName": "Wonderwall",
                  "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/Music/cover/100x100bb.jpg"
                }
              ]
            }
            """));
        var service = new ExternalAlbumArtLookupService(httpClient);

        var albumArtUrl = await service.FindAlbumArtAsync("Wonderwall", "Oasis");

        Assert.Equal("https://is1-ssl.mzstatic.com/image/thumb/Music/cover/600x600bb.jpg", albumArtUrl);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StubHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("itunes.apple.com", request.RequestUri?.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            });
        }
    }
}
