using Emerde.Core;
using Emerde.Views;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class WindowSizingTests
{
    [Theory]
    [InlineData(12.49d, 12d)]
    [InlineData(12.5d, 13d)]
    [InlineData(12.51d, 13d)]
    [InlineData(-12.5d, -13d)]
    public void RoundLayoutValue_UsesVisualMidpointRounding(double value, double expected)
    {
        Assert.Equal(expected, WindowSizing.RoundLayoutValue(value));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RoundLayoutValue_PreservesNonFiniteValues(double value)
    {
        Assert.Equal(value, WindowSizing.RoundLayoutValue(value));
    }

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

    [Fact]
    public void ContentDialogs_RemoveTemplateAndControlSizeLimits()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "WindowSizing.cs"));

        Assert.Contains("RemoveContentDialogSizeLimits(dialog)", source, StringComparison.Ordinal);
        Assert.Contains("dialog.MinWidth = 0d", source, StringComparison.Ordinal);
        Assert.Contains("dialog.MinHeight = 0d", source, StringComparison.Ordinal);
        Assert.Contains("dialog.MaxWidth = double.PositiveInfinity", source, StringComparison.Ordinal);
        Assert.Contains("dialog.MaxHeight = double.PositiveInfinity", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[DialogMinWidthResource] = 0d", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[DialogMaxWidthResource] = double.PositiveInfinity", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[DialogMinHeightResource] = 0d", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[DialogMaxHeightResource] = double.PositiveInfinity", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogMarginShortSideRatio", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyContentDialogSizeLimit", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateReleaseNotesDialog_LocksControlAndTemplateWidth()
    {
        string sizingSource = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "WindowSizing.cs"));
        string mainWindowSource = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("ApplyFixedContentDialogWidth(dialog, fixedWidth.Value)", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.Width = width", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.MinWidth = width", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.MaxWidth = width", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[DialogMinWidthResource] = width", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[DialogMaxWidthResource] = width", sizingSource, StringComparison.Ordinal);
        Assert.Contains("ApplyFixedContentDialogHeight(dialog, fixedHeight.Value)", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.Height = height", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.MinHeight = height", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.MaxHeight = height", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[DialogMinHeightResource] = height", sizingSource, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[DialogMaxHeightResource] = height", sizingSource, StringComparison.Ordinal);
        Assert.Contains("WindowSizing.ShowContentDialogAsync(dialog, this, 680d, 760d)", mainWindowSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AddRoomContentDialog.xaml")]
    [InlineData("AutoShutdownContentDialog.xaml")]
    [InlineData("ExitConfirmationContentDialog.xaml")]
    [InlineData("StartupAboutNoticeContentDialog.xaml")]
    [InlineData("UpdateReleaseNotesContentDialog.xaml")]
    public void ContentDialogRootContent_DoesNotDeclareSizeLimits(string fileName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));
        XElement root = document.Root!;
        XElement content = root.Elements().First(element => !element.Name.LocalName.EndsWith("Resources", StringComparison.Ordinal));

        Assert.Null(content.Attribute("MinWidth"));
        Assert.Null(content.Attribute("MinHeight"));
        Assert.Null(content.Attribute("MaxWidth"));
        Assert.Null(content.Attribute("MaxHeight"));
    }

    [Fact]
    public void StartupAboutNoticeDialog_UsesACompactStableWidth()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "StartupAboutNoticeContentDialog.xaml"));

        Assert.Equal("640", (string?)document.Root!.Attribute("Width"));
    }

    [Fact]
    public void UpdateReleaseNotesDialog_UsesStableWidthAndWrappedText()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UpdateReleaseNotesContentDialog.xaml"));
        XElement root = document.Root!;
        XElement content = root.Elements().First(element => !element.Name.LocalName.EndsWith("Resources", StringComparison.Ordinal));

        Assert.Equal("680", (string?)root.Attribute("Width"));
        Assert.Equal("760", (string?)root.Attribute("Height"));
        Assert.Null(content.Attribute("Width"));
        XElement scrollViewer = content.Descendants().Single(element => element.Name.LocalName == "ScrollViewer");
        Assert.Equal("Stretch", (string?)scrollViewer.Attribute("HorizontalContentAlignment"));
        Assert.Equal("Disabled", (string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"));
        Assert.All(
            document.Descendants().Where(element => ((string?)element.Attribute("Style"))?.Contains("UpdateReleaseNotes", StringComparison.Ordinal) == true),
            element => Assert.True(element.Name.LocalName != "TextBlock" || (string?)element.Attribute("TextWrapping") == null));
    }

    [Fact]
    public void UpdateReleaseNotesDialog_ScrollsOnlyTheBody()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UpdateReleaseNotesContentDialog.xaml"));
        XElement rootGrid = document.Root!.Elements().Single(element => element.Name.LocalName == "Grid");
        XElement scrollViewer = rootGrid.Descendants().Single(element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal(3, rootGrid.Descendants().Count(element => element.Name.LocalName == "RowDefinition"));
        Assert.Equal("2", (string?)scrollViewer.Attribute("Grid.Row"));
        Assert.Equal("0", (string?)rootGrid.Elements().First(element => element.Name.LocalName == "Border").Attribute("Grid.Row"));
        Assert.Equal("1", (string?)rootGrid.Elements().First(element => element.Name.LocalName == "StackPanel").Attribute("Grid.Row"));
        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
