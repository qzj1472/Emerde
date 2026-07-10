using Fischless.Configuration;
using Emerde.Core;
using System.Reflection;

namespace Emerde;

[Obfuscation]
public static class Configurations
{
    public static ConfigurationDefinition<string> Language { get; } = new(nameof(Language), string.Empty);
    public static ConfigurationDefinition<string> Theme { get; } = new(nameof(Theme), string.Empty);
    public static ConfigurationDefinition<int> DisplayScale { get; } = new(nameof(DisplayScale), 100);
    public static ConfigurationDefinition<int> UpdateChannel { get; } = new(nameof(UpdateChannel), 0);
    public static ConfigurationDefinition<bool> IsSessionLogEnabled { get; } = new(nameof(IsSessionLogEnabled), true);
    public static ConfigurationDefinition<bool> IsOffRemindCloseToTray { get; } = new(nameof(IsOffRemindCloseToTray), false);
    public static ConfigurationDefinition<Room[]> Rooms { get; } = new(nameof(Rooms), []);
    public static ConfigurationDefinition<bool> IsUseStatusTray { get; } = new(nameof(IsUseStatusTray), true);
    public static ConfigurationDefinition<int> RoutineInterval { get; } = new(nameof(RoutineInterval), 3000);
    public static ConfigurationDefinition<int> RoutineIntervalUnit { get; } = new(nameof(RoutineIntervalUnit), 1);
    public static ConfigurationDefinition<bool> IsMonitorRunning { get; } = new(nameof(IsMonitorRunning), true);
    public static ConfigurationDefinition<bool> IsToMonitor { get; } = new(nameof(IsToMonitor), true);
    public static ConfigurationDefinition<int> RoutineScheduleMode { get; } = new(nameof(RoutineScheduleMode), 0);
    public static ConfigurationDefinition<string> RoutineScheduleDays { get; } = new(nameof(RoutineScheduleDays), "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday");
    public static ConfigurationDefinition<int> RoutineScheduleStartHour { get; } = new(nameof(RoutineScheduleStartHour), 0);
    public static ConfigurationDefinition<int> RoutineScheduleStartMinute { get; } = new(nameof(RoutineScheduleStartMinute), 0);
    public static ConfigurationDefinition<int> RoutineScheduleEndHour { get; } = new(nameof(RoutineScheduleEndHour), 23);
    public static ConfigurationDefinition<int> RoutineScheduleEndMinute { get; } = new(nameof(RoutineScheduleEndMinute), 59);
    public static ConfigurationDefinition<bool> IsToNotify { get; } = new(nameof(IsToNotify), true);
    public static ConfigurationDefinition<bool> IsToNotifyWithSystem { get; } = new(nameof(IsToNotifyWithSystem), true);
    public static ConfigurationDefinition<bool> IsToNotifyWithMusic { get; } = new(nameof(IsToNotifyWithMusic), true);
    public static ConfigurationDefinition<string?> ToNotifyWithMusicPath { get; } = new(nameof(ToNotifyWithMusicPath), null);
    public static ConfigurationDefinition<bool> IsToNotifyWithEmail { get; } = new(nameof(IsToNotifyWithEmail), false);
    public static ConfigurationDefinition<string> ToNotifyWithEmailSmtp { get; } = new(nameof(ToNotifyWithEmailSmtp), null!);
    public static ConfigurationDefinition<string> ToNotifyWithEmailUserName { get; } = new(nameof(ToNotifyWithEmailUserName), null!);
    public static ConfigurationDefinition<string> ToNotifyWithEmailPassword { get; } = new(nameof(ToNotifyWithEmailPassword), null!);
    public static ConfigurationDefinition<bool> IsToNotifyGotoRoomUrl { get; } = new(nameof(IsToNotifyGotoRoomUrl), false);
    public static ConfigurationDefinition<bool> IsToNotifyGotoRoomUrlAndMute { get; } = new(nameof(IsToNotifyGotoRoomUrlAndMute), false);
    public static ConfigurationDefinition<bool> IsToRecord { get; } = new(nameof(IsToRecord), true);
    public static ConfigurationDefinition<string> PreferredStreamQuality { get; } = new(nameof(PreferredStreamQuality), StreamQualityCatalog.Original);
    public static ConfigurationDefinition<string> RecordFormat { get; } = new(nameof(RecordFormat), "TS/FLV");
    public static ConfigurationDefinition<bool> IsRemoveTs { get; } = new(nameof(IsRemoveTs), false);
    public static ConfigurationDefinition<bool> IsToSegment { get; } = new(nameof(IsToSegment), false);
    public static ConfigurationDefinition<int> SegmentTime { get; } = new(nameof(SegmentTime), 1800);
    public static ConfigurationDefinition<int> SegmentTimeUnit { get; } = new(nameof(SegmentTimeUnit), 1);
    public static ConfigurationDefinition<string> SaveFolder { get; } = new(nameof(SaveFolder), string.Empty);
    public static ConfigurationDefinition<int> SaveFolderPathLevel { get; } = new(nameof(SaveFolderPathLevel), 3);
    public static ConfigurationDefinition<bool> SaveFolderDistinguishedByAuthors { get; } = new(nameof(SaveFolderDistinguishedByAuthors), true);
    public static ConfigurationDefinition<int> SaveFileNameRule { get; } = new(nameof(SaveFileNameRule), 0);
    public static ConfigurationDefinition<string> SaveFileNameCustomRule { get; } = new(nameof(SaveFileNameCustomRule), string.Empty);
    public static ConfigurationDefinition<bool> IsDataRetentionEnabled { get; } = new(nameof(IsDataRetentionEnabled), false);
    public static ConfigurationDefinition<int> DataRetentionValue { get; } = new(nameof(DataRetentionValue), 1);
    public static ConfigurationDefinition<int> DataRetentionUnit { get; } = new(nameof(DataRetentionUnit), 1);
    public static ConfigurationDefinition<string> Player { get; } = new(nameof(Player), "ffplay");
    public static ConfigurationDefinition<bool> IsPlayerRect { get; } = new(nameof(IsPlayerRect), false);
    public static ConfigurationDefinition<bool> IsUseKeepAwake { get; } = new(nameof(IsUseKeepAwake), false);
    public static ConfigurationDefinition<bool> IsUseAutoShutdown { get; } = new(nameof(IsUseAutoShutdown), false);
    public static ConfigurationDefinition<string> AutoShutdownTime { get; } = new(nameof(AutoShutdownTime), "00:00");
    public static ConfigurationDefinition<bool> IsUseProxy { get; } = new(nameof(IsUseProxy), false);
    public static ConfigurationDefinition<string> ProxyUrl { get; } = new(nameof(ProxyUrl), string.Empty);
    public static ConfigurationDefinition<string> PlatformCookies { get; } = new(nameof(PlatformCookies), string.Empty);
    public static ConfigurationDefinition<string> CookieChina { get; } = new(nameof(CookieChina), string.Empty);
    public static ConfigurationDefinition<string> CookieOversea { get; } = new(nameof(CookieOversea), string.Empty);
    public static ConfigurationDefinition<string> UserAgent { get; } = new(nameof(UserAgent), string.Empty);
}

