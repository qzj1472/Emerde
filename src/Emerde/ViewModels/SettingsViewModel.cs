using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputedConverters;
using Fischless.Configuration;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using Emerde.Core;
using Emerde.Extensions;
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
    public sealed record UnitOption(int Value, string DisplayName);

    public IReadOnlyList<UnitOption> TimeUnitOptions { get; } =
    [
        new((int)TimeUnitIndexEnum.Milliseconds, "毫秒"),
        new((int)TimeUnitIndexEnum.Seconds, "秒"),
        new((int)TimeUnitIndexEnum.Minutes, "分钟"),
        new((int)TimeUnitIndexEnum.Hours, "小时"),
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

    public string ChinaCookiePlatformsText => "Douyin / Bilibili / Kuaishou / Huya / Douyu / Baidu / MaoerFM / Lianjie / 6Rooms / VVXqiu / Blued / Liuxing / Changliao / Yinbo / Zhihu / PPLive / CatShow / Laixiu / JD / Weibo / Huajiao / Look / Taobao / Xiaohongshu / Kugou / Yingke / AcFun / YY / NeteaseCC / QianduRebo";

    public string OverseaCookiePlatformsText => "TikTok / Bigo / ShowRoom / 17Live / CHZZK / Picarto / LangLive / PandaTV / WinkTV / Twitch / YouTube / Shopee / TwitCasting / Faceit / SOOP / FlexTV / PopkonTV / LiveMe";

    public string DirectStreamPlatformsText => "Direct stream";

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
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private int updateChannelIndex = Math.Clamp(Configurations.UpdateChannel.Get(), 0, 2);

    partial void OnUpdateChannelIndexChanged(int value)
    {
        int next = Math.Clamp(value, 0, 2);
        if (next != value)
        {
            UpdateChannelIndex = next;
            return;
        }

        Configurations.UpdateChannel.Set(next);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionLogStatus))]
    private bool isSessionLogEnabled = Configurations.IsSessionLogEnabled.Get();

    partial void OnIsSessionLogEnabledChanged(bool value)
    {
        Configurations.IsSessionLogEnabled.Set(value);
        ConfigurationManager.Save();

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
        ? "保存本地运行日志，可导出最近或全部日志。"
        : "已关闭运行日志，仅能导出已存在的历史日志。";

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

        Configurations.Language.Set(language);
        ConfigurationManager.Save();
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
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isUseStatusTray = Configurations.IsUseStatusTray.Get();

    partial void OnIsUseStatusTrayChanged(bool value)
    {
        Configurations.IsUseStatusTray.Set(value);
        ConfigurationManager.Save();
        TrayIconManager.GetInstance().UpdateTrayIcon();
    }

    [RelayCommand]
    private void CreateDesktopShortcut()
    {
        ShortcutHelper.CreateShortcutOnDesktop(
            shortcutName: "Emerde",
            targetPath: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName),
            arguments: null!,
            description: "Title".Tr(),
            iconLocation: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName + ".exe"));

        Toast.Success("SuccOp".Tr());
    }

    [ObservableProperty]
    private bool isToNotify = Configurations.IsToNotify.Get();

    partial void OnIsToNotifyChanged(bool value)
    {
        Configurations.IsToNotify.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isToNotifyWithSystem = Configurations.IsToNotifyWithSystem.Get();

    partial void OnIsToNotifyWithSystemChanged(bool value)
    {
        Configurations.IsToNotifyWithSystem.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isToNotifyWithMusic = Configurations.IsToNotifyWithMusic.Get();

    partial void OnIsToNotifyWithMusicChanged(bool value)
    {
        Configurations.IsToNotifyWithMusic.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private string? toNotifyWithMusicPath = Configurations.ToNotifyWithMusicPath.Get();

    partial void OnToNotifyWithMusicPathChanged(string? value)
    {
        Configurations.ToNotifyWithMusicPath.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isToNotifyWithEmail = Configurations.IsToNotifyWithEmail.Get();

    partial void OnIsToNotifyWithEmailChanged(bool value)
    {
        Configurations.IsToNotifyWithEmail.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private string toNotifyWithEmailSmtp = Configurations.ToNotifyWithEmailSmtp.Get();

    partial void OnToNotifyWithEmailSmtpChanged(string value)
    {
        Configurations.ToNotifyWithEmailSmtp.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private string toNotifyWithEmailUserName = Configurations.ToNotifyWithEmailUserName.Get();

    partial void OnToNotifyWithEmailUserNameChanged(string value)
    {
        Configurations.ToNotifyWithEmailUserName.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private string toNotifyWithEmailPassword = Configurations.ToNotifyWithEmailPassword.Get();

    partial void OnToNotifyWithEmailPasswordChanged(string value)
    {
        Configurations.ToNotifyWithEmailPassword.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isToNotifyGotoRoomUrl = Configurations.IsToNotifyGotoRoomUrl.Get();

    partial void OnIsToNotifyGotoRoomUrlChanged(bool value)
    {
        Configurations.IsToNotifyGotoRoomUrl.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isToNotifyGotoRoomUrlAndMute = Configurations.IsToNotifyGotoRoomUrlAndMute.Get();

    partial void OnIsToNotifyGotoRoomUrlAndMuteChanged(bool value)
    {
        Configurations.IsToNotifyGotoRoomUrlAndMute.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isToMonitor = Configurations.IsToMonitor.Get();

    partial void OnIsToMonitorChanged(bool value)
    {
        Configurations.IsToMonitor.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isToRecord = Configurations.IsToRecord.Get();

    partial void OnIsToRecordChanged(bool value)
    {
        Configurations.IsToRecord.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private double routineInterval = ConvertMillisecondsToTimeUnit(
        Configurations.RoutineInterval.Get(),
        Math.Clamp(Configurations.RoutineIntervalUnit.Get(), (int)TimeUnitIndexEnum.Milliseconds, (int)TimeUnitIndexEnum.Hours));

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
    private int routineIntervalUnitIndex = Math.Clamp(
        Configurations.RoutineIntervalUnit.Get(),
        (int)TimeUnitIndexEnum.Milliseconds,
        (int)TimeUnitIndexEnum.Hours);

    partial void OnRoutineIntervalUnitIndexChanged(int value)
    {
        int next = Math.Clamp(value, (int)TimeUnitIndexEnum.Milliseconds, (int)TimeUnitIndexEnum.Hours);
        if (next != value)
        {
            RoutineIntervalUnitIndex = next;
            return;
        }

        isUpdatingRoutineInterval = true;
        try
        {
            RoutineInterval = ConvertMillisecondsToTimeUnit(Configurations.RoutineInterval.Get(), next);
        }
        finally
        {
            isUpdatingRoutineInterval = false;
        }

        Configurations.RoutineIntervalUnit.Set(next);
        ConfigurationManager.Save();
    }

    private void SaveRoutineInterval(double value, int unitIndex)
    {
        int milliseconds = ConvertTimeUnitToMilliseconds(value, unitIndex);
        milliseconds = Math.Max(500, milliseconds);
        GlobalMonitor.RoutinePeriodicWait.Period = TimeSpan.FromMilliseconds(milliseconds);
        Configurations.RoutineInterval.Set(milliseconds);
        Configurations.RoutineIntervalUnit.Set(unitIndex);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRoutineScheduleCustom))]
    [NotifyPropertyChangedFor(nameof(IsRoutineSchedulePreset))]
    private int routineScheduleModeIndex = Math.Clamp(Configurations.RoutineScheduleMode.Get(), 0, 4);

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

        Configurations.RoutineScheduleMode.Set(next);
        ConfigurationManager.Save();
    }

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
    private int recordFormatIndex = Configurations.RecordFormat.Get() switch
    {
        "TS/FLV -> MP4" => 1,
        "TS/FLV -> MKV" => 2,
        "TS/FLV" or _ => 0,
    };

    partial void OnRecordFormatIndexChanged(int value)
    {
        Configurations.RecordFormat.Set(value switch
        {
            1 => "TS/FLV -> MP4",
            2 => "TS/FLV -> MKV",
            0 or _ => "TS/FLV",
        });
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isRemoveTs = Configurations.IsRemoveTs.Get();

    partial void OnIsRemoveTsChanged(bool value)
    {
        Configurations.IsRemoveTs.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isToSegment = Configurations.IsToSegment.Get();

    partial void OnIsToSegmentChanged(bool value)
    {
        Configurations.IsToSegment.Set(value);
        ConfigurationManager.Save();
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

    public string SegmentTimeValueLabel => SegmentTimeUnitHelper.IsSizeUnit(SegmentTimeUnitIndex) ? "分段大小" : "分段时长";

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
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private string saveFolder = Configurations.SaveFolder.Get();

    partial void OnSaveFolderChanged(string value)
    {
        Configurations.SaveFolder.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool saveFolderDistinguishedByAuthors = Configurations.SaveFolderDistinguishedByAuthors.Get();

    partial void OnSaveFolderDistinguishedByAuthorsChanged(bool value)
    {
        Configurations.SaveFolderDistinguishedByAuthors.Set(value);
        ConfigurationManager.Save();
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
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaveFileNameRuleCustom))]
    private int saveFileNameRuleIndex = Math.Clamp(Configurations.SaveFileNameRule.Get(), 0, 4);

    public bool IsSaveFileNameRuleCustom => SaveFileNameRuleIndex == 4;

    partial void OnSaveFileNameRuleIndexChanged(int value)
    {
        int next = Math.Clamp(value, 0, 4);
        if (next != value)
        {
            SaveFileNameRuleIndex = next;
            return;
        }

        Configurations.SaveFileNameRule.Set(next);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private string saveFileNameCustomRule = string.IsNullOrWhiteSpace(Configurations.SaveFileNameCustomRule.Get())
        ? "{主播名}_{录制时间}"
        : Configurations.SaveFileNameCustomRule.Get();

    partial void OnSaveFileNameCustomRuleChanged(string value)
    {
        Configurations.SaveFileNameCustomRule.Set(string.IsNullOrWhiteSpace(value) ? "{主播名}_{录制时间}" : value);
        ConfigurationManager.Save();
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
    private void ResetSaveFileNameRule()
    {
        SaveFileNameCustomRule = "{主播名}_{录制时间}";
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
        // TODO: Implement for other platforms
        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(
                SaveFolderHelper.GetSaveFolder(Configurations.SaveFolder.Get())
            )
        );
    }

    [ObservableProperty]
    private int playerIndex = Configurations.Player.Get() switch
    {
        "ffplay" => 0,
        "system" or _ => 1,
    };

    partial void OnPlayerIndexChanged(int value)
    {
        Configurations.Player.Set(value switch
        {
            0 => "ffplay",
            1 or _ => "system",
        });
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isPlayerRect = Configurations.IsPlayerRect.Get();

    partial void OnIsPlayerRectChanged(bool value)
    {
        Configurations.IsPlayerRect.Set(value);
        ConfigurationManager.Save();
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
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isUseAutoShutdown = Configurations.IsUseAutoShutdown.Get();

    partial void OnIsUseAutoShutdownChanged(bool value)
    {
        Configurations.IsUseAutoShutdown.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private int autoShutdownTimeHour = Configurations.AutoShutdownTime.Get().Split(':')[0].IntParse(fallback: 0);

    partial void OnAutoShutdownTimeHourChanged(int value)
    {
        Configurations.AutoShutdownTime.Set($"{value:D2}:{AutoShutdownTimeMinute:D2}");
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private int autoShutdownTimeMinute = Configurations.AutoShutdownTime.Get().Split(':')[1].IntParse(fallback: 0);

    partial void OnAutoShutdownTimeMinuteChanged(int value)
    {
        Configurations.AutoShutdownTime.Set($"{AutoShutdownTimeHour:D2}:{value:D2}");
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private bool isUseProxy = Configurations.IsUseProxy.Get();

    partial void OnIsUseProxyChanged(bool value)
    {
        Configurations.IsUseProxy.Set(value);
        ConfigurationManager.Save();
    }

    [ObservableProperty]
    private string proxyUrl = Configurations.ProxyUrl.Get();

    partial void OnProxyUrlChanged(string value)
    {
        Configurations.ProxyUrl.Set(value);
        ConfigurationManager.Save();
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

        try
        {
            HttpResponseMessage response = await httpClient.GetAsync("https://www.google.com");
            response.EnsureSuccessStatusCode();

            Toast.Success("ProxySuccOfStatusCode".Tr(response.StatusCode));
        }
        catch (HttpRequestException e)
        {
            Toast.Error("ProxyErrorOfExceptionMessage".Tr(e.Message));
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

        string input = value.Trim();
        string url = input.Contains("://", StringComparison.Ordinal) ? input : $"http://{input}";

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || string.IsNullOrWhiteSpace(uri.Host))
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
    private string cookieChina = Configurations.CookieChina.Get();

    partial void OnCookieChinaChanged(string value)
    {
        Configurations.CookieChina.Set(value);
        ConfigurationManager.Save();
    }

    [RelayCommand]
    private async Task OpenHowToGetCookieChinaAsync()
    {
        string html = ResourcesProvider.GetString("pack://application:,,,/Emerde;component/Assets/GETCOOKIE_DOUYIN.html");
        string filePath = Path.GetFullPath(ConfigurationSpecialPath.GetPath("GETCOOKIE_DOUYIN.html", AppConfig.PackName));

        File.WriteAllText(filePath, html);

        // TODO: Implement for other platforms
        await Launcher.LaunchUriAsync(new Uri($"file://{filePath}"));
    }

    [ObservableProperty]
    private string cookieOversea = Configurations.CookieOversea.Get();

    partial void OnCookieOverseaChanged(string value)
    {
        Configurations.CookieOversea.Set(value);
        ConfigurationManager.Save();
    }

    [RelayCommand]
    private async Task OpenHowToGetCookieOverseaAsync()
    {
        string html = ResourcesProvider.GetString("pack://application:,,,/Emerde;component/Assets/GETCOOKIE_TIKTOK.html");
        string filePath = Path.GetFullPath(ConfigurationSpecialPath.GetPath("GETCOOKIE_TIKTOK.html", AppConfig.PackName));

        File.WriteAllText(filePath, html);

        // TODO: Implement for other platforms
        await Launcher.LaunchUriAsync(new Uri($"file://{filePath}"));
    }

    [ObservableProperty]
    private string userAgent = Configurations.UserAgent.Get();

    partial void OnUserAgentChanged(string value)
    {
        Configurations.UserAgent.Set(value);
        ConfigurationManager.Save();
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
    private void ExportRecentLogs()
    {
        ExportLogs(latest: true);
    }

    [RelayCommand]
    private void ExportAllLogs()
    {
        ExportLogs(latest: false);
    }

    private static void ExportLogs(bool latest)
    {
        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
            EnsurePathExists = true,
            Title = "选择日志导出目录",
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }

        try
        {
            string exportPath = latest
                ? LogExporter.ExportLatest(dialog.FileName)
                : LogExporter.ExportAll(dialog.FileName);
            AppSessionLogger.Write($"logs exported to {exportPath}");
            Toast.Success($"日志已导出：{exportPath}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error($"日志导出失败：{e.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        using CommonOpenFileDialog dialog = new()
        {
            EnsureFileExists = true,
            IsFolderPicker = false,
            Title = "导入配置",
        };

        dialog.Filters.Add(new CommonFileDialogFilter("YAML", "*.yaml;*.yml"));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }

        try
        {
            string backupPath = ConfigFileManager.Import(dialog.FileName);
            AppSessionLogger.Write($"config imported from {dialog.FileName}; backup={backupPath}");
            Toast.Success("配置已导入");
            _ = await MessageBox.InformationAsync($"配置已导入，重启后生效。当前配置备份：{Environment.NewLine}{backupPath}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error($"配置导入失败：{e.Message}");
        }
    }

    [RelayCommand]
    private void ExportConfig()
    {
        using CommonSaveFileDialog dialog = new()
        {
            DefaultExtension = "yaml",
            DefaultFileName = $"config-{DateTime.Now:yyyyMMddHHmmss}.yaml",
            Title = "导出配置",
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
            Toast.Success("配置已导出");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error($"配置导出失败：{e.Message}");
        }
    }

    [RelayCommand]
    private async Task ResetConfigAsync()
    {
        if (MessageBox.Question("确定要重置配置文件吗？当前配置会先备份，重启后生效。") != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            string[] backupPaths = ConfigFileManager.Reset();
            string backupText = backupPaths.Length == 0 ? "没有找到需要备份的配置文件。" : string.Join(Environment.NewLine, backupPaths);
            AppSessionLogger.Write($"config reset; backups={string.Join("|", backupPaths)}");
            Toast.Success("配置已重置");
            _ = await MessageBox.InformationAsync($"配置已重置，重启后生效。配置备份：{Environment.NewLine}{backupText}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            Toast.Error($"配置重置失败：{e.Message}");
        }
    }

    private static bool IsRoutineScheduleDayEnabled(DayOfWeek day)
    {
        return Configurations.RoutineScheduleDays.Get()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(day.ToString(), StringComparer.OrdinalIgnoreCase);
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
        ConfigurationManager.Save();
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

        ConfigurationManager.Save();
    }

    private static int ConvertTimeUnitToMilliseconds(double value, int unitIndex)
    {
        double multiplier = unitIndex switch
        {
            (int)TimeUnitIndexEnum.Hours => 3600000d,
            (int)TimeUnitIndexEnum.Minutes => 60000d,
            (int)TimeUnitIndexEnum.Seconds => 1000d,
            (int)TimeUnitIndexEnum.Milliseconds or _ => 1d,
        };

        return (int)Math.Clamp(Math.Round(value * multiplier, MidpointRounding.AwayFromZero), 1, int.MaxValue);
    }

    private static double ConvertMillisecondsToTimeUnit(int milliseconds, int unitIndex)
    {
        return unitIndex switch
        {
            (int)TimeUnitIndexEnum.Hours => milliseconds / 3600000d,
            (int)TimeUnitIndexEnum.Minutes => milliseconds / 60000d,
            (int)TimeUnitIndexEnum.Seconds => milliseconds / 1000d,
            (int)TimeUnitIndexEnum.Milliseconds or _ => milliseconds,
        };
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
