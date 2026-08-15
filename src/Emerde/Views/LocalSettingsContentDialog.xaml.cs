using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Controls;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Windows.Storage;
using Windows.System;
using WindowsAPICodePack.Dialogs;
using Wpf.Ui.Controls;
using WpfPoint = System.Windows.Point;

namespace Emerde.Views;

[ObservableObject]
public sealed partial class LocalSettingsContentDialog : System.Windows.Controls.UserControl
{
    private const int Seconds = 1;
    private const int Minutes = 2;
    private const int Hours = 3;
    private const double DialogWidthRatio = 0.72d;
    private const double DialogHeightRatio = 0.85d;
    internal const double DialogWidthRatioValue = DialogWidthRatio;
    private int routineIntervalMilliseconds;
    private bool isUpdatingRoutineInterval;
    private long segmentRawValue;
    private int segmentRawUnit;
    private bool isUpdatingSegmentTime;
    private bool isUpdatingRoutineScheduleDates;
    private readonly bool useGlobalQualityOptionsWhenPlatformUnknown;

    [ObservableProperty]
    private bool isUiXEnabled;

    public sealed record UnitOption(int Value, string DisplayName);

    public IReadOnlyList<UnitOption> TimeUnitOptions =>
    [
        new(Seconds, "Seconds".Tr()),
        new(Minutes, "Minutes".Tr()),
        new(Hours, "Hours".Tr()),
    ];

    public IReadOnlyList<UnitOption> SegmentUnitOptions =>
    [
        new(SegmentTimeUnitHelper.Milliseconds, "Milliseconds".Tr()),
        new(SegmentTimeUnitHelper.Seconds, "Seconds".Tr()),
        new(SegmentTimeUnitHelper.Minutes, "Minutes".Tr()),
        new(SegmentTimeUnitHelper.Hours, "Hours".Tr()),
        new(SegmentTimeUnitHelper.Megabytes, "MB"),
        new(SegmentTimeUnitHelper.Gigabytes, "GB"),
    ];

    [ObservableProperty]
    private IReadOnlyList<StreamQualityOption> qualityOptions = StreamQualityCatalog.GetOptions(string.Empty);

    [ObservableProperty]
    private bool showIdentityHeader = true;

    [ObservableProperty]
    private bool showSettingsHeader = true;

    [ObservableProperty]
    private string nickName = string.Empty;

    [ObservableProperty]
    private string roomUrl = string.Empty;

    [ObservableProperty]
    private bool isFollowGlobalSettings = true;

    partial void OnIsFollowGlobalSettingsChanged(bool value)
    {
        if (!value)
        {
            InitializeFromGlobalSettings();
        }
        OnPropertyChanged(nameof(CanEditLocalSettings));
    }

    public bool CanEditLocalSettings => !IsFollowGlobalSettings;

    [ObservableProperty]
    private bool isToNotify = true;

    [ObservableProperty]
    private bool isToMonitor = true;

    [ObservableProperty]
    private bool isToRecord = true;

    [ObservableProperty]
    private string preferredQuality = StreamQualityCatalog.Original;

    [ObservableProperty]
    private int routineScheduleModeIndex;

    public bool IsRoutineScheduleCustom => RoutineScheduleModeIndex == 4;

    public bool IsRoutineSchedulePreset => !IsRoutineScheduleCustom;

    partial void OnRoutineScheduleModeIndexChanged(int value)
    {
        int next = Math.Clamp(value, 0, 4);
        if (next != value)
        {
            RoutineScheduleModeIndex = next;
            return;
        }

        OnPropertyChanged(nameof(IsRoutineScheduleCustom));
        OnPropertyChanged(nameof(IsRoutineSchedulePreset));
    }

    [ObservableProperty]
    private bool routineScheduleMonday = true;

    [ObservableProperty]
    private bool routineScheduleTuesday = true;

    [ObservableProperty]
    private bool routineScheduleWednesday = true;

    [ObservableProperty]
    private bool routineScheduleThursday = true;

    [ObservableProperty]
    private bool routineScheduleFriday = true;

    [ObservableProperty]
    private bool routineScheduleSaturday = true;

    [ObservableProperty]
    private bool routineScheduleSunday = true;

    [ObservableProperty]
    private DateTime? routineScheduleStartDate;

    partial void OnRoutineScheduleStartDateChanged(DateTime? value) => NormalizeRoutineScheduleDates(changedStart: true);

