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
    public void DouyinResolver_OriginalFallsBackWhenCandidateMetadataIsIncomplete()
    {
        string json = """
            {
              "data": {
                "data": [{
                  "status": 2,
                  "stream_url": {
                    "hls_pull_url_map": {
                      "ORIGIN": "https://example.test/origin.m3u8",
                      "FULL_HD1": "https://example.test/full.m3u8?resolution=1280x720&bitrate=1800"
                    },
                    "flv_pull_url": {
                      "ORIGIN": "https://example.test/origin.flv",
                      "FULL_HD1": "https://example.test/full.flv?resolution=1280x720&bitrate=1800"
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

        Assert.Equal("https://example.test/origin.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/origin.flv", result.FlvUrl);
        Assert.Equal("ORIGIN", result.Quality);
    }

    [Fact]
    public void DouyinResolver_OriginalUsesPullDataOriginWhenAvailable()
    {
        string primaryStreamData = CreateDouyinStreamData(
            "https://example.test/primary.flv",
            "https://example.test/primary.m3u8",
            "h265");
        string landscapeStreamData = CreateDouyinStreamData(
            "https://example.test/landscape.flv?token=first",
            "https://example.test/landscape.m3u8?token=first",
            "h264",
            12000000,
            "1920x1080");
        string secondaryStreamData = CreateDouyinStreamData(
            "https://example.test/secondary.flv",
            "https://example.test/secondary.m3u8",
            "h266");
        string json = $$"""
            {
              "data": {
                "data": [{
                  "status": 2,
                  "stream_url": {
                    "live_core_sdk_data": {
                      "pull_data": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(primaryStreamData)}}
                      }
                    },
                    "pull_datas": {
                      "landscape": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(landscapeStreamData)}}
                      },
                      "secondary": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(secondaryStreamData)}}
                      }
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

        Assert.Equal("https://example.test/landscape.m3u8?token=first&codec=h265", result.HlsUrl);
        Assert.Equal("https://example.test/landscape.flv?token=first&codec=h265", result.FlvUrl);
        Assert.Equal("https://example.test/landscape.m3u8?token=first&codec=h265", result.RecordUrl);
        Assert.Equal("ORIGIN", result.Quality);
        Assert.Equal("1920x1080", result.Resolution);
        Assert.Equal("12 Mbps", result.Bitrate);
    }

    [Fact]
    public void DouyinResolver_OriginalUsesPrimaryOriginAndFlvForH264()
    {
        string primaryStreamData = CreateDouyinStreamData(
            "https://example.test/primary.flv?token=primary",
            "https://example.test/primary.m3u8?token=primary",
            "h264",
            6000000,
            "1280x720");
        string json = $$"""
            {
              "data": {
                "data": [{
                  "status": 2,
                  "stream_url": {
                    "hls_pull_url_map": {
                      "ORIGIN": "https://example.test/mapped-origin.m3u8"
                    },
                    "flv_pull_url": {
                      "ORIGIN": "https://example.test/mapped-origin.flv"
                    },
                    "live_core_sdk_data": {
                      "pull_data": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(primaryStreamData)}}
                      }
                    },
                    "pull_datas": {}
                  }
                }]
              }
            }
            """;

        StreamResolverResult result = StreamResolver.ExtractDouyinWebEnterData(
            "https://live.douyin.com/123456",
            json,
            StreamQualityCatalog.Original);

        Assert.Equal("https://example.test/primary.m3u8?token=primary&codec=h264", result.HlsUrl);
        Assert.Equal("https://example.test/primary.flv?token=primary&codec=h264", result.FlvUrl);
        Assert.Equal("https://example.test/primary.flv?token=primary&codec=h264", result.RecordUrl);
        Assert.Equal("ORIGIN", result.Quality);
        Assert.Equal("1280x720", result.Resolution);
        Assert.Equal("6 Mbps", result.Bitrate);
    }

    [Fact]
    public void DouyinResolver_PullDataWithoutOriginFallsBackToMappedOrigin()
    {
        string primaryStreamData = CreateDouyinStreamData(
            "https://example.test/primary.flv",
            "https://example.test/primary.m3u8",
            "h265");
        string pullDataWithoutOrigin = """
            {
              "data": {
                "uhd": {
                  "main": {
                    "flv": "https://example.test/uhd.flv"
                  }
                }
              }
            }
            """;
        string json = $$"""
            {
              "data": {
                "data": [{
                  "status": 2,
                  "stream_url": {
                    "hls_pull_url_map": {
                      "ORIGIN": "https://example.test/mapped-origin.m3u8"
                    },
                    "flv_pull_url": {
                      "ORIGIN": "https://example.test/mapped-origin.flv"
                    },
                    "live_core_sdk_data": {
                      "pull_data": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(primaryStreamData)}}
                      }
                    },
                    "pull_datas": {
                      "landscape": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(pullDataWithoutOrigin)}}
                      }
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

        Assert.Equal("https://example.test/mapped-origin.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/mapped-origin.flv", result.FlvUrl);
        Assert.Equal("ORIGIN", result.Quality);
    }

    [Fact]
    public void DouyinResolver_DoesNotMixDeclaredAndMappedProtocols()
    {
        string primaryStreamData = CreateDouyinStreamData(
            "https://example.test/primary.flv",
            "https://example.test/primary.m3u8",
            "h264");
        string pullDataStream = """
            {
              "data": {
                "origin": {
                  "main": {
                    "hls": "https://example.test/pull-origin.m3u8"
                  }
                }
              }
            }
            """;
        string json = $$"""
            {
              "data": {
                "data": [{
                  "status": 2,
                  "stream_url": {
                    "flv_pull_url": {
                      "ORIGIN": "https://example.test/mapped-origin.flv"
                    },
                    "live_core_sdk_data": {
                      "pull_data": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(primaryStreamData)}}
                      }
                    },
                    "pull_datas": {
                      "landscape": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(pullDataStream)}}
                      }
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

        Assert.Equal("https://example.test/pull-origin.m3u8?codec=h264", result.HlsUrl);
        Assert.Null(result.FlvUrl);
        Assert.Equal("https://example.test/pull-origin.m3u8?codec=h264", result.RecordUrl);
    }

    [Fact]
    public void DouyinResolver_InvalidDeclaredStreamFallsBackToMappedOrigin()
    {
        string json = """
            {
              "data": {
                "data": [{
                  "status": 2,
                  "stream_url": {
                    "hls_pull_url_map": {
                      "ORIGIN": "https://example.test/mapped-origin.m3u8"
                    },
                    "flv_pull_url": {
                      "ORIGIN": "https://example.test/mapped-origin.flv"
                    },
                    "live_core_sdk_data": "invalid",
                    "pull_datas": {
                      "invalid": "invalid"
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

        Assert.Equal("https://example.test/mapped-origin.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/mapped-origin.flv", result.FlvUrl);
        Assert.Equal("https://example.test/mapped-origin.flv", result.RecordUrl);
        Assert.Equal("ORIGIN", result.Quality);
        Assert.True(result.IsLiveStreaming);
    }

    [Fact]
    public void DouyinResolver_ReflowExtractsLiveCoreStreamForNonOriginalPreference()
    {
        string streamData = CreateDouyinStreamData(
            "https://example.test/reflow.flv",
            "https://example.test/reflow.m3u8",
            "h264",
            8000000,
            "1920x1080");
        string json = $$"""
            {
              "data": {
                "room": {
                  "status": 2,
                  "title": "linkmic live",
                  "owner": {
                    "nickname": "anchor",
                    "sec_uid": "sec-user"
                  },
                  "stream_url": {
                    "hls_pull_url_map": {},
                    "flv_pull_url": {},
                    "live_core_sdk_data": {
                      "pull_data": {
                        "stream_data": {{System.Text.Json.JsonSerializer.Serialize(streamData)}}
                      }
                    }
                  }
                }
              }
            }
            """;

        StreamResolverResult result = StreamResolver.ExtractDouyinReflowData(
            "https://live.douyin.com/123456",
            json,
            StreamQualityCatalog.High);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("linkmic live", result.Title);
        Assert.Equal("https://example.test/reflow.flv?codec=h264", result.RecordUrl);
        Assert.Equal("1920x1080", result.Resolution);
        Assert.Equal("8 Mbps", result.Bitrate);
    }

    [Fact]
    public void DouyinResolver_AppFallbackRequiresConfirmedLiveWithoutStream()
    {
        Assert.True(StreamResolver.NeedsDouyinAppFallback(new StreamResolverResult
        {
            IsLiveStreaming = true,
            Title = "live",
        }));
        Assert.False(StreamResolver.NeedsDouyinAppFallback(new StreamResolverResult
        {
            IsLiveStreaming = true,
            RecordUrl = "https://example.test/live.flv",
        }));
        Assert.False(StreamResolver.NeedsDouyinAppFallback(new StreamResolverResult
        {
            IsLiveStreaming = false,
        }));
    }

    [Fact]
    public void DouyinResolver_OfflineWithoutIdentityRequestsMetadataSupplement()
    {
        Assert.True(StreamResolver.NeedsDouyinMetadataSupplement(new StreamResolverResult
        {
            IsLiveStreaming = false,
        }));
        Assert.False(StreamResolver.NeedsDouyinMetadataSupplement(new StreamResolverResult
        {
            IsLiveStreaming = false,
            Nickname = "anchor",
        }));
    }

    [Fact]
    public void DouyinResolver_CookieFallbackDoesNotReplaceConfirmedLiveIdentityWithEmptyOfflineData()
    {
        StreamResolverResult primary = new()
        {
            IsLiveStreaming = true,
            Nickname = "anchor",
        };
        StreamResolverResult emptyOfflineFallback = new()
        {
            IsLiveStreaming = false,
        };
        StreamResolverResult identifiedOfflineFallback = new()
        {
            IsLiveStreaming = false,
            Nickname = "anchor",
        };

        Assert.False(StreamResolver.ShouldUseDouyinCookieFallback(primary, emptyOfflineFallback));
        Assert.True(StreamResolver.ShouldUseDouyinCookieFallback(emptyOfflineFallback, identifiedOfflineFallback));
    }

    [Fact]
    public void DouyinResolver_ReflowIdentityRequiresRealRoomAndUserIds()
    {
        const string validJson = """
            {
              "data": {
                "data": [{
                  "id_str": "7350000000000000000",
                  "owner": { "sec_uid": "sec-user" }
                }]
              }
            }
            """;
        const string missingRoomJson = """
            {
              "data": {
                "data": [],
                "user": { "sec_uid": "sec-user" }
              }
            }
            """;

        Assert.True(StreamResolver.TryExtractDouyinReflowIdentity(validJson, out string roomId, out string secUid));
        Assert.Equal("7350000000000000000", roomId);
        Assert.Equal("sec-user", secUid);
        Assert.False(StreamResolver.TryExtractDouyinReflowIdentity(missingRoomJson, out _, out _));
    }

    private static string CreateDouyinStreamData(
        string flvUrl,
        string hlsUrl,
        string codec,
        int bitrate = 0,
        string resolution = "")
    {
        string sdkParams = System.Text.Json.JsonSerializer.Serialize(new
        {
            VCodec = codec,
            vbitrate = bitrate,
            resolution,
        });
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            data = new
            {
                origin = new
                {
                    main = new
                    {
                        flv = flvUrl,
                        hls = hlsUrl,
                        sdk_params = sdkParams,
                    },
                },
            },
        });
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
