using Emerde.Core;

namespace Emerde.Tests;

public sealed class SegmentTimeUnitHelperTests
{
    [Fact]
    public void ToConfigValue_PreservesLargeGigabyteValues()
    {
        long value = SegmentTimeUnitHelper.ToConfigValue(10, SegmentTimeUnitHelper.Gigabytes);

        Assert.Equal(10_000_000_000L, value);
        Assert.Equal(10, SegmentTimeUnitHelper.ToDisplayValue(value, SegmentTimeUnitHelper.Gigabytes));
    }
}