    [ObservableProperty]
    private DateTime? routineScheduleEndDate;

    partial void OnRoutineScheduleEndDateChanged(DateTime? value) => NormalizeRoutineScheduleDates(changedStart: false);

    [ObservableProperty]
    private bool routineScheduleUseDays = true;

    [ObservableProperty]
    private bool routineScheduleUseTimeRange = true;

    [ObservableProperty]
    private double routineScheduleStartHour;

    [ObservableProperty]
    private double routineScheduleStartMinute;

    [ObservableProperty]
    private double routineScheduleEndHour = 23;

    [ObservableProperty]
    private double routineScheduleEndMinute = 59;

    [ObservableProperty]
    private double routineIntervalValue = 5;

    partial void OnRoutineIntervalValueChanged(double value)
    {
        if (isUpdatingRoutineInterval)
        {
            return;
        }

        routineIntervalMilliseconds = MonitorTiming.NormalizeRoutineInterval(ConvertTimeUnitToMilliseconds(value, RoutineIntervalUnitIndex));
    }

    [ObservableProperty]
    private int routineIntervalUnitIndex = Seconds;

    partial void OnRoutineIntervalUnitIndexChanged(int value)
    {
        int next = NormalizeRoutineIntervalUnitIndex(value);
        if (next != value)
        {
            RoutineIntervalUnitIndex = next;
            return;
        }

        isUpdatingRoutineInterval = true;
        try
        {
            RoutineIntervalValue = ConvertMillisecondsToTimeUnit(routineIntervalMilliseconds, next);
        }
        finally
        {
            isUpdatingRoutineInterval = false;
        }
    }

    [ObservableProperty]
    private int recordFormatIndex;

    private int lastValidRecordFormatIndex;

    partial void OnRecordFormatIndexChanged(int value)
    {
        if (SettingsViewModel.IsRecordFormatIndexValid(value))
        {
            lastValidRecordFormatIndex = value;
            OnPropertyChanged(nameof(IsMp4RecordFormat));
            OnPropertyChanged(nameof(IsTranscodedRecordFormat));
            return;
        }
        RecordFormatIndex = lastValidRecordFormatIndex;
    }

    public bool IsMp4RecordFormat => RecordFormatIndex == 1;

    public bool IsTranscodedRecordFormat => RecordFormatIndex is 1 or 2;

    [ObservableProperty]
    private bool isRemoveTs;

    [ObservableProperty]
    private bool isOptimizeAudio;

    [ObservableProperty]
    private bool isToSegment;

    [ObservableProperty]
    private double segmentTimeValue = 30;

    partial void OnSegmentTimeValueChanged(double value)
    {
        if (isUpdatingSegmentTime)
        {
            return;
        }

        segmentRawValue = SegmentTimeUnitHelper.ToConfigValue(value, SegmentTimeUnitIndex);
    }

    [ObservableProperty]
    private int segmentTimeUnitIndex = SegmentTimeUnitHelper.Minutes;

    partial void OnSegmentTimeUnitIndexChanged(int value)
    {
        int next = SegmentTimeUnitHelper.NormalizeUnit(value);
        if (next != value)
        {
            SegmentTimeUnitIndex = next;
            return;
        }

        bool canConvert = SegmentTimeUnitHelper.IsTimeUnit(segmentRawUnit) == SegmentTimeUnitHelper.IsTimeUnit(next);
        double displayValue = canConvert
            ? SegmentTimeUnitHelper.ConvertDisplayValue(segmentRawValue, segmentRawUnit, next)
            : SegmentTimeValue;

        isUpdatingSegmentTime = true;
        try
        {
            SegmentTimeValue = displayValue;
        }
        finally
        {
            isUpdatingSegmentTime = false;
        }

        segmentRawUnit = next;
        segmentRawValue = SegmentTimeUnitHelper.ToConfigValue(displayValue, next);
    }

    [ObservableProperty]
    private string saveFolder = string.Empty;

    [ObservableProperty]
    private int saveFolderPathLevelIndex = 3;

    [ObservableProperty]
    private string saveFileNameCustomRule = string.Empty;

    public LocalSettingsContentDialog(RoomStatusReactive room)
        : this(room, true, true, false)
    {
    }

