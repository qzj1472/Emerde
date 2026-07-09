using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.ViewModels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Storage;
using Windows.System;
using WindowsAPICodePack.Dialogs;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

[ObservableObject]
public sealed partial class LocalSettingsContentDialog : ContentDialog
{
    private const int Milliseconds = 0;
    private const int Seconds = 1;
    private const int Minutes = 2;
    private const int Hours = 3;

    private int routineIntervalMilliseconds;
    private bool isUpdatingRoutineInterval;
    private int segmentRawValue;
    private int segmentRawUnit;
    private bool isUpdatingSegmentTime;

    public sealed record UnitOption(int Value, string DisplayName);

    public IReadOnlyList<UnitOption> TimeUnitOptions { get; } =
    [
        new(Milliseconds, "毫秒"),
        new(Seconds, "秒"),
        new(Minutes, "分钟"),
        new(Hours, "小时"),
    ];

    public IReadOnlyList<UnitOption> SegmentUnitOptions { get; } =
    [
        new(SegmentTimeUnitHelper.Milliseconds, "毫秒"),
        new(SegmentTimeUnitHelper.Seconds, "秒"),
        new(SegmentTimeUnitHelper.Minutes, "分钟"),
        new(SegmentTimeUnitHelper.Hours, "小时"),
        new(SegmentTimeUnitHelper.Megabytes, "MB"),
        new(SegmentTimeUnitHelper.Gigabytes, "GB"),
    ];

    [ObservableProperty]
    private string nickName = string.Empty;

    [ObservableProperty]
    private string roomUrl = string.Empty;

    [ObservableProperty]
    private bool isFollowGlobalSettings = true;

