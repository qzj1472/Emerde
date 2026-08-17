using Emerde.Controls;
using Emerde.Core;
using Emerde.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using AppResources = Emerde.Properties.Resources;
using WpfBinding = System.Windows.Data.Binding;
using WpfPoint = System.Windows.Point;
using WpfPanel = System.Windows.Controls.Panel;

namespace Emerde.Views;

public partial class SettingsWindow : System.Windows.Controls.UserControl
{
    private const int InitialSettingsElementCount = 4;
    private const double SettingsUiXTwoColumnEnterWidth = 1000d;
    private const double SettingsUiXTwoColumnExitWidth = 940d;
    private const double SettingsUiXSaveMetadataOneRowEnterWidth = 712d;
    private const double SettingsUiXSaveMetadataOneRowExitWidth = 688d;
    private const double SettingsUiXGroupSpacing = 16d;
    private const double SettingsUiXChildIndent = 52d;

    private static readonly BooleanToVisibilityConverter SettingsUiXVisibilityConverter = new();
    private static readonly Dictionary<int, string> SettingsUiXGroupTitleKeys = new()
    {
        [0] = nameof(AppResources.AppearanceSettings),
        [1] = nameof(AppResources.FilesAndData),
        [2] = nameof(AppResources.LiveNotification),
        [3] = nameof(AppResources.RecordSettings),
        [4] = nameof(AppResources.AutomaticMonitoring),
        [5] = nameof(AppResources.SaveSettings),
        [6] = nameof(AppResources.LongRunningSettings),
        [7] = nameof(AppResources.NetworkSettings)
    };

    private readonly Dictionary<CardExpander, object?> deferredCardExpanderContents = [];
    private readonly Queue<UIElement> deferredStartupSections = [];
    private readonly Dictionary<CardExpander, bool> initialCardExpanderStates = [];
    private readonly Dictionary<CardExpander, object?> settingsUiXDependentExpanderContents = [];
    private readonly Dictionary<FrameworkElement, Thickness> settingsUiXOriginalMargins = [];
    private readonly Dictionary<FrameworkElement, Dictionary<DependencyProperty, object>> settingsSectionPresentations = [];
    private readonly Dictionary<int, (System.Windows.Controls.Border Container, System.Windows.Controls.StackPanel GroupPanel, System.Windows.Controls.StackPanel RowsPanel)> settingsUiXGroups = [];
    private readonly Dictionary<int, System.Windows.Controls.TextBlock> settingsUiXGroupTitles = [];
    private readonly Stopwatch startupRestoreStopwatch = new();
    private List<UIElement> settingsSectionOrder = [];
    private long maxStartupRestoreBatchMilliseconds;
    private int startupRestoreBatchCount;
    private bool startupSectionsQueued;
    private bool? isSettingsUiXTwoColumnApplied;
    private bool? isSettingsUiXSaveMetadataOneRowApplied;
    private bool? isSettingsUiXSaveMetadataModeApplied;
    private double? pendingSettingsScrollOffset;
    private int settingsScrollRestoreVersion;
    private int selectedSettingsFocus;
    private DispatcherOperation? settingsFocusIndicatorUpdateOperation;
    private DispatcherOperation? settingsResponsiveLayoutUpdateOperation;
    private bool pendingSettingsFocusIndicatorAnimation;

    public SettingsViewModel ViewModel { get; }

