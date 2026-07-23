using Emerde.ViewModels;

namespace Emerde.Tests;

public sealed class NetworkCapacityTests
{
    [Theory]
    [InlineData(0d, 5d, null)]
    [InlineData(100d, 0d, null)]
    [InlineData(double.NaN, 5d, null)]
    [InlineData(double.PositiveInfinity, 5d, null)]
    [InlineData(100d, 5d, 14)]
    [InlineData(1d, 10d, 1)]
    public void CalculateNetworkCapacity_OnlyReturnsValidMeasuredResults(double measuredMbps, double perRoomMbps, int? expected)
    {
        Assert.Equal(expected, MainViewModel.CalculateNetworkCapacity(measuredMbps, perRoomMbps));
    }

    [Fact]
    public void CalculateNetworkCapacity_DoesNotEstimateMissingMeasurement()
    {
        Assert.Null(MainViewModel.CalculateNetworkCapacity(null, 5d));
    }
}
