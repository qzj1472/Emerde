using Emerde.Views;
using System.Xml.Linq;

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
    public void PreviewPaneWidths_HideDetailsOnWideWindows()
    {
        Assert.Equal((320d, 0d), MainWindow.CalculatePreviewPaneWidths(1600d));
    }

    [Fact]
    public void SmallCardAvatarSize_FitsSmallCardContainer()
    {
        Assert.Equal(18d, MainWindow.CalculateRoomCardAvatarSize(0.5d));
    }

    [Fact]
    public void PreviewPaneWidths_HideDetailsOnMediumWindows()
    {
        Assert.Equal((280d, 0d), MainWindow.CalculatePreviewPaneWidths(1100d));
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
    public void ResponsiveLayout_KeepsCardsInsideStretchLimits()
    {
        const double availableWidth = 590d;

        (int columns, double slotWidth, double cardWidth) = MainWindow.CalculateRoomCardLayout(availableWidth, 250d, 1d, 12d);

        Assert.Equal(2, columns);
        Assert.Equal(availableWidth, slotWidth * columns, 6);
        Assert.Equal(283d, cardWidth, 6);
    }

    [Fact]
    public void ResponsiveLayout_DoesNotCreateUndersizedCardAtColumnBoundary()
    {
        (int columns, _, double cardWidth) = MainWindow.CalculateRoomCardLayout(350d, 200d, 1d, 12d);

        Assert.Equal(2, columns);
        Assert.Equal(172d, cardWidth, 6);
    }

    [Fact]
    public void ResponsiveLayout_AddsColumnsInsteadOfStretchingCardsAcrossWideRows()
    {
        (int columns, double slotWidth, double cardWidth) = MainWindow.CalculateRoomCardLayout(1160d, 264d, 1d, 12d);

        Assert.Equal(4, columns);
        Assert.Equal(290d, slotWidth, 6);
        Assert.Equal(278d, cardWidth, 6);
    }

    [Fact]
    public void ColumnStabilization_KeepsCurrentColumnsInsideHysteresisBand()
    {
        Assert.Equal(2, MainWindow.StabilizeRoomCardColumns(2, 3, 440d, 200d, 12d));
        Assert.Equal(3, MainWindow.StabilizeRoomCardColumns(3, 2, 580d, 200d, 12d));
    }

    [Fact]
    public void ColumnStabilization_AcceptsCandidateOutsideHysteresisBand()
    {
        Assert.Equal(3, MainWindow.StabilizeRoomCardColumns(2, 3, 500d, 200d, 12d));
        Assert.Equal(2, MainWindow.StabilizeRoomCardColumns(3, 2, 510d, 200d, 12d));
    }

    [Fact]
    public void RoomCardItemsPanel_UsesExplicitColumnsWithoutAutomaticWrapping()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement itemsPanel = document.Descendants().First(element => element.Name.LocalName == "ItemsPanelTemplate");
        XElement panel = itemsPanel.Elements().Single();

        Assert.Equal("UniformGrid", panel.Name.LocalName);
        Assert.Contains("RoomCardColumnCount", (string?)panel.Attribute("Columns") ?? string.Empty);
        Assert.Equal("Top", (string?)panel.Attribute("VerticalAlignment"));
        Assert.DoesNotContain(itemsPanel.Descendants(), element => element.Name.LocalName == "WrapPanel");
    }

    [Fact]
    public void TopAlignedRoomCardGrid_KeepsFixedRowSpacingWhenTheViewportIsUnderfilled()
    {
        RunOnStaThread(() =>
        {
            System.Windows.Controls.Primitives.UniformGrid panel = new()
            {
                Columns = 3,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
            };
            List<System.Windows.Controls.Border> cards = [];
            for (int index = 0; index < 6; index++)
            {
                System.Windows.Controls.Border card = new()
                {
                    Width = 100d,
                    Height = 60d,
                    Margin = new System.Windows.Thickness(6d),
                };
                cards.Add(card);
                panel.Children.Add(card);
            }

            panel.Measure(new System.Windows.Size(600d, 400d));
            panel.Arrange(new System.Windows.Rect(0d, 0d, 600d, 400d));

            System.Windows.Point firstRow = cards[0].TranslatePoint(new System.Windows.Point(), panel);
            System.Windows.Point secondRow = cards[3].TranslatePoint(new System.Windows.Point(), panel);

            Assert.Equal(144d, panel.ActualHeight);
            Assert.Equal(72d, secondRow.Y - firstRow.Y);
        });
    }

    [Fact]
    public void RoomCardScrollViewer_UsesTheSharedScrollBarStyleInsideTheEighteenPixelRail()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement viewer = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RoomCardScrollViewer");
        XElement roomCardList = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RoomCardList");
        XElement style = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "CenteredVerticalScrollViewerStyle");
        XElement template = style.Descendants().Single(element => element.Name.LocalName == "ControlTemplate");
        XElement scrollBar = template.Descendants().Single(element => element.Name.LocalName == "ScrollBar");

        Assert.Equal("{StaticResource CenteredVerticalScrollViewerStyle}", (string?)viewer.Attribute("Style"));
        Assert.Equal("6", (string?)viewer.Attribute("Padding"));
        Assert.Equal("Auto", (string?)roomCardList.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
        Assert.Equal("6", (string?)style.Elements().Single(element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "Padding").Attribute("Value"));
        Assert.Contains(template.Descendants(), element => element.Name.LocalName == "ColumnDefinition" && (string?)element.Attribute("Width") == "Auto");
        Assert.Equal("18", (string?)scrollBar.Attribute("Width"));
        Assert.Equal("1", (string?)scrollBar.Attribute("Grid.Column"));
        Assert.Equal("Center", (string?)scrollBar.Attribute("HorizontalAlignment"));
        Assert.Equal("{StaticResource UiScrollBar}", (string?)scrollBar.Attribute("Style"));
        Assert.Null(scrollBar.Attribute("Margin"));
        Assert.Empty(scrollBar.Elements());
        Assert.Contains(template.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "RoomCardScrollContentPresenter"
            && (string?)element.Attribute("Property") == "Margin"
            && (string?)element.Attribute("Value") == "6,6,-6,6");
        XElement listStyle = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "RoomCardListBoxStyle");
        XElement listTemplate = listStyle.Descendants().Single(element => element.Name.LocalName == "ControlTemplate");
        XElement topFade = listTemplate.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RoomCardTopFade");
        XElement bottomFade = listTemplate.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RoomCardBottomFade");
        Assert.Equal("6,6,6,0", (string?)topFade.Attribute("Margin"));
        Assert.Equal("6,0,6,6", (string?)bottomFade.Attribute("Margin"));
    }

    [Fact]
    public void HomeStatusTray_UsesTheSameSixPixelInsetAboveAndBelow()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement statusTray = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "HomeStatusTray");
        XElement shellSurface = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "ShellContentSurface");

        Assert.Null(statusTray.Attribute("Margin"));
        Assert.Contains(statusTray.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Margin"
            && (string?)element.Attribute("Value") == "2,6,2,0");
        Assert.Contains(statusTray.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Margin"
            && (string?)element.Attribute("Value") == "18,6,18,0");
        Assert.EndsWith(",6", (string?)shellSurface.Attribute("Padding"));
    }

    [Theory]
    [InlineData(100d, 300d, -20d, 80d, 80d)]
    [InlineData(100d, 300d, 350d, 100d, 250d)]
    [InlineData(100d, 300d, 50d, 100d, 100d)]
    public void ScrollOffset_RevealsTheWholeSelectedCard(
        double currentOffset,
        double viewportHeight,
        double itemTop,
        double itemHeight,
        double expected)
    {
        Assert.Equal(expected, MainWindow.CalculateScrollOffsetToReveal(currentOffset, viewportHeight, itemTop, itemHeight));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
