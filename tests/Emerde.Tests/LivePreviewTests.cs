using Emerde.Core;
using Emerde.Models;
using Emerde.ViewModels;
using Emerde.Views;
using Vanara.PInvoke;

namespace Emerde.Tests;

public sealed class LivePreviewTests
{
    [Theory]
    [InlineData(System.Windows.MessageBoxResult.None, false)]
    [InlineData(System.Windows.MessageBoxResult.OK, true)]
    [InlineData(System.Windows.MessageBoxResult.Cancel, false)]
    public void ShouldPersistStartupAboutNoticeAcknowledgement_RequiresExplicitConfirmation(System.Windows.MessageBoxResult result, bool expected)
    {
        Assert.Equal(expected, MainWindow.ShouldPersistStartupAboutNoticeAcknowledgement(result));
    }

    [Fact]
    public void ShouldRefreshPreviewStreamBeforePlayback_UsesCachedStreamImmediately()
    {
        RoomStatusReactive cached = new()
        {
            HlsUrl = "https://example.test/live.m3u8",
        };
        RoomStatusReactive missing = new();

        Assert.False(MainViewModel.ShouldRefreshPreviewStreamBeforePlayback(cached));
        Assert.True(MainViewModel.ShouldRefreshPreviewStreamBeforePlayback(missing));
    }

    [Fact]
    public void LivePreviewPlayer_UsesLowLatencyCache()
    {
        Assert.Equal(300, LivePreviewPlayer.CacheMilliseconds);
    }

    [Fact]
    public void TryReadSnapshotDimensions_ReadsCompletedPng()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-preview-test-{Guid.NewGuid():N}.png");
        try
        {
            byte[] pixels = new byte[4 * 3 * 4];
            System.Windows.Media.Imaging.BitmapSource bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                4,
                3,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixels,
                4 * 4);
            System.Windows.Media.Imaging.PngBitmapEncoder encoder = new();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            bool resolved = LivePreviewPlayer.TryReadSnapshotDimensions(path, out uint width, out uint height);

            Assert.True(resolved);
            Assert.Equal(4u, width);
            Assert.Equal(3u, height);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
    public void PreviewUrl_UsesFlvBeforeHls()
    {
        RoomStatusReactive room = new()
        {
            StreamStatus = StreamStatus.Streaming,
            FlvUrl = "https://example.test/live.flv",
            HlsUrl = "https://example.test/live.m3u8",
        };

        Assert.Equal("https://example.test/live.flv", room.PreviewUrl);
        Assert.Equal("FLV", room.PreviewSourceText);
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
        Assert.Equal("FLV", room.PreviewSourceText);
    }

