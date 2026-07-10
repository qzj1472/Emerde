using Emerde.Views;

namespace Emerde.Tests;

public sealed class RoomCardLayoutTests
{
    [Fact]
    public void PreviewWidth_KeepsMediumCardsOnOneColumn()
    {
        (int columns, _, _) = MainWindow.CalculateRoomCardLayout(276d, 264d, 1d, 12d);

        Assert.Equal(1, columns);
    }

    [Fact]
    public void PreviewWidth_WrapsSmallCardsIntoTwoColumns()
    {
        (int columns, _, _) = MainWindow.CalculateRoomCardLayout(276d, 264d, 0.5d, 12d);

        Assert.Equal(2, columns);
    }

    [Fact]
    public void HomeDetailWidth_SubtractsOneSeventhFromDefaultMaximum()
    {
        Assert.Equal(309d, MainWindow.GetHomeDetailPanelMaxWidth());
    }

    [Fact]
    public void PreviewDetailWidth_KeepsSeventyFivePercentOfDefaultWidth()
    {
        Assert.Equal(232d, MainWindow.GetPreviewDetailColumnWidth());
    }

    [Fact]
    public void SmallCardAvatarSize_FitsSmallCardContainer()
    {
        Assert.Equal(18d, MainWindow.CalculateRoomCardAvatarSize(0.5d));
    }

    [Fact]
    public void PreviewRoomCardColumnWidth_ReducesMediumWidthByFifteenPercent()
    {
        Assert.Equal(266d, MainWindow.CalculatePreviewRoomCardColumnWidth(264d));
    }

    [Fact]
    public void SmallCardSpacing_ReducesDefaultGapByOneThird()
    {
        Assert.Equal(8d, MainWindow.GetRoomCardHorizontalGap(0.5d));
        Assert.Equal(8d, MainWindow.GetRoomCardVerticalGap(0.5d));
    }
}
