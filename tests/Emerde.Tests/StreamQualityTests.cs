using Emerde.Core;

namespace Emerde.Tests;

public sealed class StreamQualityTests
{
    [Fact]
    public void DouyinResolver_SelectsRequestedQualityStream()
    {
        string json = """
            {
              "data": {
                "data": [{
                  "status": 2,
                  "stream_url": {
                    "hls_pull_url_map": {
                      "ORIGIN": "https://example.test/origin.m3u8",
                      "FULL_HD1": "https://example.test/blue-ray.m3u8",
                      "HD1": "https://example.test/high.m3u8"
                    },
                    "flv_pull_url": {
                      "ORIGIN": "https://example.test/origin.flv",
                      "FULL_HD1": "https://example.test/blue-ray.flv",
                      "HD1": "https://example.test/high.flv"
                    }
                  }
                }]
              }
            }
            """;

        StreamResolverResult result = StreamResolver.ExtractDouyinWebEnterData(
            "https://live.douyin.com/123456",
            json,
            StreamQualityCatalog.High);

        Assert.Equal("https://example.test/high.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/high.flv", result.FlvUrl);
        Assert.Equal("HD1", result.Quality);
        Assert.Equal("高清", StreamQualityCatalog.GetDisplayName("Douyin", result.Quality, null));
    }

    [Fact]
    public void DouyinResolver_OriginalSelectsHighestObservableQuality()
    {
        string json = """
            {
              "data": {
                "data": [{
                  "status": 2,
                  "stream_url": {
                    "hls_pull_url_map": {
                      "ORIGIN": "https://example.test/origin.m3u8?resolution=1280x720&bitrate=1800",
                      "FULL_HD1": "https://example.test/full.m3u8?resolution=1920x1080&bitrate=6000"
                    },
                    "flv_pull_url": {
                      "ORIGIN": "https://example.test/origin.flv?resolution=1280x720&bitrate=1800",
                      "FULL_HD1": "https://example.test/full.flv?resolution=1920x1080&bitrate=6000"
                    }
                  }
                }]
              }
            }
            """;

        StreamResolverResult result = StreamResolver.ExtractDouyinWebEnterData(
            "https://live.douyin.com/123456",
            json,
            StreamQualityCatalog.Original);

        Assert.Contains("full.m3u8", result.HlsUrl);
        Assert.Contains("full.flv", result.FlvUrl);
        Assert.Equal("FULL_HD1", result.Quality);
        Assert.Equal("1920x1080", SpiderResultMetadata.GetResolution(result));
        Assert.Equal("6 Mbps", SpiderResultMetadata.GetBitrate(result));
    }

    [Fact]
    public void KuaishouResolver_SelectsRequestedVariantAndMetadata()
    {
        string json = """
            {
              "payload": {
                "author": { "name": "anchor" },
                "liveStream": {
                  "playUrls": {
                    "h264": {
                      "adaptationSet": {
                        "representation": [
                          { "url": "https://example.test/high.flv", "bitrate": 4500, "width": 1920, "height": 1080 },
                          { "url": "https://example.test/low.flv", "bitrate": 600, "width": 640, "height": 360 }
                        ]
                      }
                    }
                  }
                }
              }
            }
            """;
        KuaishouSpiderResult result = new();

        KuaishouSpider.ExtractInitialState(json, result, StreamQualityCatalog.Smooth);

        Assert.Equal("https://example.test/low.flv", result.FlvUrl);
        Assert.Equal(StreamQualityCatalog.Smooth, result.Quality);
        Assert.Equal("640x360", result.Resolution);
        Assert.Equal("600 Kbps", result.Bitrate);
    }

    [Fact]
    public void StreamMetadata_ParsesResolutionBitrateAndQualityFromUrl()
    {
        StreamResolverResult result = new()
        {
            FlvUrl = "https://example.test/live.flv?vwidth=1920&vheight=1080&bitrate=4500&quality=FULL_HD1",
        };

        Assert.Equal("1920x1080", SpiderResultMetadata.GetResolution(result));
        Assert.Equal("4.5 Mbps", SpiderResultMetadata.GetBitrate(result));
        Assert.Equal("FULL_HD1", SpiderResultMetadata.GetQuality(result));
    }

      [Fact]
      public void HlsMasterPlaylist_SelectsHighestResolutionAndAverageBandwidth()
      {
        const string playlist = """
          #EXTM3U
          #EXT-X-STREAM-INF:BANDWIDTH=2500000,AVERAGE-BANDWIDTH=1800000,RESOLUTION=1280x720
          720/index.m3u8
          #EXT-X-STREAM-INF:BANDWIDTH=6500000,AVERAGE-BANDWIDTH=5200000,RESOLUTION=1920x1080
          1080/index.m3u8
          #EXT-X-STREAM-INF:BANDWIDTH=9000000,RESOLUTION=1920x1080
          https://cdn.example.test/1080-high/index.m3u8
          """;

        HlsVariant result = StreamResolver.ParseHighestHlsVariant("https://example.test/live/master.m3u8", playlist);

        Assert.Equal("https://cdn.example.test/1080-high/index.m3u8", result.Url);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal(9000000, result.Bandwidth);
      }

    [Fact]
    public void HlsMasterPlaylist_RelativeVariantInheritsMasterQuery()
    {
        const string playlist = """
          #EXTM3U
          #EXT-X-STREAM-INF:BANDWIDTH=6500000,RESOLUTION=1920x1080
          1080/index.m3u8
          """;

        HlsVariant result = StreamResolver.ParseHighestHlsVariant(
            "https://cdn.example.test/live/master.m3u8?token=abc&expires=123",
            playlist);

        Assert.Equal("https://cdn.example.test/live/1080/index.m3u8?token=abc&expires=123", result.Url);
    }

    [Fact]
    public void HlsMasterPlaylist_RelativeVariantPreservesOwnQuery()
    {
        const string playlist = """
          #EXTM3U
          #EXT-X-STREAM-INF:BANDWIDTH=6500000,RESOLUTION=1920x1080
          1080/index.m3u8?token=child
          """;

        HlsVariant result = StreamResolver.ParseHighestHlsVariant(
            "https://cdn.example.test/live/master.m3u8?token=master",
            playlist);

        Assert.Equal("https://cdn.example.test/live/1080/index.m3u8?token=child", result.Url);
    }

    [Fact]
    public void PlatformQualityOptions_OnlyExposeSupportedChoices()
    {
        Assert.Equal(6, StreamQualityCatalog.GetOptions("Douyin").Count);
        Assert.Equal(5, StreamQualityCatalog.GetOptions("Bilibili").Count);
        Assert.Equal(4, StreamQualityCatalog.GetOptions("Kuaishou").Count);
        Assert.Single(StreamQualityCatalog.GetOptions("Direct"));
    }
}
