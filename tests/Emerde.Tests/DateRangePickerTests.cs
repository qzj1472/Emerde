using System.Xml.Linq;
using Emerde.Controls;

namespace Emerde.Tests;

public class DateRangePickerTests
{
    [Fact]
    public void SelectPoint_OrdersPointsRegardlessOfClickOrder()
    {
        DateRangeSelectionResult first = DateRangePicker.SelectPoint(null, false, new DateTime(2026, 8, 20));
        DateRangeSelectionResult second = DateRangePicker.SelectPoint(
            first.AnchorDate,
            first.AwaitingSecondPoint,
            new DateTime(2026, 8, 10));

        Assert.Equal(new DateTime(2026, 8, 10), second.StartDate);
        Assert.Equal(new DateTime(2026, 8, 20), second.EndDate);
        Assert.False(second.AwaitingSecondPoint);
        Assert.True(second.IsComplete);
    }

    [Fact]
    public void SelectPoint_ClickingThePendingPointAgainClearsTheRange()
    {
        DateRangeSelectionResult first = DateRangePicker.SelectPoint(null, false, new DateTime(2026, 8, 12));
        DateRangeSelectionResult second = DateRangePicker.SelectPoint(
            first.AnchorDate,
            first.AwaitingSecondPoint,
            new DateTime(2026, 8, 12));

        Assert.Null(second.StartDate);
        Assert.Null(second.EndDate);
        Assert.Null(second.AnchorDate);
        Assert.False(second.AwaitingSecondPoint);
        Assert.False(second.IsComplete);
    }

    [Fact]
    public void DateRangePicker_UsesOneMonthAndKeepsEndpointAndMiddleStyles()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Controls", "DateRangePicker.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Controls", "DateRangePicker.xaml.cs"));

        XElement dayStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "UiXDateRangeDayButtonStyle");
        string style = dayStyle.ToString();
        Assert.Contains("Value=\"Start\"", style, StringComparison.Ordinal);
        Assert.Contains("Value=\"Middle\"", style, StringComparison.Ordinal);
        Assert.Contains("Value=\"End\"", style, StringComparison.Ordinal);
        Assert.Contains("SystemAccentColorPrimaryBrush", style, StringComparison.Ordinal);
        Assert.Contains("UiXSelectionFillBrush", style, StringComparison.Ordinal);
        Assert.Contains(dayStyle.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "FocusVisualStyle"
            && (string?)element.Attribute("Value") == "{x:Null}");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "Calendar");
        Assert.Single(document.Descendants(), element => ((string?)element.Attribute("ItemsSource"))?.Contains("MonthDays", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("RightMonthDays", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (result.IsComplete)", code, StringComparison.Ordinal);
        Assert.Contains("UseUiXVisualsProperty", code, StringComparison.Ordinal);
        Assert.Contains("SolidBackgroundFillColorBaseBrush", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("UiXDialogElevatedBrush", document.ToString(), StringComparison.Ordinal);
        XElement rangeToggle = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "RangeToggle");
        Assert.Equal("224", (string?)rangeToggle.Attribute("MinWidth"));
        Assert.Equal("Stretch", (string?)rangeToggle.Attribute("HorizontalAlignment"));
        Assert.Contains("EmerdeExtensionInputBorderBrush", rangeToggle.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SettingsWindow.xaml")]
    [InlineData("LocalSettingsContentDialog.xaml")]
    [InlineData("UiXRoomWorkspace.xaml")]
    public void ScheduleSurfaces_UseOneRangePickerWithoutDateOrRestrictionToggles(string fileName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));

        XElement picker = Assert.Single(document.Descendants(), element => element.Name.LocalName == "DateRangePicker");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "DatePicker");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ToggleSwitch"
            && ((((string?)element.Attribute("IsChecked"))?.Contains("RoutineScheduleUseDays", StringComparison.Ordinal) == true)
                || (((string?)element.Attribute("IsChecked"))?.Contains("RoutineScheduleUseTimeRange", StringComparison.Ordinal) == true)));
        Assert.Contains("RoutineScheduleStartDate", (string?)picker.Attribute("StartDate") ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("RoutineScheduleEndDate", (string?)picker.Attribute("EndDate") ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("224", (string?)picker.Attribute("Width"));
        Assert.Equal("Left", (string?)picker.Attribute("HorizontalAlignment"));
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