[Obfuscation]
public sealed class Room
{
    public string NickName { get; set; } = null!;
    public string RoomUrl { get; set; } = null!;
    public string AvatarThumbUrl { get; set; } = string.Empty;
    public string AvatarLocalPath { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public string LiveTitle { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string Bitrate { get; set; } = string.Empty;
    public string Headers { get; set; } = string.Empty;
    public string FlvUrl { get; set; } = string.Empty;
    public string HlsUrl { get; set; } = string.Empty;
    public string RecordUrl { get; set; } = string.Empty;
    public DateTime LastInfoUpdatedAt { get; set; } = DateTime.MinValue;
    public bool IsToNotify { get; set; } = true;
    public bool IsToRecord { get; set; } = true;
    public bool IsToMonitor { get; set; } = true;
    public bool IsFollowGlobalSettings { get; set; } = true;
    public string? PreferredStreamQuality { get; set; }
    public string? RecordFormat { get; set; }
    public bool? IsRemoveTs { get; set; }
    public bool? IsToSegment { get; set; }
    public int? SegmentTime { get; set; }
    public int? SegmentTimeUnit { get; set; }
    public int? RoutineInterval { get; set; }
    public int? RoutineScheduleMode { get; set; }
    public string? RoutineScheduleDays { get; set; }
    public int? RoutineScheduleStartHour { get; set; }
    public int? RoutineScheduleStartMinute { get; set; }
    public int? RoutineScheduleEndHour { get; set; }
    public int? RoutineScheduleEndMinute { get; set; }
    public string? SaveFolder { get; set; }
    public int? SaveFolderPathLevel { get; set; }
    public string? SaveFileNameCustomRule { get; set; }

    public override string ToString() => $"{RoomUrl},{NickName}";
}
