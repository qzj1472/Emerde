using Emerde.Core;

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

}
