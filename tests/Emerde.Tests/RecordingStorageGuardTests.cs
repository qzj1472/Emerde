using Emerde.Core;

namespace Emerde.Tests;

public sealed class RecordingStorageGuardTests
{
    [Fact]
    public void MinimumFreeSpaceUsesTenGibibytesAndIncludesBoundary()
    {
        Assert.Equal(10L * 1024 * 1024 * 1024, RecordingStorageGuard.MinimumFreeBytes);
        Assert.True(RecordingStorageGuard.IsLowBytes(RecordingStorageGuard.MinimumFreeBytes));
        Assert.False(RecordingStorageGuard.IsLowBytes(RecordingStorageGuard.MinimumFreeBytes + 1));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    public void ExhaustedStorageOnlyMatchesZeroAvailableBytes(long availableBytes, bool expected)
    {
        Assert.Equal(expected, RecordingStorageGuard.IsExhaustedBytes(availableBytes));
    }
}
