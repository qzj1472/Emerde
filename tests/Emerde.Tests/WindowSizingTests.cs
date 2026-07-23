using Emerde.Core;
using Emerde.Views;

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

    [Fact]
    public void MainWindowMaximizedBounds_UseCurrentScreenWorkArea()
    {
        System.Drawing.Rectangle monitor = new(1920, 0, 2560, 1440);
        System.Drawing.Rectangle workArea = new(1920, 0, 2560, 1392);

        MaximizedWindowBounds bounds = MainWindow.CalculateMaximizedWindowBounds(monitor, workArea);

        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(2560, bounds.Width);
        Assert.Equal(1392, bounds.Height);
        Assert.Equal(2560, bounds.MaxTrackWidth);
        Assert.Equal(1392, bounds.MaxTrackHeight);
    }

    [Fact]
    public void MainWindowMaximizedBounds_HandleOffsetTaskbar()
    {
        System.Drawing.Rectangle monitor = new(-1280, 0, 1280, 720);
        System.Drawing.Rectangle workArea = new(-1272, 8, 1264, 704);

        MaximizedWindowBounds bounds = MainWindow.CalculateMaximizedWindowBounds(monitor, workArea);

        Assert.Equal(8, bounds.X);
        Assert.Equal(8, bounds.Y);
        Assert.Equal(1264, bounds.Width);
        Assert.Equal(704, bounds.Height);
    }
}