    internal LocalSettingsContentDialog(
        RoomStatusReactive room,
        bool showIdentityHeader,
        bool showSettingsHeader,
        bool useGlobalQualityOptions,
        bool initializeView = true)
    {
        NickName = room.NickName;
        RoomUrl = room.RoomUrl;
        IsFollowGlobalSettings = room.IsFollowGlobalSettings;
        IsToNotify = room.IsToNotify;
        IsToMonitor = room.IsFollowGlobalSettings ? Configurations.IsToMonitor.Get() : room.IsToMonitor;
        IsToRecord = room.IsFollowGlobalSettings ? Configurations.IsToRecord.Get() : room.IsToRecord;
        ShowIdentityHeader = showIdentityHeader;
        ShowSettingsHeader = showSettingsHeader;
        useGlobalQualityOptionsWhenPlatformUnknown = useGlobalQualityOptions;
        QualityOptions = useGlobalQualityOptions
            ? StreamQualityCatalog.GlobalOptions
            : StreamQualityCatalog.GetOptions(room.PlatformName);

        Room? storedRoom = Configurations.Rooms.Get()
            .FirstOrDefault(item => string.Equals(item.RoomUrl, room.RoomUrl, StringComparison.OrdinalIgnoreCase));
        InitializeRecordingOptions(room.IsFollowGlobalSettings || storedRoom == null
            ? RoomRecordingSettings.GetGlobal()
            : RoomRecordingSettings.Get(storedRoom));

        if (initializeView)
        {
            DataContext = this;
            InitializeComponent();
            Loaded += LocalSettingsContentDialogLoaded;
            Unloaded += LocalSettingsContentDialogUnloaded;
        }
    }

    private void LocalSettingsContentDialogLoaded(object sender, RoutedEventArgs e)
    {
        Locale.CultureChanged -= LocaleCultureChanged;
        Locale.CultureChanged += LocaleCultureChanged;
        IsUiXEnabled = Application.Current?.MainWindow?.DataContext is MainViewModel { StatusOfIsUiXEnabled: true };
    }

    private void LocalSettingsContentDialogUnloaded(object sender, RoutedEventArgs e)
    {
        Locale.CultureChanged -= LocaleCultureChanged;
    }

