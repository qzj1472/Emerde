using Emerde.Core;
using Emerde.Models;
using Emerde.ViewModels;

namespace Emerde.Tests;

public sealed class LivePreviewTests
{
    [Fact]
    public void PreviewUrl_UsesHlsBeforeFlv()
    {
        RoomStatusReactive room = new()
        {
            FlvUrl = "https://example.test/live.flv",
            HlsUrl = "https://example.test/live.m3u8",
        };

        Assert.Equal("https://example.test/live.m3u8", room.PreviewUrl);
        Assert.Equal("HLS", room.PreviewSourceText);
    }

    [Fact]
    public void PreviewUrl_UsesRecordUrlBeforeHls()
    {
        RoomStatusReactive room = new()
        {
            RecordUrl = "https://example.test/live-record.flv",
            FlvUrl = "https://example.test/live.flv",
            HlsUrl = "https://example.test/live.m3u8",
        };

        Assert.Equal("https://example.test/live-record.flv", room.PreviewUrl);
        Assert.Equal("Record", room.PreviewSourceText);
    }

    [Fact]
    public void PreviewPlaybackUrl_UsesDisplayedLiveStream()
    {
        RoomStatusReactive room = new()
        {
            FlvUrl = "https://example.test/live.flv",
            HlsUrl = "https://example.test/live.m3u8",
        };

        Assert.Equal(room.PreviewUrl, MainViewModel.GetPreviewPlaybackUrl(room));
    }

    [Fact]
    public void PreviewUrl_FallsBackToFlv()
    {
        RoomStatusReactive room = new()
        {
            FlvUrl = "https://example.test/live.flv",
        };

        Assert.Equal("https://example.test/live.flv", room.PreviewUrl);
        Assert.Equal("FLV", room.PreviewSourceText);
    }

    [Theory]
    [InlineData(StreamStatus.Streaming, "https://example.test/live.m3u8", true)]
    [InlineData(StreamStatus.NotStreaming, "https://example.test/live.m3u8", false)]
    [InlineData(StreamStatus.Streaming, "", false)]
    public void CanPreview_RequiresStreamingAndPreviewUrl(StreamStatus streamStatus, string hlsUrl, bool expected)
    {
        RoomStatusReactive room = new()
        {
            StreamStatus = streamStatus,
            HlsUrl = hlsUrl,
        };

        Assert.Equal(expected, room.CanPreview);
    }

    [Theory]
    [InlineData(StreamStatus.Streaming, true)]
    [InlineData(StreamStatus.NotStreaming, false)]
    [InlineData(StreamStatus.Disabled, false)]
    public void IsStreaming_OnlyReflectsActiveLiveState(StreamStatus streamStatus, bool expected)
    {
        RoomStatusReactive room = new() { StreamStatus = streamStatus };

        Assert.Equal(expected, room.IsStreaming);
    }

    [Theory]
    [InlineData(RecordStatus.Recording, true)]
    [InlineData(RecordStatus.NotRecording, false)]
    [InlineData(RecordStatus.Disabled, false)]
    public void IsRecording_OnlyReflectsActiveRecordState(RecordStatus recordStatus, bool expected)
    {
        RoomStatusReactive room = new() { RecordStatus = recordStatus };

        Assert.Equal(expected, room.IsRecording);
    }

    [Fact]
    public void ApplyRoomInfoResult_PreservesStableIdentityAndPartialStreamData()
    {
        const string roomUrl = "https://example.test/original-room";
        RoomStatusReactive room = new()
        {
            RoomUrl = roomUrl,
            HlsUrl = "https://example.test/original.m3u8",
            Headers = "Referer: https://example.test/",
            Uid = "original-uid",
            StreamStatus = StreamStatus.Streaming,
        };
        StreamResolverResult result = new()
        {
            RoomUrl = "https://example.test/canonical-room",
            PlatformName = "Direct",
            IsLiveStreaming = null,
        };

        try
        {
            MainViewModel.ApplyRoomInfoResult(room, result);

            Assert.Equal(roomUrl, room.RoomUrl);
            Assert.Equal("https://example.test/original.m3u8", room.HlsUrl);
            Assert.Equal("Referer: https://example.test/", room.Headers);
            Assert.Equal("original-uid", room.Uid);
            Assert.Equal(StreamStatus.Streaming, room.StreamStatus);
        }
        finally
        {
            _ = GlobalMonitor.RoomStatus.TryRemove(roomUrl, out _);
        }
    }
}
