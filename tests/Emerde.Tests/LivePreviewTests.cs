using Emerde.Core;
using Emerde.Controls;
using Emerde.Models;
using Emerde.ViewModels;
using Emerde.Views;
using LibVLCSharp.Shared;
using Vanara.PInvoke;

namespace Emerde.Tests;

public sealed class LivePreviewTests
{
    [Fact]
    public void GridLengthAnimation_ClonesItsEasingFunction()
    {
        GridLengthAnimation animation = new()
        {
            From = 10d,
            To = 20d,
            EasingFunction = new System.Windows.Media.Animation.CubicEase(),
        };

        GridLengthAnimation clone = (GridLengthAnimation)animation.Clone();

        Assert.IsType<System.Windows.Media.Animation.CubicEase>(clone.EasingFunction);
        Assert.Equal(10d, clone.From);
        Assert.Equal(20d, clone.To);
    }

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
        Assert.Equal(80, LivePreviewPlayer.CacheMilliseconds);
        Assert.Equal(TimeSpan.FromSeconds(3), LivePreviewPlayer.PlaybackStartTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(250), LivePreviewPlayer.PlaybackStopTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), LivePreviewPlayer.StandbyRetention);
        Assert.Equal(3, LivePreviewPlayer.MaximumSessionCount);
    }

    [Fact]
    public void LivePreviewPlayer_AppliesLowLatencyOptionsToInstanceAndMedia()
    {
        Assert.Contains("--network-caching=80", LivePreviewPlayer.LibVlcOptions);
        Assert.Contains("--live-caching=80", LivePreviewPlayer.LibVlcOptions);
        Assert.Contains("--file-caching=80", LivePreviewPlayer.LibVlcOptions);
        Assert.Contains("--clock-synchro=0", LivePreviewPlayer.LibVlcOptions);
        Assert.Contains("--drop-late-frames", LivePreviewPlayer.LibVlcOptions);
        Assert.Contains("--skip-frames", LivePreviewPlayer.LibVlcOptions);

        Assert.Contains(":network-caching=80", LivePreviewPlayer.MediaLowLatencyOptions);
        Assert.Contains(":live-caching=80", LivePreviewPlayer.MediaLowLatencyOptions);
        Assert.Contains(":file-caching=80", LivePreviewPlayer.MediaLowLatencyOptions);
        Assert.Contains(":clock-synchro=0", LivePreviewPlayer.MediaLowLatencyOptions);
        Assert.Contains(":drop-late-frames", LivePreviewPlayer.MediaLowLatencyOptions);
        Assert.Contains(":skip-frames", LivePreviewPlayer.MediaLowLatencyOptions);
    }

    [Fact]
    public void LivePreviewFrameSource_CopiesLatestFrameBeforeCoalescingPresentation()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "LivePreviewFrameSource.cs"));
        int unlockIndex = source.IndexOf("private void UnlockVideo", StringComparison.Ordinal);
        int presentIndex = source.IndexOf("private void PresentFrame", unlockIndex, StringComparison.Ordinal);
        string unlockMethod = source[unlockIndex..presentIndex];

        Assert.True(unlockMethod.IndexOf("Marshal.Copy", StringComparison.Ordinal)
            < unlockMethod.IndexOf("Interlocked.CompareExchange(ref framePending", StringComparison.Ordinal));
        Assert.Contains("lock (syncRoot)", source[presentIndex..]);
    }

    [Fact]
    public void LivePreviewPlayer_UsesImmediateAudioSwitchingWithTrackRecovery()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(10), LivePreviewPlayer.AudioTrackRestoreStep);
        Assert.Equal(TimeSpan.FromSeconds(3), LivePreviewPlayer.AudioTrackRestoreTimeout);
    }

    [Theory]
    [InlineData(2, null, new[] { -1, 1 }, 2)]
    [InlineData(-1, 3, new[] { -1, 1 }, 3)]
    [InlineData(-1, null, new[] { -1, 4 }, 4)]
    [InlineData(-1, null, new[] { -1 }, null)]
    public void LivePreviewPlayer_RestoresAnAvailableAudioTrack(
        int currentTrack,
        int? rememberedTrack,
        int[] availableTracks,
        int? expected)
    {
        Assert.Equal(expected, LivePreviewPlayer.SelectAudioTrackToRestore(currentTrack, rememberedTrack, availableTracks));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LivePreviewPlayer_WaitsForStartOnlyOnInitialPlayback(bool replaceCurrentPlayback, bool expected)
    {
        Assert.Equal(expected, LivePreviewPlayer.ShouldWaitForPlaybackStart(replaceCurrentPlayback));
    }

    [Theory]
    [InlineData("room-a", "room-a", true, false, true)]
    [InlineData("room-a", "room-a", true, true, false)]
    [InlineData("room-a", "room-b", true, false, false)]
    [InlineData("room-a", "room-a", false, false, false)]
    public void LivePreviewPlayer_ReusesOnlyMatchingHealthySessions(
        string currentKey,
        string targetKey,
        bool hasMedia,
        bool restartCurrentPlayback,
        bool expected)
    {
        Assert.Equal(expected, LivePreviewPlayer.ShouldReuseSession(currentKey, targetKey, hasMedia, restartCurrentPlayback));
    }

    [Fact]
    public void LivePreviewPlayer_EvictsOldestUnpinnedStandbyBeforeAddingFourthSession()
    {
        DateTime now = DateTime.UtcNow;
        IReadOnlyList<string> removed = LivePreviewPlayer.SelectStandbyKeysToRemove(
        [
            ("current", now, true, false),
            ("older", now.AddSeconds(-2), false, false),
            ("newer", now.AddSeconds(-1), false, false),
        ], LivePreviewPlayer.MaximumSessionCount);

        Assert.Equal(["older"], removed);
    }

    [Theory]
    [InlineData(VLCState.Playing, true)]
    [InlineData(VLCState.Opening, true)]
    [InlineData(VLCState.Buffering, true)]
    [InlineData(VLCState.Paused, true)]
    [InlineData(VLCState.Error, false)]
    [InlineData(VLCState.Ended, false)]
    [InlineData(VLCState.Stopped, false)]
    public void PreviewRefreshCooldown_AppliesOnlyToActivePlayback(VLCState state, bool expected)
    {
        Assert.Equal(expected, MainViewModel.ShouldApplyPreviewRefreshCooldown(state));
    }

    [Fact]
    public void PreviewRefreshCooldown_IsIndependentPerRoomAndExpires()
    {
        Dictionary<string, long> timestamps = new(StringComparer.OrdinalIgnoreCase);

        Assert.True(MainViewModel.TryRegisterPreviewRefresh(timestamps, "room-a", 1000, 2000, out long firstRemaining));
        Assert.False(MainViewModel.TryRegisterPreviewRefresh(timestamps, "room-a", 1100, 2000, out long repeatedRemaining));
        Assert.True(MainViewModel.TryRegisterPreviewRefresh(timestamps, "room-b", 1100, 2000, out long otherRoomRemaining));
        Assert.True(MainViewModel.TryRegisterPreviewRefresh(timestamps, "room-a", 3000, 2000, out long expiredRemaining));

        Assert.Equal(0, firstRemaining);
        Assert.Equal(1900, repeatedRemaining);
        Assert.Equal(0, otherRoomRemaining);
        Assert.Equal(0, expiredRemaining);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    [InlineData(2000, 2)]
    public void PreviewRefreshCooldown_RoundsRemainingTimeUp(long remainingMilliseconds, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, MainViewModel.GetPreviewRefreshRemainingSeconds(remainingMilliseconds));
    }

    [Theory]
    [InlineData(3, 3, 8, 8, true, true)]
    [InlineData(2, 3, 8, 8, true, false)]
    [InlineData(3, 3, 7, 8, true, false)]
    [InlineData(3, 3, 8, 8, false, false)]
    public void LivePreviewFrameSource_AcceptsOnlyCurrentEnabledPresentation(
        int expectedGeneration,
        int currentGeneration,
        int expectedPresentationEpoch,
        int currentPresentationEpoch,
        bool presentationEnabled,
        bool expected)
    {
        Assert.Equal(expected, LivePreviewFrameSource.IsCurrentPresentation(
            expectedGeneration,
            currentGeneration,
            expectedPresentationEpoch,
            currentPresentationEpoch,
            presentationEnabled));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(80, 80)]
    [InlineData(101, 100)]
    public void NormalizeVolume_ClampsToLibVlcRange(int value, int expected)
    {
        Assert.Equal(expected, LivePreviewPlayer.NormalizeVolume(value));
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.Space)]
    [InlineData(System.Windows.Input.Key.M)]
    [InlineData(System.Windows.Input.Key.OemMinus)]
    [InlineData(System.Windows.Input.Key.OemPlus)]
    [InlineData(System.Windows.Input.Key.G)]
    [InlineData(System.Windows.Input.Key.Escape)]
    public void IsPreviewControlShortcut_AcceptsPreviewPlaybackKeys(System.Windows.Input.Key key)
    {
        Assert.True(MainWindow.IsPreviewControlShortcut(true, key, System.Windows.Input.ModifierKeys.None));
        Assert.False(MainWindow.IsPreviewControlShortcut(false, key, System.Windows.Input.ModifierKeys.None));
        Assert.False(MainWindow.IsPreviewControlShortcut(true, key, System.Windows.Input.ModifierKeys.Control));
    }

    [Fact]
    public void IsPreviewControlShortcut_UsesVForFullScreen()
    {
        Assert.True(MainWindow.IsPreviewControlShortcut(true, System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.None));
        Assert.False(MainWindow.IsPreviewControlShortcut(true, System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.Control));
        Assert.False(MainWindow.IsPreviewControlShortcut(true, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Alt));
        Assert.False(MainWindow.IsPreviewControlShortcut(true, System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.None));
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.Left)]
    [InlineData(System.Windows.Input.Key.Right)]
    [InlineData(System.Windows.Input.Key.Up)]
    [InlineData(System.Windows.Input.Key.Down)]
    [InlineData(System.Windows.Input.Key.Q)]
    public void IsPreviewControlShortcut_LeavesUnrelatedKeysAvailable(System.Windows.Input.Key key)
    {
        Assert.False(MainWindow.IsPreviewControlShortcut(true, key, System.Windows.Input.ModifierKeys.None));
    }

    [Fact]
    public void ShouldBypassAppShortcutsForDialog_DisablesAppShortcutDispatchOnlyWhileDialogIsOpen()
    {
        Assert.True(MainWindow.ShouldBypassAppShortcutsForDialog(true));
        Assert.False(MainWindow.ShouldBypassAppShortcutsForDialog(false));
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

    [Theory]
    [InlineData("https://example.test/a", "https://example.test/b", true, true)]
    [InlineData("https://example.test/a", "https://example.test/A", true, false)]
    [InlineData(null, "https://example.test/b", true, false)]
    [InlineData("https://example.test/a", null, true, false)]
    [InlineData("https://example.test/a", "https://example.test/b", false, false)]
    public void ShouldAnimatePreviewRoomSwitch_OnlyAnimatesBetweenDifferentVisibleRooms(
        string? previousRoomUrl,
        string? nextRoomUrl,
        bool hasCurrentFrame,
        bool expected)
    {
        Assert.Equal(expected, LivePreviewPanel.ShouldAnimatePreviewRoomSwitch(previousRoomUrl, nextRoomUrl, hasCurrentFrame));
    }

    [Fact]
    public void PreviewRoomSwitch_UsesASeparateNonInteractiveFrameLayer()
    {
        System.Xml.Linq.XDocument document = System.Xml.Linq.XDocument.Load(
            FindRepositoryFile("src", "Emerde", "Views", "LivePreviewPanel.xaml"));
        System.Xml.Linq.XElement frame = document.Descendants()
            .Single(element => (string?)element.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "PreviewRoomTransitionFrame");

        Assert.Equal("False", (string?)frame.Attribute("IsHitTestVisible"));
        Assert.Equal("0", (string?)frame.Attribute("Opacity"));
        Assert.Equal("Collapsed", (string?)frame.Attribute("Visibility"));
    }

    [Theory]
    [InlineData(120, 5)]
    [InlineData(1, 5)]
    [InlineData(0, 0)]
    [InlineData(-1, -5)]
    [InlineData(-120, -5)]
    public void GetPreviewVolumeWheelStep_AdjustsFivePercentPerWheelEvent(int wheelDelta, int expected)
    {
        Assert.Equal(expected, LivePreviewPanel.GetPreviewVolumeWheelStep(wheelDelta));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(120, false)]
    [InlineData(160, true)]
    [InlineData(255, true)]
    public void IsLightPreviewSample_ChoosesPureContrastColor(int channelValue, bool expected)
    {
        using System.Drawing.Bitmap sample = new(8, 8);
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(sample);
        graphics.Clear(System.Drawing.Color.FromArgb(channelValue, channelValue, channelValue));

        Assert.Equal(expected, LivePreviewPanel.IsLightPreviewSample(sample));
    }

    [Theory]
    [InlineData(472, 480, 1.5, 708, 398)]
    [InlineData(480, 270, 1.5, 720, 405)]
    [InlineData(360, 640, 1.25, 450, 253)]
    public void CalculateVideoSurfaceSize_FillsAvailablePhysicalPixels(
        double viewportWidth,
        double viewportHeight,
        double dpiScale,
        double expectedPixelWidth,
        double expectedPixelHeight)
    {
        System.Windows.Size size = LivePreviewPanel.CalculateVideoSurfaceSize(
            viewportWidth,
            viewportHeight,
            1920,
            1080,
            dpiScale,
            dpiScale);

        Assert.Equal(expectedPixelWidth, size.Width * dpiScale, 6);
        Assert.Equal(expectedPixelHeight, size.Height * dpiScale, 6);
        Assert.True(size.Width <= viewportWidth);
        Assert.True(size.Height <= viewportHeight);
    }

    [Fact]
    public void CalculateVideoSurfaceSize_RejectsInvalidGeometry()
    {
        Assert.Equal(
            new System.Windows.Size(0d, 0d),
            LivePreviewPanel.CalculateVideoSurfaceSize(0d, 480d, 1920, 1080, 1.5d, 1.5d));
    }

    [Fact]
    public void ResolveAnimatedHomePreviewWidths_AllocatesPreviewRemainder()
    {
        (double roomListWidth, double previewWidth, double detailWidth) = MainWindow.ResolveAnimatedHomePreviewWidths(
            1000d,
            new System.Windows.GridLength(260d),
            new System.Windows.GridLength(1d, System.Windows.GridUnitType.Star),
            new System.Windows.GridLength(240d));

        Assert.Equal(260d, roomListWidth);
        Assert.Equal(500d, previewWidth);
        Assert.Equal(240d, detailWidth);
    }

    [Fact]
    public void ResolveAnimatedHomePreviewWidths_UsesNormalLayoutRatiosWhenPreviewIsClosed()
    {
        (double roomListWidth, double previewWidth, double detailWidth) = MainWindow.ResolveAnimatedHomePreviewWidths(
            1000d,
            new System.Windows.GridLength(7d, System.Windows.GridUnitType.Star),
            new System.Windows.GridLength(0d),
            new System.Windows.GridLength(3d, System.Windows.GridUnitType.Star));

        Assert.Equal(700d, roomListWidth);
        Assert.Equal(0d, previewWidth);
        Assert.Equal(300d, detailWidth);
    }

    [Fact]
    public void ResolveAnimatedHomePreviewWidths_PreservesTheRightEdgeWhenDetailWidthIsCapped()
    {
        (double roomListWidth, double previewWidth, double detailWidth) = MainWindow.ResolveAnimatedHomePreviewWidths(
            1400d,
            new System.Windows.GridLength(7d, System.Windows.GridUnitType.Star),
            new System.Windows.GridLength(0d),
            new System.Windows.GridLength(3d, System.Windows.GridUnitType.Star),
            309d);

        Assert.Equal(1091d, roomListWidth);
        Assert.Equal(0d, previewWidth);
        Assert.Equal(309d, detailWidth);
        Assert.Equal(1400d, roomListWidth + previewWidth + detailWidth);
    }

    [Fact]
    public void ResolveAnimatedHomePreviewWidths_UsesTheConfiguredStarWeights()
    {
        (double roomListWidth, double previewWidth, double detailWidth) = MainWindow.ResolveAnimatedHomePreviewWidths(
            900d,
            new System.Windows.GridLength(2d, System.Windows.GridUnitType.Star),
            new System.Windows.GridLength(0d),
            new System.Windows.GridLength(1d, System.Windows.GridUnitType.Star));

        Assert.Equal(600d, roomListWidth);
        Assert.Equal(0d, previewWidth);
        Assert.Equal(300d, detailWidth);
    }

    [Fact]
    public void ResolveAnimatedHomePreviewWidths_ConstrainsFixedPanesToTheAvailableWidth()
    {
        (double roomListWidth, double previewWidth, double detailWidth) = MainWindow.ResolveAnimatedHomePreviewWidths(
            400d,
            new System.Windows.GridLength(350d),
            new System.Windows.GridLength(1d, System.Windows.GridUnitType.Star),
            new System.Windows.GridLength(200d));

        Assert.Equal(350d, roomListWidth);
        Assert.Equal(0d, previewWidth);
        Assert.Equal(50d, detailWidth);
        Assert.Equal(400d, roomListWidth + previewWidth + detailWidth);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1d)]
    public void ResolveAnimatedHomePreviewWidths_NormalizesInvalidAvailableWidths(double totalWidth)
    {
        (double roomListWidth, double previewWidth, double detailWidth) = MainWindow.ResolveAnimatedHomePreviewWidths(
            totalWidth,
            new System.Windows.GridLength(7d, System.Windows.GridUnitType.Star),
            new System.Windows.GridLength(0d),
            new System.Windows.GridLength(3d, System.Windows.GridUnitType.Star));

        Assert.Equal(0d, roomListWidth);
        Assert.Equal(0d, previewWidth);
        Assert.Equal(0d, detailWidth);
    }

    [Theory]
    [InlineData(1920, 1080, 7680, 8294400)]
    [InlineData(1919, 1080, 7680, 8294400)]
    [InlineData(0, 1080, 0, 0)]
    public void CalculateBufferLayout_AlignsFramesForLibVlc(
        uint width,
        uint height,
        int expectedPitch,
        int expectedBufferLength)
    {
        (int pitch, int bufferLength) = LivePreviewFrameSource.CalculateBufferLayout(width, height);

        Assert.Equal(expectedPitch, pitch);
        Assert.Equal(expectedBufferLength, bufferLength);
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
    public void PreviewUrl_UsesFlvBeforeRecordUrl()
    {
        RoomStatusReactive room = new()
        {
            StreamStatus = StreamStatus.Streaming,
            RecordUrl = "https://example.test/live-record.flv",
            FlvUrl = "https://example.test/live.flv",
            HlsUrl = "https://example.test/live.m3u8",
        };

        Assert.Equal("https://example.test/live.flv", room.PreviewUrl);
        Assert.Equal("FLV", room.PreviewSourceText);
    }

    [Fact]
    public void PreviewSourceText_UsesLowLatencyFlvFormat()
    {
        RoomStatusReactive room = new()
        {
            StreamStatus = StreamStatus.Streaming,
            RecordUrl = "https://example.test/live-record.m3u8",
            FlvUrl = "https://example.test/live.flv",
        };

        Assert.Equal("FLV", room.PreviewSourceText);
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
    [InlineData(RecordStatus.Recording, false)]
    [InlineData(RecordStatus.NotRecording, false)]
    [InlineData(RecordStatus.Disabled, false)]
    public void IsRecording_OnlyReflectsActiveRecordState(RecordStatus recordStatus, bool expected)
    {
        RoomStatusReactive room = new() { RecordStatus = recordStatus };

        Assert.Equal(expected, room.IsRecording);
    }

    [Fact]
    public void IsRecording_RequiresConfirmedMediaProgress()
    {
        RoomStatusReactive room = new()
        {
            RecordStatus = RecordStatus.Recording,
            IsRecordingConfirmed = true,
        };

        Assert.True(room.IsRecording);
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

    [Fact]
    public void ApplyRoomInfoResult_StopsRecorderStateWhenManualRefreshConfirmsOffline()
    {
        const string roomUrl = "https://example.test/recording-room";
        RoomStatusReactive room = new()
        {
            RoomUrl = roomUrl,
            PlatformName = "Direct",
            StreamStatus = StreamStatus.Streaming,
            RecordStatus = RecordStatus.Recording,
        };
        RoomStatus status = new()
        {
            RoomUrl = roomUrl,
            PlatformName = "Direct",
            StreamStatus = StreamStatus.Streaming,
            RecordStatus = RecordStatus.Recording,
        };
        StreamResolverResult result = new()
        {
            IsLiveStreaming = false,
        };

        try
        {
            GlobalMonitor.RoomStatus[roomUrl] = status;

            MainViewModel.ApplyRoomInfoResult(room, result);

            Assert.Equal(StreamStatus.NotStreaming, room.StreamStatus);
            Assert.Equal(RecordStatus.NotRecording, room.RecordStatus);
            Assert.Equal(RecordStatus.NotRecording, status.RecordStatus);
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
    [InlineData(true, false, true, false, true, false, false)]
    [InlineData(false, true, true, false, true, false, false)]
    [InlineData(false, false, true, false, true, false, true)]
    [InlineData(true, false, false, false, true, false, true)]
    [InlineData(true, false, false, true, true, false, false)]
    [InlineData(true, false, true, false, false, false, true)]
    [InlineData(true, false, true, false, true, true, true)]
    [InlineData(false, true, false, false, true, false, true)]
    public void ShouldSuspendPreviewPresentation_RequiresAVisiblePresentationSurface(
        bool previewing,
        bool closing,
        bool homePageSelected,
        bool fullScreen,
        bool windowVisible,
        bool windowMinimized,
        bool expected)
    {
        Assert.Equal(expected, MainWindow.ShouldSuspendPreviewPresentation(
            previewing,
            closing,
            homePageSelected,
            fullScreen,
            windowVisible,
            windowMinimized));
    }

    [Theory]
    [InlineData(false, true, false, false, false, true)]
    [InlineData(false, true, true, false, false, false)]
    [InlineData(false, true, false, true, false, false)]
    [InlineData(false, true, false, false, true, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(false, false, false, false, false, false)]
    public void ShouldPausePreviewForPage_OnlyPausesActivePlayingPreview(
        bool homePageSelected,
        bool previewing,
        bool transitioning,
        bool paused,
        bool pausedByPage,
        bool expected)
    {
        Assert.Equal(expected, MainViewModel.ShouldPausePreviewForPage(homePageSelected, previewing, transitioning, paused, pausedByPage));
    }

    [Theory]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, true, false, false, false)]
    public void ShouldRefreshPreviewForHomePage_OnlyRefreshesPagePausedPreview(
        bool homePageSelected,
        bool previewing,
        bool transitioning,
        bool pausedByPage,
        bool expected)
    {
        Assert.Equal(expected, MainViewModel.ShouldRefreshPreviewForHomePage(homePageSelected, previewing, transitioning, pausedByPage));
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

    [Fact]
    public void VideoLayoutRefresh_IsCancelableAndDoesNotUseAsyncVoid()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "LivePreviewPanel.xaml.cs"));

        Assert.Contains("private async Task RefreshVideoSurfaceSizeAsync(CancellationTokenSource cancellation)", source);
        Assert.DoesNotContain("private async void RefreshVideoSurfaceSize", source);
        Assert.Contains("Task.Delay(250, cancellation.Token)", source);
        Assert.Contains("CancelVideoLayoutRefresh()", source);
        Assert.Contains("catch (OperationCanceledException) when (cancellation.IsCancellationRequested)", source);
    }

    [Fact]
    public void MainViewModelDispose_CancelsTransitionWithoutTakingOverItsLifetime()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        int methodStart = source.IndexOf("public void Dispose()", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("internal enum PreviewControlFeedbackKind", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        Assert.Contains("transitionCancellation?.Cancel()", method);
        Assert.DoesNotContain("transitionCancellation?.Dispose()", method);
        Assert.Contains("cancellation.Dispose()", source);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

}
