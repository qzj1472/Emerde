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
    public void PreviewPaneWidths_KeepThreeColumnsOnWideWindows()
    {
        Assert.Equal((320d, 260d), MainWindow.CalculatePreviewPaneWidths(1600d));
    }

    [Fact]
    public void SmallCardAvatarSize_FitsSmallCardContainer()
    {
        Assert.Equal(18d, MainWindow.CalculateRoomCardAvatarSize(0.5d));
    }

    [Fact]
    public void PreviewPaneWidths_CompressSideColumnsOnMediumWindows()
    {
        Assert.Equal((280d, 220d), MainWindow.CalculatePreviewPaneWidths(1100d));
    }

    [Theory]
    [InlineData(850d, 230d)]
    [InlineData(700d, 190d)]
    public void PreviewPaneWidths_HideDetailsOnNarrowWindows(double availableWidth, double expectedRoomListWidth)
    {
        Assert.Equal((expectedRoomListWidth, 0d), MainWindow.CalculatePreviewPaneWidths(availableWidth));
    }

    [Fact]
    public void SmallCardSpacing_ReducesDefaultGapByOneThird()
    {
        Assert.Equal(8d, MainWindow.GetRoomCardHorizontalGap(0.5d));
        Assert.Equal(8d, MainWindow.GetRoomCardVerticalGap(0.5d));
    }

    [Fact]
    public void ResponsiveLayout_FillsAvailableWidthAfterColumnChange()
    {
        const double availableWidth = 590d;

        (int columns, double slotWidth, double cardWidth) = MainWindow.CalculateRoomCardLayout(availableWidth, 250d, 1d, 12d);

        Assert.Equal(availableWidth, slotWidth * columns, 6);
        Assert.Equal(slotWidth - 12d, cardWidth, 6);
    }

    [Fact]
    public void ResponsiveLayout_DoesNotCreateOversizedCardAtColumnBoundary()
    {
        (int columns, _, double cardWidth) = MainWindow.CalculateRoomCardLayout(350d, 200d, 1d, 12d);

        Assert.Equal(2, columns);
        Assert.Equal(163d, cardWidth, 6);
    }
}
