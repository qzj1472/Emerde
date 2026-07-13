using Emerde.Core;
using Emerde.Views;

namespace Emerde.Tests;

public sealed class AddRoomContentDialogTests
{
    [Fact]
    public void GetRoomInfoErrorMessage_AppendsResolverDetail()
    {
        string result = AddRoomContentDialog.GetRoomInfoErrorMessage(null, "resolver failed");

        Assert.Contains("resolver failed", result);
    }

    [Fact]
    public void HasAddableRoomInfo_AllowsOfflineValidRoom()
    {
        StreamResolverResult result = new()
        {
            RoomUrl = "https://live.douyin.com/123456",
            PlatformName = "Douyin",
            IsLiveStreaming = false,
            Nickname = "anchor",
        };

        Assert.True(AddRoomContentDialog.HasAddableRoomInfo(result, result.RoomUrl));
        Assert.Equal("anchor", AddRoomContentDialog.GetConfirmedNickName(result));
    }

    [Fact]
    public void HasAddableRoomInfo_RejectsOfflineStatusWithoutRoomIdentity()
    {
        StreamResolverResult result = new()
        {
            RoomUrl = "https://live.douyin.com/123456",
            PlatformName = "Douyin",
            IsLiveStreaming = false,
        };

        Assert.False(AddRoomContentDialog.HasAddableRoomInfo(result, result.RoomUrl));
    }
}
