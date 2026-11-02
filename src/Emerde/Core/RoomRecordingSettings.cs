using Fischless.Configuration;
using System.Globalization;

namespace Emerde.Core;

public sealed record RoomRecordingOptions
{
    public bool IsMonitorFollowingGlobal { get; init; }

    public bool IsRecordFollowingGlobal { get; init; }

    public bool IsMonitoringScheduleFollowingGlobal { get; init; }

    public bool IsRoutineIntervalFollowingGlobal { get; init; }

    public bool IsQualityFollowingGlobal { get; init; }

    public bool IsRecordFormatFollowingGlobal { get; init; }

    public bool IsSegmentFollowingGlobal { get; init; }

    public bool IsOutputFolderFollowingGlobal { get; init; }

    public bool IsOutputPathLevelFollowingGlobal { get; init; }

    public bool IsOutputNamingFollowingGlobal { get; init; }

    public string PreferredStreamQuality { get; init; } = StreamQualityCatalog.Original;

    public string RecordFormat { get; init; } = "TS/FLV";

    public bool IsRemoveTs { get; init; }


    public bool IsToSegment { get; init; }

    public long SegmentTime { get; init; } = 1800;

    public int SegmentTimeUnit { get; init; } = SegmentTimeUnitHelper.Seconds;

    public int RoutineInterval { get; init; } = MonitorTiming.DefaultRoutineIntervalMilliseconds;

    public int RoutineScheduleMode { get; init; }

    public DateOnly? RoutineScheduleStartDate { get; init; }

    public DateOnly? RoutineScheduleEndDate { get; init; }

    public bool RoutineScheduleUseDays { get; init; } = true;

    public string RoutineScheduleDays { get; init; } = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";

    public bool RoutineScheduleUseTimeRange { get; init; } = true;

    public int RoutineScheduleStartHour { get; init; }

    public int RoutineScheduleStartMinute { get; init; }

    public int RoutineScheduleEndHour { get; init; } = 23;

    public int RoutineScheduleEndMinute { get; init; } = 59;

    public string SaveFolder { get; init; } = string.Empty;

    public int SaveFolderPathLevel { get; init; } = 3;

    public string SaveFileNameCustomRule { get; init; } = RecordingFinalizationService.DefaultRule;
}

internal static class RoomRecordingSettings
{
    private const string DefaultSaveFileNameCustomRule = RecordingFinalizationService.DefaultRule;
    private const string DefaultScheduleDays = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";
    private const string LegacyRecordTimeToken = "{录制时间}";
    private const string RecordStartTimeToken = "{录制开始时间}";

    public static RoomRecordingOptions GetGlobal()
    {
        string scheduleDays = Configurations.RoutineScheduleUseDays.Get()
            ? NormalizeScheduleDaysSelection(Configurations.RoutineScheduleDays.Get())
            : string.Empty;
        int scheduleStartHour = Math.Clamp(Configurations.RoutineScheduleStartHour.Get(), 0, 23);
        int scheduleStartMinute = Math.Clamp(Configurations.RoutineScheduleStartMinute.Get(), 0, 59);
        int scheduleEndHour = Math.Clamp(Configurations.RoutineScheduleEndHour.Get(), 0, 23);
        int scheduleEndMinute = Math.Clamp(Configurations.RoutineScheduleEndMinute.Get(), 0, 59);
        if (!Configurations.RoutineScheduleUseTimeRange.Get())
        {
            scheduleStartHour = 0;
            scheduleStartMinute = 0;
            scheduleEndHour = 23;
            scheduleEndMinute = 59;
        }
        return new RoomRecordingOptions
        {
            PreferredStreamQuality = StreamQualityCatalog.NormalizePreference(Configurations.PreferredStreamQuality.Get()),
            RecordFormat = NormalizeRecordFormat(Configurations.RecordFormat.Get()),
            IsRemoveTs = Configurations.IsRemoveTs.Get(),
            IsToSegment = Configurations.IsToSegment.Get(),
            SegmentTime = Math.Max(1, Configurations.SegmentTime.Get()),
            SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(Configurations.SegmentTimeUnit.Get()),
            RoutineInterval = MonitorTiming.NormalizeRoutineInterval(Configurations.RoutineInterval.Get()),
            RoutineScheduleMode = Math.Clamp(Configurations.RoutineScheduleMode.Get(), 0, 4),
            RoutineScheduleStartDate = ParseScheduleDate(Configurations.RoutineScheduleStartDate.Get()),
            RoutineScheduleEndDate = ParseScheduleDate(Configurations.RoutineScheduleEndDate.Get()),
            RoutineScheduleUseDays = HasRoutineScheduleDayRestriction(scheduleDays),
            RoutineScheduleDays = scheduleDays,
            RoutineScheduleUseTimeRange = HasRoutineScheduleTimeRestriction(
                scheduleStartHour,
                scheduleStartMinute,
                scheduleEndHour,
                scheduleEndMinute),
            RoutineScheduleStartHour = scheduleStartHour,
            RoutineScheduleStartMinute = scheduleStartMinute,
            RoutineScheduleEndHour = scheduleEndHour,
            RoutineScheduleEndMinute = scheduleEndMinute,
            SaveFolder = Configurations.SaveFolder.Get() ?? string.Empty,
            SaveFolderPathLevel = Math.Clamp(Configurations.SaveFolderPathLevel.Get(), 0, 3),
            SaveFileNameCustomRule = NormalizeCustomRule(Configurations.SaveFileNameCustomRule.Get()),
        };
    }

