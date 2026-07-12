using Emerde.Core;
using Emerde.Models;
using Emerde.ViewModels;
using Emerde.Views;

namespace Emerde.Tests;

public sealed class LivePreviewTests
{
    [Fact]
    public void HasPointerMoved_RequiresActualPointerMovement()
    {
        System.Windows.Point position = new(120, 80);

        Assert.True(LivePreviewPanel.HasPointerMoved(null, position));
        Assert.False(LivePreviewPanel.HasPointerMoved(position, position));
        Assert.False(LivePreviewPanel.HasPointerMoved(position, new System.Windows.Point(120.5, 80.5)));
        Assert.True(LivePreviewPanel.HasPointerMoved(position, new System.Windows.Point(121, 80)));
    }

    [Fact]
    public void PreviewUrl_UsesHlsBeforeFlv()
    {
        RoomStatusReactive room = new()
        {
            StreamStatus = StreamStatus.Streaming,
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
            StreamStatus = StreamStatus.Streaming,
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
            StreamStatus = StreamStatus.Streaming,
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
            StreamStatus = StreamStatus.Streaming,
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
    public void LiveMetadataText_HidesWhenRoomIsNotStreaming()
    {
        RoomStatusReactive room = new()
        {
            StreamStatus = StreamStatus.NotStreaming,
            LiveTitle = "old title",
            Quality = StreamQualityCatalog.BlueRay,
            Resolution = "1920x1080",
            Bitrate = "8 Mbps",
            HlsUrl = "https://example.test/live.m3u8",
        };

        Assert.Equal(string.Empty, room.LiveTitleText);
        Assert.Equal("-", room.LiveStreamText);
        Assert.Equal("-", room.PreviewSourceText);
        Assert.Equal("-", room.QualityText);
        Assert.Equal("-", room.ResolutionText);
        Assert.Equal("-", room.BitrateText);
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

    [Fact]
    public void ApplyRoomInfoResult_PreservesConfirmedLiveSessionWhenStatusIsUnknown()
    {
        const string roomUrl = "https://example.test/original-room";
        RoomStatusReactive room = new()
        {
            RoomUrl = roomUrl,
            LiveTitle = "confirmed live",
            HlsUrl = "https://example.test/original.m3u8",
            Headers = "Referer: https://example.test/",
            StreamStatus = StreamStatus.Streaming,
            RecordStatus = RecordStatus.NotRecording,
        };
        StreamResolverResult result = new()
        {
            RoomUrl = roomUrl,
            IsLiveStreaming = null,
        };

        try
        {
            MainViewModel.ApplyRoomInfoResult(room, result);

            Assert.Equal("confirmed live", room.LiveTitle);
            Assert.Equal("https://example.test/original.m3u8", room.HlsUrl);
            Assert.Equal("Referer: https://example.test/", room.Headers);
            Assert.Equal(StreamStatus.Streaming, room.StreamStatus);
        }
        finally
        {
            _ = GlobalMonitor.RoomStatus.TryRemove(roomUrl, out _);
        }
    }

    [Fact]
    public void ApplyRoomInfoResult_ClearsStaleLiveDataWhenOffline()
    {
        const string roomUrl = "https://example.test/original-room";
        RoomStatusReactive room = new()
        {
            RoomUrl = roomUrl,
            StreamStatus = StreamStatus.Streaming,
            LiveTitle = "old live",
            HlsUrl = "https://example.test/old.m3u8",
            Headers = "Referer: https://example.test/",
            Quality = StreamQualityCatalog.BlueRay,
            Resolution = "1920x1080",
            Bitrate = "8 Mbps",
        };
        StreamResolverResult result = new()
        {
            IsLiveStreaming = false,
        };

        try
        {
            MainViewModel.ApplyRoomInfoResult(room, result);

            Assert.Equal(StreamStatus.NotStreaming, room.StreamStatus);
            Assert.Equal(string.Empty, room.LiveTitle);
            Assert.Equal(string.Empty, room.HlsUrl);
            Assert.Equal(string.Empty, room.Headers);
            Assert.Equal(string.Empty, room.Quality);
            Assert.Equal(string.Empty, room.Resolution);
            Assert.Equal(string.Empty, room.Bitrate);
            Assert.False(room.CanPreview);
        }
        finally
        {
            _ = GlobalMonitor.RoomStatus.TryRemove(roomUrl, out _);
        }
    }

    [Theory]
    [InlineData(0x0100)]
    [InlineData(0x0104)]
    public void IsPreviewFullScreenExitMessage_AcceptsEscapeFromNativeChildWindow(int message)
    {
        Assert.True(MainWindow.IsPreviewFullScreenExitMessage(true, message, new IntPtr(0x1B)));
        Assert.False(MainWindow.IsPreviewFullScreenExitMessage(false, message, new IntPtr(0x1B)));
        Assert.False(MainWindow.IsPreviewFullScreenExitMessage(true, message, new IntPtr(0x0D)));
    }

    [Fact]
    public void IsPreviewFullScreenExitMessage_RejectsOtherTopLevelWindow()
    {
        Assert.False(MainWindow.IsPreviewFullScreenExitMessage(true, 0x0100, new IntPtr(0x1B), new IntPtr(10), new IntPtr(20)));
    }
}
