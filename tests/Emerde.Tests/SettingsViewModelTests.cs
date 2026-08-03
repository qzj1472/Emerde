using Emerde.ViewModels;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void SessionLogRetentionInput_UsesStandardSettingsWidth()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XElement input = document.Descendants()
            .Single(element => element.Name.LocalName == "CompactNumberBox"
                && ((string?)element.Attribute("Value"))?.Contains("SessionLogRetentionDays", StringComparison.Ordinal) == true);

        Assert.Equal("112", (string?)input.Attribute("Width"));
    }

    [Fact]
    public void RecordFormatOptions_UseFormatSpecificVisibility()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XElement optimizeAudio = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsOptimizeAudio", StringComparison.Ordinal) == true);
        XElement removeSource = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsRemoveTs", StringComparison.Ordinal) == true);

        Assert.Contains("IsMp4RecordFormat", (string?)optimizeAudio.Attribute("Visibility"));
        Assert.Contains("IsTranscodedRecordFormat", (string?)removeSource.Attribute("Visibility"));
    }

    [Fact]
    public void LocalRecordFormatOptions_UseFormatSpecificVisibility()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml"));
        XElement optimizeAudio = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsOptimizeAudio", StringComparison.Ordinal) == true);
        XElement removeSource = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsRemoveTs", StringComparison.Ordinal) == true);

        Assert.Contains("IsMp4RecordFormat", (string?)optimizeAudio.Attribute("Visibility"));
        Assert.Contains("IsTranscodedRecordFormat", (string?)removeSource.Attribute("Visibility"));
    }

    [Fact]
    public void LocalSettings_InsetsScrollingContentAndScrollbar()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml"));
        XElement scrollViewer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "LocalSettingsScrollViewer");

        Assert.Equal("0,0,8,0", (string?)scrollViewer.Attribute("Margin"));
        Assert.Equal("14,12,6,12", (string?)scrollViewer.Attribute("Padding"));
    }

    [Fact]
    public void EmbeddedLocalSettings_UsesDeeperScrollFades()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement topFade = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "TopScrollFade");
        XElement bottomFade = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "BottomScrollFade");

        Assert.Contains(topFade.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Height"
            && (string?)element.Attribute("Value") == "32");
        Assert.Contains(bottomFade.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Height"
            && (string?)element.Attribute("Value") == "36");
        Assert.All(topFade.Descendants().Where(element => element.Name.LocalName == "MultiDataTrigger")
            .Concat(bottomFade.Descendants().Where(element => element.Name.LocalName == "MultiDataTrigger")), trigger =>
            Assert.Equal(2, trigger.Descendants().Count(element => element.Name.LocalName == "Condition")));
    }

    [Fact]
    public void LocalSettings_UsesACompactGlobalOptionsCard()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml"));
        XElement card = document.Descendants()
            .Single(element => element.Name.LocalName == "Card"
                && ((string?)element.Attribute("Visibility"))?.Contains("ShowSettingsHeader", StringComparison.Ordinal) == true);

        Assert.Equal("64", (string?)card.Attribute("MinHeight"));
        Assert.Equal("14,10,14,8", (string?)card.Attribute("Margin"));
        Assert.Equal("14,8", (string?)card.Attribute("Padding"));
    }

    [Fact]
    public void LocalSegmentInputs_AreCollapsedWhenSegmentationIsDisabled()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml"));
        XElement numberBox = document.Descendants()
            .Single(element => element.Name.LocalName == "CompactNumberBox"
                && ((string?)element.Attribute("Value"))?.Contains("SegmentTimeValue", StringComparison.Ordinal) == true);
        XElement inputGroup = numberBox.Ancestors()
            .First(element => element.Name.LocalName == "StackPanel"
                && element.Attribute("Visibility") != null);

        Assert.Contains("IsToSegment", (string?)inputGroup.Attribute("Visibility"));
    }

    [Theory]
    [InlineData("LocalSettingsContentDialog.xaml")]
    [InlineData("SettingsWindow.xaml")]
    public void CustomScheduleDays_UseSevenEqualColumns(string fileName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));
        XElement uniformGrid = document.Descendants()
            .Single(element => element.Name.LocalName == "UniformGrid"
                && element.Elements().Count(child => child.Name.LocalName == "ToggleButton") == 7);

        Assert.Equal("7", (string?)uniformGrid.Attribute("Columns"));
        Assert.All(uniformGrid.Elements(), button => Assert.Equal("Stretch", (string?)button.Attribute("HorizontalAlignment")));
    }

    [Theory]
    [InlineData("127.0.0.1:7890", "http://127.0.0.1:7890/")]
    [InlineData("localhost:8080", "http://localhost:8080/")]
    [InlineData("proxy.example.com:3128", "http://proxy.example.com:3128/")]
    [InlineData("http://localhost:65535", "http://localhost:65535/")]
    [InlineData("[::1]:7890", "http://[::1]:7890/")]
    public void TryCreateProxyUri_AcceptsHostAndPort(string value, string expected)
    {
        bool result = SettingsViewModel.TryCreateProxyUri(value, out Uri? proxyUri, out string errorKey);

        Assert.True(result);
        Assert.Equal(expected, proxyUri?.AbsoluteUri);
        Assert.Equal(string.Empty, errorKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:notaport")]
    [InlineData("http://localhost")]
    public void TryCreateProxyUri_RejectsInvalidEndpoint(string value)
    {
        bool result = SettingsViewModel.TryCreateProxyUri(value, out Uri? proxyUri, out string errorKey);

        Assert.False(result);
        Assert.Null(proxyUri);
        Assert.False(string.IsNullOrWhiteSpace(errorKey));
    }

    [Theory]
    [InlineData("TS/FLV -> MP4", 0, true)]
    [InlineData("TS/FLV -> MKV", 0, true)]
    [InlineData("TS/FLV", 0, false)]
    [InlineData("TS/FLV -> MP4", 2, false)]
    [InlineData("TS/FLV", 1, false)]
    [InlineData("TS/FLV -> MP4", -1, false)]
    [InlineData("TS/FLV -> MKV", 3, false)]
    public void ShouldCancelConversionsOnRecordFormatChange_OnlyCancelsWhenSwitchingToRaw(
        string previousRecordFormat,
        int nextRecordFormatIndex,
        bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.ShouldCancelConversionsOnRecordFormatChange(previousRecordFormat, nextRecordFormatIndex));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void RecordFormatIndex_AcceptsOnlyVisibleOptions(int value, bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.IsRecordFormatIndexValid(value));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
