using Emerde.ViewModels;
using Emerde.Core;
using Emerde.Views;
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
        Assert.Contains("SettingsFocusButtonClick", code, StringComparison.Ordinal);
        Assert.Contains("IsSettingsSectionInSelectedFocus", code, StringComparison.Ordinal);
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "RadioButton"
            && (string?)element.Attribute("Content") == "{I18N UiXWorkspaceAll}");
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
        Assert.Same(optimizeAudio.Parent, removeSource.Parent);
        Assert.Equal("http://schemas.microsoft.com/winfx/2006/xaml/presentation", optimizeAudio.Parent?.Name.NamespaceName);
        Assert.Null(optimizeAudio.Parent?.Attribute("Spacing"));
        Assert.Equal("0,0,0,12", (string?)optimizeAudio.Attribute("Margin"));
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
    public void LocalSettings_UiXUsesAnIndependentFocusedWorkspaceWithoutChangingLegacyControls()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml"));
        XDocument workspace = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml.cs"));

        Assert.Contains(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "LocalSettingsItemsPanel");
        Assert.Contains(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "RoomActionsCard");
        Assert.Contains(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "SaveCard");
        Assert.DoesNotContain(workspace.Descendants(), element => element.Name.LocalName == "RadioButton"
            && (string?)element.Attribute("Content") == "{I18N UiXWorkspaceOverview}");
        Assert.Contains(workspace.Descendants(), element => element.Name.LocalName == "RadioButton"
            && (string?)element.Attribute("Content") == "{I18N UiXWorkspaceOutput}");
        Assert.Contains(workspace.Descendants(), element => element.Name.LocalName == "Grid"
            && ((string?)element.Attribute("Visibility"))?.Contains("CustomWorkspaceVisibility", StringComparison.Ordinal) == true);
        Assert.Contains(workspace.Descendants(), element => element.Name.LocalName == "Button"
            && ((string?)element.Attribute("Command"))?.Contains("DeleteLastSaveFileNameTokenCommand", StringComparison.Ordinal) == true);
        Assert.Contains(workspace.Descendants(), element => element.Name.LocalName == "Button"
            && ((string?)element.Attribute("Command"))?.Contains("ClearSaveFileNameCustomRuleCommand", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(workspace.Descendants(), element => element.Name.LocalName == "CardExpander");
        Assert.DoesNotContain("ApplyUiXLayout()", source, StringComparison.Ordinal);
        Assert.Contains("bool initializeView = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"RecordFormatCard\" IsExpanded=\"True\"", document.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SaveCard\" IsExpanded=\"True\"", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UiXFocusedWorkspaces_UseContentHeightAlignedControlsAndCleanFocusChrome()
    {
        XDocument workspace = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));
        XDocument settings = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement workspaceSurface = workspace.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "WorkspaceSurface");
        XElement notificationStyle = workspace.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(xaml + "Key") == "WorkspaceNotificationButtonStyle");
        XElement stageStyle = workspace.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(xaml + "Key") == "WorkspaceStageButtonStyle");
        XElement settingsFocusStyle = settings.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(xaml + "Key") == "SettingsFocusButtonStyle");

        Assert.Null(workspaceSurface.Attribute("MaxHeight"));
        AssertStyleSetter(notificationStyle, "Width", "34");
        AssertStyleSetter(notificationStyle, "Height", "34");
        AssertStyleSetter(notificationStyle, "FocusVisualStyle", "{x:Null}");
        AssertStyleSetter(stageStyle, "FocusVisualStyle", "{x:Null}");
        AssertStyleSetter(settingsFocusStyle, "FocusVisualStyle", "{x:Null}");
        Assert.True(workspace.Descendants().Count(element => element.Name.LocalName == "ColumnDefinition"
            && (string?)element.Attribute("Width") == "220") >= 7);
    }

    [Fact]
    public void SettingsUiX_CustomScheduleUsesCompactInputsWithoutChangingLegacyWidth()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml.cs"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] names =
        [
            "RoutineScheduleStartHourInput",
            "RoutineScheduleStartMinuteInput",
            "RoutineScheduleEndHourInput",
            "RoutineScheduleEndMinuteInput",
        ];

        foreach (string name in names)
        {
            XElement input = document.Descendants()
                .Single(element => (string?)element.Attribute(xaml + "Name") == name);
            Assert.Equal("112", (string?)input.Attribute("Width"));
        }

        Assert.Contains("if (ViewModel.IsUiXEnabled && ShouldUseSettingsUiXTwoColumns())", code, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(Math.Floor(scheduleContentWidth / 4d), 64d, 112d)", code, StringComparison.Ordinal);
        Assert.Contains("ApplySettingsUiXScheduleWidths();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsUiX_UsesOneBorderPaletteForEveryInputType()
    {
        XDocument settings = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XDocument resources = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement uiXPanel = settings.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "SettingsUiXPanel");
        XElement panelResources = uiXPanel.Elements()
            .Single(element => element.Name.LocalName == "Grid.Resources");

        Assert.Contains(panelResources.Elements(), element => element.Name.LocalName == "StaticResource"
            && (string?)element.Attribute(xaml + "Key") == "EmerdeTextInputBorderBrush"
            && (string?)element.Attribute("ResourceKey") == "UiXStrongStrokeBrush");
        Assert.Contains(panelResources.Elements(), element => element.Name.LocalName == "StaticResource"
            && (string?)element.Attribute(xaml + "Key") == "EmerdeTextInputFocusedBorderBrush"
            && (string?)element.Attribute("ResourceKey") == "UiXSelectionStrokeBrush");

        string[] inputTypes =
        [
            "{x:Type TextBox}",
            "{x:Type ui:TextBox}",
            "{x:Type ui:PasswordBox}",
            "{x:Type ui:NumberBox}",
        ];
        foreach (string inputType in inputTypes)
        {
            XElement style = resources.Root!.Elements()
                .Single(element => element.Name.LocalName == "Style"
                    && element.Attribute(xaml + "Key") is null
                    && (string?)element.Attribute("TargetType") == inputType);
            Assert.Contains(style.Elements(), element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "BorderBrush"
                && (string?)element.Attribute("Value") == "{DynamicResource EmerdeTextInputBorderBrush}");
            Assert.Contains(style.Descendants(), element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "BorderBrush"
                && (string?)element.Attribute("Value") == "{DynamicResource EmerdeTextInputFocusedBorderBrush}");
        }

        XElement comboStyle = resources.Root!.Elements()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute(xaml + "Key") is null
                && (string?)element.Attribute("TargetType") == "{x:Type ComboBox}");
        Assert.Contains(comboStyle.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource ControlStrokeColorDefaultBrush}");
        Assert.Contains(comboStyle.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource EmerdeTextInputFocusedBorderBrush}");

        XElement uiXComboStyle = panelResources.Elements()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "{x:Type ComboBox}");
        Assert.Equal("{StaticResource {x:Type ComboBox}}", (string?)uiXComboStyle.Attribute("BasedOn"));
        Assert.Contains(uiXComboStyle.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource EmerdeTextInputBorderBrush}");

        XElement compactStyle = resources.Root!.Elements()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "{x:Type controls:CompactNumberBox}");
        Assert.Contains(compactStyle.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource ControlStrokeColorDefaultBrush}");
        XElement compactBorder = compactStyle.Descendants()
            .Single(element => element.Name.LocalName == "Border"
                && (string?)element.Attribute(xaml + "Name") == "RootBorder");
        Assert.Equal("{TemplateBinding BorderBrush}", (string?)compactBorder.Attribute("BorderBrush"));
        Assert.Contains(compactStyle.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "RootBorder"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource EmerdeTextInputFocusedBorderBrush}");

        XElement uiXCompactStyle = panelResources.Elements()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "{x:Type controls:CompactNumberBox}");
        Assert.Contains(uiXCompactStyle.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource EmerdeTextInputBorderBrush}");
    }

    [Fact]
    public void UiXSettings_UsesCompleteFocusNavigationAndResponsiveOutputRows()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml.cs"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        string[] expectedFocusValues = ["0", "1", "2", "3", "4", "5", "6"];
        string[] actualFocusValues = document.Descendants()
            .Where(element => element.Name.LocalName == "RadioButton"
                && (string?)element.Attribute("GroupName") == "SettingsFocus")
            .Select(element => (string?)element.Attribute("CommandParameter"))
            .OfType<string>()
            .ToArray();
        Assert.Equal(expectedFocusValues, actualFocusValues);
        Assert.Contains("selectedSettingsFocus = Math.Clamp(focus, 0, 6)", code, StringComparison.Ordinal);
        Assert.Contains("GetSettingsUiXGroupIndex(section) is 1 or 5", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(section, CookieSettingsExpander)", code, StringComparison.Ordinal);

        XElement saveMetadataLayout = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "SaveMetadataLayout");
        Assert.Contains(saveMetadataLayout.Descendants(), element => (string?)element.Attribute(xaml + "Name") == "SavePathLevelSelector");
        Assert.Equal("48", saveMetadataLayout.Elements()
            .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements()
            .ElementAt(1)
            .Attribute("Width")?.Value);
        XElement retentionPanel = saveMetadataLayout.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "DataRetentionPanel");
        XElement retentionControls = retentionPanel.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "DataRetentionControls");
        Assert.Equal(["CompactNumberBox", "ComboBox", "ToggleSwitch"], retentionControls.Elements().Select(element => element.Name.LocalName));
        Assert.Contains("bool keepOnOneRow = availableWidth >= 712d", code, StringComparison.Ordinal);
        Assert.Contains("SavePathLevelSelector.Width = ShouldUseSettingsUiXTwoColumns() ? 148d : 168d", code, StringComparison.Ordinal);
        Assert.Contains("DataRetentionControls.Children.Add(DataRetentionSwitch)", code, StringComparison.Ordinal);
        Assert.Contains("DataRetentionControls.Children.Add(DataRetentionValueInput)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXSettings_HidesPreviewAndUserAgentWhileKeepingLegacyUserAgentAndNetworkCookie()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml.cs"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement userAgentExpander = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "UserAgentExpander");
        Assert.Contains(userAgentExpander.Descendants(), element => element.Name.LocalName == "TextBox"
            && ((string?)element.Attribute("Text"))?.Contains("UserAgent", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute(xaml + "Name") == "UserAgentHeaderEditor");
        Assert.Contains("&& !ReferenceEquals(section, UserAgentExpander)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySettingsUiXUserAgentLayout", code, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(section, PreviewSettingsCard)", code, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(section, UserAgentExpander)", code, StringComparison.Ordinal);
        Assert.Contains("&& !ReferenceEquals(section, CookieSettingsExpander)", code, StringComparison.Ordinal);
        Assert.Contains("6 => GetSettingsUiXGroupIndex(section) == 7", code, StringComparison.Ordinal);
        Assert.Contains("SaveMetadataLayout.SizeChanged += SaveMetadataLayoutSizeChanged", code, StringComparison.Ordinal);
        Assert.Contains("ApplySettingsUiXSaveMetadataLayout()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXAddWorkspace_AlignsTrailingControlsAndCentersTheAddressClearButton()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml.cs"));

        XElement customMode = document.Descendants()
            .Single(element => element.Name.LocalName == "RadioButton"
                && (string?)element.Attribute("Content") == "{I18N Custom}");
        Assert.Equal("0", (string?)customMode.Attribute("Margin"));

        XElement skipValidation = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && (string?)element.Attribute("Content") == "{I18N SkipValidation}");
        Assert.Equal("0,8,0,0", (string?)skipValidation.Parent?.Attribute("Margin"));
        Assert.Equal("0", (string?)skipValidation.Attribute("MinWidth"));
        Assert.Equal("Right", (string?)skipValidation.Attribute("HorizontalAlignment"));
        Assert.Contains("AlignAddressClearButton()", code, StringComparison.Ordinal);
        Assert.Contains("FindVisualChildren<WpfButton>(RoomUrlTextBox)", code, StringComparison.Ordinal);
        Assert.Contains("button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center", code, StringComparison.Ordinal);
        Assert.Contains("button.VerticalContentAlignment = System.Windows.VerticalAlignment.Center", code, StringComparison.Ordinal);
        Assert.Contains("button.VerticalAlignment = System.Windows.VerticalAlignment.Center", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXRoomWorkspace_UsesVerticalScheduleAndVisibleInheritedOutputDefaults()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));
        string editorCode = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml.cs"));

        XElement customSchedule = document.Descendants()
            .Single(element => element.Name.LocalName == "Border"
                && ((string?)element.Attribute("Visibility"))?.Contains("IsRoutineScheduleCustom", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(customSchedule.Descendants(), element => element.Name.LocalName == "DockPanel");
        Assert.Equal(2, customSchedule.Descendants().Count(element => element.Name.LocalName == "DatePicker"));
        Assert.Contains(customSchedule.Descendants(), element => element.Name.LocalName == "UniformGrid"
            && (string?)element.Attribute("Columns") == "7");
        Assert.Contains(customSchedule.Descendants(), element => element.Name.LocalName == "ToggleSwitch"
            && ((string?)element.Attribute("IsChecked"))?.Contains("RoutineScheduleUseDays", StringComparison.Ordinal) == true);
        Assert.Contains(customSchedule.Descendants(), element => element.Name.LocalName == "ToggleSwitch"
            && ((string?)element.Attribute("IsChecked"))?.Contains("RoutineScheduleUseTimeRange", StringComparison.Ordinal) == true);

        XElement saveFolder = document.Descendants()
            .Single(element => element.Name.LocalName == "TextBox"
                && ((string?)element.Attribute("Text"))?.Contains("Editor.SaveFolder", StringComparison.Ordinal) == true);
        Assert.Equal("EmerdeDownloads", (string?)saveFolder.Attribute("PlaceholderText"));
        Assert.Contains("RoutineScheduleStartDate = settings.RoutineScheduleStartDate", editorCode, StringComparison.Ordinal);
        Assert.Contains("RoutineScheduleUseDays = settings.RoutineScheduleUseDays", editorCode, StringComparison.Ordinal);
        Assert.Contains("RoutineScheduleUseTimeRange = settings.RoutineScheduleUseTimeRange", editorCode, StringComparison.Ordinal);
        Assert.Contains("SaveFileNameCustomRule = settings.SaveFileNameCustomRule", editorCode, StringComparison.Ordinal);
        Assert.DoesNotContain("? string.Empty\n            : settings.SaveFileNameCustomRule", editorCode, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXAddWorkspace_IdentifiesAddressAutomaticallyWithoutAnIdentifyButton()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml.cs"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement addressInput = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "RoomUrlTextBox");
        Assert.DoesNotContain(addressInput.Parent!.Elements(), element => element.Name.LocalName == "Button");
        Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute("Click") == "ResolveRoomClick");
        Assert.Contains("TimeSpan.FromMilliseconds(180)", code, StringComparison.Ordinal);
        Assert.Contains("QueueAutomaticResolution()", code, StringComparison.Ordinal);
        Assert.Contains("await ResolveRoomAsync(false)", code, StringComparison.Ordinal);
        Assert.Contains("resolutionCancellation?.Cancel()", code, StringComparison.Ordinal);
        Assert.Contains("if (showErrorToast)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXRoomWorkspaces_KeepAddAndEditModesIndependent()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml.cs"));

        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "RadioButton"
            && (string?)element.Attribute("GroupName") is "WorkspaceMode" or "WorkspaceStage");
        Assert.Contains("IsFollowGlobalSettings = true", source, StringComparison.Ordinal);
        Assert.Contains("CreateForEdit(RoomStatusReactive room) => new(room, false)", source, StringComparison.Ordinal);
        Assert.Contains("Editor = new LocalSettingsContentDialog(room", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static bool isCustomMode", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiXSurfaces_ClearWindowFocusWithoutChangingLegacyFocusPolicy()
    {
        string workspaceCode = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml.cs"));
        string mainCode = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("window.Deactivated += OwnerDeactivated", workspaceCode, StringComparison.Ordinal);
        Assert.Contains("Keyboard.ClearFocus()", workspaceCode, StringComparison.Ordinal);
        Assert.Contains("FocusManager.SetFocusedElement(this, null)", workspaceCode, StringComparison.Ordinal);
        Assert.Contains("FocusManager.SetFocusedElement(UiXWorkspaceOverlay, null)", mainCode, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalSettings_FollowGlobalUsesCurrentGlobalValuesAndCustomStartsFromTheirSnapshot()
    {
        bool oldMonitor = Configurations.IsToMonitor.Get();
        bool oldRecord = Configurations.IsToRecord.Get();
        string oldQuality = Configurations.PreferredStreamQuality.Get();
        string oldFormat = Configurations.RecordFormat.Get();
        try
        {
            Configurations.IsToMonitor.Set(false);
            Configurations.IsToRecord.Set(true);
            Configurations.PreferredStreamQuality.Set(StreamQualityCatalog.High);
            Configurations.RecordFormat.Set("TS/FLV -> MKV");
            RunOnStaThread(() =>
            {
                LocalSettingsContentDialog editor = new(new RoomStatusReactive
                {
                    RoomUrl = "https://example.test/follow-global",
                    IsFollowGlobalSettings = true,
                    IsToMonitor = true,
                    IsToRecord = false,
                }, false, false, true, false);

                Assert.False(editor.IsToMonitor);
                Assert.True(editor.IsToRecord);
                Assert.Equal(StreamQualityCatalog.High, editor.PreferredQuality);
                Assert.Equal(2, editor.RecordFormatIndex);

                Configurations.IsToMonitor.Set(true);
                Configurations.IsToRecord.Set(false);
                Configurations.PreferredStreamQuality.Set(StreamQualityCatalog.Original);
                Configurations.RecordFormat.Set("TS/FLV -> MP4");
                editor.IsFollowGlobalSettings = false;

                Assert.True(editor.IsToMonitor);
                Assert.False(editor.IsToRecord);
                Assert.Equal(StreamQualityCatalog.Original, editor.PreferredQuality);
                Assert.Equal(1, editor.RecordFormatIndex);
            });
        }
        finally
        {
            Configurations.IsToMonitor.Set(oldMonitor);
            Configurations.IsToRecord.Set(oldRecord);
            Configurations.PreferredStreamQuality.Set(oldQuality);
            Configurations.RecordFormat.Set(oldFormat);
        }
    }

    [Fact]
    public void LocalSettingsEditors_DoNotShareAddAndExistingRoomModes()
    {
        RunOnStaThread(() =>
        {
            RoomStatusReactive addRoom = new() { IsFollowGlobalSettings = true };
            LocalSettingsContentDialog addEditor = new(addRoom, false, false, true, false);
            addEditor.IsFollowGlobalSettings = false;

            RoomStatusReactive followingRoom = new() { IsFollowGlobalSettings = true };
            LocalSettingsContentDialog followingEditor = new(followingRoom, false, false, false, false);
            RoomStatusReactive customRoom = new() { IsFollowGlobalSettings = false };
            LocalSettingsContentDialog customEditor = new(customRoom, false, false, false, false);

            Assert.False(addEditor.IsFollowGlobalSettings);
            Assert.True(addRoom.IsFollowGlobalSettings);
            Assert.True(followingEditor.IsFollowGlobalSettings);
            Assert.False(customEditor.IsFollowGlobalSettings);
        });
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
    [InlineData("TS/FLV", 1, true)]
    [InlineData("TS/FLV", 2, true)]
    [InlineData("TS/FLV -> MP4", 0, true)]
    [InlineData("TS/FLV -> MP4", 1, false)]
    [InlineData("TS/FLV", -1, false)]
    public void ShouldApplyPendingRecordingFormatChange_HandlesBothDirections(
        string previousRecordFormat,
        int nextRecordFormatIndex,
        bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.ShouldApplyPendingRecordingFormatChange(previousRecordFormat, nextRecordFormatIndex));
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

    private static void AssertStyleSetter(XElement style, string property, string value)
    {
        Assert.Contains(style.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == property
            && (string?)element.Attribute("Value") == value);
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
