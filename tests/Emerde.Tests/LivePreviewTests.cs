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
}