    public SettingsWindow()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        DataContext = ViewModel = new();
        long viewModelElapsed = stopwatch.ElapsedMilliseconds;
        ViewModel.OwnerWindow = Application.Current.MainWindow;
        InitializeComponent();
        settingsSectionOrder = SettingsStackPanel.Children.OfType<UIElement>().ToList();
        foreach (FrameworkElement section in settingsSectionOrder.OfType<FrameworkElement>())
        {
            CaptureSettingsSectionPresentation(section);
        }
        foreach (CardExpander expander in FindLogicalDescendants<CardExpander>(SettingsLayoutHost))
        {
            initialCardExpanderStates[expander] = expander.IsExpanded;
        }
        ViewModel.PropertyChanged += SettingsViewModelPropertyChanged;
        SizeChanged += SettingsDialogSizeChanged;
        SettingsFocusNavigationPanel.SizeChanged += SettingsFocusNavigationPanelSizeChanged;
        SaveMetadataLayout.SizeChanged += SaveMetadataLayoutSizeChanged;
        ApplySettingsLayoutMode();
        long initializeElapsed = stopwatch.ElapsedMilliseconds;
        int deferredCount = DeferCollapsedCardExpanderContent(SettingsContentRoot);
        int deferredSectionCount = ViewModel.IsUiXEnabled ? 0 : DeferStartupSections();
        Loaded += SettingsDialogLoaded;
        IsVisibleChanged += SettingsDialogIsVisibleChanged;
        Unloaded += SettingsDialogUnloaded;
        AppSessionLogger.Write(
            $"perf SettingsDialog ctor vm={viewModelElapsed} ms init={initializeElapsed - viewModelElapsed} ms defer={stopwatch.ElapsedMilliseconds - initializeElapsed} ms deferredCards={deferredCount} deferredSections={deferredSectionCount} total={stopwatch.ElapsedMilliseconds} ms");
    }

    private void SettingsDialogLoaded(object sender, RoutedEventArgs e)
    {
        QueueStartupSectionRestore();
        QueueSettingsFocusIndicatorUpdate(false);
    }

    private void SettingsDialogIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            QueueStartupSectionRestore();
        }
    }

    private void SettingsDialogUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SettingsDialogLoaded;
        IsVisibleChanged -= SettingsDialogIsVisibleChanged;
        Unloaded -= SettingsDialogUnloaded;
        ViewModel.PropertyChanged -= SettingsViewModelPropertyChanged;
        SizeChanged -= SettingsDialogSizeChanged;
        SettingsFocusNavigationPanel.SizeChanged -= SettingsFocusNavigationPanelSizeChanged;
        SaveMetadataLayout.SizeChanged -= SaveMetadataLayoutSizeChanged;
        settingsFocusIndicatorUpdateOperation?.Abort();
        settingsFocusIndicatorUpdateOperation = null;
        settingsResponsiveLayoutUpdateOperation?.Abort();
        settingsResponsiveLayoutUpdateOperation = null;
        pendingSettingsFocusIndicatorAnimation = false;

        foreach (CardExpander expander in deferredCardExpanderContents.Keys.ToArray())
        {
            DependencyPropertyDescriptor
                .FromProperty(CardExpander.IsExpandedProperty, typeof(CardExpander))
                ?.RemoveValueChanged(expander, CardExpanderIsExpandedChanged);
        }

        RestoreAllSettingsUiXDependentExpanderContents();
        RestoreSettingsUiXControlMargins();
        CancelPendingSettingsScrollRestore();
        deferredCardExpanderContents.Clear();
        settingsUiXDependentExpanderContents.Clear();
        deferredStartupSections.Clear();
    }

    private void SettingsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsUiXEnabled))
        {
            RestoreAllDeferredStartupSections();
            ApplySettingsLayoutMode();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.LanguageIndex))
        {
            UpdateSettingsUiXGroupTitles();
        }
        else if (e.PropertyName is nameof(SettingsViewModel.IsToNotify)
            or nameof(SettingsViewModel.IsToSegment)
            or nameof(SettingsViewModel.IsDataRetentionEnabled)
            or nameof(SettingsViewModel.IsUseAutoShutdown)
            or nameof(SettingsViewModel.IsUseProxy))
        {
            ApplySettingsDependentVisibilityMode();
        }
    }

    private void SettingsDialogSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && ViewModel.IsUiXEnabled)
        {
            QueueSettingsResponsiveLayoutUpdate();
        }
    }

    private void SettingsFocusNavigationPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ViewModel.IsUiXEnabled && e.HeightChanged)
        {
            QueueSettingsFocusIndicatorUpdate(false);
        }
    }

    private void SaveMetadataLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ViewModel.IsUiXEnabled && e.WidthChanged)
        {
            QueueSettingsResponsiveLayoutUpdate();
        }
    }

    private void QueueSettingsResponsiveLayoutUpdate()
    {
        if (settingsResponsiveLayoutUpdateOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        settingsResponsiveLayoutUpdateOperation = Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (!IsLoaded || !ViewModel.IsUiXEnabled)
                {
                    return;
                }
                UpdateSettingsUiXItemWidths();
                ApplySettingsUiXSaveMetadataLayout();
            }
            finally
            {
                settingsResponsiveLayoutUpdateOperation = null;
            }
        }, DispatcherPriority.Render);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        if (ViewModel.IsUiXEnabled)
        {
            CommitFocusedEditor(source);
        }

        if (IsInteractiveElement(source))
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        CardExpander? expander = FindVisualAncestor<CardExpander>(source);
        if (expander == null || !IsPointInsideHeader(expander, e.GetPosition(expander)))
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        expander.IsExpanded = !expander.IsExpanded;
        e.Handled = true;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private void CommitFocusedEditor(DependencyObject clickedSource)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused
            || IsVisualAncestorOf(focused, clickedSource))
        {
            return;
        }

        if (focused is System.Windows.Controls.TextBox textBox)
        {
            textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }

        if (!IsInteractiveElement(clickedSource))
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, null);
        }
    }

    private static bool IsVisualAncestorOf(DependencyObject ancestor, DependencyObject source)
    {
        for (DependencyObject? current = source; current != null; current = GetVisualParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInsideHeader(CardExpander expander, WpfPoint point)
    {
        if (expander.Template.FindName("HeaderChrome", expander) is not FrameworkElement header)
        {
            return point.Y >= 0 && point.Y <= 62;
        }

        WpfPoint topLeft = header.TranslatePoint(new WpfPoint(0, 0), expander);
        return point.X >= topLeft.X
            && point.X <= topLeft.X + header.ActualWidth
            && point.Y >= topLeft.Y
            && point.Y <= topLeft.Y + header.ActualHeight;
    }

    private static bool IsInteractiveElement(DependencyObject source)
    {
        for (DependencyObject? current = source; current != null; current = GetVisualParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.Primitives.Selector
                or System.Windows.Controls.Slider
                or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Primitives.Thumb
                or System.Windows.Controls.Primitives.ToggleButton
                or Wpf.Ui.Controls.TextBox
                or Wpf.Ui.Controls.NumberBox
                or Wpf.Ui.Controls.ToggleSwitch)
            {
                return true;
            }

            if (current is CompactNumberBox)
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindVisualAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (DependencyObject? current = source; current != null; current = GetVisualParent(current))
        {
            if (current is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static DependencyObject? GetVisualParent(DependencyObject source)
    {
        return source is Visual or Visual3D ? VisualTreeHelper.GetParent(source) : null;
    }

    private int DeferStartupSections()
    {
        List<UIElement> sections = GetAttachedSettingsSections()
            .Skip(InitialSettingsElementCount)
            .ToList();

        foreach (UIElement section in sections)
        {
            RemoveSettingsSectionFromParent(section);
            deferredStartupSections.Enqueue(section);
        }

        return sections.Count;
    }

    private void QueueStartupSectionRestore()
    {
        if (startupSectionsQueued || deferredStartupSections.Count == 0)
        {
            return;
        }

        startupSectionsQueued = true;
        startupRestoreStopwatch.Restart();
        maxStartupRestoreBatchMilliseconds = 0;
        startupRestoreBatchCount = 0;
        _ = Dispatcher.BeginInvoke(RestoreNextStartupSection, DispatcherPriority.ContextIdle);
    }

    private void RestoreNextStartupSection()
    {
        if (!IsVisible)
        {
            startupSectionsQueued = false;
            return;
        }

        Stopwatch batchStopwatch = Stopwatch.StartNew();
        int restored = 0;
        while (restored < 6
            && batchStopwatch.ElapsedMilliseconds < 4
            && deferredStartupSections.TryDequeue(out UIElement? section))
        {
            AddSettingsSection(section);
            restored++;
        }

        batchStopwatch.Stop();
        startupRestoreBatchCount++;
        maxStartupRestoreBatchMilliseconds = Math.Max(maxStartupRestoreBatchMilliseconds, batchStopwatch.ElapsedMilliseconds);

        if (deferredStartupSections.Count > 0)
        {
            _ = Dispatcher.BeginInvoke(RestoreNextStartupSection, DispatcherPriority.Background);
            return;
        }

        AppSessionLogger.Write(
            $"perf SettingsDialog deferred sections restored in {startupRestoreStopwatch.ElapsedMilliseconds} ms batches={startupRestoreBatchCount} maxBatch={maxStartupRestoreBatchMilliseconds} ms");
    }

    private void ApplySettingsLayoutMode()
    {
        IReadOnlyList<UIElement> orderedSections = GetSettingsLayoutOrder();

        foreach (UIElement section in settingsSectionOrder)
        {
            RemoveSettingsSectionFromParent(section);
        }
        ClearSettingsUiXLayout();

        foreach (UIElement section in orderedSections)
        {
            AddSettingsSection(section);
        }

        SettingsStackPanel.Visibility = ViewModel.IsUiXEnabled ? Visibility.Collapsed : Visibility.Visible;
        SettingsUiXPanel.Visibility = ViewModel.IsUiXEnabled ? Visibility.Visible : Visibility.Collapsed;
        ApplyCardExpanderLayoutMode();
        ApplySettingsDependentVisibilityMode();
        UpdateSettingsUiXItemWidths(preserveScrollOffset: false);
    }

    private IReadOnlyList<UIElement> GetSettingsLayoutOrder()
    {
        if (!ViewModel.IsUiXEnabled)
        {
            return settingsSectionOrder;
        }

        IEnumerable<UIElement> sections = (selectedSettingsFocus == 0
            ? settingsSectionOrder
            : settingsSectionOrder.Where(IsSettingsSectionInSelectedFocus))
            .Where(section => !ReferenceEquals(section, PreviewSettingsCard)
                && !ReferenceEquals(section, UserAgentExpander));
        if (!settingsSectionOrder.Contains(CookieSettingsExpander) || selectedSettingsFocus != 0)
        {
            return sections.ToArray();
        }

        return sections
            .Where(section => !ReferenceEquals(section, CookieSettingsExpander))
            .Append(CookieSettingsExpander)
            .ToArray();
    }

    private bool IsSettingsSectionInSelectedFocus(UIElement section)
    {
        return selectedSettingsFocus switch
        {
            1 => GetSettingsUiXGroupIndex(section) == 0
                && !ReferenceEquals(section, CookieSettingsExpander),
            2 => ReferenceEquals(section, LiveNotificationExpander)
                || ReferenceEquals(section, EnableRecordCard)
                || ReferenceEquals(section, AutomaticMonitoringCard)
                || ReferenceEquals(section, RoutineIntervalCard)
                || ReferenceEquals(section, MonitoringSchedulePresetCard)
                || ReferenceEquals(section, MonitoringScheduleCustomExpander),
            3 => ReferenceEquals(section, QualitySettingsCard)
                || ReferenceEquals(section, RecordFormatExpander)
                || ReferenceEquals(section, SegmentExpander),
            4 => GetSettingsUiXGroupIndex(section) is 1 or 5,
            5 => GetSettingsUiXGroupIndex(section) == 6,
            6 => GetSettingsUiXGroupIndex(section) == 7
                || ReferenceEquals(section, CookieSettingsExpander),
            _ => true,
        };
    }

    private void SettingsFocusButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton { CommandParameter: string value }
            || !int.TryParse(value, out int focus)
            || focus == selectedSettingsFocus)
        {
            return;
        }

        if (ViewModel.IsUiXEnabled)
        {
            MotionAssist.PrepareEntrance(SettingsUiXColumns);
            MotionAssist.PrepareEntrance(SettingsUiXBottomPanel);
        }
        selectedSettingsFocus = Math.Clamp(focus, 0, 6);
        isSettingsUiXTwoColumnApplied = null;
        ApplySettingsLayoutMode();
        SettingsScrollViewer.ScrollToTop();
        MoveSettingsFocusIndicator((System.Windows.Controls.RadioButton)sender, true);
        if (ViewModel.IsUiXEnabled)
        {
            _ = Dispatcher.BeginInvoke(
                () =>
                {
                    MotionAssist.PlayEntrance(SettingsUiXColumns);
                    MotionAssist.PlayEntrance(SettingsUiXBottomPanel);
                },
                DispatcherPriority.DataBind);
        }
    }

    private void QueueSettingsFocusIndicatorUpdate(bool animate)
    {
        pendingSettingsFocusIndicatorAnimation |= animate;
        if (settingsFocusIndicatorUpdateOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        settingsFocusIndicatorUpdateOperation = Dispatcher.BeginInvoke(() =>
        {
            bool shouldAnimate = pendingSettingsFocusIndicatorAnimation;
            pendingSettingsFocusIndicatorAnimation = false;
            settingsFocusIndicatorUpdateOperation = null;
            if (!IsLoaded)
            {
                return;
            }

            System.Windows.Controls.RadioButton? selectedButton = SettingsFocusNavigationPanel.Children
                .OfType<System.Windows.Controls.RadioButton>()
                .FirstOrDefault(button => button.CommandParameter is string value
                    && int.TryParse(value, out int focus)
                    && focus == selectedSettingsFocus);
            if (selectedButton != null)
            {
                MoveSettingsFocusIndicator(selectedButton, shouldAnimate);
            }
        }, DispatcherPriority.Render);
    }

    private void MoveSettingsFocusIndicator(System.Windows.Controls.RadioButton button, bool animate)
    {
        if (!ViewModel.IsUiXEnabled || !button.IsLoaded || button.ActualWidth <= 0d)
        {
            return;
        }

        WpfPoint position = button.TransformToAncestor(SettingsFocusNavigationRoot).Transform(new WpfPoint(0d, 0d));
        double targetX = WindowSizing.RoundLayoutValue(position.X + (button.ActualWidth - SettingsFocusSelectionIndicator.Width) / 2d);
        double targetY = WindowSizing.RoundLayoutValue(position.Y + button.ActualHeight - 5d);
        MotionAssist.MoveNavigationIndicator(SettingsFocusSelectionIndicator, targetX, targetY, animate);
    }

    private void ApplyCardExpanderLayoutMode()
    {
        foreach (CardExpander expander in initialCardExpanderStates.Keys)
        {
            if (ViewModel.IsUiXEnabled && !ReferenceEquals(expander, CookieSettingsExpander))
            {
                expander.IsExpanded = true;
            }
            else
            {
                expander.IsExpanded = initialCardExpanderStates.TryGetValue(expander, out bool isExpanded) && isExpanded;
            }
        }
    }

    private void UpdateSettingsUiXItemWidths(bool preserveScrollOffset = true)
    {
        if (!preserveScrollOffset)
        {
            CancelPendingSettingsScrollRestore();
        }

        if (!ViewModel.IsUiXEnabled)
        {
            RestoreSettingsUiXControlMargins();
            isSettingsUiXTwoColumnApplied = null;
            ApplySettingsUiXSaveMetadataLayout();
            return;
        }

        double availableWidth = Math.Max(0, SettingsLayoutHost.ActualWidth);
        if (availableWidth <= 0)
        {
            return;
        }

        bool useTwoColumns = ShouldUseSettingsUiXTwoColumns();
        bool layoutChanged = isSettingsUiXTwoColumnApplied != useTwoColumns;
        double verticalOffset = pendingSettingsScrollOffset ?? SettingsScrollViewer.VerticalOffset;
        isSettingsUiXTwoColumnApplied = useTwoColumns;

        if (!layoutChanged)
        {
            return;
        }

        ApplySettingsUiXSaveMetadataLayout();
        SettingsUiXGapColumnDefinition.Width = useTwoColumns ? new GridLength(16) : new GridLength(0);
        SettingsUiXRightColumnDefinition.Width = useTwoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SettingsUiXRightColumn.Visibility = useTwoColumns ? Visibility.Visible : Visibility.Collapsed;
        MoveSettingsUiXGroupContainers();
        ApplySettingsUiXControlAlignment();
        if (preserveScrollOffset)
        {
            RestoreSettingsScrollOffset(verticalOffset);
        }
    }

    private void RestoreAllDeferredStartupSections()
    {
        while (deferredStartupSections.TryDequeue(out UIElement? section))
        {
            AddSettingsSection(section);
        }
        startupSectionsQueued = false;
    }

    private IReadOnlyList<UIElement> GetAttachedSettingsSections()
    {
        return settingsSectionOrder
            .Where(section => section is FrameworkElement { Parent: WpfPanel })
            .ToList();
    }

    private void MoveSettingsUiXGroupContainers()
    {
        foreach ((int groupIndex, (System.Windows.Controls.Border container, _, _)) in settingsUiXGroups.OrderBy(pair => pair.Key))
        {
            WpfPanel targetColumn = GetSettingsUiXGroupColumn(groupIndex);
            if (ReferenceEquals(container.Parent, targetColumn))
            {
                continue;
            }

            if (container.Parent is WpfPanel currentColumn)
            {
                currentColumn.Children.Remove(container);
            }
            targetColumn.Children.Add(container);
        }
    }

    private void AddSettingsSection(UIElement section)
    {
        RemoveSettingsSectionFromParent(section);

        if (!ViewModel.IsUiXEnabled)
        {
            SettingsStackPanel.Children.Add(section);
            ApplySettingsSectionChrome(section);
            return;
        }

        AddSettingsSectionToUiX(section);
        ApplySettingsSectionChrome(section);
    }

    private void AddSettingsSectionToUiX(UIElement section)
    {
        if (ReferenceEquals(section, CookieSettingsExpander))
        {
            SettingsUiXBottomPanel.Children.Add(section);
            return;
        }

        if (section is FrameworkElement { Height: 10 })
        {
            return;
        }

        if (section is System.Windows.Controls.TextBlock)
        {
            return;
        }

        if (ReferenceEquals(section, PlatformAccessCard))
        {
            return;
        }

        int groupIndex = GetSettingsUiXGroupIndex(section);
        (_, _, System.Windows.Controls.StackPanel rowsPanel) = GetOrCreateSettingsUiXGroup(groupIndex);
        rowsPanel.Children.Add(section);
    }

    private void ApplySettingsUiXSaveMetadataLayout()
    {
        bool isUiXEnabled = ViewModel.IsUiXEnabled;
        double availableWidth = SaveMetadataLayout.ActualWidth > 0d
            ? SaveMetadataLayout.ActualWidth
            : Math.Max(0d, SettingsLayoutHost.ActualWidth - 140d);
        bool keepOnOneRow = isUiXEnabled
            && ResolveSettingsUiXSaveMetadataOneRowState(
                availableWidth,
                isSettingsUiXSaveMetadataModeApplied == true ? isSettingsUiXSaveMetadataOneRowApplied : null);
        bool layoutChanged = isSettingsUiXSaveMetadataModeApplied != isUiXEnabled
            || isSettingsUiXSaveMetadataOneRowApplied != keepOnOneRow;
        isSettingsUiXSaveMetadataModeApplied = isUiXEnabled;
        isSettingsUiXSaveMetadataOneRowApplied = keepOnOneRow;

        double selectorWidth = isUiXEnabled
            ? ShouldUseSettingsUiXTwoColumns() ? 148d : 168d
            : 220d;
        if (Math.Abs(SavePathLevelSelector.Width - selectorWidth) > 0.1d)
        {
            SavePathLevelSelector.Width = selectorWidth;
        }

        if (!layoutChanged)
        {
            return;
        }

        DataRetentionControls.Children.Clear();
        if (!isUiXEnabled)
        {
            DataRetentionSwitch.Margin = new Thickness(10, 0, 0, 0);
            DataRetentionValueInput.Margin = new Thickness(10, 0, 0, 0);
            DataRetentionUnitSelector.Margin = new Thickness(8, 0, 0, 0);
            DataRetentionControls.Children.Add(DataRetentionSwitch);
            DataRetentionControls.Children.Add(DataRetentionValueInput);
            DataRetentionControls.Children.Add(DataRetentionUnitSelector);
            SavePathLevelPanel.SetValue(System.Windows.Controls.Grid.RowProperty, 0);
            SavePathLevelPanel.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);
            SavePathLevelPanel.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, 3);
            DataRetentionPanel.SetValue(System.Windows.Controls.Grid.RowProperty, 1);
            DataRetentionPanel.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);
            DataRetentionPanel.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, 3);
            DataRetentionPanel.Margin = new Thickness(0, 16, 0, 0);
            return;
        }

        DataRetentionValueInput.Margin = new Thickness(0);
        DataRetentionUnitSelector.Margin = new Thickness(8, 0, 0, 0);
        DataRetentionSwitch.Margin = new Thickness(10, 0, 0, 0);
        DataRetentionControls.Children.Add(DataRetentionValueInput);
        DataRetentionControls.Children.Add(DataRetentionUnitSelector);
        DataRetentionControls.Children.Add(DataRetentionSwitch);
        SavePathLevelPanel.SetValue(System.Windows.Controls.Grid.RowProperty, 0);
        SavePathLevelPanel.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);
        SavePathLevelPanel.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, keepOnOneRow ? 1 : 3);
        DataRetentionPanel.SetValue(System.Windows.Controls.Grid.RowProperty, keepOnOneRow ? 0 : 1);
        DataRetentionPanel.SetValue(System.Windows.Controls.Grid.ColumnProperty, keepOnOneRow ? 2 : 0);
        DataRetentionPanel.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, keepOnOneRow ? 1 : 3);
        DataRetentionPanel.Margin = keepOnOneRow ? new Thickness(0) : new Thickness(0, 16, 0, 0);
    }

    internal static bool ResolveSettingsUiXSaveMetadataOneRowState(double availableWidth, bool? currentState)
    {
        double threshold = currentState == true
            ? SettingsUiXSaveMetadataOneRowExitWidth
            : SettingsUiXSaveMetadataOneRowEnterWidth;
        return availableWidth >= threshold;
    }

    private (System.Windows.Controls.Border Container, System.Windows.Controls.StackPanel GroupPanel, System.Windows.Controls.StackPanel RowsPanel) GetOrCreateSettingsUiXGroup(int groupIndex)
    {
        if (settingsUiXGroups.TryGetValue(groupIndex, out (System.Windows.Controls.Border Container, System.Windows.Controls.StackPanel GroupPanel, System.Windows.Controls.StackPanel RowsPanel) group))
        {
            return group;
        }

        System.Windows.Controls.Border container = new();
        container.SetResourceReference(StyleProperty, "UiXSettingsGroupBorderStyle");
        container.Margin = new Thickness(0, 0, 0, SettingsUiXGroupSpacing);

        System.Windows.Controls.StackPanel groupPanel = new();
        System.Windows.Controls.StackPanel rowsPanel = new();
        System.Windows.Controls.TextBlock groupTitle = new()
        {
            Margin = new Thickness(2, 1, 2, 8),
            Text = GetSettingsUiXGroupTitle(groupIndex)
        };
        groupTitle.SetResourceReference(StyleProperty, "UiXSectionTitleTextStyle");
        settingsUiXGroupTitles[groupIndex] = groupTitle;
        groupPanel.Children.Add(groupTitle);
        groupPanel.Children.Add(rowsPanel);
        container.Child = groupPanel;

        GetSettingsUiXGroupColumn(groupIndex).Children.Add(container);
        group = (container, groupPanel, rowsPanel);
        settingsUiXGroups[groupIndex] = group;
        return group;
    }

    private WpfPanel GetSettingsUiXGroupColumn(int groupIndex)
    {
        if (!ShouldUseSettingsUiXTwoColumns())
        {
            return SettingsUiXLeftColumn;
        }

        return groupIndex is 0 or 1 or 2 or 3
            ? SettingsUiXLeftColumn
            : SettingsUiXRightColumn;
    }

    private bool ShouldUseSettingsUiXTwoColumns()
    {
        return ViewModel.IsUiXEnabled
            && selectedSettingsFocus == 0
            && ResolveSettingsUiXTwoColumnState(SettingsLayoutHost.ActualWidth, isSettingsUiXTwoColumnApplied);
    }

    internal static bool ResolveSettingsUiXTwoColumnState(double availableWidth, bool? currentState)
    {
        double threshold = currentState == true
            ? SettingsUiXTwoColumnExitWidth
            : SettingsUiXTwoColumnEnterWidth;
        return availableWidth >= threshold;
    }

    private void RestoreSettingsScrollOffset(double verticalOffset)
    {
        pendingSettingsScrollOffset = verticalOffset;
        int restoreVersion = ++settingsScrollRestoreVersion;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (restoreVersion != settingsScrollRestoreVersion || pendingSettingsScrollOffset is not double offset)
            {
                return;
            }

            pendingSettingsScrollOffset = null;
            double boundedOffset = Math.Min(offset, SettingsScrollViewer.ScrollableHeight);
            SettingsScrollViewer.ScrollToVerticalOffset(boundedOffset);
        }, DispatcherPriority.Loaded);
    }

    private void CancelPendingSettingsScrollRestore()
    {
        settingsScrollRestoreVersion++;
        pendingSettingsScrollOffset = null;
    }

    private int GetSettingsUiXGroupIndex(UIElement section)
    {
        return section switch
        {
            _ when ReferenceEquals(section, LanguageSettingsCard)
                || ReferenceEquals(section, ThemeSettingsCard)
                || ReferenceEquals(section, TraySettingsCard)
                || ReferenceEquals(section, UiXSettingsCard)
                || ReferenceEquals(section, ShortcutSettingsCard) => 0,
            _ when ReferenceEquals(section, LogsSettingsCard)
                || ReferenceEquals(section, ConfigSettingsCard) => 1,
            _ when ReferenceEquals(section, LiveNotificationExpander) => 2,
            _ when ReferenceEquals(section, EnableRecordCard)
                || ReferenceEquals(section, QualitySettingsCard)
                || ReferenceEquals(section, RecordFormatExpander)
                || ReferenceEquals(section, SegmentExpander) => 3,
            _ when ReferenceEquals(section, AutomaticMonitoringCard)
                || ReferenceEquals(section, RoutineIntervalCard)
                || ReferenceEquals(section, MonitoringSchedulePresetCard)
                || ReferenceEquals(section, MonitoringScheduleCustomExpander) => 4,
            _ when ReferenceEquals(section, SaveSettingsExpander) => 5,
            _ when ReferenceEquals(section, PreviewSettingsCard)
                || ReferenceEquals(section, KeepAwakeCard)
                || ReferenceEquals(section, AutoShutdownExpander) => 6,
            _ when ReferenceEquals(section, ProxyExpander)
                || ReferenceEquals(section, UserAgentExpander) => 7,
            _ => 0
        };
    }

    private static string GetSettingsUiXGroupTitle(int groupIndex)
    {
        return SettingsUiXGroupTitleKeys.TryGetValue(groupIndex, out string? key)
            ? AppResources.ResourceManager.GetString(key, Locale.Culture) ?? key
            : string.Empty;
    }

    private void UpdateSettingsUiXGroupTitles()
    {
        foreach ((int groupIndex, System.Windows.Controls.TextBlock title) in settingsUiXGroupTitles)
        {
            title.Text = GetSettingsUiXGroupTitle(groupIndex);
        }
    }

    private void ApplySettingsDependentVisibilityMode()
    {
        if (ViewModel.IsUiXEnabled)
        {
            ApplySettingsUiXExpanderContent(LiveNotificationExpander, ViewModel.IsToNotify);
            ApplySettingsUiXExpanderContent(SegmentExpander, ViewModel.IsToSegment);
            ApplySettingsUiXExpanderContent(AutoShutdownExpander, ViewModel.IsUseAutoShutdown);
            ApplySettingsUiXExpanderContent(ProxyExpander, ViewModel.IsUseProxy);
            BindSettingsUiXVisibility(DataRetentionValueInput, nameof(SettingsViewModel.IsDataRetentionEnabled));
            BindSettingsUiXVisibility(DataRetentionUnitSelector, nameof(SettingsViewModel.IsDataRetentionEnabled));
            return;
        }

        RestoreAllSettingsUiXDependentExpanderContents();
        ClearSettingsUiXVisibility(DataRetentionValueInput);
        ClearSettingsUiXVisibility(DataRetentionUnitSelector);
    }

    private void ApplySettingsUiXExpanderContent(CardExpander expander, bool isContentVisible)
    {
        if (isContentVisible)
        {
            RestoreSettingsUiXDependentExpanderContent(expander);
            return;
        }

        if (settingsUiXDependentExpanderContents.ContainsKey(expander) || expander.Content == null)
        {
            return;
        }

        settingsUiXDependentExpanderContents[expander] = expander.Content;
        expander.Content = null;
    }

    private void RestoreSettingsUiXDependentExpanderContent(CardExpander expander)
    {
        if (!settingsUiXDependentExpanderContents.Remove(expander, out object? content))
        {
            return;
        }

        expander.Content = content;
    }

    private void RestoreAllSettingsUiXDependentExpanderContents()
    {
        foreach (CardExpander expander in settingsUiXDependentExpanderContents.Keys.ToArray())
        {
            RestoreSettingsUiXDependentExpanderContent(expander);
        }
    }

    private static void BindSettingsUiXVisibility(FrameworkElement element, string path)
    {
        BindingOperations.SetBinding(element, VisibilityProperty, new WpfBinding(path)
        {
            Converter = SettingsUiXVisibilityConverter
        });
    }

    private static void ClearSettingsUiXVisibility(FrameworkElement element)
    {
        BindingOperations.ClearBinding(element, VisibilityProperty);
    }

    private void ApplySettingsUiXControlAlignment()
    {
        foreach (FrameworkElement section in GetAttachedSettingsSections().OfType<FrameworkElement>())
        {
            foreach (FrameworkElement element in FindLogicalDescendants<FrameworkElement>(section))
            {
                Thickness margin = element.Margin;
                bool isIndentedContent = margin.Left >= 42 && margin.Right >= 28;
                bool isTrailingControl = element.Parent is System.Windows.Controls.Grid parentGrid
                    && parentGrid.ColumnDefinitions.Count >= 2
                    && System.Windows.Controls.Grid.GetColumn(element) == parentGrid.ColumnDefinitions.Count - 1
                    && margin.Right > 0;
                if (isIndentedContent)
                {
                    settingsUiXOriginalMargins.TryAdd(element, margin);
                    element.Margin = new Thickness(SettingsUiXChildIndent, margin.Top, margin.Right, margin.Bottom);
                    continue;
                }

                if (!isTrailingControl)
                {
                    continue;
                }

                settingsUiXOriginalMargins.TryAdd(element, margin);
                element.Margin = new Thickness(margin.Left, margin.Top, 0, margin.Bottom);
            }
        }
    }

    private void RestoreSettingsUiXControlMargins()
    {
        foreach ((FrameworkElement element, Thickness margin) in settingsUiXOriginalMargins)
        {
            element.Margin = margin;
        }

        settingsUiXOriginalMargins.Clear();
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (T descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RemoveSettingsSectionFromParent(UIElement section)
    {
        if (section is FrameworkElement { Parent: WpfPanel parent })
        {
            parent.Children.Remove(section);
        }
    }

    private void ApplySettingsSectionChrome(UIElement section)
    {
        if (section is not FrameworkElement element)
        {
            return;
        }

        RestoreSettingsSectionPresentation(element);

        if (!ViewModel.IsUiXEnabled)
        {
            return;
        }

        element.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        element.Margin = new Thickness(0);

        if (section is System.Windows.Controls.Control uiControl)
        {
            uiControl.Background = ReferenceEquals(section, CookieSettingsExpander)
                ? (System.Windows.Media.Brush)FindResource("UiXCardBrush")
                : System.Windows.Media.Brushes.Transparent;
            uiControl.BorderBrush = System.Windows.Media.Brushes.Transparent;
            uiControl.BorderThickness = new Thickness(0);
            uiControl.Padding = ReferenceEquals(section, CookieSettingsExpander)
                ? new Thickness(14, 11, 14, 11)
                : new Thickness(10, 9, 10, 9);
            uiControl.MinHeight = ReferenceEquals(section, CookieSettingsExpander) ? 58 : 52;
        }

        if (section is CardExpander expander)
        {
            expander.ContentPadding = new Thickness(0, 6, 0, 10);
            if (!ReferenceEquals(section, CookieSettingsExpander))
            {
                expander.SetResourceReference(Control.TemplateProperty, "UiXFixedCardExpanderTemplate");
            }
        }
    }

    private void CaptureSettingsSectionPresentation(FrameworkElement element)
    {
        if (settingsSectionPresentations.ContainsKey(element))
        {
            return;
        }

        Dictionary<DependencyProperty, object> values = new()
        {
            [WidthProperty] = element.GetValue(WidthProperty),
            [MarginProperty] = element.GetValue(MarginProperty),
            [HorizontalAlignmentProperty] = element.GetValue(HorizontalAlignmentProperty),
        };
        if (element is System.Windows.Controls.Control control)
        {
            values[System.Windows.Controls.Control.BackgroundProperty] = control.GetValue(System.Windows.Controls.Control.BackgroundProperty);
            values[System.Windows.Controls.Control.BorderBrushProperty] = control.GetValue(System.Windows.Controls.Control.BorderBrushProperty);
            values[System.Windows.Controls.Control.BorderThicknessProperty] = control.GetValue(System.Windows.Controls.Control.BorderThicknessProperty);
            values[System.Windows.Controls.Control.PaddingProperty] = control.GetValue(System.Windows.Controls.Control.PaddingProperty);
            values[MinHeightProperty] = control.GetValue(MinHeightProperty);
        }
        if (element is CardExpander expander)
        {
            values[CardExpander.ContentPaddingProperty] = expander.GetValue(CardExpander.ContentPaddingProperty);
            values[TemplateProperty] = expander.GetValue(TemplateProperty);
        }
        settingsSectionPresentations[element] = values;
    }

    private void RestoreSettingsSectionPresentation(FrameworkElement element)
    {
        CaptureSettingsSectionPresentation(element);
        foreach ((DependencyProperty property, object value) in settingsSectionPresentations[element])
        {
            element.SetCurrentValue(property, value);
        }
    }

    private void ClearSettingsUiXLayout()
    {
        SettingsUiXLeftColumn.Children.Clear();
        SettingsUiXRightColumn.Children.Clear();
        SettingsUiXBottomPanel.Children.Clear();
        settingsUiXGroups.Clear();
        settingsUiXGroupTitles.Clear();
    }

    private int DeferCollapsedCardExpanderContent(DependencyObject root)
    {
        int deferredCount = 0;
        foreach (CardExpander expander in FindLogicalDescendants<CardExpander>(root))
        {
            DependencyPropertyDescriptor
                .FromProperty(CardExpander.IsExpandedProperty, typeof(CardExpander))
                ?.AddValueChanged(expander, CardExpanderIsExpandedChanged);

            if (DeferCardExpanderContent(expander))
            {
                deferredCount++;
            }
        }

        return deferredCount;
    }

    private void CardExpanderIsExpandedChanged(object? sender, EventArgs e)
    {
        if (sender is not CardExpander expander)
        {
            return;
        }

        if (expander.IsExpanded)
        {
            RestoreCardExpanderContent(expander);
        }
    }

    private void RestoreCardExpanderContent(CardExpander expander)
    {
        if (!deferredCardExpanderContents.Remove(expander, out object? content))
        {
            return;
        }

        expander.Content = content;
    }

    private bool DeferCardExpanderContent(CardExpander expander)
    {
        if (expander.IsExpanded ||
            expander.Content == null ||
            deferredCardExpanderContents.ContainsKey(expander))
        {
            return false;
        }

        deferredCardExpanderContents[expander] = expander.Content;
        expander.Content = null;
        return true;
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (object childObject in LogicalTreeHelper.GetChildren(root))
        {
            if (childObject is not DependencyObject child)
            {
                continue;
            }

            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T descendant in FindLogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
