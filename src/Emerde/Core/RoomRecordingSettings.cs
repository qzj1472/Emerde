namespace Emerde.Core;

public sealed record RoomRecordingOptions
{
    public string PreferredStreamQuality { get; init; } = StreamQualityCatalog.Original;

    public string RecordFormat { get; init; } = "TS/FLV";

    public bool IsRemoveTs { get; init; }

    public bool IsToSegment { get; init; }

    public int SegmentTime { get; init; } = 1800;

    public int SegmentTimeUnit { get; init; } = SegmentTimeUnitHelper.Seconds;

    public int RoutineInterval { get; init; } = 3000;

    public int RoutineScheduleMode { get; init; }

    public string RoutineScheduleDays { get; init; } = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";

    public int RoutineScheduleStartHour { get; init; }

    public int RoutineScheduleStartMinute { get; init; }

    public int RoutineScheduleEndHour { get; init; } = 23;

    public int RoutineScheduleEndMinute { get; init; } = 59;

    public string SaveFolder { get; init; } = string.Empty;

    public int SaveFolderPathLevel { get; init; } = 3;

    public string SaveFileNameCustomRule { get; init; } = "{主播名}_{录制时间}";
}

internal static class RoomRecordingSettings
{
    private const string DefaultSaveFileNameCustomRule = "{主播名}_{录制时间}";
    private const string DefaultScheduleDays = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";

    public static RoomRecordingOptions GetGlobal()
    {
        return new RoomRecordingOptions
        {
            PreferredStreamQuality = StreamQualityCatalog.NormalizePreference(Configurations.PreferredStreamQuality.Get()),
            RecordFormat = NormalizeRecordFormat(Configurations.RecordFormat.Get()),
            IsRemoveTs = Configurations.IsRemoveTs.Get(),
            IsToSegment = Configurations.IsToSegment.Get(),
            SegmentTime = Math.Max(1, Configurations.SegmentTime.Get()),
            SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(Configurations.SegmentTimeUnit.Get()),
            RoutineInterval = Math.Max(500, Configurations.RoutineInterval.Get()),
            RoutineScheduleMode = Math.Clamp(Configurations.RoutineScheduleMode.Get(), 0, 4),
            RoutineScheduleDays = NormalizeScheduleDays(Configurations.RoutineScheduleDays.Get()),
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
            IsToSegment = room.IsToSegment ?? global.IsToSegment,
            SegmentTime = Math.Max(1, room.SegmentTime ?? global.SegmentTime),
            SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(room.SegmentTimeUnit ?? global.SegmentTimeUnit),
            RoutineInterval = Math.Max(500, room.RoutineInterval ?? global.RoutineInterval),
            RoutineScheduleMode = Math.Clamp(room.RoutineScheduleMode ?? global.RoutineScheduleMode, 0, 4),
            RoutineScheduleDays = NormalizeScheduleDays(room.RoutineScheduleDays, global.RoutineScheduleDays),
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

    public static void Apply(Room room, RoomRecordingOptions settings)
    {
        room.PreferredStreamQuality = StreamQualityCatalog.NormalizePreference(settings.PreferredStreamQuality);
        room.RecordFormat = NormalizeRecordFormat(settings.RecordFormat);
        room.IsRemoveTs = settings.IsRemoveTs;
        room.IsToSegment = settings.IsToSegment;
        room.SegmentTime = Math.Max(1, settings.SegmentTime);
        room.SegmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(settings.SegmentTimeUnit);
        room.RoutineInterval = Math.Max(500, settings.RoutineInterval);
        room.RoutineScheduleMode = Math.Clamp(settings.RoutineScheduleMode, 0, 4);
        room.RoutineScheduleDays = NormalizeScheduleDays(settings.RoutineScheduleDays);
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

    private static string NormalizeScheduleDays(string? value, string fallback = DefaultScheduleDays)
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

    private static string NormalizeCustomRule(string? value, string fallback = DefaultSaveFileNameCustomRule)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
