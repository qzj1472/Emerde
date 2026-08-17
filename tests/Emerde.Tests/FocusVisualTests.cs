using Emerde.Controls;
using Emerde.Views;
using System.Xml.Linq;
using Emerde.Core;

namespace Emerde.Tests;

public sealed class FocusVisualTests
{
    [Theory]
    [InlineData(false, AppThemeBrushes.DarkThemeTransitionDurationMilliseconds)]
    [InlineData(true, AppThemeBrushes.LightThemeTransitionDurationMilliseconds)]
    public void ThemeTransition_UsesALongerDurationWhenSwitchingToLight(bool isLightTheme, int expectedMilliseconds)
    {
        Assert.Equal(expectedMilliseconds, AppThemeBrushes.GetTransitionDurationMilliseconds(isLightTheme));
        Assert.Equal(
            AppThemeBrushes.DarkThemeTransitionDurationMilliseconds * 5 / 4,
            AppThemeBrushes.LightThemeTransitionDurationMilliseconds);
    }

    [Fact]
    public void SelectedRoomRefresh_DoesNotResetTheWholeCardView()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        AssertMethodDoesNotResetRoomView(
            source,
            "private async Task RefreshSelectedRoomInfoAsync()",
            "private bool TryBeginManualRefresh(out long remainingMilliseconds)");
        AssertMethodDoesNotResetRoomView(
            source,
            "private async Task<bool> RefreshPreviewStreamQualityAsync(",
            "private bool ShouldRefreshPreviewStreamQuality(");
    }

    [Fact]
    public void RoomView_UsesLiveSortingAndFilteringAfterTargetedRefresh()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("LiveSortingProperties.Add(nameof(RoomStatusReactive.NickName))", source);
        Assert.Contains("liveView.IsLiveSorting = true", source);
        Assert.Contains("LiveFilteringProperties.Add(nameof(RoomStatusReactive.PlatformName))", source);
        Assert.Contains("liveView.IsLiveFiltering = true", source);
    }

    private static void AssertMethodDoesNotResetRoomView(string source, string methodSignature, string nextMethodSignature)
    {
        int methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        int methodEnd = source.IndexOf(nextMethodSignature, methodStart, StringComparison.Ordinal);

        Assert.True(methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        Assert.DoesNotContain("RoomStatusesView.Refresh()", method);
    }

    [Fact]
    public void MotionAssist_PreservesReusableAnimationState()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Controls", "MotionAssist.cs"));

        Assert.Contains("GetAnimationBaseValue(UIElement.OpacityProperty)", source);
        Assert.DoesNotContain("element.Opacity = 0d", source);
        Assert.Contains("EntranceOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing }", source);
        Assert.Contains("HasPlayedEntrance", source);
        Assert.Contains("HasObservedEntranceTrigger", source);
        Assert.Contains("EntranceReplayRequested", source);
        Assert.Contains("state.EntranceOperation?.Abort()", source);
        Assert.Contains("ResetEntrance(element, state)", source);
        Assert.Contains("ResetInteractionScale(element)", source);
        Assert.Contains("AnimateInteractionScale(element, GetPressScale(element), PressDurationMilliseconds)", source);
        Assert.Contains("GetIsUiXScope(element)", source);
        Assert.Contains("FrameworkPropertyMetadataOptions.Inherits", source);
        Assert.Contains("element.Unloaded += SpinUnloaded", source);
        Assert.Contains("element.Unloaded += MotionElementUnloaded", source);
        Assert.Contains("EntranceAnimationGeneration", source);
        Assert.Contains("PulseAnimationGeneration", source);
        Assert.Contains("ResetPulse(element)", source);
        Assert.Contains("translationAnimation.Completed", source);
        Assert.Contains("SystemParameters.ClientAreaAnimation", source);
        Assert.DoesNotContain("EntranceScale.BeginAnimation", source);
        Assert.DoesNotContain("PulseScale", source);
        Assert.DoesNotContain("FrameworkElement.WidthProperty", source);
        Assert.DoesNotContain("FrameworkElement.HeightProperty", source);
        Assert.Contains("ReadLocalValue(FrameworkElement.RenderTransformOriginProperty)", source);
    }

    [Theory]
    [InlineData(255, 255, 255, 23, 27, 32)]
    [InlineData(0, 0, 0, 255, 255, 255)]
    [InlineData(0, 120, 212, 255, 255, 255)]
    public void AccentText_UsesTheHigherContrastForeground(
        byte red,
        byte green,
        byte blue,
        byte expectedRed,
        byte expectedGreen,
        byte expectedBlue)
    {
        System.Windows.Media.Color result = AppThemeBrushes.GetTextOnAccentColor(System.Windows.Media.Color.FromRgb(red, green, blue));

        Assert.Equal(System.Windows.Media.Color.FromRgb(expectedRed, expectedGreen, expectedBlue), result);
    }

    [Fact]
    public void MotionAssist_UsesTheUiXMotionTimingRanges()
    {
        Assert.InRange(MotionAssist.PressDurationMilliseconds, 80, 100);
        Assert.InRange(MotionAssist.EntranceDurationMilliseconds, 400, 440);
        Assert.InRange(MotionAssist.ExitDurationMilliseconds, 260, 300);
        Assert.Equal(320, MotionAssist.StateTransitionEnterDurationMilliseconds);
        Assert.Equal(240, MotionAssist.StateTransitionExitDurationMilliseconds);
        Assert.Equal(300, MotionAssist.NavigationIndicatorDurationMilliseconds);
    }

    [Fact]
    public void ContentDialogs_KeepNativePopupExitAnimationAndCoordinateOnlyTheBackdrop()
    {
        string sizingSource = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "WindowSizing.cs"));
        string blurSource = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "DialogBlurScope.cs"));

        Assert.DoesNotContain("FindName(\"LayoutRoot\", sender)", sizingSource);
        Assert.DoesNotContain("MotionAssist.PlayExitAsync", sizingSource);
        Assert.Contains("contentDialog.Closing += ContentDialogClosing", blurSource);
        Assert.Contains("_ = CompleteContentDialogExitAsync(args)", blurSource);
        Assert.DoesNotContain("args.GetDeferral()", blurSource);
    }

    [Fact]
    public void LocalSettings_UsesNativeDialogExitWithoutACompetingDeferral()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml.cs"));

        Assert.DoesNotContain("dialog.Closing += DialogClosing", source);
        Assert.DoesNotContain("PlayContentDialogExitTransformAsync(LocalSettingsSurface)", source);
        Assert.DoesNotContain("args.GetDeferral()", source);
        Assert.DoesNotContain("ExpandDialogVisualPath", source);
        Assert.Contains("WindowSizing.RemoveContentDialogSizeLimits(dialog)", source);
        Assert.DoesNotContain("dialog.MinWidth = targetWidth", source);
        Assert.DoesNotContain("dialog.MaxWidth = targetWidth", source);
        Assert.DoesNotContain("dialog.MinHeight = targetHeight", source);
        Assert.DoesNotContain("dialog.MaxHeight = targetHeight", source);

        string viewModelSource = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        int methodStart = viewModelSource.IndexOf("private async Task OpenLocalSettingsDialogAsync()", StringComparison.Ordinal);
        int methodEnd = viewModelSource.IndexOf("[RelayCommand]", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        Assert.Contains("FocusVisualStyle = null", viewModelSource[methodStart..methodEnd]);
    }

    [Fact]
    public void DialogBlurScope_DoesNotRemoveTheBackdropBeforeClosingIsConfirmed()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "DialogBlurScope.cs"));
        int methodStart = source.IndexOf("private void ContentDialogClosing", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("private async Task CompleteContentDialogExitAsync", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        Assert.DoesNotContain("ClearDialogMaskVisuals(sender)", method);
        Assert.DoesNotContain("EnableOwnerWindow(ownerWindow)", method);
    }

    [Theory]
    [InlineData("StatusTrayChipButtonStyle")]
    [InlineData("StatusTrayCapacityButtonStyle")]
    [InlineData("StatusTrayCapacityRefreshButtonStyle")]
    public void HomeStatusTrayButtons_DoNotRenderWindowSwitchFocusOutline(string styleKey)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement style = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == styleKey);

        Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
            (string?)setter.Attribute("Value") == "{x:Null}");
    }

    [Fact]
    public void HomeActiveActionButtons_KeepTheirBackgroundWhenHovered()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement template = document.Descendants()
            .Single(element => element.Name.LocalName == "ControlTemplate"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "StableActiveActionButtonTemplate");

        Assert.DoesNotContain(template.Descendants(), element =>
            element.Name.LocalName == "Trigger"
            && (string?)element.Attribute("Property") == "IsMouseOver");
        foreach (string styleKey in new[] { "MonitorActionButtonStyle", "RecordActionButtonStyle" })
        {
            XElement style = document.Descendants()
                .Single(element => element.Name.LocalName == "Style"
                    && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == styleKey);
            Assert.Contains(style.Descendants(), element =>
                element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "Template"
                && (string?)element.Attribute("Value") == "{StaticResource StableActiveActionButtonTemplate}");
        }
    }

    [Fact]
    public void HomeRoomDetailScrollViewer_DoesNotRenderWindowSwitchFocusOutline()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement roomDetailPanel = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomDetailPanel");
        XElement scrollViewer = roomDetailPanel.Descendants()
            .First(element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal("False", (string?)scrollViewer.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)scrollViewer.Attribute("FocusVisualStyle"));
        Assert.Equal("0", (string?)scrollViewer.Attribute("BorderThickness"));
    }

    [Fact]
    public void HomeRoomCardPanel_DoesNotRenderWindowSwitchFocusOutline()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement panel = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardPanel");
        XElement content = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardPanelContent");
        XElement list = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardList");

        Assert.Equal("False", (string?)panel.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)panel.Attribute("FocusVisualStyle"));
        Assert.Equal("False", (string?)content.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)content.Attribute("FocusVisualStyle"));
        Assert.Equal("{x:Null}", (string?)list.Attribute("FocusVisualStyle"));
    }

    [Fact]
    public void MainWindowAndExtensionList_DoNotRenderWindowSwitchOutlines()
    {
        XDocument mainWindow = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement root = mainWindow.Root!;

        Assert.Equal("Transparent", (string?)root.Attribute("BorderBrush"));
        Assert.Equal("0", (string?)root.Attribute("BorderThickness"));

        XDocument extensionPage = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ExtensionCenterWindow.xaml"));
        XElement list = extensionPage.Descendants().Single(element => element.Name.LocalName == "ListBox");
        XElement listStyle = extensionPage.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ExtensionListBoxStyle");
        XElement listScrollViewer = listStyle.Descendants().Single(element => element.Name.LocalName == "ScrollViewer");
        XElement itemsPresenter = listScrollViewer.Descendants().Single(element => element.Name.LocalName == "ItemsPresenter");
        XElement itemStyle = extensionPage.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "{x:Type ListBoxItem}");
        XElement cardStyle = extensionPage.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ExtensionCardStyle");

        Assert.Equal("{StaticResource ExtensionListBoxStyle}", (string?)list.Attribute("Style"));
        Assert.Equal("0", (string?)listScrollViewer.Attribute("BorderThickness"));
        Assert.Equal("Transparent", (string?)listScrollViewer.Attribute("BorderBrush"));
        Assert.Equal("False", (string?)listScrollViewer.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)listScrollViewer.Attribute("FocusVisualStyle"));
        Assert.Equal("20,0,20,14", (string?)itemsPresenter.Attribute("Margin"));
        Assert.Contains(itemStyle.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Focusable"
            && (string?)setter.Attribute("Value") == "False");
        Assert.Contains(cardStyle.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "BorderThickness"
            && (string?)setter.Attribute("Value") == "0");

        XElement extensionContent = extensionPage.Descendants()
            .Single(element => element.Name.LocalName == "ContentPresenter" && (string?)element.Attribute("Content") == "{Binding Content}");
        Assert.Equal("False", (string?)extensionContent.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)extensionContent.Attribute("FocusVisualStyle"));
    }

    [Fact]
    public void ExtensionToggle_KeepsEnabledStateOwnedByItsCommand()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ExtensionCenterWindow.xaml"));
        XElement toggle = document.Descendants().Single(element =>
            element.Name.LocalName == "ToggleSwitch"
            && element.Attribute("Command") != null);

        Assert.Equal("{Binding IsEnabled, Mode=OneWay}", (string?)toggle.Attribute("IsChecked"));
        Assert.Equal("{Binding DataContext.ToggleExtensionCommand, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}", (string?)toggle.Attribute("Command"));
    }

    [Theory]
    [InlineData(719, true, false)]
    [InlineData(720, true, true)]
    [InlineData(759, false, false)]
    [InlineData(760, false, true)]
    public void ExtensionSettings_UseDirectionalColumnHysteresis(double width, bool? currentState, bool expected)
    {
        Assert.Equal(expected, ExtensionCenterWindow.ResolveExtensionSettingsTwoColumnState(width, currentState));
    }

    [Fact]
    public void ExtensionDetails_RenderManifestSettingsAndOptionalIcon()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ExtensionCenterWindow.xaml"));
        XElement details = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "SelectedDetails");
        XElement cardStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ExtensionCardStyle");

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ImageBrush"
            && (string?)element.Attribute("ImageSource") == "{Binding IconSource}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ToggleSwitch"
            && (string?)element.Attribute("IsChecked") == "{Binding BooleanValue, Mode=TwoWay}");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "CardExpander"
            && (string?)element.Attribute("Header") == "扩展设置");

        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Style"
            && (string?)element.Attribute("TargetType") == "{x:Type ui:FontIcon}"
            && (string?)element.Attribute("BasedOn") == "{StaticResource {x:Type ui:FontIcon}}");
        Assert.Equal("StackPanel", details.Name.LocalName);
        Assert.Null(details.Attribute("Background"));
        Assert.Null(details.Attribute("Padding"));
        Assert.Contains(cardStyle.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background"
            && (string?)setter.Attribute("Value") == "{DynamicResource EmerdePanelBrush}");
    }

    [Fact]
    public void ExtensionPage_AlignsToolbarAndCardsOutsideTheScrollBar()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ExtensionCenterWindow.xaml"));
        XElement toolbar = document.Descendants()
            .Single(element => element.Name.LocalName == "Grid"
                && (string?)element.Attribute("ColumnDefinitions") == "*,Auto,Auto,Auto");
        XElement sectionHeader = document.Descendants()
            .Single(element => element.Name.LocalName == "TextBlock"
                && (string?)element.Attribute("Text") == "{I18N InstalledExtensionSection}")
            .Parent!
            .Parent!;

        Assert.Equal("20,10,22,14", (string?)toolbar.Attribute("Margin"));
        Assert.Contains(sectionHeader.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Margin"
            && (string?)element.Attribute("Value") == "20,0,20,10");
        Assert.Contains(sectionHeader.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Margin"
            && (string?)element.Attribute("Value") == "20,8,20,12");
    }

    [Fact]
    public void ExtensionInputStyles_KeepHoverChromeConsistentWithSettings()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XDocument extensionDocument = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ExtensionCenterWindow.xaml"));
        XElement textStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ExtensionTextInputStyle");
        XElement choiceStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ExtensionChoiceInputStyle");
        XElement passwordStyle = extensionDocument.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ExtensionPasswordInputStyle");
        XElement borderBrush = document.Descendants()
            .Single(element => element.Name.LocalName == "SolidColorBrush"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "EmerdeExtensionInputBorderBrush");

        Assert.Equal("#24000000", (string?)borderBrush.Attribute("Color"));
        Assert.Contains("SetBrush(\"EmerdeExtensionInputBorderBrush\"", File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "AppThemeBrushes.cs")), StringComparison.Ordinal);
        AssertExtensionInputStyleUsesStateTriggers(textStyle);
        Assert.Contains(textStyle.Descendants(), element =>
            element.Name.LocalName == "SolidColorBrush"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "TextControlFocusedBorderBrush"
            && (string?)element.Attribute("Color") == "Transparent");
        AssertExtensionInputStyleUsesStateTriggers(choiceStyle);
        Assert.Contains(choiceStyle.Descendants(), element =>
            element.Name.LocalName == "SolidColorBrush"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ComboBoxDropDownBorderBrush"
            && (string?)element.Attribute("Color") == "Transparent");
        Assert.Contains(choiceStyle.Descendants(), element =>
            element.Name.LocalName == "SolidColorBrush"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ControlElevationBorderBrush"
            && (string?)element.Attribute("Color") == "Transparent");
        AssertExtensionInputStyleUsesStateTriggers(passwordStyle);
        Assert.Contains(passwordStyle.Descendants(), element =>
            element.Name.LocalName == "SolidColorBrush"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "TextControlFocusedBorderBrush"
            && (string?)element.Attribute("Color") == "Transparent");
    }

    [Fact]
    public void GlobalToolTip_UsesApplicationRoundedChrome()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XElement style = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "{x:Type ToolTip}");

        Assert.Contains(style.Descendants().Where(element => element.Name.LocalName == "Border"), border =>
            (string?)border.Attribute("CornerRadius") == "{StaticResource Win11ControlCornerRadius}");
    }

    [Fact]
    public void AddRoomDialog_HasNamedUrlInputForInitialFocus()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml"));
        XElement textBox = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomUrlTextBox");

        Assert.Equal("{Binding Url, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", (string?)textBox.Attribute("Text"));
    }

    [Fact]
    public void AddRoomDialog_UsesOneUniformInputBorderWithoutTextBoxElevation()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml"));
        XElement border = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomUrlInputBorder");
        XElement textBox = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomUrlTextBox");

        Assert.Equal("1", (string?)border.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource ControlStrokeColorDefaultBrush}", (string?)border.Attribute("BorderBrush"));
        Assert.Equal("False", (string?)border.Attribute("IsHitTestVisible"));
        Assert.Same(border.Parent, textBox.Parent);
        Assert.Equal("0", (string?)textBox.Attribute("BorderThickness"));
        Assert.Equal("Transparent", (string?)textBox.Attribute("BorderBrush"));
        Assert.Equal("{x:Null}", (string?)textBox.Attribute("FocusVisualStyle"));
        Assert.Equal("RoomUrlTextBoxFocusWithinChanged", (string?)textBox.Attribute("IsKeyboardFocusWithinChanged"));
        Assert.Contains(textBox.Descendants(), element =>
            element.Name.LocalName == "SolidColorBrush"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ControlStrokeColorDefaultBrush"
            && (string?)element.Attribute("Color") == "Transparent");
        Assert.Contains(textBox.Descendants(), element =>
            element.Name.LocalName == "SolidColorBrush"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "TextControlFocusedBorderBrush"
            && (string?)element.Attribute("Color") == "Transparent");

        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml.cs"));
        Assert.Contains("(IsUiXEnabled ? UiXRoomUrlTextBox : RoomUrlTextBox).IsKeyboardFocusWithin", source);
        Assert.Contains("SystemAccentColorPrimaryBrush", source);
        Assert.Contains("ControlStrokeColorDefaultBrush", source);
    }

    [Theory]
    [InlineData("{x:Type TextBox}")]
    [InlineData("{x:Type ui:TextBox}")]
    [InlineData("{x:Type ui:PasswordBox}")]
    [InlineData("{x:Type ui:NumberBox}")]
    public void GlobalInputStyles_RemoveBottomAccentAndCommitOnEnter(string targetType)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XElement style = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == targetType
                && element.Attribute(XName.Get("Key", XamlNamespace)) == null);

        Assert.Contains(style.Descendants(), element =>
            element.Name.LocalName == "SolidColorBrush"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ControlStrokeColorDefaultBrush"
            && (string?)element.Attribute("Color") == "Transparent");
        Assert.Contains(style.Descendants(), element =>
            element.Name.LocalName == "SolidColorBrush"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "TextControlFocusedBorderBrush"
            && (string?)element.Attribute("Color") == "Transparent");
        Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "controls:InputAssist.CommitOnEnter"
            && (string?)setter.Attribute("Value") == "True");
    }

    [Fact]
    public void InputAssist_UpdatesExplicitTextBindingSource()
    {
        string value = "before";
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                System.Windows.Controls.TextBox textBox = new();
                System.Windows.Data.Binding binding = new()
                {
                    Source = new ExplicitBindingTarget(
                        () => value,
                        updated => value = updated),
                    Path = new System.Windows.PropertyPath(nameof(ExplicitBindingTarget.Value)),
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.Explicit,
                };
                System.Windows.Data.BindingOperations.SetBinding(textBox, System.Windows.Controls.TextBox.TextProperty, binding);
                textBox.Text = "after";

                InputAssist.UpdateBindingSources(textBox);
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Equal("after", value);
    }

    [Fact]
    public void InputAssist_PreservesEnterForMultilineTextBoxes()
    {
        Exception? error = null;
        bool preservesMultilineEnter = false;
        bool acceptsExplicitCommand = false;
        Thread thread = new(() =>
        {
            try
            {
                System.Windows.Controls.TextBox textBox = new() { AcceptsReturn = true };
                preservesMultilineEnter = !InputAssist.ShouldProcessEnter(textBox, null);
                acceptsExplicitCommand = InputAssist.ShouldProcessEnter(
                    textBox,
                    new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.True(preservesMultilineEnter);
        Assert.True(acceptsExplicitCommand);
    }

    [Fact]
    public void CompactNumberBox_NormalizesValuesToDisplayedPrecision()
    {
        Exception? error = null;
        double? integerValue = null;
        double? highPrecisionValue = null;
        Thread thread = new(() =>
        {
            try
            {
                CompactNumberBox numberBox = new()
                {
                    Minimum = 0,
                    Maximum = 10,
                    MaxDecimalPlaces = 0,
                    Value = 1.6,
                };
                integerValue = numberBox.Value;

                numberBox.MaxDecimalPlaces = 99;
                numberBox.Value = 1.2345678901234567;
                highPrecisionValue = numberBox.Value;
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Equal(2d, integerValue);
        Assert.Equal(1.234567890123457d, highPrecisionValue);
    }

    [Fact]
    public void SplitInput_ConfirmsOnEnterAndSelectsValueWhenOpened()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement input = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "SplitDurationTextBox");

        Assert.Equal("{Binding ConfirmSplitCommand}", input.Attributes().Single(attribute => attribute.Name.LocalName == "InputAssist.EnterCommand").Value);
        Assert.Equal("True", input.Attributes().Single(attribute => attribute.Name.LocalName == "InputAssist.SelectAllOnVisible").Value);

        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Controls", "CompactNumberBox.cs"));
        Assert.Contains("UpdateTextFromValue(force: true)", source, StringComparison.Ordinal);
        Assert.Contains("Keyboard.ClearFocus()", source, StringComparison.Ordinal);

        string assistSource = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Controls", "InputAssist.cs"));
        Assert.Contains("if (GetCommitOnEnter(element))", assistSource, StringComparison.Ordinal);
        Assert.Contains("Keyboard.ClearFocus()", assistSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitConfirmationDialog_UsesApplicationContentDialogTemplate()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ExitConfirmationContentDialog.xaml"));
        XElement dialog = document.Root!;

        Assert.Equal("ContentDialog", dialog.Name.LocalName);
        Assert.Equal("{StaticResource EmerdeContentDialogStyle}", (string?)dialog.Attribute("Style"));
        Assert.Equal("Primary", (string?)dialog.Attribute("DefaultButton"));
        Assert.Equal("{I18N Yes}", (string?)dialog.Attribute("PrimaryButtonText"));
        Assert.Equal("{I18N No}", (string?)dialog.Attribute("CloseButtonText"));
    }

    [Fact]
    public void GlobalComboBoxStyle_CommitsSelectionOnEnter()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XElement style = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "{x:Type ComboBox}"
                && element.Attribute(XName.Get("Key", XamlNamespace)) == null);

        Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "controls:InputAssist.CommitOnEnter"
            && (string?)setter.Attribute("Value") == "True");
    }

    [Fact]
    public void UiXDialogs_UseTheApplicationSurfaceHierarchy()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XNamespace x = XamlNamespace;
        XElement borderBrush = document.Descendants()
            .Single(element => element.Name.LocalName == "StaticResource"
                && (string?)element.Attribute(x + "Key") == "ContentDialogBorderBrush");
        XElement backgroundBrush = document.Descendants()
            .Single(element => element.Name.LocalName == "SolidColorBrush"
                && (string?)element.Attribute(x + "Key") == "EmerdeDialogBackgroundBrush");
        XElement outerBorderBrush = document.Descendants()
            .Single(element => element.Name.LocalName == "SolidColorBrush"
                && (string?)element.Attribute(x + "Key") == "EmerdeDialogOuterBorderBrush");
        XElement dialogStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(x + "Key") == "EmerdeContentDialogStyle");
        XElement surfaceStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(x + "Key") == "EmerdeDialogSurfaceStyle");
        XElement sectionStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(x + "Key") == "EmerdeDialogSectionStyle");

        Assert.Equal("EmerdeDialogOuterBorderBrush", (string?)borderBrush.Attribute("ResourceKey"));
        Assert.StartsWith("#FF", (string?)backgroundBrush.Attribute("Color"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("#FF", (string?)outerBorderBrush.Attribute("Color"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dialogStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Background"
            && (string?)element.Attribute("Value") == "{DynamicResource EmerdeDialogBackgroundBrush}");
        Assert.DoesNotContain(dialogStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Background"
            && (string?)element.Attribute("Value") == "{DynamicResource UiXWindowFallbackBrush}");
        Assert.Contains(dialogStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderThickness"
            && (string?)element.Attribute("Value") == "0");
        Assert.Contains(surfaceStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Background"
            && (string?)element.Attribute("Value") == "{DynamicResource UiXDialogSurfaceBrush}");
        Assert.Contains(sectionStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Background"
            && (string?)element.Attribute("Value") == "{DynamicResource UiXDialogSectionBrush}");
        XElement messageBoxStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "{x:Type vio:MessageBoxDialog}");
        Assert.Contains(messageBoxStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderThickness"
            && (string?)element.Attribute("Value") == "1");
    }

    [Fact]
    public void ConfigRestoreDialog_UsesOneOpaqueOuterSurface()
    {
        XDocument resources = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XElement sharedDialogStyle = resources.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "EmerdeContentDialogStyle");

        Assert.Contains(sharedDialogStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Background"
            && (string?)element.Attribute("Value") == "{DynamicResource EmerdeDialogBackgroundBrush}");

        string viewModel = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "SettingsViewModel.cs"));
        Assert.Contains("Content = content", viewModel, StringComparison.Ordinal);
        Assert.Contains("DialogBlurScope.ForLightDismiss(OwnerWindow, dialog)", viewModel, StringComparison.Ordinal);
        Assert.Contains("WindowSizing.ShowContentDialogAsync(dialog, OwnerWindow)", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigRestoreWindow", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXRoomWorkspace_UsesOneOpaqueOuterSurface()
    {
        XDocument mainDocument = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XDocument workspaceDocument = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));
        XElement shell = mainDocument.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "UiXWorkspaceShell");
        XElement background = shell.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "UiXWorkspaceBackground");
        XElement stroke = shell.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "UiXWorkspaceStroke");
        XElement host = shell.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "UiXWorkspaceHost");
        XElement surface = workspaceDocument.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "WorkspaceSurface");
        XElement[] shellLayers = shell.Elements().ToArray();

        Assert.Equal("Grid", shell.Name.LocalName);
        Assert.Equal([background, host, stroke], shellLayers);
        Assert.Equal("{DynamicResource EmerdeDialogBackgroundBrush}", (string?)background.Attribute("Background"));
        Assert.Equal("8", (string?)background.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource ControlElevationBorderBrush}", (string?)stroke.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)stroke.Attribute("BorderThickness"));
        Assert.Equal("8", (string?)stroke.Attribute("CornerRadius"));
        Assert.Equal("False", (string?)stroke.Attribute("IsHitTestVisible"));
        Assert.Equal("True", (string?)stroke.Attribute("SnapsToDevicePixels"));
        Assert.Null(host.Attribute("Margin"));
        Assert.Null(surface.Attribute("Style"));
        Assert.Equal("Transparent", (string?)surface.Attribute("Background"));
        Assert.Null(surface.Attribute("BorderBrush"));
        Assert.Equal("0", (string?)surface.Attribute("BorderThickness"));
        Assert.Equal("0", (string?)surface.Attribute("CornerRadius"));
        Assert.Null(surface.Attribute("Opacity"));
    }

    [Fact]
    public void RoomInformation_UsesTheLiveAvatarSourceAndShowsStatisticsBeforeStreamDetails()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        int statisticsIndex = source.IndexOf("RoomRecordingStatistics\".Tr()", StringComparison.Ordinal);
        int streamIndex = source.IndexOf("CreateRoomInformationSectionTitle(\"LiveStream\".Tr())", StringComparison.Ordinal);

        Assert.Contains("nameof(RoomStatusReactive.AvatarDisplaySource)", source, StringComparison.Ordinal);
        Assert.Contains("TryGetDialogVisualSize(owner, 0.42d, 0.72d", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(targetWidth, 560d, 680d)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth = 680", source, StringComparison.Ordinal);
        Assert.Contains("Padding = new Thickness(20, 8, 28, 20)", source, StringComparison.Ordinal);
        Assert.Contains("dialog.Resources[\"EmerdeWideContentDialog\"] = true", source, StringComparison.Ordinal);
        Assert.Contains("LocalSettingsContentDialog.ApplyWideDialogVisualSize(dialog, targetWidth, targetHeight)", source, StringComparison.Ordinal);
        Assert.Contains("LocalSettingsContentDialog.ClearWideDialogVisualSize(dialog)", source, StringComparison.Ordinal);
        Assert.True(statisticsIndex >= 0);
        Assert.True(streamIndex > statisticsIndex);
    }

    [Theory]
    [InlineData("SettingsWindow.xaml", "SettingsScrollViewer")]
    [InlineData("LocalSettingsContentDialog.xaml", "LocalSettingsScrollViewer")]
    public void SettingsScrollViewers_UseSurfaceSpecificKeyboardFocus(string fileName, string scrollViewerName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));
        XElement scrollViewer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == scrollViewerName);

        bool supportsKeyboardFocus = fileName == "LocalSettingsContentDialog.xaml";
        Assert.Equal(supportsKeyboardFocus ? "True" : "False", (string?)scrollViewer.Attribute("Focusable"));
        Assert.Equal(supportsKeyboardFocus ? null : "{x:Null}", (string?)scrollViewer.Attribute("FocusVisualStyle"));
        if (supportsKeyboardFocus)
        {
            Assert.Equal("True", (string?)scrollViewer.Attribute("IsTabStop"));
        }
        Assert.Equal("0", (string?)scrollViewer.Attribute("BorderThickness"));
    }

    [Fact]
    public void SettingsCards_DoNotRenderWindowSwitchFocusOutline()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        foreach (string targetType in new[] { "{x:Type ui:Card}", "{x:Type ui:CardExpander}" })
        {
            XElement style = document.Descendants()
                .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == targetType);
            Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
                (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
                (string?)setter.Attribute("Value") == "{x:Null}");
        }
    }

    [Fact]
    public void SaveFolderPathLevel_DefaultsToAuthorYearMonthDate()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Configurations.cs"));

        Assert.Contains("SaveFolderPathLevel), 3", source);
    }

    [Theory]
    [InlineData("Button")]
    [InlineData("ToggleButton")]
    [InlineData("CheckBox")]
    public void GlobalInteractiveControls_UseMotionFeedback(string targetType)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XElement style = document.Descendants()
            .First(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == $"{{x:Type {targetType}}}");

        Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), IsPressMotionSetter);
    }

    [Fact]
    public void MainPageSurfaces_UseEntranceMotion()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        foreach (string elementName in new[] { "MainContentRoot", "ShellNavigationPanel" })
        {
            XElement element = document.Descendants()
                .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == elementName);
            Assert.True(HasMotionAttribute(element, "IsEntranceEnabled"));
        }
    }

    [Theory]
    [InlineData("HomePageRoot")]
    [InlineData("VideoListPage")]
    [InlineData("ExtensionsPage")]
    [InlineData("SettingsPage")]
    [InlineData("AboutPage")]
    public void UiXMainPageNavigation_UsesOnePreparedEntrancePerActivePage(string elementName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        XElement element = document.Descendants()
            .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == elementName);

        Assert.False(HasAttribute(element, "EntranceTrigger"));
        Assert.False(HasMotionAttribute(element, "IsEntranceEnabled"));
        Assert.Contains("QueueActiveMainPageEntrance(selectedPageIndex)", source, StringComparison.Ordinal);
        Assert.Contains("MotionAssist.PrepareEntrance(pendingPage)", source, StringComparison.Ordinal);
        Assert.Contains("MotionAssist.PlayEntrance(page)", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.DataBind", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.StatusOfIsUiXEnabled || replayPageEntrance", source, StringComparison.Ordinal);
        Assert.Contains("!ViewModel.StatusOfIsUiXEnabled && (pageIndex == 1 || pageIndex >= 5)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionCards_DoNotStackEntranceMotionOnPageNavigation()
    {
        XDocument main = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XDocument video = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement roomCard = main.Descendants()
            .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardShell");
        XElement videoCard = video.Descendants()
            .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == "VideoCardShell");

        Assert.False(HasAttribute(roomCard, "EntranceTrigger"));
        Assert.False(HasAttribute(videoCard, "EntranceTrigger"));
    }

    [Fact]
    public void VideoListPage_UsesTheCentralUiXPageEntrance()
    {
        XDocument main = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XDocument video = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement page = main.Descendants()
            .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == "VideoListPage");

        Assert.False(HasMotionAttribute(page, "IsEntranceEnabled"));
        Assert.False(HasAttribute(video.Descendants().Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == "VideoListContentRoot"), "IsEntranceEnabled"));
    }

    [Fact]
    public void UiXNavigationSurfaces_UseSharedMovingIndicators()
    {
        XDocument main = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XDocument settings = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XDocument about = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        string motionSource = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Controls", "MotionAssist.cs"));

        foreach ((XDocument document, string name) in new[]
                 {
                     (main, "ShellNavigationSelectionIndicator"),
                     (settings, "SettingsFocusSelectionIndicator"),
                     (about, "AboutNavigationSelectionIndicator"),
                 })
        {
            XElement indicator = document.Descendants()
                .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == name);
            Assert.Equal("False", (string?)indicator.Attribute("IsHitTestVisible"));
            Assert.Contains(indicator.Descendants(), element => element.Name.LocalName == "TranslateTransform");
        }

        Assert.Contains("MoveNavigationIndicator", motionSource, StringComparison.Ordinal);
        Assert.Contains("QuarticEase", motionSource, StringComparison.Ordinal);
        Assert.Contains("HandoffBehavior.SnapshotAndReplace", motionSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SettingsWindow.xaml", "SettingsDialogContent")]
    [InlineData("AboutContentDialog.xaml", "AboutContentRoot")]
    public void EmbeddedUiXPageRoots_DisableTheirAutomaticEntrance(string fileName, string rootName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));
        XElement root = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == rootName);

        Assert.False(HasMotionAttribute(root, "IsEntranceEnabled"));
        Assert.Contains(root.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "controls:MotionAssist.IsEntranceEnabled"
            && (string?)element.Attribute("Value") == "False");
    }

    [Theory]
    [InlineData("RoomCardSelectionLayer", "MainWindow.xaml")]
    public void SelectionLayers_DoNotSetFinalOpacityBeforeAnimation(string elementName, string fileName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));

        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == elementName
            && (string?)element.Attribute("Property") == "Opacity"
            && (string?)element.Attribute("Value") == "1");
    }

    [Theory]
    [InlineData("MainWindow.xaml")]
    public void CardContainers_DoNotGrowOnHover(string fileName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));

        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "controls:MotionAssist.HoverScale"
            && double.TryParse((string?)element.Attribute("Value"), out double value)
            && value > 1d
            && element.Ancestors().Any(ancestor => ancestor.Name.LocalName.Contains("ItemContainerStyle", StringComparison.Ordinal)));
    }

    [Fact]
    public void MainPageEntranceResolver_CoversBuiltInAndExtensionPages()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("0 => HomePageRoot", source, StringComparison.Ordinal);
        Assert.Contains("1 => VideoListPage", source, StringComparison.Ordinal);
        Assert.Contains("2 => ExtensionsPage", source, StringComparison.Ordinal);
        Assert.Contains("3 => SettingsPage", source, StringComparison.Ordinal);
        Assert.Contains("4 => AboutPage", source, StringComparison.Ordinal);
        Assert.Contains("ExtensionPageHost.Children[pageIndex - 5]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsCardExpander_AnimatesTheMeasuredContentLayout()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XElement style = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "{x:Type ui:CardExpander}");

        XElement expandSite = style.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "ExpandSite");
        Assert.Contains(expandSite.Descendants(), element =>
            element.Name.LocalName == "ScaleTransform" && (string?)element.Attribute("ScaleY") == "0");
        Assert.Contains(style.Descendants().Where(element => element.Name.LocalName == "DoubleAnimation"), animation =>
            (string?)animation.Attribute("Storyboard.TargetName") == "ExpandSite"
            && (string?)animation.Attribute("Storyboard.TargetProperty") == "(FrameworkElement.LayoutTransform).(ScaleTransform.ScaleY)"
            && animation.Attribute("From") == null
            && (string?)animation.Attribute("To") == "1");
        Assert.Contains(style.Descendants().Where(element => element.Name.LocalName == "DoubleAnimation"), animation =>
            (string?)animation.Attribute("Storyboard.TargetName") == "ExpandSite"
            && (string?)animation.Attribute("Storyboard.TargetProperty") == "(FrameworkElement.LayoutTransform).(ScaleTransform.ScaleY)"
            && animation.Attribute("From") == null
            && (string?)animation.Attribute("To") == "0");
    }

    [Fact]
    public void HomeRoomCards_DrawSelectionAcrossTheSurfaceWithAnInnerStroke()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement card = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardShell");
        XElement selectionLayer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardSelectionLayer");
        XElement content = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardContent");
        XElement surface = card.Elements().Single(element => element.Name.LocalName == "Grid");

        Assert.Equal("0", (string?)card.Attribute("Padding"));
        Assert.Equal("0", (string?)card.Attribute("BorderThickness"));
        Assert.Equal("Transparent", (string?)card.Attribute("BorderBrush"));
        Assert.Equal("0", (string?)selectionLayer.Attribute("Margin"));
        Assert.Equal("#2A4DA7B0", (string?)selectionLayer.Attribute("Background"));
        Assert.Equal("#884DA7B0", (string?)selectionLayer.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)selectionLayer.Attribute("BorderThickness"));
        Assert.Same(surface, selectionLayer.Parent);
        Assert.Same(surface, content.Parent);
        XElement contentStyle = content.Elements().Single(element => element.Name.LocalName == "Border.Style");
        Assert.Contains(contentStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Padding"
            && (string?)element.Attribute("Value") == "{Binding RoomCardPadding, RelativeSource={RelativeSource AncestorType={x:Type views:MainWindow}}}");
        Assert.Contains(contentStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Padding"
            && (string?)element.Attribute("Value") == "0");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "RoomCardShell"
            && (string?)element.Attribute("Property") is "BorderBrush" or "BorderThickness");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "RoomCardSelectionLayer"
            && (string?)element.Attribute("Background") == null
            && (string?)element.Attribute("Value") == "#2A4DA7B0");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "RoomCardSelectionLayer"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "#884DA7B0");
    }

    [Fact]
    public void UiXHomeRoomCards_AnimateStatusLayersWithoutChangingLegacyBackground()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement card = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardShell");
        XElement cardStyle = card.Elements().Single(element => element.Name.LocalName == "Border.Style");
        string[] layers = ["RoomCardBaseStateLayer", "RoomCardMonitorStateLayer", "RoomCardLiveStateLayer", "RoomCardRecordingStateLayer"];
        string[] brushes = ["UiXCardBrush", "UiXMonitorCardBrush", "UiXLiveCardBrush", "UiXRecordingCardBrush"];

        for (int index = 0; index < layers.Length; index++)
        {
            XElement layer = document.Descendants()
                .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == layers[index]);
            Assert.Contains(layer.DescendantsAndSelf(), element =>
                (string?)element.Attribute("Background") == $"{{DynamicResource {brushes[index]}}}"
                || element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "Background"
                && (string?)element.Attribute("Value") == $"{{DynamicResource {brushes[index]}}}");
            Assert.Equal("False", (string?)layer.Attribute("IsHitTestVisible"));
            Assert.Equal("0", (string?)layer.Attribute("Opacity"));
            Assert.Contains(layer.Descendants(), element =>
                element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "Visibility"
                && (string?)element.Attribute("Value") == "Visible");
            Assert.Contains(layer.Descendants(), element =>
                element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "controls:MotionAssist.IsStateTransitionActive"
                && (string?)element.Attribute("Value") == "True");
        }

        XElement monitorLayer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == layers[1]);
        XElement liveLayer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == layers[2]);
        Assert.Contains(monitorLayer.Descendants(), element =>
            element.Name.LocalName == "Condition"
            && (string?)element.Attribute("Binding") == "{Binding IsStreaming}"
            && (string?)element.Attribute("Value") == "False");
        Assert.Contains(liveLayer.Descendants(), element =>
            element.Name.LocalName == "Condition"
            && (string?)element.Attribute("Binding") == "{Binding IsRecording}"
            && (string?)element.Attribute("Value") == "False");

        Assert.Contains(cardStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Background"
            && (string?)element.Attribute("Value") == "{DynamicResource EmerdeCardBrush}");
        Assert.Contains(cardStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Background"
            && (string?)element.Attribute("Value") == "Transparent");
        Assert.DoesNotContain(cardStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && ((string?)element.Attribute("Value")) is "{DynamicResource UiXMonitorCardBrush}"
                or "{DynamicResource UiXLiveCardBrush}"
                or "{DynamicResource UiXRecordingCardBrush}");
    }

    [Fact]
    public void HomeRoomCardContextMenu_UsesHoverShortcutHintsAndActiveStateColors()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement preview = FindMenuItem(document, "{I18N PreviewLiveRoom}");
        XElement gotoRoom = FindMenuItem(document, "{I18N GotoLiveRoom}");
        XElement monitor = FindMenuItem(document, "{Binding PlacementTarget.DataContext.MonitorMenuText, RelativeSource={RelativeSource AncestorType=ContextMenu}}");
        XElement record = FindMenuItem(document, "{Binding PlacementTarget.DataContext.RecordMenuText, RelativeSource={RelativeSource AncestorType=ContextMenu}}");
        XElement remove = FindMenuItem(document, "{I18N RemoveRoom}");

        Assert.Null(preview.Attribute("InputGestureText"));
        Assert.Null(gotoRoom.Attribute("InputGestureText"));
        Assert.Null(monitor.Attribute("InputGestureText"));
        Assert.Null(record.Attribute("InputGestureText"));
        Assert.Null(remove.Attribute("InputGestureText"));
        Assert.Equal("{I18N PreviewRoomMenuToolTip}", (string?)preview.Attribute("ToolTip"));
        Assert.Equal("{I18N OpenRoomMenuToolTip}", (string?)gotoRoom.Attribute("ToolTip"));
        Assert.Equal("{I18N MonitorRoomMenuToolTip}", (string?)monitor.Attribute("ToolTip"));
        Assert.Equal("{I18N RecordRoomMenuToolTip}", (string?)record.Attribute("ToolTip"));
        Assert.Equal("{I18N RemoveRoomMenuToolTip}", (string?)remove.Attribute("ToolTip"));
        Assert.Equal("{StaticResource SelectedContextMenuOptionStyle}", (string?)preview.Attribute("Style"));
        Assert.Equal("{StaticResource SelectedContextMenuOptionStyle}", (string?)gotoRoom.Attribute("Style"));
        Assert.Equal("{StaticResource SelectedContextMenuOptionStyle}", (string?)remove.Attribute("Style"));
        Assert.Equal("{StaticResource MonitorContextMenuActionStyle}", (string?)monitor.Attribute("Style"));
        Assert.Equal("{StaticResource RecordContextMenuActionStyle}", (string?)record.Attribute("Style"));
        Assert.Equal("{Binding PlacementTarget.DataContext.EffectiveIsToMonitor, RelativeSource={RelativeSource AncestorType=ContextMenu}}", (string?)monitor.Attribute("Tag"));
        Assert.Equal("{Binding PlacementTarget.DataContext.RecordMenuIsActive, RelativeSource={RelativeSource AncestorType=ContextMenu}}", (string?)record.Attribute("Tag"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Foreground"
            && (string?)element.Attribute("Value") == "{StaticResource MonitorActiveForegroundBrush}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Foreground"
            && (string?)element.Attribute("Value") == "{StaticResource RecordActiveForegroundBrush}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "FontSize"
            && (string?)element.Attribute("Value") == "14");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "MinHeight"
            && (string?)element.Attribute("Value") == "36");
    }

    [Fact]
    public void HomeBackgroundContextMenu_UsesSidebarStyleSelectionIndicators()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement template = document.Descendants()
            .Single(element => element.Name.LocalName == "ControlTemplate"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "UiXContextMenuRadioOptionTemplate");
        XElement indicator = template.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "SelectionIndicator");

        Assert.Equal("3", (string?)indicator.Attribute("Width"));
        Assert.Equal("20", (string?)indicator.Attribute("Height"));
        Assert.Equal("Left", (string?)indicator.Attribute("HorizontalAlignment"));
        Assert.Equal("5,0,0,0", (string?)indicator.Attribute("Margin"));
        Assert.Equal("0", (string?)indicator.Attribute("Opacity"));
    }

    [Fact]
    public void HomeBackgroundContextMenu_UsesCheckedStateForCurrentSelection()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement radioStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "SelectedContextMenuRadioOptionStyle");

        Assert.Equal("{StaticResource SelectedContextMenuOptionStyle}", (string?)radioStyle.Attribute("BasedOn"));
        Assert.Contains(radioStyle.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "OverridesDefaultStyle"
            && (string?)element.Attribute("Value") == "True");
        Assert.Contains(radioStyle.Descendants(), element => element.Name.LocalName == "Trigger"
            && (string?)element.Attribute("Property") == "IsChecked");
        XElement radioTemplate = document.Descendants()
            .Single(element => element.Name.LocalName == "ControlTemplate"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "UiXContextMenuRadioOptionTemplate");
        Assert.Contains(radioTemplate.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "SelectionSurface"
            && (string?)element.Attribute("Property") == "Background"
            && (string?)element.Attribute("Value") == "{DynamicResource UiXSelectionFillBrush}");
        Assert.Contains(radioTemplate.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "SelectionIndicator"
            && (string?)element.Attribute("Property") == "Opacity"
            && (string?)element.Attribute("Value") == "1");

        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        Assert.Contains("SetContextMenuRadioSelection(large", code, StringComparison.Ordinal);
        Assert.Contains("SetContextMenuRadioSelection(byName", code, StringComparison.Ordinal);
        Assert.Contains("IsChecked = string.Equals(normalizedOption, selectedPlatform", code, StringComparison.Ordinal);
        Assert.Contains("menu.FindResource(\"PlatformFilterOptionStyle\")", code, StringComparison.Ordinal);
        Assert.Contains("menu.FindResource(\"PlatformFilterIndicatorStyle\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindResource(\"SelectedContextMenuIndicatorStyle\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryFindResource(\"SelectedContextMenuRadioOptionStyle\")", code, StringComparison.Ordinal);

        XElement platformFilterStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "PlatformFilterOptionStyle");
        Assert.Equal("{StaticResource SelectedContextMenuRadioOptionStyle}", (string?)platformFilterStyle.Attribute("BasedOn"));
        XElement platformIndicatorStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "PlatformFilterIndicatorStyle");
        Assert.Equal("{StaticResource SelectedContextMenuIndicatorStyle}", (string?)platformIndicatorStyle.Attribute("BasedOn"));
        XElement contextMenu = document.Descendants()
            .Single(element => element.Name.LocalName == "ContextMenu"
                && (string?)element.Attribute("Opened") == "HomeBackgroundContextMenuOpened");
        Assert.NotNull(contextMenu);
        Assert.DoesNotContain("x:Reference MainWindowRoot", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("ApplyHomeContextMenuMode(menu, isUiXEnabled, submenuTemplate, radioTemplate)", code, StringComparison.Ordinal);
        Assert.Contains("ApplyUiXContextMenuRadioTemplate(item, radioTemplate)", code, StringComparison.Ordinal);
        Assert.Contains("item.SetCurrentValue(Control.TemplateProperty, submenuTemplate)", code, StringComparison.Ordinal);
        Assert.Contains("item.ClearValue(Control.TemplateProperty)", code, StringComparison.Ordinal);
        Assert.Contains("ShellRoot.Resources[\"UiXContextMenuSubmenuHeaderTemplate\"]", code, StringComparison.Ordinal);
        Assert.Contains("ShellRoot.Resources[\"UiXContextMenuRadioOptionTemplate\"]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("menu.FindResource(\"UiXContextMenu", code, StringComparison.Ordinal);
        Assert.Contains("Tag = ViewModel.StatusOfIsUiXEnabled", code, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeBackgroundContextMenu_RemovesNestedPopupShadowAndMovesMenusRight()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement template = document.Descendants()
            .Single(element => element.Name.LocalName == "ControlTemplate"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "UiXContextMenuSubmenuHeaderTemplate");
        XElement popup = template.Descendants().Single(element => element.Name.LocalName == "Popup");
        XElement roomCardPanel = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardPanel");
        XElement contextMenu = roomCardPanel.Elements()
            .Single(element => element.Name.LocalName == "Border.ContextMenu")
            .Elements()
            .Single(element => element.Name.LocalName == "ContextMenu");

        Assert.DoesNotContain(template.Descendants(), element => element.Name.LocalName == "DropShadowEffect");
        Assert.Equal("PART_Popup", (string?)popup.Attribute(XName.Get("Name", XamlNamespace)));
        Assert.Equal("1", (string?)popup.Attribute("HorizontalOffset"));
        Assert.Equal("0", (string?)popup.Attribute("VerticalOffset"));
        XElement submenuBorder = template.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "SubmenuBorder");
        Assert.Equal("0", (string?)submenuBorder.Attribute("Margin"));
        Assert.DoesNotContain(template.Descendants(), element => element.Name.LocalName == "TranslateTransform");
        Assert.Equal("1", (string?)contextMenu.Attribute("HorizontalOffset"));
        Assert.Equal(2, document.Descendants().Count(element => element.Name.LocalName == "MenuItem"
            && (string?)element.Attribute("Style") == "{StaticResource UiXContextMenuSubmenuHeaderStyle}"));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Template"
            && (string?)element.Attribute("Value") == "{StaticResource UiXContextMenuSubmenuHeaderTemplate}");
    }

    [Fact]
    public void HomeBackgroundContextMenu_AppliesUiXTemplatesDirectlyAndRestoresLegacyTemplates()
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                System.Windows.Controls.ContextMenu menu = new();
                System.Windows.Controls.MenuItem submenu = new();
                System.Windows.Controls.MenuItem option = new()
                {
                    Icon = new System.Windows.Controls.Border(),
                };
                submenu.Items.Add(option);
                menu.Items.Add(submenu);
                System.Windows.Controls.ControlTemplate submenuTemplate = new(typeof(System.Windows.Controls.MenuItem));
                System.Windows.Controls.ControlTemplate radioTemplate = new(typeof(System.Windows.Controls.MenuItem));

                Emerde.Views.MainWindow.ApplyHomeContextMenuMode(menu, true, submenuTemplate, radioTemplate);

                Assert.Same(submenuTemplate, submenu.Template);
                Assert.Same(radioTemplate, option.Template);
                Assert.True(option.OverridesDefaultStyle);
                Assert.Same(System.Windows.Media.Brushes.Transparent, option.Background);

                Emerde.Views.MainWindow.ApplyHomeContextMenuMode(menu, false, null, null);

                Assert.Equal(System.Windows.DependencyProperty.UnsetValue, submenu.ReadLocalValue(System.Windows.Controls.Control.TemplateProperty));
                Assert.Equal(System.Windows.DependencyProperty.UnsetValue, option.ReadLocalValue(System.Windows.Controls.Control.TemplateProperty));
                Assert.Equal(System.Windows.DependencyProperty.UnsetValue, option.ReadLocalValue(System.Windows.FrameworkElement.OverridesDefaultStyleProperty));
                Assert.Equal(System.Windows.DependencyProperty.UnsetValue, option.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty));
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void HomeRoomCardContextMenu_SeparatesRemoveFromRoomInformation()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement[] menuItems = document.Descendants()
            .Where(element => element.Name.LocalName == "MenuItem")
            .ToArray();
        int removeIndex = Array.FindIndex(menuItems, item => (string?)item.Attribute("Header") == "{I18N RemoveRoom}");
        int informationIndex = Array.FindIndex(menuItems, item => (string?)item.Attribute("Header") == "{I18N RoomInformation}");

        Assert.True(removeIndex >= 0);
        Assert.True(informationIndex >= 0);
        Assert.True(informationIndex < removeIndex);
        XElement informationItem = menuItems[informationIndex];
        XElement removeItem = menuItems[removeIndex];
        Assert.Contains(informationItem.ElementsAfterSelf().TakeWhile(element => element != removeItem),
            element => element.Name.LocalName == "Separator");
    }

    [Fact]
    public void HomeRoomCards_KeepLegacyAndUiXLayoutsBehindTheUiXSwitch()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        XElement legacy = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardLegacyLayout");
        XElement uiX = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardUiXLayout");

        Assert.Contains(legacy.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Visibility"
            && (string?)element.Attribute("Value") == "Collapsed");
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Visibility"
            && (string?)element.Attribute("Value") == "Visible");
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute("Text") == "{Binding NickName}"
            && (string?)element.Attribute("TextAlignment") == "Center");
        Assert.Contains("UiXRoomCardAvatarSize", uiX.ToString(), StringComparison.Ordinal);
        Assert.Contains("UiXRoomCardNameFontSize", uiX.ToString(), StringComparison.Ordinal);
        Assert.Contains("UiXRoomCardTitleFontSize", uiX.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding StreamStatusText}", uiX.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding RecordStatusText}", uiX.ToString(), StringComparison.Ordinal);
        Assert.Contains("UiXHomeRoomCardBaseWidth = 180d", source, StringComparison.Ordinal);
        Assert.Contains("UiXPreviewRoomCardBaseWidth = 200d", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.StatusOfIsUiXEnabled ? 0.72d : 2d / 3d", source, StringComparison.Ordinal);
        Assert.Contains(": Math.Clamp(WindowSizing.RoundLayoutValue(13d * scale), 12d, 14d)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXTheme_UsesFluentWpfCoreWithoutImportingItsControlTheme()
    {
        string project = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Emerde.csproj"));
        string app = File.ReadAllText(FindRepositoryFile("src", "Emerde", "App.xaml"));
        string mainWindow = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        string mainWindowCode = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        string uiXTheme = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Themes", "UiXTheme.xaml"));
        XDocument uiXThemeDocument = XDocument.Parse(uiXTheme);
        XElement surfaceBorderThickness = uiXThemeDocument.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "UiXSurfaceBorderThickness");

        Assert.Contains("FluentWpfCore\" Version=\"1.0.5", project, StringComparison.Ordinal);
        Assert.Contains("Themes/UiXTheme.xaml", app, StringComparison.Ordinal);
        Assert.DoesNotContain("FluentWpfCore;component/Themes/Generic.xaml", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UiXWindowMaterial\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("MaterialMode=\"None\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("MaterialType.Mica", mainWindowCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MaterialType.Acrylic", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("UiXWindowFallbackBrush", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("MaterialType.None", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("UiXPanelBrush", uiXTheme, StringComparison.Ordinal);
        Assert.Contains("UiXCardBrush", uiXTheme, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardBrush", uiXTheme, StringComparison.Ordinal);
        Assert.Contains("UiXShellSurfaceCornerRadius", mainWindow, StringComparison.Ordinal);
        Assert.Contains("UiXPageSurfaceCornerRadius", mainWindow, StringComparison.Ordinal);
        Assert.Contains("UiXStrokeBrush", uiXTheme, StringComparison.Ordinal);
        Assert.Contains("UiXTranscodeFillBrush", uiXTheme, StringComparison.Ordinal);
        Assert.Contains("UiXStallFillBrush", uiXTheme, StringComparison.Ordinal);
        Assert.Contains("UiXInformationBandStyle", uiXTheme, StringComparison.Ordinal);
        Assert.Contains("UiXStatusDotStyle", uiXTheme, StringComparison.Ordinal);
        Assert.Equal("0", surfaceBorderThickness.Value);
        Assert.Contains("Value=\"{StaticResource UiXSurfaceBorderThickness}\"", uiXTheme, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"40\" />", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<Setter TargetName=\"ButtonSurface\" Property=\"Width\" Value=\"40\" />", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<Setter TargetName=\"ButtonSurface\" Property=\"Height\" Value=\"40\" />", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<Setter TargetName=\"ButtonSurface\" Property=\"CornerRadius\" Value=\"{StaticResource UiXNavigationInsetCornerRadius}\" />", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigation_UsesTheSameVisualAndKeyboardPageOrder()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        string viewModel = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        XElement navigation = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "ShellNavigationItems");
        string[] commands = navigation.Elements()
            .Where(element => element.Name.LocalName == "ToggleButton")
            .Select(element => (string)element.Attribute("Command")!)
            .ToArray();

        Assert.Equal(
            [
                "{Binding ShowHomePageCommand}",
                "{Binding OpenScreenRecordListCommand}",
                "{Binding OpenExtensionsCommand}",
                "{Binding OpenSettingsDialogCommand}",
                "{Binding OpenAboutCommand}",
            ],
            commands);
        Assert.Contains("IsExtensionsPageSelected => SelectedMainPageIndex == 2", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsSettingsPageSelected => SelectedMainPageIndex == 3", viewModel, StringComparison.Ordinal);
        Assert.Contains("private void OpenExtensions()", viewModel, StringComparison.Ordinal);
        Assert.Contains("private void OpenSettingsDialog()", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigation_ExitButtonUsesCloudLegacyContentAndCompactUiXContent()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement exitButton = document.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && (string?)element.Attribute("Command") == "{Binding ExitApplicationCommand}");
        XElement exitStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ShellExitButtonStyle");
        XElement uiXTrigger = exitStyle.Elements()
            .Single(element => element.Name.LocalName == "Style.Triggers")
            .Elements()
            .Single(element => element.Name.LocalName == "DataTrigger");

        Assert.Single(exitButton.Elements(), element => element.Name.LocalName == "StackPanel");
        Assert.Single(exitButton.Descendants(), element => element.Name.LocalName == "FontIcon");
        XElement exitLabel = exitButton.Descendants()
            .Single(element => element.Name.LocalName == "TextBlock");
        Assert.Equal("{I18N TrayMenuExit}", (string?)exitLabel.Attribute("Text"));
        Assert.Contains(exitLabel.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Visibility"
            && (string?)element.Attribute("Value") == "Collapsed");
        Assert.Contains(exitStyle.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Width"
            && (string?)element.Attribute("Value") == "64");
        Assert.Contains(exitStyle.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Height"
            && (string?)element.Attribute("Value") == "34");
        Assert.Contains(uiXTrigger.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Width"
            && (string?)element.Attribute("Value") == "40");
        Assert.Contains(uiXTrigger.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Height"
            && (string?)element.Attribute("Value") == "40");
    }

    [Fact]
    public void UiXSecondaryPages_UseSharedSemanticThemeTokens()
    {
        string about = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        string extensions = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ExtensionCenterWindow.xaml"));
        string localSettings = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml"));
        string settings = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        string videos = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        string releaseNotes = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UpdateReleaseNotesContentDialog.xaml"));

        Assert.Contains("UiXCardBrush", about, StringComparison.Ordinal);
        Assert.Contains("UiXCardBrush", extensions, StringComparison.Ordinal);
        Assert.Contains("UiXDialogSectionBrush", localSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsUiXCardExpanderTemplate", settings, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardBrush", videos, StringComparison.Ordinal);
        Assert.Contains("UiXDialogElevatedBrush", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"TextWrapping\" Value=\"Wrap\" />", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateReleaseNotesSelector_UsesUiXInputStrokePalette()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "UpdateReleaseNotesContentDialog.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement style = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Key") == "UpdateReleaseNotesComboBoxStyle");
        XElement selector = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "ReleaseNoteVersionPicker");

        Assert.Equal("{StaticResource UpdateReleaseNotesComboBoxStyle}", (string?)selector.Attribute("Style"));
        Assert.Contains(style.Elements(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource UiXStrongStrokeBrush}");
        Assert.Contains(style.Descendants(), element => element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource UiXSelectionStrokeBrush}");
    }

    [Fact]
    public void VideoCards_DrawAllStrokesInsideTheSurface()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement card = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "VideoCardShell");
        XElement surfaceStroke = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "VideoCardSurfaceStroke");
        XElement selectionLayer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "VideoCardSelectionLayer");
        XElement content = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "VideoCardContent");
        XElement surface = card.Elements().Single(element => element.Name.LocalName == "Grid");

        Assert.Equal("0", (string?)card.Attribute("Padding"));
        Assert.Equal("0", (string?)card.Attribute("BorderThickness"));
        Assert.Same(surface, surfaceStroke.Parent);
        Assert.Same(surface, selectionLayer.Parent);
        Assert.Same(surface, content.Parent);
        Assert.Equal("1", (string?)surfaceStroke.Attribute("BorderThickness"));
        Assert.Equal("1", (string?)selectionLayer.Attribute("BorderThickness"));
        XElement paddingStyle = content.Elements()
            .Single(element => element.Name.LocalName == "Border.Style")
            .Elements()
            .Single(element => element.Name.LocalName == "Style");
        Assert.Contains(paddingStyle.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Padding"
            && (string?)setter.Attribute("Value") == "14");
        Assert.Contains(paddingStyle.Descendants().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Padding"
            && ((string?)setter.Attribute("Value"))?.StartsWith("{Binding VideoCardPadding", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "VideoCardShell"
            && (string?)element.Attribute("Property") is "BorderBrush" or "BorderThickness");
        Assert.Equal("#78337DFF", (string?)selectionLayer.Attribute("BorderBrush"));
    }

    [Fact]
    public void VideoList_KeepsLegacyListAndUiXCardLayoutsBehindTheUiXSwitch()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement listStyle = document.Descendants()
            .First(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("BasedOn") == "{StaticResource VideoListBoxStyle}");
        XElement legacy = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "VideoCardLegacyLayout");
        XElement uiX = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "VideoCardUiXLayout");
        XElement groupPanel = document.Descendants()
            .Single(element => element.Name.LocalName == "ItemsPanelTemplate"
                && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "UiXVideoGroupPanelTemplate");
        XElement legacyListPanel = listStyle.Elements()
            .Single(element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "ItemsPanel")
            .Descendants()
            .Single(element => element.Name.LocalName == "ItemsPanelTemplate");
        XElement uiXListPanel = listStyle.Descendants()
            .Where(element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "ItemsPanel")
            .Skip(1)
            .Single()
            .Descendants()
            .Single(element => element.Name.LocalName == "ItemsPanelTemplate");

        Assert.Contains(legacyListPanel.Descendants(), element => element.Name.LocalName == "VirtualizingStackPanel"
            && (string?)element.Attribute("IsItemsHost") == "True");
        Assert.Contains(uiXListPanel.Descendants(), element => element.Name.LocalName == "VirtualizingWrapPanel"
            && (string?)element.Attribute("IsItemsHost") == "True"
            && ((string?)element.Attribute("ItemSize"))?.StartsWith("{Binding VideoCardItemSize", StringComparison.Ordinal) == true
            && (string?)element.Attribute("StretchItems") == "False");
        Assert.Contains(groupPanel.Descendants(), element => element.Name.LocalName == "VirtualizingStackPanel"
            && (string?)element.Attribute("Orientation") == "Vertical");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ControlTemplate"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "UiXVideoGroupTemplate");
        XElement videoList = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "VideoListBox");
        Assert.Equal("True", (string?)videoList.Attribute("VirtualizingPanel.IsVirtualizingWhenGrouping"));
        Assert.Contains(legacy.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Visibility"
            && (string?)element.Attribute("Value") == "Collapsed");
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Visibility"
            && (string?)element.Attribute("Value") == "Visible");
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "Border"
            && (string?)element.Attribute("Width") == "136"
            && (string?)element.Attribute("Height") == "91");
        XElement cover = uiX.Descendants()
            .Single(element => element.Name.LocalName == "Border"
                && (string?)element.Attribute("Width") == "136"
                && (string?)element.Attribute("Height") == "91");
        Assert.Equal("{StaticResource UiXNestedCornerRadius}", (string?)cover.Attribute("CornerRadius"));
        XElement statusRow = uiX.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "VideoCardStatusRow");
        Assert.Equal("3", (string?)statusRow.Attribute("Grid.Row"));
        Assert.Null(statusRow.Attribute("Margin"));
        Assert.DoesNotContain(uiX.Descendants(), element => element.Name.LocalName == "Button");
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute("Text") == "{Binding FileName}");
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute("Text") == "{Binding UiXStreamerText}");
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute("Text") == "{Binding RecordingTimeText}");
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute("Text") == "{Binding FileSizeText}");
        Assert.DoesNotContain(uiX.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute("Text") is "{Binding UiXSummaryText}" or "{Binding ResolutionChipText}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "Height"
            && ((string?)element.Attribute("Value"))?.StartsWith("{Binding VideoCardHeight", StringComparison.Ordinal) == true);
        Assert.Contains(uiX.Descendants(), element =>
            element.Name.LocalName == "Border"
            && (string?)element.Attribute("Width") == "136");
        Assert.DoesNotContain(uiX.Descendants(), element => element.Name.LocalName == "Run");
        Assert.DoesNotContain(uiX.Descendants(), element =>
            ((string?)element.Attribute("CornerRadius"))?.Contains("Binding", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void PreviewOpenAndClose_UseTheSameColumnAnimationWithoutPanelTranslation()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("RoomCardPanelTranslate", xaml);
        Assert.DoesNotContain("RoomDetailPanelTranslate", xaml);
        Assert.DoesNotContain("ApplyClosedHomePreviewLayout", source);
        Assert.DoesNotContain("AnimateClosedPreviewPanel", source);
        Assert.Contains("IsPreviewSurfaceVisible", xaml);
        Assert.Contains("SetVideoPresentationState(isSuspended, isPreviewClosingTransitionActive)", source);
        Assert.Contains("InterruptHomePreviewColumnAnimation()", source);
        Assert.Contains("UpdateHomePreviewLayout(true)", source);
        Assert.Equal(480, Emerde.Views.MainWindow.HomePreviewLayoutTransitionMilliseconds);
        Assert.Contains("homePreviewLayoutUpdateGeneration != layoutUpdateGeneration", source);
        Assert.Contains("previewPresentationUpdateGeneration == updateGeneration", source);
        Assert.Contains("!ViewModel.IsHomePageSelected && isPreviewClosingTransitionActive", source);
        Assert.Contains("CompletePreviewClosingTransition()", source);
        Assert.Contains("HomePreviewPanel.FirstFrameReady += HomePreviewPanelFirstFrameReady", source);
        Assert.Contains("CompletePreviewOpeningTransitionAfterTimeoutAsync", source);
        Assert.Contains("suspendRoomCardMetricsDuringPreviewTransition = true", source);
        Assert.Contains("UpdateRoomCardMetrics(targetRoomListWidth)", source);
    }

    [Fact]
    public void ConfigRestoreCards_DrawStateStrokesInsideTheSurface()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ConfigRestoreContentDialog.xaml"));
        XElement card = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "Card");
        XElement strokeLayer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "CardStrokeLayer");
        XElement surface = card.Elements().Single(element => element.Name.LocalName == "Grid");

        Assert.Equal("0", (string?)card.Attribute("Padding"));
        Assert.Equal("0", (string?)card.Attribute("BorderThickness"));
        Assert.Equal("1", (string?)strokeLayer.Attribute("BorderThickness"));
        Assert.Same(surface, strokeLayer.Parent);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "Card"
            && (string?)element.Attribute("Property") is "BorderBrush" or "BorderThickness");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "CardStrokeLayer"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource UiXSelectionStrokeBrush}");
    }

    [Fact]
    public void SettingsCards_UseBorderlessPanelSurfaces()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        foreach (string targetType in new[] { "{x:Type ui:Card}", "{x:Type ui:CardExpander}" })
        {
            XElement style = document.Descendants()
                .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == targetType);
            Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
                (string?)setter.Attribute("Property") == "Background"
                && (string?)setter.Attribute("Value") == "{DynamicResource EmerdePanelBrush}");
            Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
                (string?)setter.Attribute("Property") == "BorderThickness"
                && (string?)setter.Attribute("Value") == "0");
        }
    }

    [Fact]
    public void AddRoomDetectionSummary_DoesNotDrawAnInputAdjacentBorder()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml"));
        XElement summary = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomDetectionSummary");

        Assert.Equal("0", (string?)summary.Attribute("BorderThickness"));
    }

    [Fact]
    public void TooltipTraversal_OnlyUsesVisualTreeForVisualObjects()
    {
        Assert.False(Emerde.Views.MainWindow.CanEnumerateVisualChildren(new System.Windows.Controls.RowDefinition()));
        Assert.True(Emerde.Views.MainWindow.CanEnumerateVisualChildren(new System.Windows.Media.DrawingVisual()));
    }

    [Theory]
    [InlineData("AddRoomContentDialog.xaml")]
    [InlineData("AutoShutdownContentDialog.xaml")]
    [InlineData("ExitConfirmationContentDialog.xaml")]
    [InlineData("ConfigRestoreContentDialog.xaml")]
    [InlineData("LocalSettingsContentDialog.xaml")]
    [InlineData("UpdateReleaseNotesContentDialog.xaml")]
    [InlineData("TrayMenuWindow.xaml")]
    public void DialogAndPageSurfaces_UseEntranceMotion(string fileName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));

        Assert.Contains(document.Descendants(), element => HasMotionAttribute(element, "IsEntranceEnabled"));
    }

    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static void AssertExtensionInputStyleUsesStateTriggers(XElement style)
    {
        Assert.DoesNotContain(style.Descendants(), element =>
            element.Name.LocalName == "StaticResource"
            && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == "ControlStrokeColorDefaultBrush");
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == "{DynamicResource EmerdeExtensionInputBorderBrush}");

        XElement[] triggers = style.Descendants()
            .Where(element => element.Name.LocalName == "Trigger")
            .ToArray();
        Assert.Contains(triggers, trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver"
            && (string?)trigger.Attribute("Value") == "True"
            && HasBorderBrushSetter(trigger, "{DynamicResource EmerdeExtensionInputBorderBrush}"));
        Assert.Contains(triggers, trigger =>
            (string?)trigger.Attribute("Property") == "IsKeyboardFocusWithin"
            && (string?)trigger.Attribute("Value") == "True"
            && HasBorderBrushSetter(trigger, "{DynamicResource SystemAccentColorPrimaryBrush}"));
    }

    private static bool HasBorderBrushSetter(XElement trigger, string value)
    {
        return trigger.Elements().Any(element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "BorderBrush"
            && (string?)element.Attribute("Value") == value);
    }

    private static bool IsPressMotionSetter(XElement setter)
    {
        return (string?)setter.Attribute("Property") == "controls:MotionAssist.IsPressEnabled"
            && (string?)setter.Attribute("Value") == "True";
    }

    private static bool HasMotionAttribute(XElement element, string propertyName)
    {
        return element.Attributes().Any(attribute => attribute.Name.ToString().EndsWith(propertyName, StringComparison.Ordinal) && attribute.Value == "True");
    }

    private static bool HasAttribute(XElement element, string propertyName)
    {
        return element.Attributes().Any(attribute => attribute.Name.ToString().EndsWith(propertyName, StringComparison.Ordinal));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private sealed class ExplicitBindingTarget(Func<string> getter, Action<string> setter)
    {
        public string Value
        {
            get => getter();
            set => setter(value);
        }
    }

    private static XElement FindMenuItem(XDocument document, string header)
    {
        return document.Descendants()
            .Single(element => element.Name.LocalName == "MenuItem" && (string?)element.Attribute("Header") == header);
    }
}
