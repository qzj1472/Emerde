using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ComputedConverters;
using Fischless.Configuration;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.Models;
using Emerde.Views;
using Vanara.PInvoke;
using Windows.Storage;
using Windows.System;
using WindowsAPICodePack.Dialogs;
using Wpf.Ui.Appearance;
using Wpf.Ui.Violeta.Appearance;
using Wpf.Ui.Violeta.Controls;
using Wpf.Ui.Violeta.Resources;

namespace Emerde.ViewModels;

[ObservableObject]
public partial class SettingsViewModel : ReactiveObject
{
    private const string DefaultSaveFileNameCustomRule = RecordingFinalizationService.DefaultRule;

    public sealed record UnitOption(int Value, string DisplayName);

    public System.Windows.Window? OwnerWindow { get; set; }

    public string ConfigFilePath => string.IsNullOrWhiteSpace(ConfigurationManager.FilePath)
        ? AppPaths.ConfigFilePath
        : ConfigurationManager.FilePath;

    public IReadOnlyList<UnitOption> TimeUnitOptions =>
    [
        new((int)TimeUnitIndexEnum.Seconds, "Seconds".Tr()),
        new((int)TimeUnitIndexEnum.Minutes, "Minutes".Tr()),
        new((int)TimeUnitIndexEnum.Hours, "Hours".Tr()),
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

    public IReadOnlyList<StreamQualityOption> StreamQualityOptions => StreamQualityCatalog.GlobalOptions;

    public IReadOnlyList<UnitOption> DataRetentionUnitOptions =>
    [
        new(DataRetentionUnitHelper.Days, "Days".Tr()),
        new(DataRetentionUnitHelper.Weeks, "Weeks".Tr()),
        new(DataRetentionUnitHelper.Months, "Months".Tr()),
        new(DataRetentionUnitHelper.Years, "Years".Tr()),
    ];

    private IReadOnlyList<PlatformCookieItem>? domesticCookiePlatforms;

    public IReadOnlyList<PlatformCookieItem> DomesticCookiePlatforms =>
        domesticCookiePlatforms ??= CreateCookieItems(DomesticCookiePlatformNames, SecretProtector.GetChinaCookie());

    private IReadOnlyList<PlatformCookieItem>? overseasCookiePlatforms;

    public IReadOnlyList<PlatformCookieItem> OverseasCookiePlatforms =>
        overseasCookiePlatforms ??= CreateCookieItems(OverseasCookiePlatformNames, SecretProtector.GetOverseaCookie());

    public string ChinaCookiePlatformsText => string.Join(" / ", DomesticCookiePlatformNames.Select(GetPlatformDisplayName));

    public string OverseaCookiePlatformsText => string.Join(" / ", OverseasCookiePlatformNames.Select(GetPlatformDisplayName));

    public string DirectStreamPlatformsText => string.Join(" / ", NoCookiePlatformNames.Select(GetPlatformDisplayName));

    private static readonly string[] DomesticCookiePlatformNames =
    [
        "Douyin",
        "Bilibili",
        "Kuaishou",
        "Huya",
        "Douyu",
        "Baidu",
        "MaoerFM",
        "Lianjie",
        "6Rooms",
        "VVXqiu",
        "Blued",
        "Liuxing",
        "Changliao",
        "Yinbo",
        "Zhihu",
        "PPLive",
        "CatShow",
        "Laixiu",
        "JD",
        "Weibo",
        "Huajiao",
        "Look",
        "Taobao",
        "Xiaohongshu",
        "Kugou",
        "Yingke",
        "AcFun",
        "YY",
        "NeteaseCC",
        "QianduRebo",
    ];

    private static readonly string[] OverseasCookiePlatformNames =
    [
        "TikTok",
        "Bigo",
        "ShowRoom",
        "17Live",
        "CHZZK",
        "Picarto",
        "LangLive",
        "PandaTV",
        "WinkTV",
        "Twitch",
        "YouTube",
        "Shopee",
        "TwitCasting",
        "Faceit",
        "SOOP",
        "FlexTV",
        "PopkonTV",
        "LiveMe",
    ];

    private static readonly string[] NoCookiePlatformNames =
    [
        "Direct",
    ];

    private static readonly IReadOnlyDictionary<string, string> SimplifiedChinesePlatformNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Douyin"] = "抖音",
            ["Bilibili"] = "哔哩哔哩",
            ["Kuaishou"] = "快手",
            ["Huya"] = "虎牙",
            ["Douyu"] = "斗鱼",
            ["Baidu"] = "百度",
            ["MaoerFM"] = "猫耳FM",
            ["Lianjie"] = "链街",
            ["6Rooms"] = "六间房",
            ["VVXqiu"] = "VV星球",
            ["Liuxing"] = "流星",
            ["Changliao"] = "畅聊",
            ["Yinbo"] = "音播",
            ["Zhihu"] = "知乎",
            ["Laixiu"] = "来秀",
            ["JD"] = "京东",
            ["Weibo"] = "微博",
            ["Huajiao"] = "花椒",
            ["Taobao"] = "淘宝",
            ["Xiaohongshu"] = "小红书",
            ["Kugou"] = "酷狗",
            ["Yingke"] = "映客",
            ["NeteaseCC"] = "网易CC",
            ["QianduRebo"] = "千度热播",
            ["Direct"] = "直链",
        };

