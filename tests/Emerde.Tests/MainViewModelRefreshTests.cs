using Emerde.Core;
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
}
