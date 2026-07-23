using Emerde.Core;

namespace Emerde.Tests;

public sealed class WindowSizingTests
{
    [Theory]
    [InlineData(1d, 0.70d)]
    [InlineData(1.25d, 0.775d)]
    [InlineData(1.5d, 0.85d)]
    [InlineData(2d, 0.85d)]
    public void MainWindowWidthRatio_CompensatesForSystemDpi(double dpiScale, double expected)
    {
        Assert.Equal(expected, WindowSizing.CalculateMainWindowWidthRatio(dpiScale), 6);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void MainWindowWidthRatio_InvalidDpiUsesDefault(double dpiScale)
    {
        Assert.Equal(0.70d, WindowSizing.CalculateMainWindowWidthRatio(dpiScale));
    }
}