    public static RoomRecordingOptions Get(Room room)
    {
        RoomRecordingOptions global = GetGlobal();
        bool monitorFollowsGlobal = IsFollowingGlobal(room.IsMonitorFollowingGlobal, room);
        bool recordFollowsGlobal = IsFollowingGlobal(room.IsRecordFollowingGlobal, room);
        bool scheduleFollowsGlobal = IsFollowingGlobal(room.IsMonitoringScheduleFollowingGlobal, room);
        bool intervalFollowsGlobal = IsFollowingGlobal(room.IsRoutineIntervalFollowingGlobal, room);
        bool qualityFollowsGlobal = IsFollowingGlobal(room.IsQualityFollowingGlobal, room);
        bool formatFollowsGlobal = IsFollowingGlobal(room.IsRecordFormatFollowingGlobal, room);
        bool segmentFollowsGlobal = IsFollowingGlobal(room.IsSegmentFollowingGlobal, room);
        bool outputFolderFollowsGlobal = IsFollowingGlobal(room.IsOutputFolderFollowingGlobal, room);
        bool outputPathLevelFollowsGlobal = IsFollowingGlobal(room.IsOutputPathLevelFollowingGlobal, room);
        bool outputNamingFollowsGlobal = IsFollowingGlobal(room.IsOutputNamingFollowingGlobal, room);
        string scheduleDays = !scheduleFollowsGlobal && room.RoutineScheduleUseDays == false
            ? string.Empty
            : scheduleFollowsGlobal || room.RoutineScheduleDays == null
                ? global.RoutineScheduleDays
                : NormalizeScheduleDaysSelection(room.RoutineScheduleDays);
        int scheduleStartHour = Math.Clamp(scheduleFollowsGlobal ? global.RoutineScheduleStartHour : room.RoutineScheduleStartHour ?? global.RoutineScheduleStartHour, 0, 23);
        int scheduleStartMinute = Math.Clamp(scheduleFollowsGlobal ? global.RoutineScheduleStartMinute : room.RoutineScheduleStartMinute ?? global.RoutineScheduleStartMinute, 0, 59);
        int scheduleEndHour = Math.Clamp(scheduleFollowsGlobal ? global.RoutineScheduleEndHour : room.RoutineScheduleEndHour ?? global.RoutineScheduleEndHour, 0, 23);
        int scheduleEndMinute = Math.Clamp(scheduleFollowsGlobal ? global.RoutineScheduleEndMinute : room.RoutineScheduleEndMinute ?? global.RoutineScheduleEndMinute, 0, 59);
        if (!scheduleFollowsGlobal && room.RoutineScheduleUseTimeRange == false)
        {
            scheduleStartHour = 0;
            scheduleStartMinute = 0;
            scheduleEndHour = 23;
            scheduleEndMinute = 59;
        }
        return new RoomRecordingOptions
        {
            IsMonitorFollowingGlobal = monitorFollowsGlobal,
            IsRecordFollowingGlobal = recordFollowsGlobal,
            IsMonitoringScheduleFollowingGlobal = scheduleFollowsGlobal,
            IsRoutineIntervalFollowingGlobal = intervalFollowsGlobal,
            IsQualityFollowingGlobal = qualityFollowsGlobal,
            IsRecordFormatFollowingGlobal = formatFollowsGlobal,
            IsSegmentFollowingGlobal = segmentFollowsGlobal,
            IsOutputFolderFollowingGlobal = outputFolderFollowsGlobal,
            IsOutputPathLevelFollowingGlobal = outputPathLevelFollowsGlobal,
            IsOutputNamingFollowingGlobal = outputNamingFollowsGlobal,
            PreferredStreamQuality = qualityFollowsGlobal ? global.PreferredStreamQuality : StreamQualityCatalog.NormalizePreference(room.PreferredStreamQuality, global.PreferredStreamQuality),
            RecordFormat = formatFollowsGlobal ? global.RecordFormat : NormalizeRecordFormat(room.RecordFormat, global.RecordFormat),
            IsRemoveTs = formatFollowsGlobal ? global.IsRemoveTs : room.IsRemoveTs ?? global.IsRemoveTs,
            IsToSegment = segmentFollowsGlobal ? global.IsToSegment : room.IsToSegment ?? global.IsToSegment,
            SegmentTime = Math.Max(1, segmentFollowsGlobal ? global.SegmentTime : room.SegmentTime ?? global.SegmentTime),
            SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(segmentFollowsGlobal ? global.SegmentTimeUnit : room.SegmentTimeUnit ?? global.SegmentTimeUnit),
            RoutineInterval = MonitorTiming.NormalizeRoutineInterval(intervalFollowsGlobal ? global.RoutineInterval : room.RoutineInterval ?? global.RoutineInterval),
            RoutineScheduleMode = Math.Clamp(scheduleFollowsGlobal ? global.RoutineScheduleMode : room.RoutineScheduleMode ?? global.RoutineScheduleMode, 0, 4),
            RoutineScheduleStartDate = scheduleFollowsGlobal || room.RoutineScheduleStartDate == null ? global.RoutineScheduleStartDate : ParseScheduleDate(room.RoutineScheduleStartDate),
            RoutineScheduleEndDate = scheduleFollowsGlobal || room.RoutineScheduleEndDate == null ? global.RoutineScheduleEndDate : ParseScheduleDate(room.RoutineScheduleEndDate),
            RoutineScheduleUseDays = HasRoutineScheduleDayRestriction(scheduleDays),
            RoutineScheduleDays = scheduleDays,
            RoutineScheduleUseTimeRange = HasRoutineScheduleTimeRestriction(
                scheduleStartHour,
                scheduleStartMinute,
                scheduleEndHour,
                scheduleEndMinute),
            RoutineScheduleStartHour = scheduleStartHour,
            RoutineScheduleStartMinute = scheduleStartMinute,
            RoutineScheduleEndHour = scheduleEndHour,
            RoutineScheduleEndMinute = scheduleEndMinute,
            SaveFolder = outputFolderFollowsGlobal ? global.SaveFolder : room.SaveFolder ?? global.SaveFolder,
            SaveFolderPathLevel = Math.Clamp(outputPathLevelFollowsGlobal ? global.SaveFolderPathLevel : room.SaveFolderPathLevel ?? global.SaveFolderPathLevel, 0, 3),
            SaveFileNameCustomRule = outputNamingFollowsGlobal ? global.SaveFileNameCustomRule : NormalizeCustomRule(room.SaveFileNameCustomRule, global.SaveFileNameCustomRule),
        };
    }

