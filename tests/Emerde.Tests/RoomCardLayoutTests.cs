using Emerde.Views;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class RoomCardLayoutTests
{
    [Fact]
    public void UiXRoomCardCornerRadiiStayFixedAcrossCardScales()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement card = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "RoomCardShell");
        XElement style = card.Elements().Single(element => element.Name.LocalName == "Border.Style");

        Assert.Contains(style.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "CornerRadius"
            && (string?)element.Attribute("Value") == "{StaticResource UiXSurfaceCornerRadius}");
        Assert.DoesNotContain(card.DescendantsAndSelf(), element =>
            ((string?)element.Attribute("CornerRadius"))?.Contains("Binding", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void UiXHomeBaseWidthDropsTenPercentWithoutChangingPreview()
    {
        Assert.Equal(180d, MainWindow.GetUiXRoomCardBaseWidth(false));
        Assert.Equal(200d, MainWindow.GetUiXRoomCardBaseWidth(true));
    }

    [Theory]
    [InlineData(0, 1d, 0.5d)]
    [InlineData(1, 0.5d, 1d)]
    [InlineData(2, 0.5d, 1.5d)]
    [InlineData(99, 1d, 1d)]
    public void StoredRoomCardSizePreferenceRestoresKnownValues(int storedValue, double fallback, double expected)
    {
        Assert.Equal(expected, MainWindow.GetStoredRoomCardSizePreference(storedValue, fallback));
    }

    [Theory]
    [InlineData(0.5d, 0)]
    [InlineData(1d, 1)]
    [InlineData(1.5d, 2)]
    public void RoomCardSizePreferenceUsesStableStoredValues(double preference, int expected)
    {
        Assert.Equal(expected, MainWindow.GetStoredRoomCardSizeValue(preference));
    }

    [Fact]
    public void RoomCardSizeAndSortPreferencesArePersisted()
    {
        string configurations = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Configurations.cs"));
        string window = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        string viewModel = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("RoomCardSizePreference), 1", configurations, StringComparison.Ordinal);
        Assert.Contains("PreviewRoomCardSizePreference), 0", configurations, StringComparison.Ordinal);
        Assert.Contains("IsRoomSortByName), false", configurations, StringComparison.Ordinal);
        Assert.Contains("Configurations.RoomCardSizePreference.Set", window, StringComparison.Ordinal);
        Assert.Contains("Configurations.PreviewRoomCardSizePreference.Set", window, StringComparison.Ordinal);
        Assert.Contains("Configurations.IsRoomSortByName.Get()", viewModel, StringComparison.Ordinal);
        Assert.Contains("Configurations.IsRoomSortByName.Set(sortByName)", viewModel, StringComparison.Ordinal);
    }

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
    public void PreviewMode_UsesSharedScalingWithoutSparseHomeSizing()
    {
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("NormalizePreviewRoomCardScale(availableWidth, baseWidth, activeSizePreference)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculatePreviewRoomCardScale", code, StringComparison.Ordinal);
        Assert.Contains("availableWidth / columns - horizontalGap", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculatePreviewRoomCardColumns(availableWidth, baseWidth, effectivePreference, horizontalGap)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPreviewCardWidthForColumns(availableWidth, columns, horizontalGap, baseWidth, effectivePreference)", code, StringComparison.Ordinal);
        Assert.Contains("fillAvailableWidth", code, StringComparison.Ordinal);
        Assert.Contains("availableWidth / columns - horizontalGap", code, StringComparison.Ordinal);
        Assert.Contains("isUiXPreviewMode ? effectivePreference : null", code, StringComparison.Ordinal);
        Assert.DoesNotContain("columns = Math.Min(2, columns)", code, StringComparison.Ordinal);
        Assert.Contains("fillAvailableWidth", code, StringComparison.Ordinal);
        Assert.Contains("GetRoomCardLayoutWidth(width, ViewModel.StatusOfIsUiXEnabled)", code, StringComparison.Ordinal);
        Assert.Contains("Configurations.PreviewRoomCardSizePreference.Get()", code, StringComparison.Ordinal);
        Assert.Contains("bool isSparseHomeRow = !isUiXPreviewMode", code, StringComparison.Ordinal);
        Assert.Contains(": GetCardWidthForColumns(availableWidth, columns, horizontalGap, range)", code, StringComparison.Ordinal);
        Assert.Contains("CalculateResponsiveCardLayout", code, StringComparison.Ordinal);
        Assert.Contains("GetCurrentRoomCardLayoutWidth", code, StringComparison.Ordinal);
        Assert.Contains("RoomCardPanelContent.ActualWidth", code, StringComparison.Ordinal);
        Assert.Contains("IsLoaded && (!ViewModel.IsPreviewing || !ViewModel.StatusOfIsUiXEnabled)", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1000d, 0.5d, 0.5d)]
    [InlineData(1000d, 1d, 0.70d)]
    [InlineData(1000d, 1.5d, 0.90d)]
    public void PreviewCardScale_KeepsSmallAndReducesMediumAndLarge(double availableWidth, double preference, double expectedScale)
    {
        Assert.Equal(expectedScale, MainWindow.NormalizePreviewRoomCardScale(availableWidth, 200d, preference), 6);
    }

    [Theory]
    [InlineData(166d, 1.5d, 0.70d)]
    [InlineData(132d, 1.5d, 0.5d)]
    [InlineData(132d, 1d, 0.5d)]
    public void PreviewCardScale_FallsBackThroughPreviewSizeSteps(double availableWidth, double preference, double expectedScale)
    {
        Assert.Equal(expectedScale, MainWindow.NormalizePreviewRoomCardScale(availableWidth, 200d, preference), 6);
    }

    [Theory]
    [InlineData(120d, 200d, 0.70d, 12d, 1)]
    [InlineData(269d, 200d, 0.70d, 12d, 1)]
    [InlineData(270d, 200d, 0.70d, 12d, 2)]
    [InlineData(300d, 200d, 0.70d, 12d, 2)]
    [InlineData(220d, 200d, 0.5d, 8d, 2)]
    public void PreviewColumns_UseEveryColumnThatFitsTheCurrentScale(
        double availableWidth,
        double baseWidth,
        double scale,
        double horizontalGap,
        int expectedColumns)
    {
        Assert.Equal(
            expectedColumns,
            MainWindow.CalculatePreviewRoomCardColumns(availableWidth, baseWidth, scale, horizontalGap));
    }

    [Fact]
    public void PreviewColumns_RemoveSingleColumnWhitespaceWhenTwoMediumCardsFit()
    {
        const double availableWidth = 300d;
        const double baseWidth = 200d;
        const double scale = 0.70d;
        const double horizontalGap = 12d;

        int columns = MainWindow.CalculatePreviewRoomCardColumns(availableWidth, baseWidth, scale, horizontalGap);
        double cardWidth = MainWindow.GetPreviewCardWidthForColumns(availableWidth, columns, horizontalGap, baseWidth, scale);

        Assert.Equal(2, columns);
        Assert.Equal(138d, cardWidth, 6);
        Assert.Equal(availableWidth, columns * (cardWidth + horizontalGap), 6);
    }

    [Fact]
    public void PreviewScaleBoundaries_PassThroughEveryStepWithoutOverlap()
    {
        const double baseWidth = 200d;

        double smallScale = MainWindow.NormalizePreviewRoomCardScale(132d, baseWidth, 1.5d);
        double mediumScale = MainWindow.NormalizePreviewRoomCardScale(135d, baseWidth, 1.5d);
        double mediumBeforeLarge = MainWindow.NormalizePreviewRoomCardScale(174d, baseWidth, 1.5d);
        double largeScale = MainWindow.NormalizePreviewRoomCardScale(175d, baseWidth, 1.5d);

        Assert.Equal(0.5d, smallScale, 6);
        Assert.Equal(0.70d, mediumScale, 6);
        Assert.Equal(0.70d, mediumBeforeLarge, 6);
        Assert.Equal(0.90d, largeScale, 6);

        (double smallMinimum, double smallMaximum) = MainWindow.GetPreviewRoomCardWidthRange(baseWidth, smallScale);
        (double mediumMinimum, double mediumMaximum) = MainWindow.GetPreviewRoomCardWidthRange(baseWidth, mediumScale);
        (double largeMinimum, double largeMaximum) = MainWindow.GetPreviewRoomCardWidthRange(baseWidth, largeScale);

        Assert.True(smallMinimum < smallMaximum);
        Assert.True(smallMaximum < mediumMinimum);
        Assert.True(mediumMinimum < mediumMaximum);
        Assert.True(mediumMinimum < largeMinimum);
        Assert.True(mediumMaximum < largeMaximum);
        Assert.True(largeMinimum < largeMaximum);
    }

    [Theory]
    [InlineData(0.8d, 366d, 409d)]
    [InlineData(1d, 414d, 523d)]
    [InlineData(1.3d, 528d, 594d)]
    public void CardWidthRange_UsesIntegerMidpointsWithFourPixelSafetyGaps(
        double preference,
        double expectedMinimum,
        double expectedMaximum)
    {
        MainWindow.CardWidthRange range = MainWindow.GetCardWidthRange(
            457d,
            preference,
            0.8d,
            1d,
            1.3d);

        Assert.Equal(expectedMinimum, range.Minimum);
        Assert.Equal(expectedMaximum, range.Maximum);
    }

    [Theory]
    [InlineData(160d, 160d, 178d, 160d)]
    [InlineData(200d, 183d, 228d, 200d)]
    [InlineData(260d, 233d, 260d, 260d)]
    public void SparseHomeRows_KeepTheSelectedSizeTarget(
        double targetWidth,
        double minimumWidth,
        double maximumWidth,
        double expectedWidth)
    {
        double cardWidth = MainWindow.GetSparseHomeRoomCardWidth(
            targetWidth,
            new MainWindow.CardWidthRange(minimumWidth, maximumWidth));

        Assert.Equal(expectedWidth, cardWidth);
    }

    [Theory]
    [InlineData(548.4d, 3, 12d, 171d, 170d)]
    [InlineData(320.8d, 2, 12d, 180d, 148d)]
    [InlineData(120d, 1, 12d, 144d, 108d)]
    public void ResponsiveCardWidth_NeverExceedsItsAvailableSlot(
        double availableWidth,
        int columns,
        double horizontalGap,
        double cardWidth,
        double expectedWidth)
    {
        double fittedWidth = MainWindow.FitRoomCardWidthToAvailableSpace(
            availableWidth,
            columns,
            horizontalGap,
            cardWidth);

        Assert.Equal(expectedWidth, fittedWidth);
        Assert.True(columns * (fittedWidth + horizontalGap) <= availableWidth);
    }

    [Fact]
    public void SparseHomeRows_DoNotStretchAcrossUnusedColumns()
    {
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("int layoutCapacityColumns = columns", code, StringComparison.Ordinal);
        Assert.Contains("!isUiXPreviewMode && visibleItemCount < layoutCapacityColumns", code, StringComparison.Ordinal);
        Assert.Contains("GetSparseHomeRoomCardWidth(targetCardWidth, range)", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(530d, 2)]
    [InlineData(542d, 3)]
    public void ResponsiveCardColumns_KeepCurrentCountUntilGrowthHysteresisIsCrossed(
        double availableWidth,
        int expectedColumns)
    {
        int columns = MainWindow.StabilizeResponsiveCardColumns(
            2,
            3,
            availableWidth,
            212d,
            100d,
            12d);

        Assert.Equal(expectedColumns, columns);
    }

    [Theory]
    [InlineData(519d, 3)]
    [InlineData(518d, 2)]
    public void ResponsiveCardColumns_KeepCurrentCountUntilShrinkHysteresisIsCrossed(
        double availableWidth,
        int expectedColumns)
    {
        int columns = MainWindow.StabilizeResponsiveCardColumns(
            3,
            2,
            availableWidth,
            212d,
            100d,
            12d);

        Assert.Equal(expectedColumns, columns);
    }

    [Fact]
    public void ResponsiveCardColumns_ReduceImmediatelyWhenCurrentCardsNoLongerFit()
    {
        int columns = MainWindow.StabilizeResponsiveCardColumns(
            3,
            3,
            500d,
            212d,
            180d,
            12d);

        Assert.Equal(2, columns);
    }

    [Theory]
    [InlineData(132d, 1, 8d, 0.5d, 124d)]
    [InlineData(180d, 1, 8d, 0.5d, 172d)]
    [InlineData(265d, 1, 12d, 0.70d, 253d)]
    [InlineData(266d, 2, 12d, 0.70d, 121d)]
    public void PreviewCardWidth_ElasticallyFillsTheCurrentColumnCount(
        double availableWidth,
        int columns,
        double horizontalGap,
        double scale,
        double expectedWidth)
    {
        double width = MainWindow.GetPreviewCardWidthForColumns(availableWidth, columns, horizontalGap, 200d, scale);

        Assert.Equal(expectedWidth, width, 6);
    }

    [Theory]
    [InlineData(0.5d, 86d, 114d)]
    [InlineData(0.70d, 120d, 160d)]
    [InlineData(0.90d, 155d, 205d)]
    public void PreviewCardWidth_UsesTheSameVisualSizeLimitsAsHome(double scale, double expectedMinimum, double expectedMaximum)
    {
        double targetWidth = 200d * scale;
        Assert.Equal(expectedMinimum, MainWindow.GetCardWidthForColumns(1d, 1, 0d, targetWidth), 6);
        Assert.Equal(expectedMaximum, MainWindow.GetCardWidthForColumns(1000d, 1, 0d, targetWidth), 6);
    }

    [Theory]
    [InlineData(200d, 1d, 12d, 188d)]
    [InlineData(400d, 1d, 12d, 188d)]
    [InlineData(640d, 0.75d, 12d, 148d)]
    public void SharedCardLayout_FillsWidthWithoutLeavingItsElasticRange(
        double availableWidth,
        double preference,
        double horizontalGap,
        double expectedApproximateWidth)
    {
        (int columns, _, _) = MainWindow.CalculateRoomCardLayout(availableWidth, 200d, preference, horizontalGap);
        double targetWidth = 200d * preference;
        double width = Math.Floor((availableWidth / columns - horizontalGap) * 100d) / 100d;

        Assert.InRange(width, targetWidth * 0.86d, targetWidth * 1.14d);
        Assert.InRange(width, expectedApproximateWidth - 25d, expectedApproximateWidth + 25d);
        Assert.Equal(availableWidth, columns * (width + horizontalGap), 1);
    }

    [Fact]
    public void HomeDetailWidth_SubtractsOneSeventhFromDefaultMaximum()
    {
        Assert.Equal(309d, MainWindow.GetHomeDetailPanelMaxWidth());
    }

    [Fact]
    public void PreviewPaneWidths_HideDetailsOnWideWindows()
    {
        Assert.Equal((342d, 0d), MainWindow.CalculatePreviewPaneWidths(1600d));
    }

    [Fact]
    public void SmallCardAvatarSize_FitsSmallCardContainer()
    {
        Assert.Equal(18d, MainWindow.CalculateRoomCardAvatarSize(0.5d));
    }

    [Fact]
    public void PreviewPaneWidths_HideDetailsOnMediumWindows()
    {
        Assert.Equal((254d, 0d), MainWindow.CalculatePreviewPaneWidths(1100d));
    }

    [Theory]
    [InlineData(850d, 234d)]
    [InlineData(700d, 144d)]
    public void PreviewPaneWidths_HideDetailsOnNarrowWindows(double availableWidth, double expectedRoomListWidth)
    {
        Assert.Equal((expectedRoomListWidth, 0d), MainWindow.CalculatePreviewPaneWidths(availableWidth));
    }

    [Fact]
    public void PreviewPaneWidth_TracksAvailableWindowWidthWithoutPixelClamps()
    {
        Assert.Equal((270d, 0d), MainWindow.CalculatePreviewPaneWidths(1200d));
        Assert.Equal((270d, 0d), MainWindow.CalculatePreviewPaneWidths(1300d));
        Assert.Equal((144d, 0d), MainWindow.CalculatePreviewPaneWidths(640d));
    }

    [Fact]
    public void UiXHomeCardText_StaysInsideTheCardAtNarrowWidths()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement layout = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RoomCardUiXLayout");
        XElement name = layout.Descendants().Single(element => (string?)element.Attribute("Text") == "{Binding NickName}");
        XElement title = layout.Descendants().Single(element => (string?)element.Attribute("Text") == "{Binding LiveTitleText}");

        Assert.Equal("Stretch", (string?)name.Attribute("HorizontalAlignment"));
        Assert.Equal("{Binding UiXRoomCardNameMargin, RelativeSource={RelativeSource AncestorType={x:Type views:MainWindow}}}", (string?)name.Attribute("Margin"));
        Assert.Equal("Stretch", (string?)title.Attribute("HorizontalAlignment"));
        Assert.Equal("{Binding UiXRoomCardTitleMargin, RelativeSource={RelativeSource AncestorType={x:Type views:MainWindow}}}", (string?)title.Attribute("Margin"));
    }

    [Fact]
    public void UiXRoomCards_ScaleFixedInternalLayoutWithoutScalingOuterCorners()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement viewbox = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RoomCardUiXScaleView");
        XElement layoutBorder = viewbox.Elements().Single();

        Assert.Equal("Fill", (string?)viewbox.Attribute("Stretch"));
        Assert.Equal("Both", (string?)viewbox.Attribute("StretchDirection"));
        Assert.Equal("{Binding UiXRoomCardLayoutWidth, RelativeSource={RelativeSource AncestorType={x:Type views:MainWindow}}}", (string?)layoutBorder.Attribute("Width"));
        Assert.Equal("{Binding UiXRoomCardLayoutHeight, RelativeSource={RelativeSource AncestorType={x:Type views:MainWindow}}}", (string?)layoutBorder.Attribute("Height"));
        Assert.Contains("double visualCardWidth = isUiXMode ? targetCardWidth : cardWidth", code, StringComparison.Ordinal);
        Assert.Contains("UpdateRoomCardVisualMetrics(visualCardWidth", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DoesNotReapplyWindowChromeForEverySizeChangedEvent()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("DwmAnimation.EnableDwmAnimation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged += (_, _) =>\r\n        {\r\n            EnforceBorderlessWindowChrome();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NCRenderingPolicy", code, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginInteractiveWindowMove", code, StringComparison.Ordinal);
        Assert.DoesNotContain("EndInteractiveWindowMove", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowResize_CoalescesRoomCardAndPreviewLayoutWork()
    {
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("roomCardMetricsRefreshOperation", code, StringComparison.Ordinal);
        Assert.Contains("homeResponsiveLayoutUpdateOperation", code, StringComparison.Ordinal);
        Assert.Contains("QueueRoomCardMetricsRefresh(e.NewSize.Width)", code, StringComparison.Ordinal);
        Assert.Contains("QueueHomeResponsiveLayoutUpdate(isHomePreviewColumnAnimationActive)", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateRoomCardMetrics(e.NewSize.Width)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeOptimizedStackPanel_DefersOffscreenMeasurementUntilItApproachesTheViewport()
    {
        RunOnStaThread(() =>
        {
            System.Windows.Controls.ScrollViewer viewer = new()
            {
                Width = 400d,
                Height = 100d,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            };
            Emerde.Controls.ResizeOptimizedStackPanel panel = new();
            MeasureCountingElement[] children = Enumerable.Range(0, 5)
                .Select(_ => new MeasureCountingElement())
                .ToArray();
            foreach (MeasureCountingElement child in children)
            {
                panel.Children.Add(child);
            }
            viewer.Content = panel;

            viewer.Measure(new System.Windows.Size(400d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 400d, 100d));
            viewer.UpdateLayout();
            int visibleInitialCount = children[0].MeasureCount;
            int hiddenInitialCount = children[^1].MeasureCount;
            System.Windows.Controls.ScrollViewer? owner = Emerde.Controls.ResizeOptimizedStackPanel.FindScrollOwner(panel);

            Assert.Same(viewer, owner);
            Assert.False(Emerde.Controls.ResizeOptimizedStackPanel.IsNearViewport(children[^1], owner));

            viewer.Width = 420d;
            viewer.Measure(new System.Windows.Size(420d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 420d, 100d));
            viewer.UpdateLayout();

            Assert.True(children[0].MeasureCount > visibleInitialCount);
            Assert.Equal(hiddenInitialCount, children[^1].MeasureCount);

            viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
            viewer.UpdateLayout();

            Assert.True(children[^1].MeasureCount > hiddenInitialCount);
        });
    }

    [Fact]
    public void ResponsiveCardPanel_DefersOffscreenMeasurementUntilItApproachesTheViewport()
    {
        RunOnStaThread(() =>
        {
            System.Windows.Controls.ScrollViewer viewer = new()
            {
                Width = 400d,
                Height = 100d,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            };
            System.Windows.Controls.StackPanel content = new();
            content.Children.Add(new System.Windows.Controls.Border { Height = 400d });
            Emerde.Controls.ResponsiveCardPanel panel = new();
            MeasureCountingElement child = new();
            panel.Children.Add(child);
            content.Children.Add(panel);
            viewer.Content = content;

            viewer.Measure(new System.Windows.Size(400d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 400d, 100d));
            viewer.UpdateLayout();
            int hiddenInitialCount = child.MeasureCount;

            Assert.False(Emerde.Controls.ResizeOptimizedStackPanel.IsNearViewport(panel, viewer));

            viewer.Width = 420d;
            viewer.Measure(new System.Windows.Size(420d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 420d, 100d));
            viewer.UpdateLayout();

            Assert.Equal(hiddenInitialCount, child.MeasureCount);

            viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
            viewer.UpdateLayout();

            Assert.True(child.MeasureCount > hiddenInitialCount);
        });
    }

    [Fact]
    public void ResizeOptimizedStackPanel_RemeasuresInvalidOffscreenContentImmediately()
    {
        RunOnStaThread(() =>
        {
            System.Windows.Controls.ScrollViewer viewer = new()
            {
                Width = 400d,
                Height = 100d,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            };
            Emerde.Controls.ResizeOptimizedStackPanel panel = new();
            MeasureCountingElement[] children = Enumerable.Range(0, 5)
                .Select(_ => new MeasureCountingElement())
                .ToArray();
            foreach (MeasureCountingElement child in children)
            {
                panel.Children.Add(child);
            }
            viewer.Content = panel;

            viewer.Measure(new System.Windows.Size(400d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 400d, 100d));
            viewer.UpdateLayout();
            viewer.Width = 420d;
            viewer.Measure(new System.Windows.Size(420d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 420d, 100d));
            viewer.UpdateLayout();
            int deferredMeasureCount = children[^1].MeasureCount;

            children[^1].SetDesiredHeight(180d);
            panel.InvalidateMeasure();
            viewer.Measure(new System.Windows.Size(420d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 420d, 100d));
            viewer.UpdateLayout();

            Assert.True(children[^1].MeasureCount > deferredMeasureCount);
            Assert.Equal(580d, panel.DesiredSize.Height);
        });
    }

    [Fact]
    public void ResponsiveCardPanel_RemeasuresInvalidOffscreenContentImmediately()
    {
        RunOnStaThread(() =>
        {
            System.Windows.Controls.ScrollViewer viewer = new()
            {
                Width = 400d,
                Height = 100d,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            };
            System.Windows.Controls.StackPanel content = new();
            content.Children.Add(new System.Windows.Controls.Border { Height = 400d });
            Emerde.Controls.ResponsiveCardPanel panel = new();
            MeasureCountingElement child = new();
            panel.Children.Add(child);
            content.Children.Add(panel);
            viewer.Content = content;

            viewer.Measure(new System.Windows.Size(400d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 400d, 100d));
            viewer.UpdateLayout();
            viewer.Width = 420d;
            viewer.Measure(new System.Windows.Size(420d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 420d, 100d));
            viewer.UpdateLayout();
            int deferredMeasureCount = child.MeasureCount;

            child.SetDesiredHeight(180d);
            panel.InvalidateMeasure();
            viewer.Measure(new System.Windows.Size(420d, 100d));
            viewer.Arrange(new System.Windows.Rect(0d, 0d, 420d, 100d));
            viewer.UpdateLayout();

            Assert.True(child.MeasureCount > deferredMeasureCount);
            Assert.Equal(180d, panel.DesiredSize.Height);
        });
    }

    [Fact]
    public void UiXPreviewSmallCard_UsesCompactContentMetricsWithoutChangingOtherCardSizes()
    {
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("bool isCompactPreviewCard = visualScale <= RoomCardSmallSizeScale", code, StringComparison.Ordinal);
        Assert.True(MainWindow.ShouldUseCompactUiXMetrics(true, false, 0.72d));
        Assert.False(MainWindow.ShouldUseCompactUiXMetrics(true, false, 0.73d));
        Assert.True(MainWindow.ShouldUseCompactUiXMetrics(false, true, 0.90d));
        Assert.False(MainWindow.ShouldUseCompactUiXMetrics(false, false, 0.50d));
        Assert.Contains("SetRoomCardMetric(UiXRoomCardAvatarSizeProperty, useCompactUiXMetrics", code, StringComparison.Ordinal);
        Assert.Contains("? 28d", code, StringComparison.Ordinal);
        Assert.Contains("SetRoomCardMetric(UiXRoomCardNameFontSizeProperty, useCompactUiXMetrics", code, StringComparison.Ordinal);
        Assert.Contains("? 11d", code, StringComparison.Ordinal);
        Assert.Contains("? new Thickness(4, 3, 4, 0)", code, StringComparison.Ordinal);
        Assert.Contains(": new Thickness(6, 8, 6, 0)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXPreview_RightEdgeAlignsWithThePageHeader()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement preview = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "HomePreviewPanel");
        XElement style = preview.Elements().Single(element => element.Name.LocalName == "LivePreviewPanel.Style").Elements().Single();

        Assert.Null(preview.Attribute("Margin"));
        Assert.Contains(style.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Margin"
            && (string?)element.Attribute("Value") == "8,0,0,0");
        Assert.Contains(style.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Margin"
            && (string?)element.Attribute("Value") == "8,0,10,0");
    }

    [Fact]
    public void MainWindow_ConstrainsItsMinimumUsableSizeAndSupportsCardDoubleClickPreview()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement root = document.Root!;
        XElement itemStyle = document.Descendants()
            .First(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "{x:Type ListBoxItem}"
                && element.Descendants().Any(descendant => (string?)descendant.Attribute("Handler") == "RoomCardMouseDoubleClick"));

        Assert.Equal("640", (string?)root.Attribute("MinWidth"));
        Assert.Equal("427", (string?)root.Attribute("MinHeight"));
        Assert.Contains(itemStyle.Elements(), element => element.Name.LocalName == "EventSetter"
            && (string?)element.Attribute("Event") == "MouseDoubleClick"
            && (string?)element.Attribute("Handler") == "RoomCardMouseDoubleClick");

        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        Assert.Contains("info.MinTrackSize = new NativePoint", code, StringComparison.Ordinal);
        Assert.Contains("MinWidth, MinHeight", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXHomeCard_RemovesTheBottomStreamAndRecordingStatusBand()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement layout = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RoomCardUiXLayout");

        Assert.DoesNotContain(layout.Descendants(), element => (string?)element.Attribute("Text") is "{Binding StreamStatusText}" or "{Binding RecordStatusText}");
        Assert.DoesNotContain(layout.Descendants(), element => (string?)element.Attribute("ToolTip") is "{Binding StreamStatusText}" or "{Binding RecordStatusText}");
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

    [Theory]
    [InlineData(187d, 1, 175d)]
    [InlineData(220d, 1, 199d)]
    [InlineData(350d, 2, 163d)]
    [InlineData(728d, 4, 170d)]
    public void BoundedCardLayout_FillsSlotsWithoutBreakingTheElasticRange(
        double availableWidth,
        int expectedColumns,
        double expectedCardWidth)
    {
        (int columns, double slotWidth, double cardWidth) = MainWindow.CalculateBoundedCardLayout(
            availableWidth,
            0,
            175d,
            1d,
            12d);

        Assert.Equal(expectedColumns, columns);
        Assert.Equal(expectedCardWidth, cardWidth, 6);
        Assert.Equal(availableWidth, columns * slotWidth, 1);
        Assert.InRange(cardWidth, 150.5d, 199.5d);
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

    private sealed class MeasureCountingElement : System.Windows.FrameworkElement
    {
        public int MeasureCount { get; private set; }

        public double DesiredHeight { get; private set; } = 100d;

        public void SetDesiredHeight(double height)
        {
            DesiredHeight = height;
            InvalidateMeasure();
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        {
            MeasureCount++;
            return new System.Windows.Size(Math.Min(100d, availableSize.Width), DesiredHeight);
        }
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
