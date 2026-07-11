using Emerde.Controls;
using WpfSize = System.Windows.Size;

namespace Emerde.Tests;

public sealed class AdaptiveSplitPanelTests
{
    [Fact]
    public void CalculateLayout_UsesEqualSpacingBetweenThreeContainers()
    {
        AdaptiveSplitLayout layout = AdaptiveSplitPanel.CalculateLayout(
            600,
            new WpfSize(180, 32),
            new WpfSize(140, 28),
            new WpfSize(80, 28),
            24,
            12);

        Assert.Equal(1, layout.RowCount);
        Assert.Equal(0, layout.First.X);
        Assert.Equal(280, layout.Second.X);
        Assert.Equal(520, layout.Third.X);
        Assert.Equal(100, layout.Second.X - layout.First.Right);
        Assert.Equal(100, layout.Third.X - layout.Second.Right);
        Assert.Equal(600, layout.Third.Right);
        Assert.Equal(32, layout.DesiredHeight);
    }

    [Fact]
    public void CalculateLayout_WrapsOptionsTogetherWhenSingleRowIsTooNarrow()
    {
        AdaptiveSplitLayout layout = AdaptiveSplitPanel.CalculateLayout(
            430,
            new WpfSize(180, 32),
            new WpfSize(190, 28),
            new WpfSize(80, 28),
            24,
            12);

        Assert.Equal(2, layout.RowCount);
        Assert.Equal(0, layout.First.X);
        Assert.Equal(0, layout.Second.X);
        Assert.Equal(350, layout.Third.X);
        Assert.Equal(430, layout.Third.Right);
        Assert.Equal(44, layout.Second.Y);
        Assert.Equal(44, layout.Third.Y);
        Assert.Equal(72, layout.DesiredHeight);
    }

    [Fact]
    public void CalculateLayout_WrapsEachContainerWithoutClipping()
    {
        AdaptiveSplitLayout layout = AdaptiveSplitPanel.CalculateLayout(
            240,
            new WpfSize(180, 32),
            new WpfSize(190, 28),
            new WpfSize(80, 28),
            24,
            12);

        Assert.Equal(3, layout.RowCount);
        Assert.Equal(0, layout.Second.X);
        Assert.Equal(160, layout.Third.X);
        Assert.Equal(44, layout.Second.Y);
        Assert.Equal(84, layout.Third.Y);
        Assert.Equal(112, layout.DesiredHeight);
    }
}