    public static string GetPreferredStreamQuality(string? roomUrl)
    {
        Room? room = Configurations.Rooms.Get().FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(roomUrl)
            && string.Equals(item.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase));
        return room == null ? GetGlobal().PreferredStreamQuality : Get(room).PreferredStreamQuality;
    }

    public static RoomRecordingOptions GetCurrent(string? roomUrl, RoomRecordingOptions fallback)
    {
        Room? room = Configurations.Rooms.Get().FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(roomUrl)
            && string.Equals(item.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase));
        return room == null ? fallback : Get(room);
    }

    public static void Apply(Room room, RoomRecordingOptions settings)
    {
        room.IsMonitorFollowingGlobal = settings.IsMonitorFollowingGlobal;
        room.IsRecordFollowingGlobal = settings.IsRecordFollowingGlobal;
        room.IsMonitoringScheduleFollowingGlobal = settings.IsMonitoringScheduleFollowingGlobal;
        room.IsRoutineIntervalFollowingGlobal = settings.IsRoutineIntervalFollowingGlobal;
        room.IsQualityFollowingGlobal = settings.IsQualityFollowingGlobal;
        room.IsRecordFormatFollowingGlobal = settings.IsRecordFormatFollowingGlobal;
        room.IsSegmentFollowingGlobal = settings.IsSegmentFollowingGlobal;
        room.IsOutputFolderFollowingGlobal = settings.IsOutputFolderFollowingGlobal;
        room.IsOutputPathLevelFollowingGlobal = settings.IsOutputPathLevelFollowingGlobal;
        room.IsOutputNamingFollowingGlobal = settings.IsOutputNamingFollowingGlobal;
        room.IsFollowGlobalSettings = settings.IsMonitorFollowingGlobal
            && settings.IsRecordFollowingGlobal
            && settings.IsMonitoringScheduleFollowingGlobal
            && settings.IsRoutineIntervalFollowingGlobal
            && settings.IsQualityFollowingGlobal
            && settings.IsRecordFormatFollowingGlobal
            && settings.IsSegmentFollowingGlobal
            && settings.IsOutputFolderFollowingGlobal
            && settings.IsOutputPathLevelFollowingGlobal
            && settings.IsOutputNamingFollowingGlobal;
        room.PreferredStreamQuality = StreamQualityCatalog.NormalizePreference(settings.PreferredStreamQuality);
        room.RecordFormat = NormalizeRecordFormat(settings.RecordFormat);
        room.IsRemoveTs = settings.IsRemoveTs;
        room.IsToSegment = settings.IsToSegment;
        room.SegmentTime = Math.Max(1, settings.SegmentTime);
        room.SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(settings.SegmentTimeUnit);
        room.RoutineInterval = MonitorTiming.NormalizeRoutineInterval(settings.RoutineInterval);
        room.RoutineScheduleMode = Math.Clamp(settings.RoutineScheduleMode, 0, 4);
        room.RoutineScheduleStartDate = FormatScheduleDate(settings.RoutineScheduleStartDate);
        room.RoutineScheduleEndDate = FormatScheduleDate(settings.RoutineScheduleEndDate);
        room.RoutineScheduleUseDays = settings.RoutineScheduleUseDays;
        room.RoutineScheduleDays = NormalizeScheduleDaysSelection(settings.RoutineScheduleDays);
        room.RoutineScheduleUseTimeRange = settings.RoutineScheduleUseTimeRange;
        room.RoutineScheduleStartHour = Math.Clamp(settings.RoutineScheduleStartHour, 0, 23);
        room.RoutineScheduleStartMinute = Math.Clamp(settings.RoutineScheduleStartMinute, 0, 59);
        room.RoutineScheduleEndHour = Math.Clamp(settings.RoutineScheduleEndHour, 0, 23);
        room.RoutineScheduleEndMinute = Math.Clamp(settings.RoutineScheduleEndMinute, 0, 59);
        room.SaveFolder = settings.SaveFolder ?? string.Empty;
        room.SaveFolderPathLevel = Math.Clamp(settings.SaveFolderPathLevel, 0, 3);
        room.SaveFileNameCustomRule = NormalizeCustomRule(settings.SaveFileNameCustomRule);
    }

    public static bool IsFollowingGlobal(bool? settingValue, Room room)
    {
        return settingValue ?? room.IsFollowGlobalSettings;
    }

    public static bool HasAnyGlobalInheritance(Room room)
    {
        return IsFollowingGlobal(room.IsMonitorFollowingGlobal, room)
            || IsFollowingGlobal(room.IsRecordFollowingGlobal, room)
            || IsFollowingGlobal(room.IsMonitoringScheduleFollowingGlobal, room)
            || IsFollowingGlobal(room.IsRoutineIntervalFollowingGlobal, room)
            || IsFollowingGlobal(room.IsQualityFollowingGlobal, room)
            || IsFollowingGlobal(room.IsRecordFormatFollowingGlobal, room)
            || IsFollowingGlobal(room.IsSegmentFollowingGlobal, room)
            || IsFollowingGlobal(room.IsOutputFolderFollowingGlobal, room)
            || IsFollowingGlobal(room.IsOutputPathLevelFollowingGlobal, room)
            || IsFollowingGlobal(room.IsOutputNamingFollowingGlobal, room);
    }

    private static string NormalizeRecordFormat(string? value, string fallback = "TS/FLV")
    {
        return value switch
        {
            "TS/FLV -> MP4" => "TS/FLV -> MP4",
            "TS/FLV -> MKV" => "TS/FLV -> MKV",
            "TS/FLV" => "TS/FLV",
            _ => fallback,
        };
    }

    internal static string NormalizeScheduleDays(string? value, string fallback = DefaultScheduleDays)
    {
        HashSet<DayOfWeek> days = [];
        foreach (string item in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(item, ignoreCase: true, out DayOfWeek day))
            {
                days.Add(day);
                continue;
            }

            if (int.TryParse(item, out int numericDay) && numericDay is >= 0 and <= 6)
            {
                days.Add((DayOfWeek)numericDay);
            }
        }

        if (days.Count == 0)
        {
            return fallback;
        }

        DayOfWeek[] order =
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday,
        ];
        return string.Join(",", order.Where(days.Contains));
    }

    internal static string NormalizeScheduleDaysSelection(string? value, string fallback = DefaultScheduleDays)
    {
        if (value == null)
        {
            return fallback;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        return NormalizeScheduleDays(value, string.Empty);
    }

    internal static DateOnly? ParseScheduleDate(string? value)
    {
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }

    internal static string FormatScheduleDate(DateOnly? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    internal static bool HasRoutineScheduleDayRestriction(string? days)
    {
        return !string.IsNullOrEmpty(NormalizeScheduleDays(days, fallback: string.Empty));
    }

    internal static bool HasRoutineScheduleTimeRestriction(int startHour, int startMinute, int endHour, int endMinute)
    {
        return Math.Clamp(startHour, 0, 23) != 0
            || Math.Clamp(startMinute, 0, 59) != 0
            || Math.Clamp(endHour, 0, 23) != 23
            || Math.Clamp(endMinute, 0, 59) != 59;
    }

    internal static string NormalizeCustomRule(string? value, string fallback = DefaultSaveFileNameCustomRule)
    {
        string rule = string.IsNullOrWhiteSpace(value) ? fallback : value;
        return rule.Replace(LegacyRecordTimeToken, RecordStartTimeToken, StringComparison.Ordinal);
    }

    internal static bool MigrateStoredConfiguration()
    {
        bool changed = false;
        string normalizedRule = NormalizeCustomRule(Configurations.SaveFileNameCustomRule.Get());
        if (!string.Equals(Configurations.SaveFileNameCustomRule.Get(), normalizedRule, StringComparison.Ordinal))
        {
            Configurations.SaveFileNameCustomRule.Set(normalizedRule);
            changed = true;
        }

        int mode = Math.Clamp(Configurations.RoutineScheduleMode.Get(), 0, 4);
        bool useTimeRange = Configurations.RoutineScheduleUseTimeRange.Get();
        int startHour = useTimeRange ? Math.Clamp(Configurations.RoutineScheduleStartHour.Get(), 0, 23) : 0;
        int startMinute = useTimeRange ? Math.Clamp(Configurations.RoutineScheduleStartMinute.Get(), 0, 59) : 0;
        int endHour = useTimeRange ? Math.Clamp(Configurations.RoutineScheduleEndHour.Get(), 0, 23) : 23;
        int endMinute = useTimeRange ? Math.Clamp(Configurations.RoutineScheduleEndMinute.Get(), 0, 59) : 59;
        string days = Configurations.RoutineScheduleUseDays.Get()
            ? NormalizeScheduleDaysSelection(Configurations.RoutineScheduleDays.Get())
            : string.Empty;
        DateOnly? startDate = ParseScheduleDate(Configurations.RoutineScheduleStartDate.Get());
        DateOnly? endDate = ParseScheduleDate(Configurations.RoutineScheduleEndDate.Get());
        NormalizeDateRange(ref startDate, ref endDate);
        changed |= SetIfDifferent(Configurations.RoutineScheduleMode, mode);
        changed |= SetIfDifferent(Configurations.RoutineScheduleStartHour, startHour);
        changed |= SetIfDifferent(Configurations.RoutineScheduleStartMinute, startMinute);
        changed |= SetIfDifferent(Configurations.RoutineScheduleEndHour, endHour);
        changed |= SetIfDifferent(Configurations.RoutineScheduleEndMinute, endMinute);
        changed |= SetIfDifferent(Configurations.RoutineScheduleDays, days);
        changed |= SetIfDifferent(Configurations.RoutineScheduleStartDate, FormatScheduleDate(startDate));
        changed |= SetIfDifferent(Configurations.RoutineScheduleEndDate, FormatScheduleDate(endDate));

        Room[] rooms = Configurations.Rooms.Get() ?? [];
        bool roomsChanged = false;
        foreach (Room room in rooms)
        {
            if (room.SaveFileNameCustomRule != null)
            {
                string roomRule = NormalizeCustomRule(room.SaveFileNameCustomRule);
                if (!string.Equals(room.SaveFileNameCustomRule, roomRule, StringComparison.Ordinal))
                {
                    room.SaveFileNameCustomRule = roomRule;
                    roomsChanged = true;
                }
            }
            if (room.RoutineScheduleMode.HasValue)
            {
                int value = Math.Clamp(room.RoutineScheduleMode.Value, 0, 4);
                roomsChanged |= room.RoutineScheduleMode != value;
                room.RoutineScheduleMode = value;
            }
            roomsChanged |= NormalizeRoomSchedule(room);
        }
        if (roomsChanged)
        {
            Configurations.Rooms.Set(rooms);
            changed = true;
        }
        return changed;
    }

    internal static void NormalizeDateRange(ref DateOnly? startDate, ref DateOnly? endDate)
    {
        if (startDate.HasValue && endDate.HasValue && startDate > endDate)
        {
            (startDate, endDate) = (endDate, startDate);
        }
    }

    private static bool NormalizeRoomSchedule(Room room)
    {
        bool changed = false;
        if (room.RoutineScheduleUseTimeRange == false)
        {
            changed |= room.RoutineScheduleStartHour != 0;
            changed |= room.RoutineScheduleStartMinute != 0;
            changed |= room.RoutineScheduleEndHour != 23;
            changed |= room.RoutineScheduleEndMinute != 59;
            room.RoutineScheduleStartHour = 0;
            room.RoutineScheduleStartMinute = 0;
            room.RoutineScheduleEndHour = 23;
            room.RoutineScheduleEndMinute = 59;
        }
        changed |= SetClampedValue(room.RoutineScheduleStartHour, 0, 23, value => room.RoutineScheduleStartHour = value);
        changed |= SetClampedValue(room.RoutineScheduleStartMinute, 0, 59, value => room.RoutineScheduleStartMinute = value);
        changed |= SetClampedValue(room.RoutineScheduleEndHour, 0, 23, value => room.RoutineScheduleEndHour = value);
        changed |= SetClampedValue(room.RoutineScheduleEndMinute, 0, 59, value => room.RoutineScheduleEndMinute = value);
        if (room.RoutineScheduleDays != null)
        {
            string days = room.RoutineScheduleUseDays == false
                ? string.Empty
                : NormalizeScheduleDaysSelection(room.RoutineScheduleDays);
            changed |= !string.Equals(room.RoutineScheduleDays, days, StringComparison.Ordinal);
            room.RoutineScheduleDays = days;
        }
        DateOnly? startDate = ParseScheduleDate(room.RoutineScheduleStartDate);
        DateOnly? endDate = ParseScheduleDate(room.RoutineScheduleEndDate);
        NormalizeDateRange(ref startDate, ref endDate);
        string normalizedStartDate = FormatScheduleDate(startDate);
        string normalizedEndDate = FormatScheduleDate(endDate);
        if (room.RoutineScheduleStartDate != null)
        {
            changed |= !string.Equals(room.RoutineScheduleStartDate, normalizedStartDate, StringComparison.Ordinal);
            room.RoutineScheduleStartDate = normalizedStartDate;
        }
        if (room.RoutineScheduleEndDate != null)
        {
            changed |= !string.Equals(room.RoutineScheduleEndDate, normalizedEndDate, StringComparison.Ordinal);
            room.RoutineScheduleEndDate = normalizedEndDate;
        }
        return changed;
    }

    private static bool SetClampedValue(int? value, int minimum, int maximum, Action<int?> setter)
    {
        if (!value.HasValue)
        {
            return false;
        }
        int normalized = Math.Clamp(value.Value, minimum, maximum);
        bool changed = value != normalized;
        setter(normalized);
        return changed;
    }

    private static bool SetIfDifferent<T>(ConfigurationDefinition<T> configuration, T value)
    {
        if (EqualityComparer<T>.Default.Equals(configuration.Get(), value))
        {
            return false;
        }
        configuration.Set(value);
        return true;
    }
}