    [Fact]
    public void PreviewSourceText_UsesRecordUrlFormat()
    {
        RoomStatusReactive room = new()
        {
            StreamStatus = StreamStatus.Streaming,
            RecordUrl = "https://example.test/live-record.m3u8",
            FlvUrl = "https://example.test/live.flv",
        };

        Assert.Equal("HLS", room.PreviewSourceText);
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
    public void PreviewUrl_FallsBackToHls()
    {
        RoomStatusReactive room = new()
        {
            StreamStatus = StreamStatus.Streaming,
            HlsUrl = "https://example.test/live.m3u8",
        };

        Assert.Equal("https://example.test/live.m3u8", room.PreviewUrl);
        Assert.Equal("HLS", room.PreviewSourceText);
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
    public void ApplyRoomInfoResult_PreservesResolvedMetadataWhenLiveRefreshOmitsIt()
    {
        const string roomUrl = "https://example.test/live-room";
        RoomStatusReactive room = new()
        {
            RoomUrl = roomUrl,
            StreamStatus = StreamStatus.Streaming,
            Quality = StreamQualityCatalog.BlueRay,
            Resolution = "1920x1080",
            Bitrate = "8 Mbps",
        };
        StreamResolverResult result = new()
        {
            RoomUrl = roomUrl,
            IsLiveStreaming = true,
            FlvUrl = "https://example.test/live.flv",
        };

        try
        {
            MainViewModel.ApplyRoomInfoResult(room, result);

            Assert.Equal(StreamQualityCatalog.BlueRay, room.Quality);
            Assert.Equal("1920x1080", room.Resolution);
            Assert.Equal("8 Mbps", room.Bitrate);
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
    public void IsPreviewFullScreenExitMessage_RejectsOtherMessages()
    {
        Assert.False(MainWindow.IsPreviewFullScreenExitMessage(true, 0x0101, new IntPtr(0x1B)));
        Assert.False(MainWindow.IsPreviewFullScreenExitMessage(true, 0x0200, new IntPtr(0x1B)));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void ShouldSuspendPreviewPresentation_OnlySuspendsForOverlayOrStoppedPreview(bool overlay, bool previewing, bool expected)
    {
        Assert.Equal(expected, MainWindow.ShouldSuspendPreviewPresentation(overlay, previewing));
    }

    [Fact]
    public void IsPreviewFullScreenClientHitTest_DisablesWindowEdgeHitTesting()
    {
        Assert.True(MainWindow.IsPreviewFullScreenClientHitTest(true, 0x0084));
        Assert.False(MainWindow.IsPreviewFullScreenClientHitTest(false, 0x0084));
        Assert.False(MainWindow.IsPreviewFullScreenClientHitTest(true, 0x0200));
    }

    [Fact]
    public void IsPreviewFullScreenNonClientMessage_SuppressesFramePainting()
    {
        Assert.True(MainWindow.IsPreviewFullScreenNonClientMessage(true, 0x0083));
        Assert.True(MainWindow.IsPreviewFullScreenNonClientMessage(true, 0x0085));
        Assert.False(MainWindow.IsPreviewFullScreenNonClientMessage(false, 0x0083));
        Assert.False(MainWindow.IsPreviewFullScreenNonClientMessage(true, 0x0084));
    }

    [Fact]
    public void IsPreviewFullScreenBlockedSystemCommand_BlocksMoveAndResize()
    {
        Assert.True(MainWindow.IsPreviewFullScreenBlockedSystemCommand(true, 0x0112, new IntPtr(0xF000)));
        Assert.True(MainWindow.IsPreviewFullScreenBlockedSystemCommand(true, 0x0112, new IntPtr(0xF010)));
        Assert.False(MainWindow.IsPreviewFullScreenBlockedSystemCommand(false, 0x0112, new IntPtr(0xF000)));
        Assert.False(MainWindow.IsPreviewFullScreenBlockedSystemCommand(true, 0x0112, new IntPtr(0xF030)));
    }

    [Fact]
    public void BuildPreviewFullScreenWindowStyle_UsesBorderlessPopup()
    {
        int style = (int)(User32.WindowStyles.WS_OVERLAPPEDWINDOW | User32.WindowStyles.WS_VISIBLE);
        int fullScreenStyle = MainWindow.BuildPreviewFullScreenWindowStyle(style);

        Assert.True((fullScreenStyle & unchecked((int)User32.WindowStyles.WS_POPUP)) != 0);
        Assert.True((fullScreenStyle & (int)User32.WindowStyles.WS_VISIBLE) != 0);
        Assert.False((fullScreenStyle & (int)User32.WindowStyles.WS_CAPTION) != 0);
        Assert.False((fullScreenStyle & (int)User32.WindowStyles.WS_THICKFRAME) != 0);
    }

    [Fact]
    public void BuildPreviewFullScreenWindowExStyle_RemovesEdgesAndTopmost()
    {
        int exStyle = (int)(User32.WindowStylesEx.WS_EX_APPWINDOW
            | User32.WindowStylesEx.WS_EX_TOPMOST
            | User32.WindowStylesEx.WS_EX_CLIENTEDGE
            | User32.WindowStylesEx.WS_EX_WINDOWEDGE
            | User32.WindowStylesEx.WS_EX_STATICEDGE);

        int fullScreenExStyle = MainWindow.BuildPreviewFullScreenWindowExStyle(exStyle);

        Assert.True((fullScreenExStyle & (int)User32.WindowStylesEx.WS_EX_APPWINDOW) != 0);
        Assert.False((fullScreenExStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0);
        Assert.False((fullScreenExStyle & (int)User32.WindowStylesEx.WS_EX_CLIENTEDGE) != 0);
        Assert.False((fullScreenExStyle & (int)User32.WindowStylesEx.WS_EX_WINDOWEDGE) != 0);
        Assert.False((fullScreenExStyle & (int)User32.WindowStylesEx.WS_EX_STATICEDGE) != 0);
    }

    [Fact]
    public void ExpandPreviewFullScreenBounds_OverscansEveryAvailableEdgeByTwoPixels()
    {
        System.Drawing.Rectangle bounds = new(-1920, 0, 1920, 1080);

        System.Drawing.Rectangle expandedBounds = MainWindow.ExpandPreviewFullScreenBounds(bounds, [bounds]);

        Assert.Equal(new System.Drawing.Rectangle(-1922, -2, 1924, 1084), expandedBounds);
    }

    [Fact]
    public void ExpandPreviewFullScreenBounds_DoesNotEnterAdjacentScreen()
    {
        System.Drawing.Rectangle bounds = new(0, 0, 1920, 1080);
        System.Drawing.Rectangle adjacent = new(1920, 0, 1920, 1080);

        System.Drawing.Rectangle expandedBounds = MainWindow.ExpandPreviewFullScreenBounds(bounds, [bounds, adjacent]);

        Assert.Equal(new System.Drawing.Rectangle(-2, -2, 1922, 1084), expandedBounds);
    }

    [Fact]
    public void ExpandPreviewFullScreenBounds_UsesOnlyAvailableGap()
    {
        System.Drawing.Rectangle bounds = new(0, 0, 1920, 1080);
        System.Drawing.Rectangle adjacent = new(1921, 0, 1920, 1080);

        System.Drawing.Rectangle expandedBounds = MainWindow.ExpandPreviewFullScreenBounds(bounds, [bounds, adjacent]);

        Assert.Equal(new System.Drawing.Rectangle(-2, -2, 1923, 1084), expandedBounds);
    }

}
