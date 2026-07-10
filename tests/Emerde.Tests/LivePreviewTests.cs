using Emerde.Core;
using Emerde.Models;
using Emerde.ViewModels;

namespace Emerde.Tests;

public sealed class LivePreviewTests
{
    [Fact]
    public void PreviewUrl_UsesFlvBeforeHls()
    {
        RoomStatusReactive room = new()
        {
            FlvUrl = "https://example.test/live.flv",
            HlsUrl = "https://example.test/live.m3u8",
        };

        Assert.Equal("https://example.test/live.flv", room.PreviewUrl);
        Assert.Equal("FLV", room.PreviewSourceText);
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
}