    private void LocaleCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(TimeUnitOptions));
        OnPropertyChanged(nameof(SegmentUnitOptions));
    }

    internal void SetPlatformName(string? platformName)
    {
        QualityOptions = useGlobalQualityOptionsWhenPlatformUnknown && string.IsNullOrWhiteSpace(platformName)
            ? StreamQualityCatalog.GlobalOptions
            : StreamQualityCatalog.GetOptions(platformName ?? string.Empty);
        string normalized = StreamQualityCatalog.NormalizePreference(PreferredQuality);
        PreferredQuality = QualityOptions.Any(option => option.Value == normalized)
            ? normalized
            : StreamQualityCatalog.Original;
    }

    private void LocalSettingsSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ActualWidth <= 0d || element.ActualHeight <= 0d)
        {
            return;
        }

        element.Clip = new RectangleGeometry(new Rect(0d, 0d, element.ActualWidth, element.ActualHeight), 8d, 8d);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        if (e.OriginalSource is not DependencyObject source || IsInteractiveElement(source))
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

    private static bool IsPointInsideHeader(CardExpander expander, WpfPoint point)
    {
        if (expander.Template.FindName("HeaderChrome", expander) is not FrameworkElement header)
        {
            return point.Y >= 0d && point.Y <= 86d;
        }

        WpfPoint topLeft = header.TranslatePoint(new WpfPoint(0d, 0d), expander);
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

    public void ApplyDialogVisualSize(Wpf.Ui.Violeta.Controls.ContentDialog dialog, Window? owner = null)
    {
        void ApplySize()
        {
            bool uiXEnabled = Application.Current?.MainWindow?.DataContext is MainViewModel { StatusOfIsUiXEnabled: true };
            if (!TryGetDialogVisualSize(owner, uiXEnabled ? 0.78d : DialogWidthRatio, uiXEnabled ? 0.82d : DialogHeightRatio, out double targetWidth, out double targetHeight))
            {
                return;
            }

            if (uiXEnabled)
            {
                Window? reference = owner ?? Application.Current?.MainWindow;
                double availableWidth = reference?.ActualWidth > 1d ? reference.ActualWidth - 32d : 1120d;
                double availableHeight = reference?.ActualHeight > 1d ? reference.ActualHeight - 32d : 860d;
                targetWidth = Math.Min(Math.Clamp(targetWidth, 760d, 1120d), Math.Max(1d, availableWidth));
                targetHeight = Math.Min(Math.Clamp(targetHeight, 560d, 860d), Math.Max(1d, availableHeight));
            }

            ApplyWideDialogVisualSize(dialog, targetWidth, targetHeight);

            Width = double.NaN;
            Height = double.NaN;
            MinWidth = 0d;
            MinHeight = 0d;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        }

        ApplySize();
        RoutedEventHandler? loadedHandler = null;
        loadedHandler = (_, _) =>
        {
            dialog.Loaded -= loadedHandler;
            _ = dialog.Dispatcher.BeginInvoke((Action)ApplySize, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        dialog.Loaded += loadedHandler;
    }

    internal static void ApplyWideDialogVisualSize(
        Wpf.Ui.Violeta.Controls.ContentDialog dialog,
        double targetWidth,
        double targetHeight)
    {
        dialog.Width = targetWidth;
        dialog.Height = targetHeight;
        dialog.MinWidth = 0d;
        dialog.MinHeight = 0d;
        dialog.MaxWidth = double.PositiveInfinity;
        dialog.MaxHeight = double.PositiveInfinity;
        WindowSizing.RemoveContentDialogSizeLimits(dialog);
    }

    internal static void ClearWideDialogVisualSize(Wpf.Ui.Violeta.Controls.ContentDialog dialog)
    {
        WindowSizing.RemoveContentDialogSizeLimits(dialog);
    }

    internal static bool TryGetDialogVisualSize(
        Window? owner,
        out double targetWidth,
        out double targetHeight)
    {
        return TryGetDialogVisualSize(owner, DialogHeightRatio, out targetWidth, out targetHeight);
    }

    internal static bool TryGetDialogVisualSize(
        Window? owner,
        double heightRatio,
        out double targetWidth,
        out double targetHeight)
    {
        return TryGetDialogVisualSize(owner, DialogWidthRatio, heightRatio, out targetWidth, out targetHeight);
    }

    internal static bool TryGetDialogVisualSize(
        Window? owner,
        double widthRatio,
        double heightRatio,
        out double targetWidth,
        out double targetHeight)
    {
        Window? reference = owner ?? Application.Current?.MainWindow;
        double ownerWidth = reference?.ActualWidth > 1d ? reference.ActualWidth : reference?.Width ?? 0d;
        double ownerHeight = reference?.ActualHeight > 1d ? reference.ActualHeight : reference?.Height ?? 0d;
        if (ownerWidth <= 1d || ownerHeight <= 1d)
        {
            targetWidth = 0d;
            targetHeight = 0d;
            return false;
        }

        targetWidth = Math.Max(1d, WindowSizing.RoundLayoutValue(ownerWidth * widthRatio));
        targetHeight = Math.Max(1d, WindowSizing.RoundLayoutValue(ownerHeight * heightRatio));
        return true;
    }

    public RoomRecordingOptions GetRecordingOptions()
    {
        return new RoomRecordingOptions
        {
            PreferredStreamQuality = PreferredQuality,
            RecordFormat = RecordFormatIndex switch
            {
                1 => "TS/FLV -> MP4",
                2 => "TS/FLV -> MKV",
                _ => "TS/FLV",
            },
            IsRemoveTs = IsRemoveTs,
            IsOptimizeAudio = IsOptimizeAudio,
            IsToSegment = IsToSegment,
            SegmentTime = Math.Max(1, segmentRawValue),
            SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(SegmentTimeUnitIndex),
            RoutineInterval = MonitorTiming.NormalizeRoutineInterval(routineIntervalMilliseconds),
            RoutineScheduleMode = Math.Clamp(RoutineScheduleModeIndex, 0, 4),
            RoutineScheduleStartDate = RoutineScheduleStartDate.HasValue ? DateOnly.FromDateTime(RoutineScheduleStartDate.Value) : null,
            RoutineScheduleEndDate = RoutineScheduleEndDate.HasValue ? DateOnly.FromDateTime(RoutineScheduleEndDate.Value) : null,
            RoutineScheduleUseDays = RoutineScheduleUseDays,
            RoutineScheduleDays = BuildRoutineScheduleDays(),
            RoutineScheduleUseTimeRange = RoutineScheduleUseTimeRange,
            RoutineScheduleStartHour = ClampHour(RoutineScheduleStartHour),
            RoutineScheduleStartMinute = ClampMinute(RoutineScheduleStartMinute),
            RoutineScheduleEndHour = ClampHour(RoutineScheduleEndHour),
            RoutineScheduleEndMinute = ClampMinute(RoutineScheduleEndMinute),
            SaveFolder = SaveFolder,
            SaveFolderPathLevel = Math.Clamp(SaveFolderPathLevelIndex, 0, 3),
            SaveFileNameCustomRule = SaveFileNameCustomRule,
        };
    }

    private void InitializeRecordingOptions(RoomRecordingOptions settings)
    {
        string normalizedQuality = StreamQualityCatalog.NormalizePreference(settings.PreferredStreamQuality);
        PreferredQuality = QualityOptions.Any(option => option.Value == normalizedQuality)
            ? normalizedQuality
            : StreamQualityCatalog.Original;
        RecordFormatIndex = settings.RecordFormat switch
        {
            "TS/FLV -> MP4" => 1,
            "TS/FLV -> MKV" => 2,
            _ => 0,
        };
        IsRemoveTs = settings.IsRemoveTs;
        IsOptimizeAudio = settings.IsOptimizeAudio;
        IsToSegment = settings.IsToSegment;

        segmentRawValue = Math.Max(1, settings.SegmentTime);
        segmentRawUnit = SegmentTimeUnitHelper.NormalizeUnit(settings.SegmentTimeUnit);
        SegmentTimeUnitIndex = segmentRawUnit;
        isUpdatingSegmentTime = true;
        try
        {
            SegmentTimeValue = SegmentTimeUnitHelper.ToDisplayValue(segmentRawValue, segmentRawUnit);
        }
        finally
        {
            isUpdatingSegmentTime = false;
        }

        routineIntervalMilliseconds = MonitorTiming.NormalizeRoutineInterval(settings.RoutineInterval);
        RoutineIntervalUnitIndex = GetPreferredTimeUnit(routineIntervalMilliseconds);
        isUpdatingRoutineInterval = true;
        try
        {
            RoutineIntervalValue = ConvertMillisecondsToTimeUnit(routineIntervalMilliseconds, RoutineIntervalUnitIndex);
        }
        finally
        {
            isUpdatingRoutineInterval = false;
        }

        RoutineScheduleModeIndex = Math.Clamp(settings.RoutineScheduleMode, 0, 4);
        RoutineScheduleStartDate = settings.RoutineScheduleStartDate?.ToDateTime(TimeOnly.MinValue);
        RoutineScheduleEndDate = settings.RoutineScheduleEndDate?.ToDateTime(TimeOnly.MinValue);
        RoutineScheduleUseDays = settings.RoutineScheduleUseDays;
        HashSet<DayOfWeek> days = ParseScheduleDays(settings.RoutineScheduleDays);
        RoutineScheduleMonday = days.Contains(DayOfWeek.Monday);
        RoutineScheduleTuesday = days.Contains(DayOfWeek.Tuesday);
        RoutineScheduleWednesday = days.Contains(DayOfWeek.Wednesday);
        RoutineScheduleThursday = days.Contains(DayOfWeek.Thursday);
        RoutineScheduleFriday = days.Contains(DayOfWeek.Friday);
        RoutineScheduleSaturday = days.Contains(DayOfWeek.Saturday);
        RoutineScheduleSunday = days.Contains(DayOfWeek.Sunday);
        RoutineScheduleUseTimeRange = settings.RoutineScheduleUseTimeRange;
        RoutineScheduleStartHour = Math.Clamp(settings.RoutineScheduleStartHour, 0, 23);
        RoutineScheduleStartMinute = Math.Clamp(settings.RoutineScheduleStartMinute, 0, 59);
        RoutineScheduleEndHour = Math.Clamp(settings.RoutineScheduleEndHour, 0, 23);
        RoutineScheduleEndMinute = Math.Clamp(settings.RoutineScheduleEndMinute, 0, 59);
        SaveFolder = settings.SaveFolder;
        SaveFolderPathLevelIndex = Math.Clamp(settings.SaveFolderPathLevel, 0, 3);
        SaveFileNameCustomRule = settings.SaveFileNameCustomRule;
    }

    private void NormalizeRoutineScheduleDates(bool changedStart)
    {
        if (isUpdatingRoutineScheduleDates)
        {
            return;
        }

        isUpdatingRoutineScheduleDates = true;
        try
        {
            RoutineScheduleStartDate = RoutineScheduleStartDate?.Date;
            RoutineScheduleEndDate = RoutineScheduleEndDate?.Date;
            if (RoutineScheduleStartDate.HasValue
                && RoutineScheduleEndDate.HasValue
                && RoutineScheduleStartDate > RoutineScheduleEndDate)
            {
                if (changedStart)
                {
                    RoutineScheduleEndDate = RoutineScheduleStartDate;
                }
                else
                {
                    RoutineScheduleStartDate = RoutineScheduleEndDate;
                }
            }
        }
        finally
        {
            isUpdatingRoutineScheduleDates = false;
        }
    }

    internal void InitializeFromGlobalSettings()
    {
        IsToMonitor = Configurations.IsToMonitor.Get();
        IsToRecord = Configurations.IsToRecord.Get();
        InitializeRecordingOptions(RoomRecordingSettings.GetGlobal());
    }

    [RelayCommand]
    private void AppendSaveFileNameToken(string token)
    {
        SaveFileNameCustomRule = string.IsNullOrWhiteSpace(SaveFileNameCustomRule)
            ? token
            : SaveFileNameCustomRule.TrimEnd('_') + "_" + token;
    }

    [RelayCommand]
    private void DeleteLastSaveFileNameToken()
    {
        if (string.IsNullOrWhiteSpace(SaveFileNameCustomRule))
        {
            return;
        }

        string[] tokens = SaveFileNameCustomRule
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        SaveFileNameCustomRule = tokens.Length <= 1 ? string.Empty : string.Join("_", tokens.Take(tokens.Length - 1));
    }

    [RelayCommand]
    private void ClearSaveFileNameCustomRule()
    {
        SaveFileNameCustomRule = string.Empty;
    }

    [RelayCommand]
    private void SelectSaveFolder()
    {
        CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
        };

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            SaveFolder = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task OpenSaveFolderAsync()
    {
        try
        {
            string folder = SaveFolderHelper.GetSaveFolder(SaveFolder);
            await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(folder));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            Wpf.Ui.Violeta.Controls.Toast.Warning("OpenSaveFolderFailed".Tr(e.Message));
        }
    }

    private string BuildRoutineScheduleDays()
    {
        List<DayOfWeek> days = [];
        if (RoutineScheduleMonday)
        {
            days.Add(DayOfWeek.Monday);
        }
        if (RoutineScheduleTuesday)
        {
            days.Add(DayOfWeek.Tuesday);
        }
        if (RoutineScheduleWednesday)
        {
            days.Add(DayOfWeek.Wednesday);
        }
        if (RoutineScheduleThursday)
        {
            days.Add(DayOfWeek.Thursday);
        }
        if (RoutineScheduleFriday)
        {
            days.Add(DayOfWeek.Friday);
        }
        if (RoutineScheduleSaturday)
        {
            days.Add(DayOfWeek.Saturday);
        }
        if (RoutineScheduleSunday)
        {
            days.Add(DayOfWeek.Sunday);
        }

        return days.Count == 0 ? "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday" : string.Join(",", days);
    }

    private static HashSet<DayOfWeek> ParseScheduleDays(string value)
    {
        HashSet<DayOfWeek> days = [];
        foreach (string item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(item, ignoreCase: true, out DayOfWeek day))
            {
                days.Add(day);
            }
        }

        if (days.Count == 0)
        {
            days.UnionWith([
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
                DayOfWeek.Sunday,
            ]);
        }

        return days;
    }

    private static int GetPreferredTimeUnit(int milliseconds)
    {
        if (milliseconds % 3600000 == 0)
        {
            return Hours;
        }

        if (milliseconds % 60000 == 0)
        {
            return Minutes;
        }

        if (milliseconds % 1000 == 0)
        {
            return Seconds;
        }

        return Seconds;
    }

    private static int ConvertTimeUnitToMilliseconds(double value, int unitIndex)
    {
        return MonitorTiming.ConvertToMilliseconds(value, unitIndex);
    }

    private static double ConvertMillisecondsToTimeUnit(int milliseconds, int unitIndex)
    {
        return unitIndex switch
        {
            Hours => milliseconds / 3600000d,
            Minutes => milliseconds / 60000d,
            Seconds or _ => milliseconds / 1000d,
        };
    }

    private static int NormalizeRoutineIntervalUnitIndex(int unitIndex)
    {
        return Math.Clamp(unitIndex, Seconds, Hours);
    }

    private static int ClampHour(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 23);

    private static int ClampMinute(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 59);
}