    private static readonly IReadOnlyDictionary<string, string> TraditionalChinesePlatformNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Douyin"] = "抖音",
            ["Bilibili"] = "嗶哩嗶哩",
            ["Kuaishou"] = "快手",
            ["Huya"] = "虎牙",
            ["Douyu"] = "鬥魚",
            ["Baidu"] = "百度",
            ["MaoerFM"] = "貓耳FM",
            ["Lianjie"] = "鏈街",
            ["6Rooms"] = "六間房",
            ["VVXqiu"] = "VV星球",
            ["Liuxing"] = "流星",
            ["Changliao"] = "暢聊",
            ["Yinbo"] = "音播",
            ["Zhihu"] = "知乎",
            ["Laixiu"] = "來秀",
            ["JD"] = "京東",
            ["Weibo"] = "微博",
            ["Huajiao"] = "花椒",
            ["Taobao"] = "淘寶",
            ["Xiaohongshu"] = "小紅書",
            ["Kugou"] = "酷狗",
            ["Yingke"] = "映客",
            ["NeteaseCC"] = "網易CC",
            ["QianduRebo"] = "千度熱播",
            ["Direct"] = "直鏈",
        };

    private static IReadOnlyList<PlatformCookieItem> CreateCookieItems(IEnumerable<string> platformNames, string fallbackCookie)
    {
        IReadOnlyDictionary<string, string> cookies = PlatformCookieStore.GetAll();
        return platformNames
            .Select(platformName =>
            {
                string cookie = cookies.TryGetValue(platformName, out string? savedCookie) && !string.IsNullOrWhiteSpace(savedCookie)
                    ? savedCookie
                    : fallbackCookie;
                return new PlatformCookieItem(platformName, GetPlatformDisplayName(platformName), cookie);
            })
            .ToArray();
    }

    private static string GetPlatformDisplayName(string platformName)
    {
        CultureInfo culture = global::Emerde.Locale.Culture;
        IReadOnlyDictionary<string, string>? localizedNames = culture.Name switch
        {
            "zh-Hant" => TraditionalChinesePlatformNames,
            "zh-Hans" => SimplifiedChinesePlatformNames,
            _ when string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase)
                => SimplifiedChinesePlatformNames,
            _ => null,
        };

        return localizedNames?.TryGetValue(platformName, out string? displayName) == true
            ? displayName
            : platformName;
    }

    private enum LanguageIndexEnum
    {
        Auto,
        ChineseSimplified,
        ChineseTraditional,
        English,
        Japanese,
    }

    private enum ThemeIndexEnum
    {
        Auto,
        Dark,
        Light,
    }

    private enum TimeUnitIndexEnum
    {
        Milliseconds,
        Seconds,
        Minutes,
        Hours,
    }

    [ObservableProperty]
    private int displayScale = Math.Clamp(Configurations.DisplayScale.Get(), 80, 200);

    partial void OnDisplayScaleChanged(int value)
    {
        int next = Math.Clamp(value, 80, 200);
        if (next != value)
        {
            DisplayScale = next;
            return;
        }

        Configurations.DisplayScale.Set(next);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionLogStatus))]
    private bool isSessionLogEnabled = Configurations.IsSessionLogEnabled.Get();

    partial void OnIsSessionLogEnabledChanged(bool value)
    {
        Configurations.IsSessionLogEnabled.Set(value);
        ConfigurationSaveScheduler.Request();

        if (value)
        {
            AppSessionLogger.StartNow("session logging enabled");
        }
        else
        {
            AppSessionLogger.Stop("session logging disabled");
        }
    }

    public string SessionLogStatus => IsSessionLogEnabled
        ? "SessionLogEnabledDescription".Tr()
        : "SessionLogDisabledDescription".Tr();

    [ObservableProperty]
    private double sessionLogRetentionDays = AppSessionLogger.NormalizeRetentionDays(Configurations.SessionLogRetentionDays.Get());

    partial void OnSessionLogRetentionDaysChanged(double value)
    {
        int next = AppSessionLogger.NormalizeRetentionDays((int)Math.Round(value, MidpointRounding.AwayFromZero));
        if (Math.Abs(next - value) > double.Epsilon)
        {
            SessionLogRetentionDays = next;
            return;
        }

        Configurations.SessionLogRetentionDays.Set(next);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private int languageIndex = Configurations.Language.Get() switch
    {
        "zh" or "zh-Hans" => (int)LanguageIndexEnum.ChineseSimplified,
        "zh-Hant" => (int)LanguageIndexEnum.ChineseTraditional,
        "en" => (int)LanguageIndexEnum.English,
        "ja" => (int)LanguageIndexEnum.Japanese,
        _ => (int)LanguageIndexEnum.Auto,
    };

    partial void OnLanguageIndexChanged(int value)
    {
        string language = value switch
        {
            (int)LanguageIndexEnum.ChineseSimplified => "zh-Hans",
            (int)LanguageIndexEnum.ChineseTraditional => "zh-Hant",
            (int)LanguageIndexEnum.English => "en",
            (int)LanguageIndexEnum.Japanese => "ja",
            _ => string.Empty,
        };

        Locale.Culture = value switch
        {
            (int)LanguageIndexEnum.Auto => new CultureInfo(Interop.GetUserDefaultLocaleName()),
            _ => new CultureInfo(language),
        };

        RefreshLocalizedOptions();

        Configurations.Language.Set(language);
        ConfigurationSaveScheduler.Request();
    }

    internal void RefreshLocalizedOptions()
    {
        domesticCookiePlatforms = null;
        overseasCookiePlatforms = null;
        OnPropertyChanged(nameof(TimeUnitOptions));
        OnPropertyChanged(nameof(SegmentUnitOptions));
        OnPropertyChanged(nameof(StreamQualityOptions));
        OnPropertyChanged(nameof(DataRetentionUnitOptions));
        OnPropertyChanged(nameof(DomesticCookiePlatforms));
        OnPropertyChanged(nameof(OverseasCookiePlatforms));
        OnPropertyChanged(nameof(ChinaCookiePlatformsText));
        OnPropertyChanged(nameof(OverseaCookiePlatformsText));
        OnPropertyChanged(nameof(DirectStreamPlatformsText));
    }

    [ObservableProperty]
    private int themeIndex = Configurations.Theme.Get() switch
    {
        nameof(ApplicationTheme.Light) => (int)ThemeIndexEnum.Light,
        nameof(ApplicationTheme.Dark) => (int)ThemeIndexEnum.Dark,
        _ => (int)ThemeIndexEnum.Auto,
    };

    partial void OnThemeIndexChanged(int value)
    {
        ApplicationTheme theme = value switch
        {
            (int)ThemeIndexEnum.Light => ApplicationTheme.Light,
            (int)ThemeIndexEnum.Dark => ApplicationTheme.Dark,
            _ => ApplicationTheme.Unknown,
        };

        ThemeManager.Apply(theme);
        Configurations.Theme.Set(theme switch
        {
            ApplicationTheme.Light => nameof(ApplicationTheme.Light),
            ApplicationTheme.Dark => nameof(ApplicationTheme.Dark),
            _ => string.Empty,
        });
        ConfigurationSaveScheduler.Request();
        AppThemeBrushes.Apply();
        DialogBlurScope.RefreshActiveBackdropBrushes();
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                AppThemeBrushes.Apply();
                DialogBlurScope.RefreshActiveBackdropBrushes();
            }));
    }

    [ObservableProperty]
    private bool isUseStatusTray = Configurations.IsUseStatusTray.Get();

    partial void OnIsUseStatusTrayChanged(bool value)
    {
        Configurations.IsUseStatusTray.Set(value);
        ConfigurationSaveScheduler.Request();
        TrayIconManager.GetInstance().UpdateTrayIcon();
    }

    [ObservableProperty]
    private bool isUiXEnabled = Configurations.IsUiXEnabled.Get();

    partial void OnIsUiXEnabledChanged(bool value)
    {
        Configurations.IsUiXEnabled.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [RelayCommand]
    private void CreateDesktopShortcut()
    {
        bool succeeded = ShortcutHelper.CreateShortcutOnDesktop(
            shortcutName: "Emerde",
            targetPath: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName),
            arguments: null!,
            description: "Title".Tr(),
            iconLocation: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName + ".exe"));

        if (succeeded)
        {
            Toast.Success("SuccOp".Tr());
        }
        else
        {
            Toast.Warning("FailOp".Tr());
        }
    }

    [ObservableProperty]
    private bool isToNotify = Configurations.IsToNotify.Get();

    partial void OnIsToNotifyChanged(bool value)
    {
        Configurations.IsToNotify.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private bool isToNotifyWithSystem = Configurations.IsToNotifyWithSystem.Get();

    partial void OnIsToNotifyWithSystemChanged(bool value)
    {
        Configurations.IsToNotifyWithSystem.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private bool isToNotifyWithMusic = Configurations.IsToNotifyWithMusic.Get();

    partial void OnIsToNotifyWithMusicChanged(bool value)
    {
        Configurations.IsToNotifyWithMusic.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private string? toNotifyWithMusicPath = Configurations.ToNotifyWithMusicPath.Get();

    partial void OnToNotifyWithMusicPathChanged(string? value)
    {
        Configurations.ToNotifyWithMusicPath.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private bool isToNotifyWithEmail = Configurations.IsToNotifyWithEmail.Get();

    partial void OnIsToNotifyWithEmailChanged(bool value)
    {
        Configurations.IsToNotifyWithEmail.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private string toNotifyWithEmailSmtp = Configurations.ToNotifyWithEmailSmtp.Get();

    partial void OnToNotifyWithEmailSmtpChanged(string value)
    {
        Configurations.ToNotifyWithEmailSmtp.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private string toNotifyWithEmailUserName = Configurations.ToNotifyWithEmailUserName.Get();

    partial void OnToNotifyWithEmailUserNameChanged(string value)
    {
        Configurations.ToNotifyWithEmailUserName.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private string toNotifyWithEmailPassword = SecretProtector.Unprotect(Configurations.ToNotifyWithEmailPassword.Get());

    partial void OnToNotifyWithEmailPasswordChanged(string value)
    {
        Configurations.ToNotifyWithEmailPassword.Set(SecretProtector.Protect(value));
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private bool isToNotifyGotoRoomUrl = Configurations.IsToNotifyGotoRoomUrl.Get();

    partial void OnIsToNotifyGotoRoomUrlChanged(bool value)
    {
        Configurations.IsToNotifyGotoRoomUrl.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private bool isToNotifyGotoRoomUrlAndMute = Configurations.IsToNotifyGotoRoomUrlAndMute.Get();

    partial void OnIsToNotifyGotoRoomUrlAndMuteChanged(bool value)
    {
        Configurations.IsToNotifyGotoRoomUrlAndMute.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private bool isToMonitor = Configurations.IsToMonitor.Get();

    partial void OnIsToMonitorChanged(bool value)
    {
        Configurations.IsToMonitor.Set(value);
        ConfigurationSaveScheduler.Request();
        GlobalMonitor.ClearTemporaryMonitorOverrides();
        NotifyRuntimeConfigurationChanged(recheckRooms: true);
    }

    [ObservableProperty]
    private bool isToRecord = Configurations.IsToRecord.Get();

    partial void OnIsToRecordChanged(bool value)
    {
        Configurations.IsToRecord.Set(value);
        ConfigurationSaveScheduler.Request();
        GlobalMonitor.ClearTemporaryRecordOverrides();
        NotifyRuntimeConfigurationChanged(recheckRooms: true);
    }

    [ObservableProperty]
    private string preferredStreamQuality = StreamQualityCatalog.NormalizePreference(Configurations.PreferredStreamQuality.Get());

    partial void OnPreferredStreamQualityChanged(string value)
    {
        string normalized = StreamQualityCatalog.NormalizePreference(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            PreferredStreamQuality = normalized;
            return;
        }

        Configurations.PreferredStreamQuality.Set(normalized);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged(recheckRooms: true);
    }

    [ObservableProperty]
    private double routineInterval = ConvertMillisecondsToTimeUnit(
        MonitorTiming.NormalizeRoutineInterval(Configurations.RoutineInterval.Get()),
        NormalizeRoutineIntervalUnitIndex(Configurations.RoutineIntervalUnit.Get()));

    private bool isUpdatingRoutineInterval;

    partial void OnRoutineIntervalChanged(double value)
    {
        if (isUpdatingRoutineInterval)
        {
            return;
        }

        SaveRoutineInterval(value, RoutineIntervalUnitIndex);
    }

    [ObservableProperty]
    private int routineIntervalUnitIndex = NormalizeRoutineIntervalUnitIndex(Configurations.RoutineIntervalUnit.Get());

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
            RoutineInterval = ConvertMillisecondsToTimeUnit(MonitorTiming.NormalizeRoutineInterval(Configurations.RoutineInterval.Get()), next);
        }
        finally
        {
            isUpdatingRoutineInterval = false;
        }

        Configurations.RoutineIntervalUnit.Set(next);
        ConfigurationSaveScheduler.Request();
    }

    private void SaveRoutineInterval(double value, int unitIndex)
    {
        int nextUnitIndex = NormalizeRoutineIntervalUnitIndex(unitIndex);
        int milliseconds = ConvertTimeUnitToMilliseconds(value, nextUnitIndex);
        milliseconds = MonitorTiming.NormalizeRoutineInterval(milliseconds);
        Configurations.RoutineInterval.Set(milliseconds);
        Configurations.RoutineIntervalUnit.Set(nextUnitIndex);
        ConfigurationSaveScheduler.Request();
        GlobalMonitor.RefreshRoutineInterval();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private int toNotifyWithEmailPort = Math.Clamp(Configurations.ToNotifyWithEmailPort.Get(), 1, 65535);

    partial void OnToNotifyWithEmailPortChanged(int value)
    {
        int port = Math.Clamp(value, 1, 65535);
        if (port != value)
        {
            ToNotifyWithEmailPort = port;
            return;
        }
        Configurations.ToNotifyWithEmailPort.Set(port);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRoutineScheduleCustom))]
    [NotifyPropertyChangedFor(nameof(IsRoutineSchedulePreset))]
    private int routineScheduleModeIndex = Math.Clamp(Configurations.RoutineScheduleMode.Get(), 0, 4);

    public bool IsRoutineScheduleCustom => RoutineScheduleModeIndex == 4;

    public bool IsRoutineSchedulePreset => !IsRoutineScheduleCustom;

    private bool isUpdatingRoutineScheduleDates;

    partial void OnRoutineScheduleModeIndexChanged(int value)
    {
        int next = Math.Clamp(value, 0, 4);
        if (next != value)
        {
            RoutineScheduleModeIndex = next;
            return;
        }

        Configurations.RoutineScheduleMode.Set(next);
        ConfigurationSaveScheduler.Request();
        GlobalMonitor.RefreshRoutineInterval();
        NotifyRuntimeConfigurationChanged(recheckRooms: true);
    }

    [ObservableProperty]
    private DateTime? routineScheduleStartDate = ToDateTime(RoomRecordingSettings.GetGlobal().RoutineScheduleStartDate);

    [ObservableProperty]
    private DateTime? routineScheduleEndDate = ToDateTime(RoomRecordingSettings.GetGlobal().RoutineScheduleEndDate);

    [ObservableProperty]
    private bool routineScheduleUseDays = Configurations.RoutineScheduleUseDays.Get();

    partial void OnRoutineScheduleUseDaysChanged(bool value)
    {
        Configurations.RoutineScheduleUseDays.Set(value);
        SaveRoutineScheduleChange();
    }

    partial void OnRoutineScheduleStartDateChanged(DateTime? value) => SaveRoutineScheduleDates(changedStart: true);

    partial void OnRoutineScheduleEndDateChanged(DateTime? value) => SaveRoutineScheduleDates(changedStart: false);

    [ObservableProperty]
    private bool routineScheduleMonday = IsRoutineScheduleDayEnabled(DayOfWeek.Monday);

    [ObservableProperty]
    private bool routineScheduleTuesday = IsRoutineScheduleDayEnabled(DayOfWeek.Tuesday);

    [ObservableProperty]
    private bool routineScheduleWednesday = IsRoutineScheduleDayEnabled(DayOfWeek.Wednesday);

    [ObservableProperty]
    private bool routineScheduleThursday = IsRoutineScheduleDayEnabled(DayOfWeek.Thursday);

    [ObservableProperty]
    private bool routineScheduleFriday = IsRoutineScheduleDayEnabled(DayOfWeek.Friday);

    [ObservableProperty]
    private bool routineScheduleSaturday = IsRoutineScheduleDayEnabled(DayOfWeek.Saturday);

    [ObservableProperty]
    private bool routineScheduleSunday = IsRoutineScheduleDayEnabled(DayOfWeek.Sunday);

    partial void OnRoutineScheduleMondayChanged(bool value) => SaveRoutineScheduleDays();
    partial void OnRoutineScheduleTuesdayChanged(bool value) => SaveRoutineScheduleDays();
    partial void OnRoutineScheduleWednesdayChanged(bool value) => SaveRoutineScheduleDays();
    partial void OnRoutineScheduleThursdayChanged(bool value) => SaveRoutineScheduleDays();
    partial void OnRoutineScheduleFridayChanged(bool value) => SaveRoutineScheduleDays();
    partial void OnRoutineScheduleSaturdayChanged(bool value) => SaveRoutineScheduleDays();
    partial void OnRoutineScheduleSundayChanged(bool value) => SaveRoutineScheduleDays();

    [ObservableProperty]
    private bool routineScheduleUseTimeRange = Configurations.RoutineScheduleUseTimeRange.Get();

    partial void OnRoutineScheduleUseTimeRangeChanged(bool value)
    {
        Configurations.RoutineScheduleUseTimeRange.Set(value);
        SaveRoutineScheduleChange();
    }

    [ObservableProperty]
    private int routineScheduleStartHour = Math.Clamp(Configurations.RoutineScheduleStartHour.Get(), 0, 23);

    [ObservableProperty]
    private int routineScheduleStartMinute = Math.Clamp(Configurations.RoutineScheduleStartMinute.Get(), 0, 59);

    [ObservableProperty]
    private int routineScheduleEndHour = Math.Clamp(Configurations.RoutineScheduleEndHour.Get(), 0, 23);

    [ObservableProperty]
    private int routineScheduleEndMinute = Math.Clamp(Configurations.RoutineScheduleEndMinute.Get(), 0, 59);

    partial void OnRoutineScheduleStartHourChanged(int value) => SaveRoutineScheduleTime(value, RoutineScheduleStartMinute, isStart: true);
    partial void OnRoutineScheduleStartMinuteChanged(int value) => SaveRoutineScheduleTime(RoutineScheduleStartHour, value, isStart: true);
    partial void OnRoutineScheduleEndHourChanged(int value) => SaveRoutineScheduleTime(value, RoutineScheduleEndMinute, isStart: false);
    partial void OnRoutineScheduleEndMinuteChanged(int value) => SaveRoutineScheduleTime(RoutineScheduleEndHour, value, isStart: false);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMp4RecordFormat))]
    [NotifyPropertyChangedFor(nameof(IsTranscodedRecordFormat))]
    private int recordFormatIndex = Configurations.RecordFormat.Get() switch
    {
        "TS/FLV -> MP4" => 1,
        "TS/FLV -> MKV" => 2,
        "TS/FLV" or _ => 0,
    };

    private bool isRestoringRecordFormatIndex;

    public bool IsMp4RecordFormat => RecordFormatIndex == 1;

    public bool IsTranscodedRecordFormat => RecordFormatIndex is 1 or 2;

    partial void OnRecordFormatIndexChanged(int value)
    {
        if (isRestoringRecordFormatIndex)
        {
            return;
        }
        if (!IsRecordFormatIndexValid(value))
        {
            isRestoringRecordFormatIndex = true;
            try
            {
                RecordFormatIndex = Configurations.RecordFormat.Get() switch
                {
                    "TS/FLV -> MP4" => 1,
                    "TS/FLV -> MKV" => 2,
                    _ => 0,
                };
            }
            finally
            {
                isRestoringRecordFormatIndex = false;
            }
            return;
        }
        string previousRecordFormat = Configurations.RecordFormat.Get();
        string nextRecordFormat = GetRecordFormatByIndex(value);
        Configurations.RecordFormat.Set(nextRecordFormat);
        if (ShouldApplyPendingRecordingFormatChange(previousRecordFormat, value))
        {
            _ = ApplyPendingRecordingFormatAsync(previousRecordFormat, nextRecordFormat, IsRemoveTs);
        }
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    private static async Task ApplyPendingRecordingFormatAsync(string previousRecordFormat, string nextRecordFormat, bool isRemoveTs)
    {
        try
        {
            PendingOptionsUpdateResult result = await RecordingRecoveryService.UpdatePendingOptionsForGlobalChangeAsync(new RoomRecordingOptions
            {
                RecordFormat = nextRecordFormat,
                IsRemoveTs = isRemoveTs,
                IsOptimizeAudio = Configurations.IsOptimizeAudio.Get(),
            });
            if (!string.IsNullOrWhiteSpace(Recorder.GetTargetFormat(nextRecordFormat)))
            {
                await RecordingRecoveryService.QueuePendingProcessingAsync();
                AppSessionLogger.Event("info", "settings", "pending_conversion_started_by_format_change", "pending recordings were processed after enabling automatic conversion", new
                {
                    previousRecordFormat,
                    nextRecordFormat,
                    updated = result.Updated,
                });
                return;
            }

            AppSessionLogger.Event("info", "settings", "conversion_cancelled_by_raw_format", "active conversion was cancelled because recording format changed to raw", new
            {
                previousRecordFormat,
                nextRecordFormat,
                cancelled = result.Cancelled,
                updated = result.Updated,
                deferred = result.Deferred,
            });
            if (result.Deferred > 0)
            {
                Toast.Warning("TranscodeStopPending".Tr());
            }
            else if (result.Cancelled > 0)
            {
                Toast.Information("TranscodeStopCompleted".Tr());
            }
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            Toast.Warning("TranscodeStopFailed".Tr());
        }
    }

    internal static bool ShouldCancelConversionsOnRecordFormatChange(string? previousRecordFormat, int nextRecordFormatIndex)
    {
        return IsRecordFormatIndexValid(nextRecordFormatIndex)
            && !string.IsNullOrWhiteSpace(Recorder.GetTargetFormat(previousRecordFormat ?? string.Empty))
            && string.IsNullOrWhiteSpace(Recorder.GetTargetFormat(GetRecordFormatByIndex(nextRecordFormatIndex)));
    }

    internal static bool ShouldApplyPendingRecordingFormatChange(string? previousRecordFormat, int nextRecordFormatIndex)
    {
        return IsRecordFormatIndexValid(nextRecordFormatIndex)
            && !string.Equals(previousRecordFormat, GetRecordFormatByIndex(nextRecordFormatIndex), StringComparison.Ordinal);
    }

    internal static bool IsRecordFormatIndexValid(int value)
    {
        return value is >= 0 and <= 2;
    }

    private static string GetRecordFormatByIndex(int value)
    {
        return value switch
        {
            1 => "TS/FLV -> MP4",
            2 => "TS/FLV -> MKV",
            0 or _ => "TS/FLV",
        };
    }

    [ObservableProperty]
    private bool isRemoveTs = Configurations.IsRemoveTs.Get();

    partial void OnIsRemoveTsChanged(bool value)
    {
        Configurations.IsRemoveTs.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private bool isOptimizeAudio = Configurations.IsOptimizeAudio.Get();

    partial void OnIsOptimizeAudioChanged(bool value)
    {
        Configurations.IsOptimizeAudio.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private bool isToSegment = Configurations.IsToSegment.Get();

    partial void OnIsToSegmentChanged(bool value)
    {
        Configurations.IsToSegment.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SegmentTimeValueLabel))]
    private double segmentTimeValue = SegmentTimeUnitHelper.ToDisplayValue(Configurations.SegmentTime.Get(), GetInitialSegmentTimeUnitIndex());

    private bool isUpdatingSegmentTime;

    partial void OnSegmentTimeValueChanged(double value)
    {
        if (isUpdatingSegmentTime)
        {
            return;
        }

        ApplySegmentValue(value, SegmentTimeUnitIndex);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SegmentTimeValueLabel))]
    private int segmentTimeUnitIndex = GetInitialSegmentTimeUnitIndex();

    public string SegmentTimeValueLabel => SegmentTimeUnitHelper.IsSizeUnit(SegmentTimeUnitIndex) ? "SegmentSizeLabel".Tr() : "SegmentDurationLabel".Tr();

    partial void OnSegmentTimeUnitIndexChanged(int value)
    {
        int next = SegmentTimeUnitHelper.NormalizeUnit(value);
        if (next != value)
        {
            SegmentTimeUnitIndex = next;
            return;
        }

        int previous = SegmentTimeUnitHelper.NormalizeUnit(Configurations.SegmentTimeUnit.Get());
        bool canConvert = SegmentTimeUnitHelper.IsTimeUnit(previous) == SegmentTimeUnitHelper.IsTimeUnit(next);
        double displayValue = canConvert
            ? SegmentTimeUnitHelper.ConvertDisplayValue(Configurations.SegmentTime.Get(), previous, next)
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

        ApplySegmentValue(displayValue, next);
    }

    private static int GetInitialSegmentTimeUnitIndex()
    {
        int configuredUnit = SegmentTimeUnitHelper.NormalizeUnit(Configurations.SegmentTimeUnit.Get());
        return configuredUnit == SegmentTimeUnitHelper.Milliseconds
            ? SegmentTimeUnitHelper.Seconds
            : configuredUnit;
    }

    private static void ApplySegmentValue(double value, int unitIndex)
    {
        int normalizedUnit = SegmentTimeUnitHelper.NormalizeUnit(unitIndex);
        Configurations.SegmentTime.Set(SegmentTimeUnitHelper.ToConfigValue(value, normalizedUnit));
        Configurations.SegmentTimeUnit.Set(normalizedUnit);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private string saveFolder = Configurations.SaveFolder.Get();

    partial void OnSaveFolderChanged(string value)
    {
        Configurations.SaveFolder.Set(value);
        ConfigurationSaveScheduler.Request();
        RecordingCleanupService.QueueRun();
    }

    [ObservableProperty]
    private int saveFolderPathLevelIndex = Math.Clamp(Configurations.SaveFolderPathLevel.Get(), 0, 3);

    partial void OnSaveFolderPathLevelIndexChanged(int value)
    {
        int next = Math.Clamp(value, 0, 3);
        if (next != value)
        {
            SaveFolderPathLevelIndex = next;
            return;
        }

        Configurations.SaveFolderPathLevel.Set(next);
        ConfigurationSaveScheduler.Request();
    }

    [ObservableProperty]
    private string saveFileNameCustomRule = NormalizeSaveFileNameCustomRule(Configurations.SaveFileNameCustomRule.Get());

    private static string NormalizeSaveFileNameCustomRule(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, DefaultSaveFileNameCustomRule, StringComparison.Ordinal)
            ? string.Empty
            : value;
    }

    partial void OnSaveFileNameCustomRuleChanged(string value)
    {
        Configurations.SaveFileNameCustomRule.Set(value ?? string.Empty);
        ConfigurationSaveScheduler.Request();
    }

    [RelayCommand]
    private void AppendSaveFileNameToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

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

    [ObservableProperty]
    private bool isDataRetentionEnabled = Configurations.IsDataRetentionEnabled.Get();

    partial void OnIsDataRetentionEnabledChanged(bool value)
    {
        Configurations.IsDataRetentionEnabled.Set(value);
        ConfigurationSaveScheduler.Request();

        if (value)
        {
            RecordingCleanupService.QueueRun();
        }
    }

    [ObservableProperty]
    private double dataRetentionValue = Math.Max(1, Configurations.DataRetentionValue.Get());

    partial void OnDataRetentionValueChanged(double value)
    {
        int next = Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
        if (Math.Abs(next - value) > double.Epsilon)
        {
            DataRetentionValue = next;
            return;
        }

        Configurations.DataRetentionValue.Set(next);
        ConfigurationSaveScheduler.Request();
        RecordingCleanupService.QueueRun();
    }

    [ObservableProperty]
    private int dataRetentionUnitIndex = DataRetentionUnitHelper.NormalizeUnit(Configurations.DataRetentionUnit.Get());

    partial void OnDataRetentionUnitIndexChanged(int value)
    {
        int next = DataRetentionUnitHelper.NormalizeUnit(value);
        if (next != value)
        {
            DataRetentionUnitIndex = next;
            return;
        }

        Configurations.DataRetentionUnit.Set(next);
        ConfigurationSaveScheduler.Request();
        RecordingCleanupService.QueueRun();
    }

    [RelayCommand]
    private void SelectSaveFolder()
    {
        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
        };

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            SaveFolder = dialog.FileName;
        }
    }

    [RelayCommand]
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    private async Task OpenSaveFolderAsync()
    {
        try
        {
            await Launcher.LaunchFolderAsync(
                await StorageFolder.GetFolderFromPathAsync(
                    SaveFolderHelper.GetSaveFolder(Configurations.SaveFolder.Get())
                )
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Warning("OpenSaveFolderFailed".Tr(e.Message));
        }
    }

    [ObservableProperty]
    private bool isUseKeepAwake = Configurations.IsUseKeepAwake.Get();

    partial void OnIsUseKeepAwakeChanged(bool value)
    {
        if (value)
        {
            // Start keep awake
            _ = Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS | Kernel32.EXECUTION_STATE.ES_SYSTEM_REQUIRED | Kernel32.EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
        }
        else
        {
            // Stop keep awake
            _ = Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS);
        }
        Configurations.IsUseKeepAwake.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private bool isUseAutoShutdown = Configurations.IsUseAutoShutdown.Get();

    partial void OnIsUseAutoShutdownChanged(bool value)
    {
        Configurations.IsUseAutoShutdown.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private int autoShutdownTimeHour = AutoShutdownSchedule.GetTimePart(Configurations.AutoShutdownTime.Get(), 0, 23);

    partial void OnAutoShutdownTimeHourChanged(int value)
    {
        int normalized = Math.Clamp(value, 0, 23);
        if (normalized != value)
        {
            AutoShutdownTimeHour = normalized;
            return;
        }

        Configurations.AutoShutdownTime.Set($"{normalized:D2}:{AutoShutdownTimeMinute:D2}");
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private int autoShutdownTimeMinute = AutoShutdownSchedule.GetTimePart(Configurations.AutoShutdownTime.Get(), 1, 59);

    partial void OnAutoShutdownTimeMinuteChanged(int value)
    {
        int normalized = Math.Clamp(value, 0, 59);
        if (normalized != value)
        {
            AutoShutdownTimeMinute = normalized;
            return;
        }

        Configurations.AutoShutdownTime.Set($"{AutoShutdownTimeHour:D2}:{normalized:D2}");
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private bool isAutoShutdownAfterTranscode = Configurations.IsAutoShutdownAfterTranscode.Get();

    partial void OnIsAutoShutdownAfterTranscodeChanged(bool value)
    {
        Configurations.IsAutoShutdownAfterTranscode.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private bool isAutoShutdownComputer = Configurations.IsAutoShutdownComputer.Get();

    partial void OnIsAutoShutdownComputerChanged(bool value)
    {
        Configurations.IsAutoShutdownComputer.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private bool isUseProxy = Configurations.IsUseProxy.Get();

    partial void OnIsUseProxyChanged(bool value)
    {
        Configurations.IsUseProxy.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [ObservableProperty]
    private string proxyUrl = Configurations.ProxyUrl.Get();

    partial void OnProxyUrlChanged(string value)
    {
        Configurations.ProxyUrl.Set(value);
        ConfigurationSaveScheduler.Request();
        NotifyRuntimeConfigurationChanged();
    }

    [RelayCommand]
    private async Task CheckProxyUrlAsync()
    {
        if (!TryCreateProxyUri(ProxyUrl, out Uri? proxyUri, out string errorKey))
        {
            Toast.Error(errorKey.Tr());
            return;
        }

        HttpClientHandler httpClientHandler = new()
        {
            Proxy = new WebProxy(proxyUri),
            UseProxy = true
        };

        using HttpClient httpClient = new(httpClientHandler);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync("https://www.google.com", timeout.Token);
            response.EnsureSuccessStatusCode();

            Toast.Success("ProxySuccOfStatusCode".Tr(response.StatusCode));
        }
        catch (HttpRequestException e)
        {
            Toast.Error("ProxyErrorOfExceptionMessage".Tr(e.Message));
        }
        catch (OperationCanceledException)
        {
            Toast.Error("ProxyErrorOfExceptionMessage".Tr("Timeout"));
        }
    }

    internal static bool TryCreateProxyUri(string? value, [NotNullWhen(true)] out Uri? proxyUri, out string errorKey)
    {
        proxyUri = null;
        errorKey = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            errorKey = "ProxyErrorOfEmptyUrl";
            return false;
        }

        string url = ProxyAddress.Normalize(value);

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            errorKey = "ProxyErrorOfFormat";
            return false;
        }

        if (!HasExplicitPort(uri))
        {
            errorKey = "ProxyErrorOfMissHostOrPort";
            return false;
        }

        if (uri.Port <= 0 || uri.Port > 65535)
        {
            errorKey = "ProxyErrorOfPortOutOfRange";
            return false;
        }

        proxyUri = new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;
        return true;
    }

    private static bool HasExplicitPort(Uri uri)
    {
        string authority = uri.Authority;
        int userInfoIndex = authority.LastIndexOf('@');

        if (userInfoIndex >= 0)
        {
            authority = authority[(userInfoIndex + 1)..];
        }

        if (authority.StartsWith("[", StringComparison.Ordinal))
        {
            return authority.Contains("]:", StringComparison.Ordinal);
        }

        return authority.Count(character => character == ':') == 1;
    }

    [ObservableProperty]
    private string cookieChina = SecretProtector.GetChinaCookie();

    partial void OnCookieChinaChanged(string value)
    {
        Configurations.CookieChina.Set(SecretProtector.Protect(value));
        ConfigurationSaveScheduler.Request();
    }

    [RelayCommand]
    private async Task OpenHowToGetCookieChinaAsync()
    {
        string html = ResourcesProvider.GetString("pack://application:,,,/Emerde;component/Assets/GETCOOKIE_DOUYIN.html");
        string filePath = Path.GetFullPath(ConfigurationSpecialPath.GetPath("GETCOOKIE_DOUYIN.html", AppConfig.PackName));

        AtomicFile.WriteAllText(filePath, html);

        // TODO: Implement for other platforms
        await Launcher.LaunchUriAsync(new Uri($"file://{filePath}"));
    }

    [RelayCommand]
    private void AcquirePlatformCookie(PlatformCookieItem? item)
    {
        if (item == null)
        {
            return;
        }

        if (!PlatformCookieAcquisition.TryGetProfile(item.PlatformName, out PlatformCookieAcquisitionProfile? profile)
            || profile == null)
        {
            Toast.Warning("CookieLoginUnsupported".Tr());
            return;
        }

        try
        {
            PlatformCookieLoginWindow window = new(profile, item.DisplayName, OwnerWindow);
            if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.AcquiredCookieHeader))
            {
                item.Cookie = window.AcquiredCookieHeader;
                Toast.Success("CookieLoginSaved".Tr(item.DisplayName));
            }
        }
        catch (Exception exception)
        {
            AppSessionLogger.Event("error", "settings", "platform_cookie_window_failed", exception.Message, new
            {
                item.PlatformName,
                type = exception.GetType().Name,
            });
            Toast.Error("CookieLoginOpenFailed".Tr(exception.Message));
        }
    }

    [ObservableProperty]
    private string cookieOversea = SecretProtector.GetOverseaCookie();

    partial void OnCookieOverseaChanged(string value)
    {
        Configurations.CookieOversea.Set(SecretProtector.Protect(value));
        ConfigurationSaveScheduler.Request();
    }

    [RelayCommand]
    private async Task OpenHowToGetCookieOverseaAsync()
    {
        string html = ResourcesProvider.GetString("pack://application:,,,/Emerde;component/Assets/GETCOOKIE_TIKTOK.html");
        string filePath = Path.GetFullPath(ConfigurationSpecialPath.GetPath("GETCOOKIE_TIKTOK.html", AppConfig.PackName));

        AtomicFile.WriteAllText(filePath, html);

        // TODO: Implement for other platforms
        await Launcher.LaunchUriAsync(new Uri($"file://{filePath}"));
    }

    [ObservableProperty]
    private string userAgent = Configurations.UserAgent.Get();

    partial void OnUserAgentChanged(string value)
    {
        Configurations.UserAgent.Set(value);
        ConfigurationSaveScheduler.Request();
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        Process.Start(new ProcessStartInfo()
        {
            FileName = AppPaths.LogsDirectory,
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        bool isUiXEnabled = UiXDialogContent.IsEnabled;
        UiXExportLogsContent? uiXContent = isUiXEnabled ? new UiXExportLogsContent() : null;
        ContentDialog dialog = new()
        {
            Title = "ExportLogsTitle".Tr(),
            Content = uiXContent is not null ? uiXContent : "ExportLogsPrompt".Tr(),
            CloseButtonText = "ButtonOfCancel".Tr(),
            SecondaryButtonText = isUiXEnabled ? string.Empty : "ExportToday".Tr(),
            PrimaryButtonText = isUiXEnabled ? "Export".Tr() : "ExportAll".Tr(),
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current?.TryFindResource("EmerdeContentDialogStyle") as System.Windows.Style,
        };

        using DialogBlurScope blurScope = isUiXEnabled
            ? DialogBlurScope.ForLightDismiss(OwnerWindow, dialog)
            : DialogBlurScope.ForDialog(OwnerWindow, dialog);
        ContentDialogResult result = await WindowSizing.ShowContentDialogAsync(dialog, OwnerWindow);
        if (isUiXEnabled && result == ContentDialogResult.Primary)
        {
            await ExportLogsToArchiveAsync(uiXContent!.TodayOnly);
        }
        else if (result == ContentDialogResult.Primary)
        {
            await ExportLogsToArchiveAsync(todayOnly: false);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await ExportLogsToArchiveAsync(todayOnly: true);
        }
    }

    private static async Task ExportLogsToArchiveAsync(bool todayOnly)
    {
        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
            EnsurePathExists = true,
            Title = "ChooseLogArchivePath".Tr(),
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }

        try
        {
            string targetDirectory = dialog.FileName;
            string exportPath = await Task.Run(() => todayOnly
                ? LogExporter.ExportToday(targetDirectory)
                : LogExporter.ExportAll(targetDirectory));
            AppSessionLogger.Write($"logs exported to {exportPath}");
            Toast.Success("LogsExported".Tr(exportPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error("LogExportFailed".Tr(e.Message));
        }
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        Directory.CreateDirectory(AppPaths.ActiveConfigDirectory);
        Process.Start(new ProcessStartInfo()
        {
            FileName = AppPaths.ActiveConfigDirectory,
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        await RestoreConfigAsync();
    }

    [RelayCommand]
    private void ExportConfig()
    {
        using CommonSaveFileDialog dialog = new()
        {
            DefaultExtension = "yaml",
            DefaultFileName = $"config-{DateTime.Now:yyyyMMdd_HHmmss}.yaml",
            InitialDirectory = AppPaths.ActiveConfigDirectory,
            Title = "ExportConfigTitle".Tr(),
        };

        dialog.Filters.Add(new CommonFileDialogFilter("YAML", "*.yaml;*.yml"));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }

        try
        {
            string exportPath = ConfigFileManager.Export(dialog.FileName);
            AppSessionLogger.Write($"config exported to {exportPath}");
            Toast.Success("ConfigExported".Tr());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error("ConfigExportFailed".Tr(e.Message));
        }
    }

    [RelayCommand]
    private async Task RestoreConfigAsync()
    {
        await RestoreConfigCoreAsync(null);
    }

    internal static Task RestoreConfigFromDroppedFileAsync(System.Windows.Window? owner, string filePath)
    {
        SettingsViewModel viewModel = new()
        {
            OwnerWindow = owner,
        };
        return viewModel.RestoreConfigCoreAsync(filePath);
    }

    private async Task RestoreConfigCoreAsync(string? initialImportPath)
    {
        ConfigRestoreContentDialog content = new(BuildConfigRestoreOptions());
        ContentDialog dialog = new()
        {
            Title = "RestoreConfigTitle".Tr(),
            Content = content,
            PrimaryButtonText = GetConfigRestorePrimaryButtonText(content.SelectedOption),
            CloseButtonText = "ButtonOfCancel".Tr(),
            DefaultButton = ContentDialogButton.Primary,
            FocusVisualStyle = null,
            Style = Application.Current?.TryFindResource("EmerdeContentDialogStyle") as System.Windows.Style,
        };

        content.SelectionChanged += (_, _) =>
        {
            dialog.PrimaryButtonText = GetConfigRestorePrimaryButtonText(content.SelectedOption);
        };
        content.ImportButtonClicked += (_, _) => AddImportedConfigToRestoreDialog(content);
        content.ConfigFileDropped += (_, e) => AddImportedConfigToRestoreDialog(content, e.FilePath);
        if (!string.IsNullOrWhiteSpace(initialImportPath))
        {
            if (!AddImportedConfigToRestoreDialog(content, initialImportPath))
            {
                return;
            }
        }

        double ownerWidth = OwnerWindow?.ActualWidth > 1d ? OwnerWindow.ActualWidth : System.Windows.SystemParameters.WorkArea.Width;
        double ownerHeight = OwnerWindow?.ActualHeight > 1d ? OwnerWindow.ActualHeight : System.Windows.SystemParameters.WorkArea.Height;
        double targetWidth = Math.Min(750d, Math.Max(320d, WindowSizing.RoundLayoutValue(ownerWidth - 96d)));
        double targetHeight = Math.Min(608d, Math.Max(320d, WindowSizing.RoundLayoutValue(ownerHeight - 96d)));
        dialog.Resources["EmerdeWideContentDialog"] = true;
        LocalSettingsContentDialog.ApplyWideDialogVisualSize(dialog, targetWidth, targetHeight);

        ContentDialogResult result;
        try
        {
            using DialogBlurScope blurScope = DialogBlurScope.ForLightDismiss(OwnerWindow, dialog);
            result = await WindowSizing.ShowContentDialogAsync(dialog, OwnerWindow);
        }
        finally
        {
            LocalSettingsContentDialog.ClearWideDialogVisualSize(dialog);
        }
        if (result != ContentDialogResult.Primary || content.SelectedOption is not ConfigRestoreOption selected)
        {
            return;
        }

        if (selected.Action == ConfigRestoreOptionAction.Reset)
        {
            await ResetConfigCoreAsync(confirmBeforeReset: false);
            return;
        }

        if (selected.Action == ConfigRestoreOptionAction.Import)
        {
            await ImportSelectedConfigAsync(selected);
            return;
        }

        try
        {
            string backupPath = ConfigFileManager.RestoreBackup(selected.FilePath);
            AppSessionLogger.Write($"config restored from {selected.FilePath}; backup={backupPath}");
            Toast.Success("ConfigRestored".Tr());
            await RestartIfConfirmedAsync(BuildConfigChangedRestartMessage("ConfigRestored".Tr(), backupPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error("ConfigRestoreFailed".Tr(e.Message));
        }
    }

    private static string GetConfigRestorePrimaryButtonText(ConfigRestoreOption? option)
    {
        return option?.Action switch
        {
            ConfigRestoreOptionAction.Reset => "Reset".Tr(),
            ConfigRestoreOptionAction.Import => "Import".Tr(),
            _ => "Restore".Tr(),
        };
    }

    private bool AddImportedConfigToRestoreDialog(ConfigRestoreContentDialog content, string? sourcePath = null)
    {
        string? importPath = sourcePath ?? SelectConfigurationFileForImport();
        if (string.IsNullOrWhiteSpace(importPath))
        {
            return false;
        }

        try
        {
            ConfigurationBackupPoint point = ConfigFileManager.StoreImportedConfiguration(importPath);
            if (content.SelectOptionByFilePath(point.FilePath))
            {
                AppSessionLogger.Write($"config import reused existing restore point from {importPath}; stored={point.FilePath}");
                Toast.Information("ConfigBackupAlreadyExists".Tr());
                return true;
            }

            ConfigRestoreOption option = BuildConfigRestoreOption(point);
            content.AddOptionAndSelect(option);
            AppSessionLogger.Write($"config staged for restore import from {importPath}; stored={point.FilePath}");
            Toast.Success("ConfigAddedToRestoreList".Tr());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error("ConfigImportFailed".Tr(e.Message));
            return false;
        }
    }

    private static string? SelectConfigurationFileForImport()
    {
        using CommonOpenFileDialog dialog = new()
        {
            EnsureFileExists = true,
            IsFolderPicker = false,
            Title = "ImportConfigTitle".Tr(),
        };

        dialog.Filters.Add(new CommonFileDialogFilter("YAML", "*.yaml;*.yml"));

        return dialog.ShowDialog() == CommonFileDialogResult.Ok ? dialog.FileName : null;
    }

    private async Task ImportSelectedConfigAsync(ConfigRestoreOption selected)
    {
        try
        {
            string backupPath = ConfigFileManager.Import(selected.FilePath);
            string[] unavailableSecrets = SecretProtector.GetUnavailableStoredSecretNames();
            AppSessionLogger.Write($"config imported from {selected.FilePath}; backup={backupPath}");
            if (unavailableSecrets.Length == 0)
            {
                Toast.Success("ConfigImported".Tr());
            }
            else
            {
                Toast.Warning("ConfigImportedUnavailableSecrets".Tr(string.Join("、", unavailableSecrets)));
            }
            await RestartIfConfirmedAsync(BuildConfigChangedRestartMessage("ConfigImported".Tr(), backupPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error("ConfigImportFailed".Tr(e.Message));
        }
    }

    [RelayCommand]
    private async Task ResetConfigAsync()
    {
        await ResetConfigCoreAsync(confirmBeforeReset: true);
    }

    private async Task ResetConfigCoreAsync(bool confirmBeforeReset)
    {
        if (confirmBeforeReset)
        {
            bool confirmed;
            if (UiXDialogContent.IsEnabled)
            {
                confirmed = await UiXDialogContent.ConfirmAsync(
                    OwnerWindow,
                    "ConfigReset".Tr(),
                    "ConfirmResetConfig".Tr(),
                    "Yes".Tr(),
                    "No".Tr(),
                    Wpf.Ui.Controls.FontSymbols.Delete,
                    UiXDialogTone.Danger);
            }
            else
            {
                using DialogBlurScope blurScope = DialogBlurScope.ForMessageBox(OwnerWindow);
                confirmed = MessageBox.Question("ConfirmResetConfig".Tr()) == System.Windows.MessageBoxResult.Yes;
            }
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            string[] backupPaths = ConfigFileManager.Reset();
            string backupText = backupPaths.Length == 0 ? "NoConfigFilesToBackup".Tr() : string.Join(Environment.NewLine, backupPaths);
            AppSessionLogger.Write($"config reset; backups={string.Join("|", backupPaths)}");
            Toast.Success("ConfigReset".Tr());
            await RestartIfConfirmedAsync("ConfigResetRestartPrompt".Tr(backupText));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error("ConfigResetFailed".Tr(e.Message));
        }
    }

    private static IReadOnlyList<ConfigRestoreOption> BuildConfigRestoreOptions()
    {
        List<ConfigRestoreOption> options = ConfigFileManager.GetBackupPoints()
            .Select(BuildConfigRestoreOption)
            .ToList();

        options.Add(new ConfigRestoreOption(
            "DefaultConfigTitle".Tr(),
            "DefaultConfigDescription".Tr(),
            AppPaths.ActiveConfigDirectory,
            string.Empty,
            "Reset".Tr(),
            ConfigRestoreOptionAction.Reset));

        return options;
    }

    private static ConfigRestoreOption BuildConfigRestoreOption(ConfigurationBackupPoint point)
    {
        bool imported = point.FileName.Contains(".import-", StringComparison.OrdinalIgnoreCase);
        return new ConfigRestoreOption(
            point.FileName,
            point.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            point.FilePath,
            point.FilePath,
            imported ? "UserImport".Tr() : "ConfigBackup".Tr(),
            imported ? ConfigRestoreOptionAction.Import : ConfigRestoreOptionAction.Restore);
    }

    private static string BuildConfigChangedRestartMessage(string actionText, string backupPath)
    {
        string backupText = string.IsNullOrWhiteSpace(backupPath)
            ? "NoMeaningfulConfigBackup".Tr()
            : "CurrentConfigBackup".Tr(backupPath);
        return "ConfigChangedRestartPrompt".Tr(actionText, backupText);
    }

    private static bool IsRoutineScheduleDayEnabled(DayOfWeek day)
    {
        return Configurations.RoutineScheduleDays.Get()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(day.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task RestartIfConfirmedAsync(string message)
    {
        bool confirmed;
        if (UiXDialogContent.IsEnabled)
        {
            confirmed = await UiXDialogContent.ConfirmAsync(
                OwnerWindow,
                "Title".Tr(),
                message,
                "Yes".Tr(),
                "No".Tr(),
                Wpf.Ui.Controls.FontSymbols.PowerButton,
                UiXDialogTone.Information);
        }
        else
        {
            using DialogBlurScope blurScope = DialogBlurScope.ForMessageBox(OwnerWindow);
            confirmed = await MessageBox.QuestionAsync(message) == System.Windows.MessageBoxResult.Yes;
        }
        if (confirmed)
        {
            await TrayIconManager.GetInstance().RestartApplicationAsync(confirmRecording: false);
        }
    }

    private static void NotifyRuntimeConfigurationChanged(bool recheckRooms = false)
    {
        _ = WeakReferenceMessenger.Default.Send(new RuntimeConfigurationChangedMessage(recheckRooms));
    }

    private void SaveRoutineScheduleDays()
    {
        List<string> days = [];

        if (RoutineScheduleMonday) days.Add(DayOfWeek.Monday.ToString());
        if (RoutineScheduleTuesday) days.Add(DayOfWeek.Tuesday.ToString());
        if (RoutineScheduleWednesday) days.Add(DayOfWeek.Wednesday.ToString());
        if (RoutineScheduleThursday) days.Add(DayOfWeek.Thursday.ToString());
        if (RoutineScheduleFriday) days.Add(DayOfWeek.Friday.ToString());
        if (RoutineScheduleSaturday) days.Add(DayOfWeek.Saturday.ToString());
        if (RoutineScheduleSunday) days.Add(DayOfWeek.Sunday.ToString());

        Configurations.RoutineScheduleDays.Set(string.Join(",", days));
        SaveRoutineScheduleChange();
    }

    private void SaveRoutineScheduleDates(bool changedStart)
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

            Configurations.RoutineScheduleStartDate.Set(FormatScheduleDate(RoutineScheduleStartDate));
            Configurations.RoutineScheduleEndDate.Set(FormatScheduleDate(RoutineScheduleEndDate));
        }
        finally
        {
            isUpdatingRoutineScheduleDates = false;
        }
        SaveRoutineScheduleChange();
    }

    private void SaveRoutineScheduleTime(int hour, int minute, bool isStart)
    {
        int normalizedHour = Math.Clamp(hour, 0, 23);
        int normalizedMinute = Math.Clamp(minute, 0, 59);

        if (isStart)
        {
            if (RoutineScheduleStartHour != normalizedHour)
            {
                RoutineScheduleStartHour = normalizedHour;
                return;
            }

            if (RoutineScheduleStartMinute != normalizedMinute)
            {
                RoutineScheduleStartMinute = normalizedMinute;
                return;
            }

            Configurations.RoutineScheduleStartHour.Set(normalizedHour);
            Configurations.RoutineScheduleStartMinute.Set(normalizedMinute);
        }
        else
        {
            if (RoutineScheduleEndHour != normalizedHour)
            {
                RoutineScheduleEndHour = normalizedHour;
                return;
            }

            if (RoutineScheduleEndMinute != normalizedMinute)
            {
                RoutineScheduleEndMinute = normalizedMinute;
                return;
            }

            Configurations.RoutineScheduleEndHour.Set(normalizedHour);
            Configurations.RoutineScheduleEndMinute.Set(normalizedMinute);
        }

        SaveRoutineScheduleChange();
    }

    private static DateTime? ToDateTime(DateOnly? value)
    {
        return value?.ToDateTime(TimeOnly.MinValue);
    }

    private static string FormatScheduleDate(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static void SaveRoutineScheduleChange()
    {
        ConfigurationSaveScheduler.Request();
        GlobalMonitor.RefreshRoutineInterval();
        NotifyRuntimeConfigurationChanged(recheckRooms: true);
    }

    private static int ConvertTimeUnitToMilliseconds(double value, int unitIndex)
    {
        return MonitorTiming.ConvertToMilliseconds(value, unitIndex);
    }

    private static double ConvertMillisecondsToTimeUnit(int milliseconds, int unitIndex)
    {
        return unitIndex switch
        {
            (int)TimeUnitIndexEnum.Hours => milliseconds / 3600000d,
            (int)TimeUnitIndexEnum.Minutes => milliseconds / 60000d,
            (int)TimeUnitIndexEnum.Seconds or _ => milliseconds / 1000d,
        };
    }

    private static int NormalizeRoutineIntervalUnitIndex(int unitIndex)
    {
        return Math.Clamp(unitIndex, (int)TimeUnitIndexEnum.Seconds, (int)TimeUnitIndexEnum.Hours);
    }
}

public sealed partial class PlatformCookieItem : ObservableObject
{
    public PlatformCookieItem(string platformName, string displayName, string initialCookie)
    {
        PlatformName = platformName;
        DisplayName = displayName;
        cookie = initialCookie;
    }

    public string PlatformName { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    private string cookie = string.Empty;

    partial void OnCookieChanged(string value)
    {
        PlatformCookieStore.SetCookie(PlatformName, value);
    }
}

file static class Extensions
{
    public static int IntParse(this string value, int fallback = default)
    {
        if (int.TryParse(value, out int output))
        {
            return output;
        }
        return fallback;
    }
}