    partial void OnIsFollowGlobalSettingsChanged(bool value)
    {
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
    private double routineScheduleStartHour;

    [ObservableProperty]
    private double routineScheduleStartMinute;

    [ObservableProperty]
    private double routineScheduleEndHour = 23;

    [ObservableProperty]
    private double routineScheduleEndMinute = 59;

    [ObservableProperty]
    private double routineIntervalValue = 3;

    partial void OnRoutineIntervalValueChanged(double value)
    {
        if (isUpdatingRoutineInterval)
        {
            return;
        }

        routineIntervalMilliseconds = Math.Max(500, ConvertTimeUnitToMilliseconds(value, RoutineIntervalUnitIndex));
    }

    [ObservableProperty]
    private int routineIntervalUnitIndex = Seconds;

    partial void OnRoutineIntervalUnitIndexChanged(int value)
    {
        int next = Math.Clamp(value, Milliseconds, Hours);
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

    [ObservableProperty]
    private bool isRemoveTs;

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

    public bool IsSaved { get; private set; }

    public LocalSettingsContentDialog(RoomStatusReactive room)
    {
        NickName = room.NickName;
        RoomUrl = room.RoomUrl;
        IsFollowGlobalSettings = room.IsFollowGlobalSettings;
        IsToNotify = room.IsToNotify;
        IsToMonitor = room.IsToMonitor;
        IsToRecord = room.IsToRecord;

        Room? storedRoom = Configurations.Rooms.Get()
            .FirstOrDefault(item => string.Equals(item.RoomUrl, room.RoomUrl, StringComparison.OrdinalIgnoreCase));
        InitializeRecordingOptions(storedRoom == null ? RoomRecordingSettings.GetGlobal() : RoomRecordingSettings.Get(storedRoom));

        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDialogVisualSize();
        Dispatcher.BeginInvoke(ApplyDialogVisualSize, DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(ApplyDialogVisualSize, DispatcherPriority.Render);
        Dispatcher.BeginInvoke(ApplyDialogVisualSize, DispatcherPriority.ContextIdle);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        ApplyDialogVisualSize();
        LayoutUpdated -= OnLayoutUpdated;
    }

    public void ApplyDialogVisualSize()
    {
        Window? owner = Application.Current?.MainWindow;
        double ownerWidth = owner?.ActualWidth > 1d ? owner.ActualWidth : owner?.Width ?? 0d;
        double ownerHeight = owner?.ActualHeight > 1d ? owner.ActualHeight : owner?.Height ?? 0d;
        if (ownerWidth <= 1d || ownerHeight <= 1d)
        {
            return;
        }

        double targetWidth = Math.Min(Math.Max(900d, ownerWidth - 32d), Math.Max(1260d, ownerWidth * 0.92d));
        double targetHeight = Math.Min(Math.Max(620d, ownerHeight - 32d), Math.Max(760d, ownerHeight * 0.88d));

        Width = targetWidth;
        MinWidth = targetWidth;
        MaxWidth = targetWidth;
        Height = targetHeight;
        MinHeight = targetHeight;
        MaxHeight = targetHeight;

        if (Content is FrameworkElement content)
        {
            content.Width = Math.Max(1d, targetWidth - 48d);
            content.MinWidth = content.Width;
            content.MaxWidth = content.Width;
            content.Height = Math.Max(1d, targetHeight - 128d);
            content.MinHeight = content.Height;
            content.MaxHeight = content.Height;
        }

        DependencyObject? current = this;
        for (int i = 0; i < 40; i++)
        {
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            if (current == null || current is Window)
            {
                break;
            }

            if (current is FrameworkElement element)
            {
                StretchDialogElement(element, targetWidth, targetHeight);
            }
        }
    }

    private static void StretchDialogElement(FrameworkElement element, double targetWidth, double targetHeight)
    {
        if (element.ActualWidth <= targetWidth)
        {
            element.Width = targetWidth;
            element.MinWidth = targetWidth;
            element.MaxWidth = targetWidth;
        }

        if (element.ActualHeight <= targetHeight)
        {
            element.Height = targetHeight;
            element.MinHeight = targetHeight;
            element.MaxHeight = targetHeight;
        }
    }

    public RoomRecordingOptions GetRecordingOptions()
    {
        return new RoomRecordingOptions
        {
            RecordFormat = RecordFormatIndex switch
            {
                1 => "TS/FLV -> MP4",
                2 => "TS/FLV -> MKV",
                _ => "TS/FLV",
            },
            IsRemoveTs = IsRemoveTs,
            IsToSegment = IsToSegment,
            SegmentTime = Math.Max(1, segmentRawValue),
            SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(SegmentTimeUnitIndex),
            RoutineInterval = Math.Max(500, routineIntervalMilliseconds),
            RoutineScheduleMode = Math.Clamp(RoutineScheduleModeIndex, 0, 4),
            RoutineScheduleDays = BuildRoutineScheduleDays(),
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
        RecordFormatIndex = settings.RecordFormat switch
        {
            "TS/FLV -> MP4" => 1,
            "TS/FLV -> MKV" => 2,
            _ => 0,
        };
        IsRemoveTs = settings.IsRemoveTs;
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

        routineIntervalMilliseconds = Math.Max(500, settings.RoutineInterval);
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
        HashSet<DayOfWeek> days = ParseScheduleDays(settings.RoutineScheduleDays);
        RoutineScheduleMonday = days.Contains(DayOfWeek.Monday);
        RoutineScheduleTuesday = days.Contains(DayOfWeek.Tuesday);
        RoutineScheduleWednesday = days.Contains(DayOfWeek.Wednesday);
        RoutineScheduleThursday = days.Contains(DayOfWeek.Thursday);
        RoutineScheduleFriday = days.Contains(DayOfWeek.Friday);
        RoutineScheduleSaturday = days.Contains(DayOfWeek.Saturday);
        RoutineScheduleSunday = days.Contains(DayOfWeek.Sunday);
        RoutineScheduleStartHour = Math.Clamp(settings.RoutineScheduleStartHour, 0, 23);
        RoutineScheduleStartMinute = Math.Clamp(settings.RoutineScheduleStartMinute, 0, 59);
        RoutineScheduleEndHour = Math.Clamp(settings.RoutineScheduleEndHour, 0, 23);
        RoutineScheduleEndMinute = Math.Clamp(settings.RoutineScheduleEndMinute, 0, 59);
        SaveFolder = settings.SaveFolder;
        SaveFolderPathLevelIndex = Math.Clamp(settings.SaveFolderPathLevel, 0, 3);
        SaveFileNameCustomRule = string.Equals(settings.SaveFileNameCustomRule, "{主播名}_{录制时间}", StringComparison.Ordinal)
            ? string.Empty
            : settings.SaveFileNameCustomRule;
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
    private void ClearSaveFileNameRule()
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
        string folder = SaveFolderHelper.GetSaveFolder(SaveFolder);
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(folder));
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        IsSaved = true;
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

        return Milliseconds;
    }

    private static int ConvertTimeUnitToMilliseconds(double value, int unitIndex)
    {
        double multiplier = unitIndex switch
        {
            Hours => 3600000d,
            Minutes => 60000d,
            Seconds => 1000d,
            _ => 1d,
        };

        return (int)Math.Clamp(Math.Round(value * multiplier, MidpointRounding.AwayFromZero), 1, int.MaxValue);
    }

    private static double ConvertMillisecondsToTimeUnit(int milliseconds, int unitIndex)
    {
        return unitIndex switch
        {
            Hours => milliseconds / 3600000d,
            Minutes => milliseconds / 60000d,
            Seconds => milliseconds / 1000d,
            _ => milliseconds,
        };
    }

    private static int ClampHour(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 23);

    private static int ClampMinute(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 59);
}
