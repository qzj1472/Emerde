using Fischless.Configuration;
using System.Globalization;

namespace Emerde.Core;

public sealed record RoomRecordingOptions
{
    public string PreferredStreamQuality { get; init; } = StreamQualityCatalog.Original;

    public string RecordFormat { get; init; } = "TS/FLV";

    public bool IsRemoveTs { get; init; }

    public bool IsOptimizeAudio { get; init; }

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
        return new RoomRecordingOptions
        {
            PreferredStreamQuality = StreamQualityCatalog.NormalizePreference(Configurations.PreferredStreamQuality.Get()),
            RecordFormat = NormalizeRecordFormat(Configurations.RecordFormat.Get()),
            IsRemoveTs = Configurations.IsRemoveTs.Get(),
            IsOptimizeAudio = Configurations.IsOptimizeAudio.Get(),
            IsToSegment = Configurations.IsToSegment.Get(),
            SegmentTime = Math.Max(1, Configurations.SegmentTime.Get()),
            SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(Configurations.SegmentTimeUnit.Get()),
            RoutineInterval = MonitorTiming.NormalizeRoutineInterval(Configurations.RoutineInterval.Get()),
            RoutineScheduleMode = Math.Clamp(Configurations.RoutineScheduleMode.Get(), 0, 4),
            RoutineScheduleStartDate = ParseScheduleDate(Configurations.RoutineScheduleStartDate.Get()),
            RoutineScheduleEndDate = ParseScheduleDate(Configurations.RoutineScheduleEndDate.Get()),
            RoutineScheduleUseDays = Configurations.RoutineScheduleUseDays.Get(),
            RoutineScheduleDays = NormalizeScheduleDays(Configurations.RoutineScheduleDays.Get()),
            RoutineScheduleUseTimeRange = Configurations.RoutineScheduleUseTimeRange.Get(),
            RoutineScheduleStartHour = Math.Clamp(Configurations.RoutineScheduleStartHour.Get(), 0, 23),
            RoutineScheduleStartMinute = Math.Clamp(Configurations.RoutineScheduleStartMinute.Get(), 0, 59),
            RoutineScheduleEndHour = Math.Clamp(Configurations.RoutineScheduleEndHour.Get(), 0, 23),
            RoutineScheduleEndMinute = Math.Clamp(Configurations.RoutineScheduleEndMinute.Get(), 0, 59),
            SaveFolder = Configurations.SaveFolder.Get() ?? string.Empty,
            SaveFolderPathLevel = Math.Clamp(Configurations.SaveFolderPathLevel.Get(), 0, 3),
            SaveFileNameCustomRule = NormalizeCustomRule(Configurations.SaveFileNameCustomRule.Get()),
        };
    }

    public static RoomRecordingOptions Get(Room room)
    {
        RoomRecordingOptions global = GetGlobal();
        if (room.IsFollowGlobalSettings)
        {
            return global;
        }

        return new RoomRecordingOptions
        {
            PreferredStreamQuality = StreamQualityCatalog.NormalizePreference(room.PreferredStreamQuality, global.PreferredStreamQuality),
            RecordFormat = NormalizeRecordFormat(room.RecordFormat, global.RecordFormat),
            IsRemoveTs = room.IsRemoveTs ?? global.IsRemoveTs,
            IsOptimizeAudio = room.IsOptimizeAudio ?? global.IsOptimizeAudio,
            IsToSegment = room.IsToSegment ?? global.IsToSegment,
            SegmentTime = Math.Max(1, room.SegmentTime ?? global.SegmentTime),
            SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(room.SegmentTimeUnit ?? global.SegmentTimeUnit),
            RoutineInterval = MonitorTiming.NormalizeRoutineInterval(room.RoutineInterval ?? global.RoutineInterval),
            RoutineScheduleMode = Math.Clamp(room.RoutineScheduleMode ?? global.RoutineScheduleMode, 0, 4),
            RoutineScheduleStartDate = room.RoutineScheduleStartDate == null ? global.RoutineScheduleStartDate : ParseScheduleDate(room.RoutineScheduleStartDate),
            RoutineScheduleEndDate = room.RoutineScheduleEndDate == null ? global.RoutineScheduleEndDate : ParseScheduleDate(room.RoutineScheduleEndDate),
            RoutineScheduleUseDays = room.RoutineScheduleUseDays ?? global.RoutineScheduleUseDays,
            RoutineScheduleDays = NormalizeScheduleDays(room.RoutineScheduleDays, global.RoutineScheduleDays),
            RoutineScheduleUseTimeRange = room.RoutineScheduleUseTimeRange ?? global.RoutineScheduleUseTimeRange,
            RoutineScheduleStartHour = Math.Clamp(room.RoutineScheduleStartHour ?? global.RoutineScheduleStartHour, 0, 23),
            RoutineScheduleStartMinute = Math.Clamp(room.RoutineScheduleStartMinute ?? global.RoutineScheduleStartMinute, 0, 59),
            RoutineScheduleEndHour = Math.Clamp(room.RoutineScheduleEndHour ?? global.RoutineScheduleEndHour, 0, 23),
            RoutineScheduleEndMinute = Math.Clamp(room.RoutineScheduleEndMinute ?? global.RoutineScheduleEndMinute, 0, 59),
            SaveFolder = room.SaveFolder ?? global.SaveFolder,
            SaveFolderPathLevel = Math.Clamp(room.SaveFolderPathLevel ?? global.SaveFolderPathLevel, 0, 3),
            SaveFileNameCustomRule = NormalizeCustomRule(room.SaveFileNameCustomRule, global.SaveFileNameCustomRule),
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
        room.PreferredStreamQuality = StreamQualityCatalog.NormalizePreference(settings.PreferredStreamQuality);
        room.RecordFormat = NormalizeRecordFormat(settings.RecordFormat);
        room.IsRemoveTs = settings.IsRemoveTs;
        room.IsOptimizeAudio = settings.IsOptimizeAudio;
        room.IsToSegment = settings.IsToSegment;
        room.SegmentTime = Math.Max(1, settings.SegmentTime);
        room.SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(settings.SegmentTimeUnit);
        room.RoutineInterval = MonitorTiming.NormalizeRoutineInterval(settings.RoutineInterval);
        room.RoutineScheduleMode = Math.Clamp(settings.RoutineScheduleMode, 0, 4);
        room.RoutineScheduleStartDate = FormatScheduleDate(settings.RoutineScheduleStartDate);
        room.RoutineScheduleEndDate = FormatScheduleDate(settings.RoutineScheduleEndDate);
        room.RoutineScheduleUseDays = settings.RoutineScheduleUseDays;
        room.RoutineScheduleDays = NormalizeScheduleDays(settings.RoutineScheduleDays);
        room.RoutineScheduleUseTimeRange = settings.RoutineScheduleUseTimeRange;
        room.RoutineScheduleStartHour = Math.Clamp(settings.RoutineScheduleStartHour, 0, 23);
        room.RoutineScheduleStartMinute = Math.Clamp(settings.RoutineScheduleStartMinute, 0, 59);
        room.RoutineScheduleEndHour = Math.Clamp(settings.RoutineScheduleEndHour, 0, 23);
        room.RoutineScheduleEndMinute = Math.Clamp(settings.RoutineScheduleEndMinute, 0, 59);
        room.SaveFolder = settings.SaveFolder ?? string.Empty;
        room.SaveFolderPathLevel = Math.Clamp(settings.SaveFolderPathLevel, 0, 3);
        room.SaveFileNameCustomRule = NormalizeCustomRule(settings.SaveFileNameCustomRule);
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
        int startHour = Math.Clamp(Configurations.RoutineScheduleStartHour.Get(), 0, 23);
        int startMinute = Math.Clamp(Configurations.RoutineScheduleStartMinute.Get(), 0, 59);
        int endHour = Math.Clamp(Configurations.RoutineScheduleEndHour.Get(), 0, 23);
        int endMinute = Math.Clamp(Configurations.RoutineScheduleEndMinute.Get(), 0, 59);
        string days = NormalizeScheduleDays(Configurations.RoutineScheduleDays.Get());
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
        changed |= SetClampedValue(room.RoutineScheduleStartHour, 0, 23, value => room.RoutineScheduleStartHour = value);
        changed |= SetClampedValue(room.RoutineScheduleStartMinute, 0, 59, value => room.RoutineScheduleStartMinute = value);
        changed |= SetClampedValue(room.RoutineScheduleEndHour, 0, 23, value => room.RoutineScheduleEndHour = value);
        changed |= SetClampedValue(room.RoutineScheduleEndMinute, 0, 59, value => room.RoutineScheduleEndMinute = value);
        if (room.RoutineScheduleDays != null)
        {
            string days = NormalizeScheduleDays(room.RoutineScheduleDays);
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
