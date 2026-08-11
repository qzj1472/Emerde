using Emerde.ViewModels;
using Emerde.Core;
using System.Globalization;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void RefreshLocalizedOptions_RebuildsCachedPlatformLists()
    {
        CultureInfo previousCulture = Locale.Culture;
        try
        {
            Locale.Culture = CultureInfo.GetCultureInfo("zh-Hans");
            SettingsViewModel viewModel = new();
            IReadOnlyList<PlatformCookieItem> chineseItems = viewModel.DomesticCookiePlatforms;
            IReadOnlyList<StreamQualityOption> chineseQualityOptions = viewModel.StreamQualityOptions;

            Locale.Culture = CultureInfo.GetCultureInfo("en");
            viewModel.RefreshLocalizedOptions();
            IReadOnlyList<PlatformCookieItem> englishItems = viewModel.DomesticCookiePlatforms;
            IReadOnlyList<StreamQualityOption> englishQualityOptions = viewModel.StreamQualityOptions;

            Assert.NotSame(chineseItems, englishItems);
            Assert.Equal("Douyin", englishItems.Single(item => item.PlatformName == "Douyin").DisplayName);
            Assert.Equal("高清", chineseQualityOptions.Single(item => item.Value == StreamQualityCatalog.High).DisplayName);
            Assert.Equal("High", englishQualityOptions.Single(item => item.Value == StreamQualityCatalog.High).DisplayName);
        }
        finally
        {
            Locale.Culture = previousCulture;
        }
    }

    [Fact]
    public void PageTitle_AlignsWithSettingsCards()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XElement title = document.Descendants()
            .Single(element => element.Name.LocalName == "TextBlock"
                && (string?)element.Attribute("Text") == "{I18N Settings}");

        Assert.Equal("20,10,0,16", (string?)title.Attribute("Margin"));
        Assert.Null(title.Attribute("Height"));
    }

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
    public void SettingsUiXLayout_UsesASeparateTwoColumnPanelAndKeepsCookieLast()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml.cs"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Grid"
            && (string?)element.Attribute(xaml + "Name") == "SettingsUiXPanel");
        XElement leftColumn = document.Descendants().Single(element =>
            element.Name.LocalName == "StackPanel"
            && (string?)element.Attribute(xaml + "Name") == "SettingsUiXLeftColumn");
        XElement rightColumn = document.Descendants().Single(element =>
            element.Name.LocalName == "StackPanel"
            && (string?)element.Attribute(xaml + "Name") == "SettingsUiXRightColumn");
        Assert.Equal(presentation, leftColumn.Name.Namespace);
        Assert.Equal(presentation, rightColumn.Name.Namespace);
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "StackPanel"
            && (string?)element.Attribute(xaml + "Name") == "SettingsUiXBottomPanel");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "CardExpander"
            && (string?)element.Attribute(xaml + "Name") == "CookieSettingsExpander");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "ControlTemplate"
            && (string?)element.Attribute(xaml + "Key") == "SettingsUiXCardExpanderTemplate");
        XElement fixedTemplate = document.Descendants().Single(element =>
            element.Name.LocalName == "ControlTemplate"
            && (string?)element.Attribute(xaml + "Key") == "UiXFixedCardExpanderTemplate");
        Assert.DoesNotContain(fixedTemplate.Descendants(), element => element.Name.LocalName == "Path");
        Assert.DoesNotContain(fixedTemplate.Descendants(), element =>
            element.Name.LocalName == "Trigger"
            && (string?)element.Attribute("Property") == "IsExpanded");
        Assert.Contains("GetOrCreateSettingsUiXGroup", code, StringComparison.Ordinal);
        Assert.Contains("AddSettingsSectionToUiX", code, StringComparison.Ordinal);
        Assert.Contains("SettingsUiXBottomPanel.Children.Add(section)", code, StringComparison.Ordinal);
        Assert.Contains("ClearSettingsUiXLayout", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsUiXCardExpanderTemplate", code, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Path"
            && (string?)element.Attribute(xaml + "Name") == "UiXChevronPath");
        Assert.Contains("isIndentedContent", code, StringComparison.Ordinal);
        Assert.Contains("if (isIndentedContent)", code, StringComparison.Ordinal);
        Assert.Contains("new Thickness(52, margin.Top, margin.Right, margin.Bottom)", code, StringComparison.Ordinal);
        Assert.Contains("if (!isTrailingControl)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("? new Thickness(0, margin.Top, 0, margin.Bottom)", code, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsUiXEnabled && !ReferenceEquals(expander, CookieSettingsExpander)", code, StringComparison.Ordinal);
        Assert.Contains("expander.SetResourceReference(Control.TemplateProperty, \"UiXFixedCardExpanderTemplate\")", code, StringComparison.Ordinal);
        Assert.Contains("SettingsUiXGroupTitleKeys", code, StringComparison.Ordinal);
        Assert.Contains("BindSettingsUiXVisibility", code, StringComparison.Ordinal);
        Assert.Contains("ClearSettingsUiXVisibility", code, StringComparison.Ordinal);
        Assert.Contains("ApplySettingsUiXExpanderContent", code, StringComparison.Ordinal);
        Assert.Contains("RestoreAllSettingsUiXDependentExpanderContents", code, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.StackPanel", code, StringComparison.Ordinal);
        Assert.Contains("UiXSettingsGroupBorderStyle", code, StringComparison.Ordinal);
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "SmoothScrollViewer"
            && (string?)element.Attribute("IsEnableSmoothScrolling") == "False");
        Assert.Contains("ReferenceEquals(section, LanguageSettingsCard)", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(section, SaveSettingsExpander) => 5", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(section, PlatformAccessCard)", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(section, ProxyExpander)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceEquals(section, SaveSettingsExpander)\n                || ReferenceEquals(section, ProxyExpander)", code, StringComparison.Ordinal);
        Assert.Contains("groupIndex is 0 or 1 or 2 or 3", code, StringComparison.Ordinal);
        Assert.DoesNotContain("sectionIndex switch", code, StringComparison.Ordinal);
        Dictionary<string, string> namedSectionTitles = new()
        {
            ["RecordFormatExpander"] = "{I18N RecordFormat}",
            ["SegmentExpander"] = "{I18N Segment}",
            ["SaveSettingsExpander"] = "{I18N Save}",
            ["AutoShutdownExpander"] = "{I18N AutoShutdown}",
            ["ProxyExpander"] = "{I18N UseProxy}",
        };
        foreach ((string sectionName, string title) in namedSectionTitles)
        {
            XElement section = document.Descendants().Single(element =>
                (string?)element.Attribute(xaml + "Name") == sectionName);
            Assert.Contains(section.Descendants(), element =>
                (string?)element.Attribute("Text") == title);
        }
        string[] dependentPanels =
        [
            "LiveNotificationExpander",
            "SegmentExpander",
            "LiveNotificationOptionsPanel",
            "SegmentOptionsPanel",
            "DataRetentionValueInput",
            "DataRetentionUnitSelector",
            "AutoShutdownExpander",
            "AutoShutdownOptionsPanel",
            "ProxyExpander",
            "ProxyOptionsPanel",
        ];
        foreach (string panelName in dependentPanels)
        {
            Assert.Contains(document.Descendants(), element =>
                (string?)element.Attribute(xaml + "Name") == panelName);
        }
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
