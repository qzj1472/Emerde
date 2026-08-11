using Emerde.Core;
using Emerde.Extensions;
using Emerde.Models;
using Emerde.ViewModels;

namespace Emerde.Tests;

public sealed class MainViewModelRefreshTests
{
    [Fact]
    public void FixedRoomMetadata_RefreshesFirstResult()
    {
        bool shouldRefresh = GlobalMonitor.ShouldRefreshFixedRoomMetadata(null, 1000);

        Assert.True(shouldRefresh);
    }

    [Theory]
    [InlineData(1000, 1000, false)]
    [InlineData(1000, 3600999, false)]
    [InlineData(1000, 3601000, true)]
    [InlineData(3601000, 1000, true)]
    public void FixedRoomMetadata_UsesHourlyInterval(long lastRefreshTimestamp, long currentTimestamp, bool expected)
    {
        bool shouldRefresh = GlobalMonitor.ShouldRefreshFixedRoomMetadata(lastRefreshTimestamp, currentTimestamp);

        Assert.Equal(expected, shouldRefresh);
    }

    [Fact]
    public void CopyRoomStatus_RefreshesTheMatchingCardState()
    {
        RoomStatus source = new()
        {
            NickName = "resolved broadcaster",
            AvatarThumbUrl = "https://example.test/avatar.jpg",
            PlatformName = "Douyin",
            Uid = "123",
            LiveTitle = "live title",
            Quality = "ORIGIN",
            Resolution = "1920x1080",
            Bitrate = "8 Mbps",
            StreamStatus = StreamStatus.NotStreaming,
            IsStreamCheckFailed = true,
            RecordStatus = RecordStatus.NotRecording,
        };
        RoomStatusReactive target = new()
        {
            StreamStatus = StreamStatus.Streaming,
            RecordStatus = RecordStatus.Recording,
        };

        MainViewModel.CopyRoomStatus(target, source);

        Assert.Equal(StreamStatus.NotStreaming, target.StreamStatus);
        Assert.True(target.IsStreamCheckFailed);
        Assert.Equal(RecordStatus.NotRecording, target.RecordStatus);
        Assert.Equal("resolved broadcaster", target.NickName);
        Assert.Equal("live title", target.LiveTitle);
        Assert.Equal("1920x1080", target.Resolution);
        Assert.Equal("8 Mbps", target.Bitrate);
    }

    [Fact]
    public void CopyRoomStatus_ConfirmsRecordingOnlyAfterMediaProgress()
    {
        RoomStatus source = new()
        {
            RecordStatus = RecordStatus.Recording,
        };
        RoomStatusReactive target = new();

        MainViewModel.CopyRoomStatus(target, source);

        Assert.Equal(RecordStatus.Recording, target.RecordStatus);
        Assert.False(target.IsRecordingConfirmed);
        Assert.Equal("RecordStatusOfStarting".Tr(), target.RecordStatusText);
    }

    [Theory]
    [InlineData(RecordStatus.Initialized, false, true)]
    [InlineData(RecordStatus.Initialized, true, false)]
    [InlineData(RecordStatus.Disabled, false, true)]
    [InlineData(RecordStatus.Disabled, true, false)]
    [InlineData(RecordStatus.NotRecording, false, true)]
    [InlineData(RecordStatus.NotRecording, true, false)]
    [InlineData(RecordStatus.Recording, false, false)]
    [InlineData(RecordStatus.Recording, true, false)]
    public void SelectedRoomRecordCommand_UsesRuntimeAndEffectiveRecordingState(
        RecordStatus recordStatus,
        bool effectiveIsToRecord,
        bool expected)
    {
        Assert.Equal(expected, MainViewModel.ShouldEnableSelectedRoomRecord(recordStatus, effectiveIsToRecord));
    }

    [Fact]
    public void RoomRecordingSummary_HonorsCancellationBeforeScanning()
    {
        RoomStatusReactive room = new()
        {
            RoomUrl = "https://example.test/room",
        };
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            MainViewModel.GetRoomRecordingSummary(room, cancellation.Token));
    }
}
