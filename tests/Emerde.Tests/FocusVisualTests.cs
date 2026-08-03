using Emerde.Controls;
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
            "private bool TryBeginManualRefresh()");
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
        Assert.Contains("state.EntranceOperation?.Abort()", source);
        Assert.Contains("ResetEntrance(element, state)", source);
        Assert.Contains("ResetInteractionScale(element)", source);
        Assert.Contains("state.PulseScale.BeginAnimation", source);
        Assert.Contains("element.Unloaded += SpinUnloaded", source);
        Assert.Contains("element.Unloaded += MotionElementUnloaded", source);
        Assert.Contains("EntranceAnimationGeneration", source);
        Assert.Contains("PulseAnimationGeneration", source);
        Assert.Contains("ResetPulse(state)", source);
        Assert.Contains("scaleYAnimation.Completed", source);
        Assert.DoesNotContain("AnimateInteractionScale", source);
        Assert.Contains("ReadLocalValue(FrameworkElement.RenderTransformOriginProperty)", source);
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
        Assert.Contains("dialog.Resources[DialogMinWidthResource] = targetWidth", source);
        Assert.Contains("dialog.Resources[DialogMaxWidthResource] = targetWidth", source);
        Assert.Contains("dialog.Resources[DialogMinHeightResource] = targetHeight", source);
        Assert.Contains("dialog.Resources[DialogMaxHeightResource] = targetHeight", source);

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
        Assert.Equal("20,0,22,14", (string?)itemsPresenter.Attribute("Margin"));
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

        Assert.Equal("0,0,22,14", (string?)toolbar.Attribute("Margin"));
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
        Assert.Contains("RoomUrlTextBox.IsKeyboardFocusWithin", source);
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
        Assert.Equal("{StaticResource DefaultVioletaContentDialogStyle}", (string?)dialog.Attribute("Style"));
        Assert.Equal("Primary", (string?)dialog.Attribute("DefaultButton"));
        Assert.Equal("是", (string?)dialog.Attribute("PrimaryButtonText"));
        Assert.Equal("否", (string?)dialog.Attribute("CloseButtonText"));
    }

    [Theory]
    [InlineData("SettingsWindow.xaml", "SettingsScrollViewer")]
    [InlineData("LocalSettingsContentDialog.xaml", "LocalSettingsScrollViewer")]
    public void SettingsScrollViewers_DoNotRenderWindowSwitchFocusOutline(string fileName, string scrollViewerName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));
        XElement scrollViewer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == scrollViewerName);

        Assert.Equal("False", (string?)scrollViewer.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)scrollViewer.Attribute("FocusVisualStyle"));
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
        foreach (string elementName in new[] { "MainContentRoot", "ShellNavigationPanel", "HomePageRoot" })
        {
            XElement element = document.Descendants()
                .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == elementName);
            Assert.True(HasMotionAttribute(element, "IsEntranceEnabled"));
        }
    }

    [Theory]
    [InlineData("HomePageRoot")]
    [InlineData("RoomCardShell")]
    public void HomePageSurfaces_ReplayEntranceMotionWhenPageBecomesVisible(string elementName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement element = document.Descendants()
            .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == elementName);

        Assert.True(HasAttribute(element, "EntranceTrigger"));
    }

    [Theory]
    [InlineData("VideoListContentRoot")]
    [InlineData("VideoCardShell")]
    public void VideoListSurfaces_ReplayEntranceMotionWhenPageBecomesVisible(string elementName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement element = document.Descendants()
            .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == elementName);

        Assert.True(HasAttribute(element, "EntranceTrigger"));
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
    [InlineData("ScreenRecordListWindow.xaml")]
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

    [Theory]
    [InlineData("VideoListPage", "DataContext.IsVideoListPageSelected")]
    [InlineData("SettingsPage", "DataContext.IsSettingsPageSelected")]
    [InlineData("ExtensionsPage", "DataContext.IsExtensionsPageSelected")]
    [InlineData("AboutPage", "DataContext.IsAboutPageSelected")]
    public void MainChildPages_BindEntranceMotionToMainPageState(string elementName, string expectedPath)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement element = document.Descendants()
            .Single(item => (string?)item.Attribute(XName.Get("Name", XamlNamespace)) == elementName);

        Assert.Contains(element.Attributes(), attribute =>
            attribute.Name.ToString().EndsWith("EntranceTrigger", StringComparison.Ordinal)
            && attribute.Value == $"{{Binding {expectedPath}, ElementName=ShellPageHost}}");
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
        Assert.Equal("{Binding RoomCardPadding, RelativeSource={RelativeSource AncestorType={x:Type views:MainWindow}}}", (string?)content.Attribute("Padding"));
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
        Assert.Equal("14", (string?)content.Attribute("Padding"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("TargetName") == "VideoCardShell"
            && (string?)element.Attribute("Property") is "BorderBrush" or "BorderThickness");
        Assert.Equal("#78337DFF", (string?)selectionLayer.Attribute("BorderBrush"));
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
        Assert.Contains("homePreviewLayoutUpdateGeneration != layoutUpdateGeneration", source);
        Assert.Contains("previewPresentationUpdateGeneration == updateGeneration", source);
        Assert.Contains("!ViewModel.IsHomePageSelected && isPreviewClosingTransitionActive", source);
        Assert.Contains("CompletePreviewClosingTransition()", source);
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
            && (string?)element.Attribute("Value") == "#60337DFF");
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
    [InlineData("ConfigRestoreWindow.xaml")]
    [InlineData("LoadingWindow.xaml")]
    [InlineData("LocalSettingsContentDialog.xaml")]
    [InlineData("SettingsWindow.xaml")]
    [InlineData("AboutContentDialog.xaml")]
    [InlineData("ScreenRecordListWindow.xaml")]
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
}
