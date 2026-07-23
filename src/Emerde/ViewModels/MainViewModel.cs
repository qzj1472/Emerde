using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ComputedConverters;
using Fischless.Configuration;
using Flucli;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.Models;
using Emerde.Threading;
using Emerde.Views;
using Vanara.PInvoke;
using Windows.Storage;
using Windows.System;
using Wpf.Ui.Violeta.Controls;
using Wpf.Ui.Violeta.Threading;
using CheckBox = System.Windows.Controls.CheckBox;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Emerde.ViewModels;

[ObservableObject]
public partial class MainViewModel : ReactiveObject, IDisposable
{
    internal const string AllPlatformFilter = "";
    internal const int RoomHistoryLimit = 200;
    private const long ManualRefreshCooldownMilliseconds = 5000;
    private const long PreviewQualityRefreshCooldownMilliseconds = 30000;
    private const int NetworkThroughputRoundCount = 3;
    private const int NetworkThroughputConnectionsPerEndpoint = 2;
    private const long NetworkThroughputWarmupBytesPerConnection = 1L * 1024L * 1024L;
    private const long NetworkThroughputBytesPerConnectionRound = 16L * 1024L * 1024L;
    private const long NetworkThroughputProbeBytes = 8L * 1024L * 1024L;
    private static readonly NetworkThroughputEndpoint[] NetworkThroughputTestEndpoints =
    [
        new("TUNA", NetworkThroughputRegion.Domestic, "https://mirrors.tuna.tsinghua.edu.cn/ubuntu-releases/24.04.4/ubuntu-24.04.4-desktop-amd64.iso", true),
        new("Aliyun", NetworkThroughputRegion.Domestic, "https://mirrors.aliyun.com/ubuntu-releases/24.04.4/ubuntu-24.04.4-desktop-amd64.iso", true),
        new("Ubuntu", NetworkThroughputRegion.Overseas, "https://releases.ubuntu.com/24.04.4/ubuntu-24.04.4-desktop-amd64.iso", true),
        new("OVH", NetworkThroughputRegion.Overseas, "https://proof.ovh.net/files/100Mb.dat", true),
        new("Hetzner", NetworkThroughputRegion.Overseas, "https://fsn1-speed.hetzner.com/100MB.bin", true),
    ];
    private static readonly HashSet<string> OverseasPlatformNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "17Live",
        "Bigo",
        "CatShow",
        "CHZZK",
        "Faceit",
        "FlexTV",
        "LangLive",
        "LiveMe",
        "Look",
        "Picarto",
        "PopkonTV",
        "Shopee",
        "ShowRoom",
        "SOOP",
        "TikTok",
        "TwitCasting",
        "Twitch",
        "WinkTV",
        "YouTube",
    };
    private const string PreviewStreamQualityPreference = StreamQualityCatalog.Original;
    private const string AutoShutdownRecordBlockReason = "auto-shutdown";
    protected internal ForeverDispatcherTimer DispatcherTimer { get; }

    protected internal ForeverDispatcherTimer AutoShutdownDispatcherTimer { get; }

    private readonly LivePreviewPlayer livePreviewPlayer = new();
    private readonly SemaphoreSlim previewTransitionGate = new(1, 1);
    private readonly object previewTransitionSync = new();
    private readonly object manualRefreshCooldownLock = new();
    private readonly Dictionary<string, long> previewQualityRefreshTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<RoomHistoryEntry> roomHistoryUndoStack = [];
    private readonly Stack<RoomHistoryEntry> roomHistoryRedoStack = [];
    private CancellationTokenSource? previewTransitionCancellation;
    private long lastManualRefreshTimestamp;
    private RoomStatusReactive? lastSelectedRoom;
    private readonly AutoShutdownSchedule autoShutdownSchedule = new();
    private AutoShutdownContentDialog? autoShutdownDialog;
    private bool forceShutdownAfterTranscode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomePageSelected))]
    [NotifyPropertyChangedFor(nameof(IsVideoListPageSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPageSelected))]
    [NotifyPropertyChangedFor(nameof(IsAboutPageSelected))]
    private int selectedMainPageIndex;

    public bool IsHomePageSelected => SelectedMainPageIndex == 0;

    public bool IsVideoListPageSelected => SelectedMainPageIndex == 1;

    public bool IsSettingsPageSelected => SelectedMainPageIndex == 2;

    public bool IsAboutPageSelected => SelectedMainPageIndex == 3;

    partial void OnSelectedMainPageIndexChanged(int value)
    {
        if (value < 0 || value > 3)
        {
            SelectedMainPageIndex = 0;
            return;
        }

        if (value != 2)
        {
            ReloadConfigurationStatus();
        }
    }

    [ObservableProperty]
    private ReactiveCollection<RoomStatusReactive> roomStatuses = [];

    public ICollectionView RoomStatusesView { get; }

    public IReadOnlyList<string> PlatformFilterOptions => BuildPlatformFilterOptions(RoomStatuses);

    internal static string[] BuildPlatformFilterOptions(IEnumerable<RoomStatusReactive> rooms)
    {
        return
        [
            AllPlatformFilter,
            .. rooms
            .Select(room => room.PlatformName)
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(global::Emerde.Core.PlatformDisplayName.Get, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformSummaryText))]
    [NotifyPropertyChangedFor(nameof(IsPlatformFilterActive))]
    private string selectedPlatformFilter = AllPlatformFilter;

    public bool IsPlatformFilterActive => SelectedPlatformFilter != AllPlatformFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRoomSortByAddedAt))]
    private bool isRoomSortByName;

    public bool IsRoomSortByAddedAt => !IsRoomSortByName;

    partial void OnSelectedPlatformFilterChanged(string value)
    {
        RoomStatusesView.Refresh();
    }

    public string GetPlatformFilterDisplayName(string value)
    {
        return value == AllPlatformFilter ? "全部显示" : global::Emerde.Core.PlatformDisplayName.Get(value);
    }

    public void EnsureSelectedPlatformFilterAvailable()
    {
        if (SelectedPlatformFilter != AllPlatformFilter
            && !RoomStatuses.Any(room => string.Equals(room.PlatformName, SelectedPlatformFilter, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedPlatformFilter = AllPlatformFilter;
        }

        OnPropertyChanged(nameof(PlatformFilterOptions));
    }

    public string PlatformSummaryText
    {
        get
        {
            int totalCount = RoomStatuses.Count;
            int streamingCount = RoomStatuses.Count(room => room.StreamStatus == StreamStatus.Streaming);
            int platformCount = RoomStatuses
                .Select(room => room.PlatformName)
                .Where(platform => !string.IsNullOrWhiteSpace(platform))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return "PlatformSummaryFormat".Tr(totalCount, streamingCount, platformCount);
        }
    }

    [ObservableProperty]
    private RoomStatusReactive selectedItem = new();

    [ObservableProperty]
    private bool isRoomCardSelectionVisible = true;

    [ObservableProperty]
    private bool isRoomMultiSelectMode;

    public int SelectedRoomCount => RoomStatuses.Count(room => room.IsSelected);

    public bool HasSelectedRooms => SelectedRoomCount > 0;

    public bool CanUndoRoomSelection => roomHistoryUndoStack.Count > 0;

    public bool CanRedoRoomSelection => roomHistoryRedoStack.Count > 0;

    public string SelectedRoomSummary => $"已选择 {SelectedRoomCount} 个直播间";

    [ObservableProperty]
    private bool isRefreshingSelectedRoomInfo = false;

    partial void OnSelectedItemChanged(RoomStatusReactive value)
    {
        IsRoomCardSelectionVisible = true;
        OnPropertyChanged(nameof(CanPreviewSelectedRoom));
    }

    [ObservableProperty]
    private bool isRecording = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewIdle))]
    [NotifyPropertyChangedFor(nameof(IsPreviewPlaying))]
    [NotifyPropertyChangedFor(nameof(PreviewPlaybackToolTip))]
    private bool isPreviewing = false;

    [ObservableProperty]
    private RoomStatusReactive? previewingRoom;

    [ObservableProperty]
    private bool isPreviewDetached = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewMuteToolTip))]
    private bool isPreviewMuted = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewPlaying))]
    [NotifyPropertyChangedFor(nameof(PreviewPlaybackToolTip))]
    private bool isPreviewPaused = false;

    [ObservableProperty]
    private bool isPreviewTransitioning = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LivePreviewStatusText))]
    private LivePreviewStatus livePreviewStatus = LivePreviewStatus.Idle;

    public MediaPlayer LivePreviewMediaPlayer => livePreviewPlayer.MediaPlayer;

    public bool IsPreviewIdle => !IsPreviewing;

    public bool IsPreviewPlaying => IsPreviewing && !IsPreviewPaused;

    public bool CanPreviewSelectedRoom => SelectedItem?.CanPreview ?? false;

    public string PreviewPlaybackToolTip => IsPreviewPlaying ? "PreviewPause".Tr() : "ButtonOfPlay".Tr();

    public string PreviewMuteToolTip => IsPreviewMuted ? "PreviewUnmute".Tr() : "PreviewMute".Tr();

    public string LivePreviewStatusText => LivePreviewStatus switch
    {
        LivePreviewStatus.Idle => "LivePreviewIdle".Tr(),
        LivePreviewStatus.Ready => "LivePreviewReady".Tr(),
        LivePreviewStatus.Playing => "LivePreviewPlaying".Tr(),
        LivePreviewStatus.Unavailable => "LivePreviewUnavailable".Tr(),
        LivePreviewStatus.Error => "LivePreviewError".Tr(),
        _ => "LivePreviewIdle".Tr(),
    };

    private static Room[] NormalizeStoredRooms(Room[] rooms)
    {
        List<Room> normalizedRooms = [];
        HashSet<string> seenUrls = new(StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (Room room in rooms)
        {
            string normalizedUrl = NormalizeRoomUrl(room.RoomUrl);
            if (string.IsNullOrWhiteSpace(normalizedUrl) || !seenUrls.Add(normalizedUrl))
            {
                changed = true;
                continue;
            }

            if (!string.Equals(room.RoomUrl, normalizedUrl, StringComparison.Ordinal))
            {
                room.RoomUrl = normalizedUrl;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(room.PlatformName))
            {
                room.PlatformName = Spider.GetPlatformName(normalizedUrl);
                changed = true;
            }

            normalizedRooms.Add(room);
        }

        if (changed)
        {
            Configurations.Rooms.Set(normalizedRooms.ToArray());
            ConfigurationSaveScheduler.Request();
        }

        return normalizedRooms.ToArray();
    }

    private static string NormalizeRoomUrl(string? roomUrl)
    {
        if (string.IsNullOrWhiteSpace(roomUrl))
        {
            return string.Empty;
        }

        return Spider.ParseUrl(roomUrl) ?? roomUrl.Trim();
    }

    private static RoomStatusReactive CreateRoomStatusReactive(Room room, int addedOrder)
    {
        return new RoomStatusReactive
        {
            NickName = room.NickName,
            RoomUrl = room.RoomUrl,
            AvatarThumbUrl = room.AvatarThumbUrl,
            AvatarLocalPath = AvatarCache.GetCachedAvatarSource(room.RoomUrl),
            PlatformName = string.IsNullOrWhiteSpace(room.PlatformName) ? Spider.GetPlatformName(room.RoomUrl) : room.PlatformName,
            LiveTitle = room.LiveTitle,
            Uid = room.Uid,
            Quality = room.Quality,
            Resolution = room.Resolution,
            Bitrate = room.Bitrate,
            Headers = room.Headers,
            FlvUrl = room.FlvUrl,
            HlsUrl = room.HlsUrl,
            RecordUrl = room.RecordUrl,
            IsToNotify = room.IsToNotify,
            IsToRecord = room.IsToRecord,
            IsToMonitor = room.IsToMonitor,
            IsFollowGlobalSettings = room.IsFollowGlobalSettings,
            AddedOrder = addedOrder,
        };
    }

    internal static Room CloneRoom(Room room)
    {
        return new Room
        {
            NickName = room.NickName,
            RoomUrl = room.RoomUrl,
            AvatarThumbUrl = room.AvatarThumbUrl,
            PlatformName = room.PlatformName,
            LiveTitle = room.LiveTitle,
            Uid = room.Uid,
            Quality = room.Quality,
            Resolution = room.Resolution,
            Bitrate = room.Bitrate,
            Headers = room.Headers,
            FlvUrl = room.FlvUrl,
            HlsUrl = room.HlsUrl,
            RecordUrl = room.RecordUrl,
            IsToNotify = room.IsToNotify,
            IsToRecord = room.IsToRecord,
            IsToMonitor = room.IsToMonitor,
            IsFollowGlobalSettings = room.IsFollowGlobalSettings,
            PreferredStreamQuality = room.PreferredStreamQuality,
            RecordFormat = room.RecordFormat,
            IsRemoveTs = room.IsRemoveTs,
            IsToSegment = room.IsToSegment,
            SegmentTime = room.SegmentTime,
            SegmentTimeUnit = room.SegmentTimeUnit,
            RoutineInterval = room.RoutineInterval,
            RoutineScheduleMode = room.RoutineScheduleMode,
            RoutineScheduleDays = room.RoutineScheduleDays,
            RoutineScheduleStartHour = room.RoutineScheduleStartHour,
            RoutineScheduleStartMinute = room.RoutineScheduleStartMinute,
            RoutineScheduleEndHour = room.RoutineScheduleEndHour,
            RoutineScheduleEndMinute = room.RoutineScheduleEndMinute,
            SaveFolder = room.SaveFolder,
            SaveFolderPathLevel = room.SaveFolderPathLevel,
            SaveFileNameCustomRule = room.SaveFileNameCustomRule,
        };
    }

    partial void OnIsRecordingChanged(bool value)
    {
        TrayIconManager.GetInstance().UpdateTrayIcon();
    }

    [ObservableProperty]
    private bool statusOfIsMonitorRunning = Configurations.IsMonitorRunning.Get();

    [ObservableProperty]
    private bool statusOfIsToMonitor = Configurations.IsToMonitor.Get();

    [ObservableProperty]
    private bool statusOfIsToNotify = Configurations.IsToNotify.Get();

    [ObservableProperty]
    private bool statusOfIsToRecord = Configurations.IsToRecord.Get();

    [ObservableProperty]
    private bool statusOfIsUseProxy = Configurations.IsUseProxy.Get();

    [ObservableProperty]
    private bool statusOfIsUseKeepAwake = Configurations.IsUseKeepAwake.Get();

    [ObservableProperty]
    private bool statusOfIsUseAutoShutdown = Configurations.IsUseAutoShutdown.Get();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusOfAutoShutdownCountdown))]
    [NotifyPropertyChangedFor(nameof(StatusOfAutoShutdownCountdownToolTip))]
    private string statusOfAutoShutdownTime = Configurations.AutoShutdownTime.Get();

    public string StatusOfAutoShutdownCountdown
    {
        get
        {
            if (!StatusOfIsUseAutoShutdown || !AutoShutdownSchedule.TryParseTime(StatusOfAutoShutdownTime, out TimeSpan targetTime))
            {
                return StatusOfAutoShutdownTime;
            }

            TimeSpan remaining = targetTime - DateTime.Now.TimeOfDay;
            if (remaining < TimeSpan.Zero)
            {
                remaining += TimeSpan.FromDays(1);
            }

            int totalHours = Math.Max(0, (int)Math.Floor(remaining.TotalHours));
            return $"{totalHours:D2}:{remaining.Minutes:D2}";
        }
    }

    public string StatusOfAutoShutdownCountdownToolTip => AutoShutdownSchedule.ResolveCloseTarget(Configurations.IsAutoShutdownComputer.Get()) == ScheduledCloseTarget.Computer
        ? $"将在 {StatusOfAutoShutdownCountdown} 后关闭电脑"
        : $"将在 {StatusOfAutoShutdownCountdown} 后关闭软件";

    [ObservableProperty]
    private string statusOfRecordFormat = Configurations.RecordFormat.Get();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusOfRoutineIntervalWithUnit))]
    private int statusOfRoutineInterval = MonitorTiming.NormalizeRoutineInterval(Configurations.RoutineInterval.Get());

    public string StatusOfRoutineIntervalWithUnit
    {
        get
        {
            if (StatusOfRoutineInterval > 60000d)
            {
                return $"{Math.Round(StatusOfRoutineInterval / 60000d, 1)}min";
            }
            else if (StatusOfRoutineInterval >= 1000d)
            {
                return $"{StatusOfRoutineInterval / 1000d}s";
            }
            else
            {
                return $"{MonitorTiming.MinimumRoutineIntervalMilliseconds / 1000d}s";
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkCapacityDisplayText))]
    private bool isNetworkCapacityTesting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkCapacityDisplayText))]
    private string networkCapacityText = "NetworkCapacityIdle".Tr();

    [ObservableProperty]
    private string networkCapacityToolTip = "NetworkCapacityHint".Tr();

    private NetworkCapacityState networkCapacityState = NetworkCapacityState.Idle;

    private NetworkCapacityPresentation? networkCapacityPresentation;

    public string NetworkCapacityDisplayText
    {
        get
        {
            if (IsNetworkCapacityTesting)
            {
                string testingText = "NetworkCapacityTesting".Tr();
                return string.IsNullOrWhiteSpace(testingText) || testingText == "NetworkCapacityTesting"
                    ? "测速中"
                    : testingText;
            }

            return string.IsNullOrWhiteSpace(NetworkCapacityText) || NetworkCapacityText == "NetworkCapacityIdle"
                ? "测速"
                : NetworkCapacityText;
        }
    }

    public CancellationTokenSource? ShutdownCancellationTokenSource { get; private set; } = null;

    public MainViewModel()
    {
        livePreviewPlayer.PlaybackFailed += OnLivePreviewPlaybackFailed;
        livePreviewPlayer.PlaybackEnded += OnLivePreviewPlaybackEnded;
        DispatcherTimer = new(TimeSpan.FromSeconds(3), ReloadRoomStatus);
        AutoShutdownDispatcherTimer = new(TimeSpan.FromSeconds(1), UpdateOneSecondState);
        Room[] configuredRooms = NormalizeStoredRooms(Configurations.Rooms.Get());
        AvatarCache.Prune(configuredRooms.Select(room => room.RoomUrl));

        RoomStatuses.Reset(configuredRooms.Select(CreateRoomStatusReactive));
        RoomStatusesView = CollectionViewSource.GetDefaultView(RoomStatuses);
        RoomStatusesView.Filter = FilterRoomStatus;
        ApplyRoomSort();

        Locale.CultureChanged += OnCultureChanged;

        WeakReferenceMessenger.Default.Register<ToastNotificationActivatedMessage>(this, (_, msg) =>
        {
            string arguments = msg.EventArgs.Argument;

            if (!string.IsNullOrEmpty(arguments))
            {
                NameValueCollection parsedArgs = HttpUtility.ParseQueryString(arguments);

                if (parsedArgs["AutoShutdownCancel"] != null)
                {
                    CancelAutoShutdownForCurrentSchedule();
                }
            }
        });

        ChildProcessTracerPeriodicTimer.Default.WhiteList = ["ffmpeg", "ffprobe"];
        ChildProcessTracerPeriodicTimer.Default.Start();
        RecordingRecoveryService.QueueRun();
        if (ShouldRunMonitorLoop())
        {
            GlobalMonitor.Start();
        }
        DispatcherTimer.Start();
        AutoShutdownDispatcherTimer.Start();
        RecordingCleanupService.QueueRun();
    }

    private void ReloadRoomStatus()
    {
        bool refreshPlatformSummary = false;
        bool refreshSelectedPreview = false;
        foreach (RoomStatus roomStatus in GlobalMonitor.RoomStatus.Values.ToArray())
        {
            RoomStatusReactive? roomStatusReactive = RoomStatuses.Where(room => room.RoomUrl == roomStatus.RoomUrl).FirstOrDefault();

            if (roomStatusReactive != null)
            {
                StreamStatus previousStreamStatus = roomStatusReactive.StreamStatus;
                bool previousCanPreview = roomStatusReactive.CanPreview;

                roomStatusReactive.AvatarThumbUrl = roomStatus.AvatarThumbUrl;
                roomStatusReactive.AvatarLocalPath = roomStatus.AvatarLocalPath;
                roomStatusReactive.PlatformName = roomStatus.PlatformName;
                roomStatusReactive.Uid = roomStatus.Uid;
                roomStatusReactive.LiveTitle = roomStatus.LiveTitle;
                roomStatusReactive.Quality = roomStatus.Quality;
                roomStatusReactive.Resolution = roomStatus.Resolution;

                roomStatusReactive.Bitrate = roomStatus.Bitrate;
                roomStatusReactive.Headers = roomStatus.Headers;
                roomStatusReactive.StreamStatus = roomStatus.StreamStatus;
                roomStatusReactive.RecordStatus = roomStatus.RecordStatus;
                roomStatusReactive.FlvUrl = roomStatus.FlvUrl;
                roomStatusReactive.HlsUrl = roomStatus.HlsUrl;
                roomStatusReactive.RecordUrl = roomStatus.RecordUrl;
                roomStatusReactive.StartTime = roomStatus.Recorder.StartTime;
                roomStatusReactive.EndTime = roomStatus.Recorder.EndTime;

                refreshPlatformSummary |= (previousStreamStatus == StreamStatus.Streaming)
                    != (roomStatusReactive.StreamStatus == StreamStatus.Streaming);
                refreshSelectedPreview |= ReferenceEquals(roomStatusReactive, SelectedItem) && previousCanPreview != roomStatusReactive.CanPreview;
            }
        }

        if (refreshPlatformSummary)
        {
            OnPropertyChanged(nameof(PlatformSummaryText));
        }

        IsRecording = RoomStatuses.Any(roomStatusReactive => roomStatusReactive.RecordStatus == RecordStatus.Recording);

        if (refreshSelectedPreview)
        {
            OnPropertyChanged(nameof(CanPreviewSelectedRoom));
        }

        ClosePreviewIfCurrentRoomUnavailable();
    }

    private void ReloadConfigurationStatus()
    {
        bool isMonitorRunning = Configurations.IsMonitorRunning.Get();
        bool isToNotify = Configurations.IsToNotify.Get();
        bool isToMonitor = Configurations.IsToMonitor.Get();
        bool isToRecord = Configurations.IsToRecord.Get();
        bool refreshRoomEffectiveStates = StatusOfIsMonitorRunning != isMonitorRunning
            || StatusOfIsToNotify != isToNotify
            || StatusOfIsToMonitor != isToMonitor
            || StatusOfIsToRecord != isToRecord;

        StatusOfIsMonitorRunning = isMonitorRunning;
        StatusOfIsToNotify = isToNotify;
        StatusOfIsToMonitor = isToMonitor;
        StatusOfIsToRecord = isToRecord;
        StatusOfIsUseProxy = Configurations.IsUseProxy.Get();
        StatusOfIsUseKeepAwake = Configurations.IsUseKeepAwake.Get();
        StatusOfIsUseAutoShutdown = Configurations.IsUseAutoShutdown.Get();
        StatusOfAutoShutdownTime = Configurations.AutoShutdownTime.Get();
        StatusOfRecordFormat = Configurations.RecordFormat.Get();
        StatusOfRoutineInterval = MonitorTiming.NormalizeRoutineInterval(Configurations.RoutineInterval.Get());

        if (refreshRoomEffectiveStates)
        {
            RefreshRoomEffectiveStates();
        }
    }

    private void UpdateOneSecondState()
    {
        UpdateAutoShutdownState();
        foreach (RoomStatusReactive roomStatus in RoomStatuses)
        {
            roomStatus.RefreshDuration();
        }
    }

    private void UpdateAutoShutdownState()
    {
        string previousTime = StatusOfAutoShutdownTime;
        StatusOfIsUseAutoShutdown = Configurations.IsUseAutoShutdown.Get();
        StatusOfAutoShutdownTime = Configurations.AutoShutdownTime.Get();
        OnPropertyChanged(nameof(StatusOfAutoShutdownCountdown));
        OnPropertyChanged(nameof(StatusOfAutoShutdownCountdownToolTip));

        if (ShutdownCancellationTokenSource != null
            && (!StatusOfIsUseAutoShutdown || !string.Equals(previousTime, StatusOfAutoShutdownTime, StringComparison.Ordinal)))
        {
            AbortAutoShutdownCountdown();
        }

        if (autoShutdownSchedule.ShouldStartPrompt(DateTime.Now, StatusOfIsUseAutoShutdown, StatusOfAutoShutdownTime)
            && ShutdownCancellationTokenSource == null)
        {
            StartAutoShutdownCountdown();
        }
    }

    private void StartAutoShutdownCountdown()
    {
        CancellationTokenSource cancellationTokenSource = new();
        ShutdownCancellationTokenSource = cancellationTokenSource;

        AppSessionLogger.Event("info", "shutdown", "auto_shutdown_countdown_started", "automatic shutdown countdown started", new
        {
            StatusOfAutoShutdownTime,
            waitForTranscode = Configurations.IsAutoShutdownAfterTranscode.Get(),
            closeComputer = Configurations.IsAutoShutdownComputer.Get(),
        });
        string closeTarget = AutoShutdownSchedule.ResolveCloseTarget(Configurations.IsAutoShutdownComputer.Get()) == ScheduledCloseTarget.Computer ? "电脑" : "软件";
        Notifier.AddNoticeWithButton("Title".Tr(), $"将在 1 分钟后关闭{closeTarget}", [
            new ToastContentButtonOption
            {
                Content = "ButtonOfCancel".Tr(),
                Arguments = [("AutoShutdownCancel", string.Empty)],
                ActivationType = ToastActivationType.Foreground,
            }
        ]);

        GlobalMonitor.SetRecordStartBlock(AutoShutdownRecordBlockReason, true);
        GlobalMonitor.StopAllRecorders();
        ApplicationDispatcher.BeginInvoke(async () => await ShowAutoShutdownPromptAsync());
        ApplicationDispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Task.Delay(autoShutdownSchedule.GetRemainingTime(DateTime.Now), cancellationTokenSource.Token);
                await ShutdownAfterTranscodeIfNeededAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(ShutdownCancellationTokenSource, cancellationTokenSource))
                {
                    ShutdownCancellationTokenSource = null;
                    ResetAutoShutdownReadiness();
                }
                cancellationTokenSource.Dispose();
            }
        });
    }

    private async Task ShowAutoShutdownPromptAsync()
    {
        if (autoShutdownDialog != null)
        {
            return;
        }

        AutoShutdownContentDialog dialog = new(this);
        autoShutdownDialog = dialog;
        using DialogBlurScope blurScope = DialogBlurScope.ForLightDismiss(Application.Current.MainWindow, dialog);
        try
        {
            await ShowMainContentDialogAsync(dialog);
        }
        finally
        {
            if (ReferenceEquals(autoShutdownDialog, dialog))
            {
                autoShutdownDialog = null;
            }
        }
    }

    public void CancelAutoShutdownFromPrompt()
    {
        CancelAutoShutdownForCurrentSchedule();
    }

    public void ShutdownNowFromPrompt()
    {
        ShutdownCancellationTokenSource?.Cancel();
        if (ExecuteScheduledClose())
        {
            autoShutdownSchedule.CompleteCurrent();
        }
    }

    public void ShutdownAfterTranscodeFromPrompt()
    {
        forceShutdownAfterTranscode = true;
        ShutdownCancellationTokenSource?.Cancel();
        CancellationTokenSource source = new();
        ShutdownCancellationTokenSource = source;
        _ = ShutdownAfterTranscodeAndFinalizeAsync(source);
    }

    private async Task ShutdownAfterTranscodeAndFinalizeAsync(CancellationTokenSource source)
    {
        try
        {
            await ShutdownAfterTranscodeIfNeededAsync(source.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(ShutdownCancellationTokenSource, source))
            {
                ShutdownCancellationTokenSource = null;
                ResetAutoShutdownReadiness();
            }
            source.Dispose();
        }
    }

    private async Task ShutdownAfterTranscodeIfNeededAsync(CancellationToken token)
    {
        bool waitForTranscode = forceShutdownAfterTranscode || Configurations.IsAutoShutdownAfterTranscode.Get();
        if (waitForTranscode)
        {
            AppSessionLogger.Event("info", "shutdown", "waiting_for_transcode", "automatic shutdown is waiting for recorder cleanup and conversion", new
            {
                activeRecorders = GlobalMonitor.HasActiveRecorders,
                activeConversions = Converter.ActiveConversionCount,
            });
            while (!token.IsCancellationRequested && (GlobalMonitor.HasActiveRecorders || Converter.HasActiveConversions))
            {
                await Task.Delay(500, token);
            }
        }

        if (!token.IsCancellationRequested)
        {
            if (ExecuteScheduledClose())
            {
                autoShutdownSchedule.CompleteCurrent();
            }
        }
    }

    private bool ExecuteScheduledClose()
    {
        ScheduledCloseTarget closeTarget = AutoShutdownSchedule.ResolveCloseTarget(Configurations.IsAutoShutdownComputer.Get());
        AppSessionLogger.Event("info", "shutdown", closeTarget == ScheduledCloseTarget.Computer ? "system_shutdown_requested" : "application_shutdown_requested", closeTarget == ScheduledCloseTarget.Computer ? "system shutdown was requested" : "application shutdown was requested");
        if (closeTarget == ScheduledCloseTarget.Application)
        {
            ApplicationDispatcher.BeginInvoke(() => TrayIconManager.GetInstance().ShutdownApplication(confirmRecording: false));
            return true;
        }

        if (Debugger.IsAttached)
        {
            _ = MessageBox.Information("已触发关闭电脑  调试模式不会执行系统关机");
            return true;
        }

        bool succeeded = Interop.ExitWindowsEx(User32.ExitWindowsFlags.EWX_SHUTDOWN | User32.ExitWindowsFlags.EWX_FORCE);
        if (!succeeded)
        {
            AppSessionLogger.Event("error", "shutdown", "system_shutdown_failed", "system shutdown request failed");
            Toast.Error("AutoShutdownComputerFailed".Tr());
        }
        return succeeded;
    }

    private void ResetAutoShutdownReadiness()
    {
        GlobalMonitor.SetRecordStartBlock(AutoShutdownRecordBlockReason, false);
        autoShutdownSchedule.ResetReadiness();
        forceShutdownAfterTranscode = false;
    }

    private void CancelAutoShutdownForCurrentSchedule()
    {
        ShutdownCancellationTokenSource?.Cancel();
        ShutdownCancellationTokenSource = null;
        autoShutdownSchedule.Cancel(DateTime.Now, StatusOfAutoShutdownTime);
        ResetAutoShutdownReadiness();
        ApplicationDispatcher.BeginInvoke(() => autoShutdownDialog?.Hide());
        AppSessionLogger.Event("info", "shutdown", "auto_shutdown_cancelled", "automatic shutdown was cancelled for the current schedule", new
        {
            StatusOfAutoShutdownTime,
        });
    }

    private void AbortAutoShutdownCountdown()
    {
        ShutdownCancellationTokenSource?.Cancel();
        ShutdownCancellationTokenSource = null;
        GlobalMonitor.SetRecordStartBlock(AutoShutdownRecordBlockReason, false);
        autoShutdownSchedule.ResetAll();
        forceShutdownAfterTranscode = false;
        ApplicationDispatcher.BeginInvoke(() => autoShutdownDialog?.Hide());
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PreviewLiveRoomAsync(RoomStatusReactive? roomStatus = null)
    {
        RoomStatusReactive? targetRoom = roomStatus ?? SelectedItem;
        if (targetRoom == null || !targetRoom.CanPreview)
        {
            LivePreviewStatus = LivePreviewStatus.Unavailable;
            Toast.Warning("LivePreviewUnavailable".Tr());
            return;
        }

        if (IsPreviewing && IsSameRoom(PreviewingRoom, targetRoom))
        {
            await RequestPreviewTransitionAsync(null);
            return;
        }

        if (!ReferenceEquals(SelectedItem, targetRoom))
        {
            SelectedItem = targetRoom;
        }

        await RequestPreviewTransitionAsync(targetRoom);
    }

    private async Task RequestPreviewTransitionAsync(RoomStatusReactive? targetRoom)
    {
        CancellationTokenSource cancellation = BeginPreviewTransition();
        ApplyPreviewRequestState(targetRoom);
        bool enteredGate = false;

        try
        {
            await previewTransitionGate.WaitAsync(cancellation.Token);
            enteredGate = true;
            cancellation.Token.ThrowIfCancellationRequested();
            await livePreviewPlayer.StopAsync();

            if (targetRoom == null)
            {
                return;
            }

            previewTransitionGate.Release();
            enteredGate = false;
            if (ShouldRefreshPreviewStreamBeforePlayback(targetRoom))
            {
                await RefreshPreviewStreamQualityAsync(targetRoom, cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();

            await previewTransitionGate.WaitAsync(cancellation.Token);
            enteredGate = true;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!targetRoom.CanPreview)
            {
                ApplyPreviewClosedState();
                LivePreviewStatus = LivePreviewStatus.Unavailable;
                Toast.Warning("LivePreviewUnavailable".Tr());
                return;
            }

            string proxyUrl = Configurations.IsUseProxy.Get() ? Configurations.ProxyUrl.Get() : string.Empty;
            string previewUrl = GetPreviewPlaybackUrl(targetRoom);
            if (string.IsNullOrWhiteSpace(previewUrl))
            {
                ApplyPreviewClosedState();
                LivePreviewStatus = LivePreviewStatus.Unavailable;
                Toast.Warning("LivePreviewUnavailable".Tr());
                return;
            }

            livePreviewPlayer.SetMuted(IsPreviewMuted);
            await livePreviewPlayer.PlayAsync(previewUrl, Configurations.UserAgent.Get(), proxyUrl, targetRoom.Headers, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsCurrentPreviewTransition(cancellation))
            {
                LivePreviewStatus = LivePreviewStatus.Playing;
            }
            await ResolvePreviewResolutionAsync(targetRoom, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            if (IsCurrentPreviewTransition(cancellation))
            {
                await livePreviewPlayer.StopAsync();
                ApplyPreviewClosedState();
                LivePreviewStatus = LivePreviewStatus.Error;
                Toast.Error("LivePreviewError".Tr());
            }
        }
        finally
        {
            if (enteredGate)
            {
                previewTransitionGate.Release();
            }

            if (IsCurrentPreviewTransition(cancellation))
            {
                IsPreviewTransitioning = false;
            }

            CompletePreviewTransition(cancellation);
        }
    }

    private CancellationTokenSource BeginPreviewTransition()
    {
        CancellationTokenSource current = new();
        CancellationTokenSource? previous;
        lock (previewTransitionSync)
        {
            previous = previewTransitionCancellation;
            previewTransitionCancellation = current;
        }

        previous?.Cancel();
        return current;
    }

    private bool IsCurrentPreviewTransition(CancellationTokenSource cancellation)
    {
        lock (previewTransitionSync)
        {
            return ReferenceEquals(previewTransitionCancellation, cancellation);
        }
    }

    private void CompletePreviewTransition(CancellationTokenSource cancellation)
    {
        lock (previewTransitionSync)
        {
            if (ReferenceEquals(previewTransitionCancellation, cancellation))
            {
                previewTransitionCancellation = null;
            }
        }

        cancellation.Dispose();
    }

    private void ApplyPreviewRequestState(RoomStatusReactive? targetRoom)
    {
        IsPreviewTransitioning = true;
        if (targetRoom == null)
        {
            ApplyPreviewClosedState();
            return;
        }

        PreviewingRoom = targetRoom;
        IsPreviewing = true;
        IsPreviewPaused = false;
        LivePreviewStatus = LivePreviewStatus.Ready;
    }

    private void ApplyPreviewClosedState()
    {
        IsPreviewDetached = false;
        PreviewingRoom = null;
        IsPreviewing = false;
        IsPreviewPaused = false;
        LivePreviewStatus = CanPreviewSelectedRoom ? LivePreviewStatus.Ready : LivePreviewStatus.Idle;
    }

    private void ClosePreviewIfCurrentRoomUnavailable()
    {
        if (!IsPreviewing || IsPreviewTransitioning || PreviewingRoom == null || PreviewingRoom.CanPreview)
        {
            return;
        }

        AppSessionLogger.Event("info", "preview", "preview_auto_closed_unavailable", "active preview room became unavailable", new
        {
            PreviewingRoom.RoomUrl,
            PreviewingRoom.NickName,
            PreviewingRoom.StreamStatus,
        });
        _ = RequestPreviewTransitionAsync(null);
    }

    private void OnLivePreviewPlaybackFailed(object? sender, EventArgs e)
    {
        HandleLivePreviewPlaybackTerminated(LivePreviewStatus.Error, "LivePreviewError");
    }

    private void OnLivePreviewPlaybackEnded(object? sender, EventArgs e)
    {
        HandleLivePreviewPlaybackTerminated(LivePreviewStatus.Unavailable, "LivePreviewUnavailable");
    }

    private void HandleLivePreviewPlaybackTerminated(LivePreviewStatus status, string messageKey)
    {
        ApplicationDispatcher.BeginInvoke(async () =>
        {
            if (!IsPreviewing)
            {
                return;
            }

            await RequestPreviewTransitionAsync(null);
            if (IsPreviewing)
            {
                return;
            }

            LivePreviewStatus = status;
            if (status == LivePreviewStatus.Error)
            {
                Toast.Error(messageKey.Tr());
            }
            else
            {
                Toast.Warning(messageKey.Tr());
            }
        });
    }

    private static bool IsSameRoom(RoomStatusReactive? current, RoomStatusReactive? next)
    {
        if (current == null || next == null)
        {
            return false;
        }

        return ReferenceEquals(current, next) || string.Equals(current.RoomUrl, next.RoomUrl, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ResolvePreviewResolutionAsync(RoomStatusReactive targetRoom, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(targetRoom.Resolution))
        {
            return;
        }

        (uint Width, uint Height)? dimensions = await livePreviewPlayer.ResolveVideoDimensionsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (dimensions is not { Width: > 0, Height: > 0 }
            || !IsSameRoom(PreviewingRoom, targetRoom))
        {
            return;
        }

        targetRoom.Resolution = $"{dimensions.Value.Width}x{dimensions.Value.Height}";
        if (GlobalMonitor.RoomStatus.TryGetValue(targetRoom.RoomUrl, out RoomStatus? roomStatus))
        {
            roomStatus.Resolution = targetRoom.Resolution;
        }
        SaveRoomInfo(targetRoom);
        targetRoom.FlashRefresh();
    }

    private async Task RefreshPreviewStreamQualityAsync(RoomStatusReactive targetRoom, CancellationToken cancellationToken)
    {
        string roomUrl = targetRoom.RoomUrl;
        if (string.IsNullOrWhiteSpace(roomUrl))
        {
            return;
        }

        string preferredQuality = PreviewStreamQualityPreference;
        if (!ShouldRefreshPreviewStreamQuality(targetRoom, preferredQuality))
        {
            return;
        }

        bool refreshed;
        try
        {
            refreshed = await GlobalMonitor.RunRoomUpdateAsync(roomUrl, async () =>
            {
                ISpiderResult? result = await Task.Run(() => Spider.GetResult(roomUrl, preferredQuality), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!HasPreviewRefreshResult(result))
                {
                    return false;
                }

                previewQualityRefreshTimestamps[roomUrl] = Environment.TickCount64;
                string avatarLocalPath = await CacheAvatarAsync(targetRoom.RoomUrl, result!, cancellationToken);
                ApplyRoomInfoResult(targetRoom, result!, avatarLocalPath);
                return true;
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return;
        }

        if (!refreshed)
        {
            return;
        }

        RoomStatusesView.Refresh();
        OnPropertyChanged(nameof(CanPreviewSelectedRoom));
        OnPropertyChanged(nameof(PlatformSummaryText));
        ClosePreviewIfCurrentRoomUnavailable();
    }

    private bool ShouldRefreshPreviewStreamQuality(RoomStatusReactive room, string preferredQuality)
    {
        if (room.StreamStatus != StreamStatus.Streaming || string.IsNullOrWhiteSpace(room.RoomUrl))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(GetPreviewPlaybackUrl(room)))
        {
            return true;
        }

        if (IsPreviewQualityRefreshCoolingDown(room.RoomUrl))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(room.Resolution) || string.IsNullOrWhiteSpace(room.Bitrate))
        {
            return true;
        }

        double? currentBitrate = ParseBitrateMbps(room.Bitrate);
        if (currentBitrate is > 0 && currentBitrate.Value < EstimateRequiredMbps(room) * 0.75d)
        {
            return true;
        }

        string supportedPreference = StreamQualityCatalog.GetSupportedPreference(room.PlatformName, preferredQuality);
        string currentPreference = StreamQualityCatalog.NormalizePreference(StreamQualityCatalog.GetDisplayName(room.PlatformName, room.Quality, room.Resolution));
        return GetStreamQualityRank(currentPreference) < GetStreamQualityRank(supportedPreference);
    }

    internal static string GetPreviewPlaybackUrl(RoomStatusReactive room)
    {
        return room.PreviewUrl;
    }

    internal static bool ShouldRefreshPreviewStreamBeforePlayback(RoomStatusReactive room)
    {
        return string.IsNullOrWhiteSpace(GetPreviewPlaybackUrl(room));
    }

    private bool IsPreviewQualityRefreshCoolingDown(string roomUrl)
    {
        if (!previewQualityRefreshTimestamps.TryGetValue(roomUrl, out long lastRefreshTimestamp))
        {
            return false;
        }

        return Environment.TickCount64 - lastRefreshTimestamp < PreviewQualityRefreshCooldownMilliseconds;
    }

    private static bool HasPreviewRefreshResult(ISpiderResult? result)
    {
        return result != null
            && (result.IsLiveStreaming == false
                || !string.IsNullOrWhiteSpace(result.RecordUrl)
                || !string.IsNullOrWhiteSpace(result.FlvUrl)
                || !string.IsNullOrWhiteSpace(result.HlsUrl));
    }

    private static int GetStreamQualityRank(string quality)
    {
        return StreamQualityCatalog.NormalizePreference(quality) switch
        {
            StreamQualityCatalog.Original => 5,
            StreamQualityCatalog.BlueRay => 4,
            StreamQualityCatalog.UltraHigh => 3,
            StreamQualityCatalog.High => 2,
            StreamQualityCatalog.Standard => 1,
            StreamQualityCatalog.Smooth => 0,
            _ => 0,
        };
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task StopPreviewAsync()
    {
        await RequestPreviewTransitionAsync(null);
    }

    [RelayCommand]
    private async Task TogglePreviewPlaybackAsync()
    {
        if (IsPreviewing)
        {
            await RequestPreviewTransitionAsync(null);
            return;
        }

        RoomStatusReactive? targetRoom = SelectedItem;
        if (targetRoom != null && targetRoom.CanPreview)
        {
            await RequestPreviewTransitionAsync(targetRoom);
        }
    }

    [RelayCommand]
    private async Task TogglePreviewPauseAsync()
    {
        if (!IsPreviewing)
        {
            await PreviewLiveRoomAsync();
            return;
        }

        if (IsPreviewTransitioning)
        {
            return;
        }

        IsPreviewPaused = !IsPreviewPaused;
        livePreviewPlayer.SetPaused(IsPreviewPaused);
        LivePreviewStatus = IsPreviewPaused ? LivePreviewStatus.Ready : LivePreviewStatus.Playing;
    }

    [RelayCommand]
    private void TogglePreviewMute()
    {
        IsPreviewMuted = !IsPreviewMuted;
        livePreviewPlayer.SetMuted(IsPreviewMuted);
    }

    [RelayCommand]
    private async Task ToggleMonitorAsync()
    {
        bool isMonitorRunning = !Configurations.IsMonitorRunning.Get();
        Configurations.IsMonitorRunning.Set(isMonitorRunning);
        ConfigurationSaveScheduler.Request();
        StatusOfIsMonitorRunning = isMonitorRunning;

        if (isMonitorRunning)
        {
            GlobalMonitor.Start();
            await GlobalMonitor.RunOnceAsync();
            Toast.Success("SuccOp".Tr());
        }
        else
        {
            if (HasIndependentMonitorRooms())
            {
                GlobalMonitor.Start();
                await GlobalMonitor.RunOnceAsync();
            }
            else
            {
                GlobalMonitor.Stop();
            }
            Toast.Success("SuccOp".Tr());
        }

        RefreshRoomEffectiveStates();
    }

    private static bool ShouldRunMonitorLoop()
    {
        return Configurations.IsMonitorRunning.Get() || HasIndependentMonitorRooms();
    }

    private static bool HasIndependentMonitorRooms()
    {
        return Configurations.Rooms.Get().Any(room => !room.IsFollowGlobalSettings && GlobalMonitor.GetEffectiveRoomMonitor(room));
    }

    [RelayCommand]
    private async Task ToggleStatusMonitorAsync()
    {
        StatusOfIsToMonitor = !StatusOfIsToMonitor;
        Configurations.IsToMonitor.Set(StatusOfIsToMonitor);
        ConfigurationSaveScheduler.Request();
        if (StatusOfIsToMonitor && Configurations.IsMonitorRunning.Get())
        {
            GlobalMonitor.Start();
            await GlobalMonitor.RunOnceAsync();
        }
        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private void ToggleStatusNotify()
    {
        StatusOfIsToNotify = !StatusOfIsToNotify;
        Configurations.IsToNotify.Set(StatusOfIsToNotify);
        ConfigurationSaveScheduler.Request();
        TrayIconManager.GetInstance().UpdateTrayIcon();
        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private async Task ToggleStatusRecordAsync()
    {
        StatusOfIsToRecord = !StatusOfIsToRecord;
        Configurations.IsToRecord.Set(StatusOfIsToRecord);
        ConfigurationSaveScheduler.Request();
        TrayIconManager.GetInstance().UpdateTrayIcon();

        if (StatusOfIsToRecord && Configurations.IsMonitorRunning.Get())
        {
            GlobalMonitor.ClearTemporaryRecordOverrides();
            GlobalMonitor.Start();
            await GlobalMonitor.RunOnceAsync();
        }
        else if (!StatusOfIsToRecord)
        {
            StopGlobalFollowRecorders();
        }

        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private void ToggleStatusProxy()
    {
        StatusOfIsUseProxy = !StatusOfIsUseProxy;
        Configurations.IsUseProxy.Set(StatusOfIsUseProxy);
        ConfigurationSaveScheduler.Request();
    }

    [RelayCommand]
    private void ToggleStatusKeepAwake()
    {
        StatusOfIsUseKeepAwake = !StatusOfIsUseKeepAwake;

        if (StatusOfIsUseKeepAwake)
        {
            _ = Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS | Kernel32.EXECUTION_STATE.ES_SYSTEM_REQUIRED | Kernel32.EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
        }
        else
        {
            _ = Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS);
        }

        Configurations.IsUseKeepAwake.Set(StatusOfIsUseKeepAwake);
        ConfigurationSaveScheduler.Request();
    }

    [RelayCommand]
    private void ToggleStatusAutoShutdown()
    {
        StatusOfIsUseAutoShutdown = !StatusOfIsUseAutoShutdown;
        Configurations.IsUseAutoShutdown.Set(StatusOfIsUseAutoShutdown);
        ConfigurationSaveScheduler.Request();
    }

    [RelayCommand]
    private async Task AddRoomAsync()
    {
        AddRoomContentDialog dialog = new();
        using DialogBlurScope blurScope = DialogBlurScope.ForLightDismiss(Application.Current.MainWindow, dialog);
        ContentDialogResult result = await ShowMainContentDialogAsync(dialog);

        if (result != ContentDialogResult.Primary ||
            string.IsNullOrWhiteSpace(dialog.NickName) ||
            string.IsNullOrWhiteSpace(dialog.RoomUrl))
        {
            return;
        }

        await AddConfirmedRoomAsync(dialog.NickName, dialog.RoomUrl, dialog.SpiderResult);
    }

    private static async Task<ContentDialogResult> ShowMainContentDialogAsync(ContentDialog dialog)
    {
        Window? owner = Application.Current?.MainWindow;
        MainWindow? mainWindow = owner as MainWindow;
        mainWindow?.SetPreviewPresentationSuspended(true);
        try
        {
            return await WindowSizing.ShowContentDialogAsync(dialog, owner);
        }
        finally
        {
            mainWindow?.SetPreviewPresentationSuspended(false);
        }
    }

    private async Task AddConfirmedRoomAsync(string nickName, string roomUrl, ISpiderResult? spiderResult)
    {
        RoomListHistoryState before = CaptureRoomListHistoryState();
        List<Room> rooms = [.. Configurations.Rooms.Get()];

        rooms.RemoveAll(room => room.RoomUrl == roomUrl);
        rooms.Add(new Room()
        {
            NickName = nickName,
            RoomUrl = roomUrl,
            AvatarThumbUrl = spiderResult?.AvatarThumbUrl ?? string.Empty,
            PlatformName = Spider.GetPlatformName(roomUrl),
            IsToMonitor = Configurations.IsToMonitor.Get(),
            IsToRecord = Configurations.IsToRecord.Get(),
            IsFollowGlobalSettings = true,
        });
        Configurations.Rooms.Set([.. rooms]);
        ConfigurationSaveScheduler.Request();

        RoomStatusReactive roomStatusReactive = new()
        {
            NickName = nickName,
            RoomUrl = roomUrl,
            AvatarLocalPath = AvatarCache.GetCachedAvatarSource(roomUrl),
            PlatformName = Spider.GetPlatformName(roomUrl),
            IsToMonitor = Configurations.IsToMonitor.Get(),
            IsToRecord = Configurations.IsToRecord.Get(),
            IsFollowGlobalSettings = true,
            AddedOrder = RoomStatuses.Count == 0 ? 0 : RoomStatuses.Max(room => room.AddedOrder) + 1,
        };
        if (spiderResult != null)
        {
            string avatarLocalPath = await CacheAvatarAsync(roomUrl, spiderResult);
            ApplyRoomInfoResult(roomStatusReactive, spiderResult, avatarLocalPath);
        }

        RoomStatuses.Add(roomStatusReactive);
        RoomStatusesView.Refresh();
        OnPropertyChanged(nameof(PlatformSummaryText));
        OnPropertyChanged(nameof(PlatformFilterOptions));
        PushRoomHistory(new RoomListHistoryEntry(before, CaptureRoomListHistoryState()));
    }

    [RelayCommand]
    private void ShowHomePage()
    {
        SelectedMainPageIndex = 0;
    }

    [RelayCommand]
    private void OpenScreenRecordList()
    {
        SelectedMainPageIndex = 1;
    }

    [RelayCommand]
    private void OpenSettingsDialog()
    {
        SelectedMainPageIndex = 2;
    }

    [RelayCommand]
    private async Task OpenSaveFolderAsync()
    {
        // TODO: Implement for other platforms
        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(
                SaveFolderHelper.GetSaveFolder(Configurations.SaveFolder.Get())
            )
        );
    }

    [RelayCommand]
    private async Task OpenSettingsFileFolderAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await "explorer"
                .WithArguments($"/select,\"{ConfigurationManager.FilePath}\"")
                .ExecuteAsync();
        }
        else
        {
            // TODO: Implement for other platforms
            await Launcher.LaunchUriAsync(new Uri(ConfigurationManager.FilePath));
        }
    }

    [RelayCommand]
    private void OpenAbout()
    {
        SelectedMainPageIndex = 3;
    }

    [RelayCommand]
    private async Task CopySelectedRoomUrlAsync()
    {
        await CopyTextToClipboardAsync(SelectedItem?.RoomUrl);
    }

    [RelayCommand]
    private async Task CopySelectedPreviewUrlAsync()
    {
        await CopyTextToClipboardAsync(SelectedItem?.PreviewUrl);
    }

    [RelayCommand]
    private void SortRoomsByName()
    {
        IsRoomSortByName = true;
        ApplyRoomSort();
    }

    [RelayCommand]
    private void SortRoomsByAddedAt()
    {
        IsRoomSortByName = false;
        ApplyRoomSort();
    }

    private void ApplyRoomSort()
    {
        using IDisposable refresh = RoomStatusesView.DeferRefresh();
        RoomStatusesView.SortDescriptions.Clear();
        foreach (SortDescription description in BuildRoomSortDescriptions(IsRoomSortByName))
        {
            RoomStatusesView.SortDescriptions.Add(description);
        }
    }

    internal static SortDescription[] BuildRoomSortDescriptions(bool sortByName)
    {
        return sortByName
            ?
            [
                new SortDescription(nameof(RoomStatusReactive.NickName), ListSortDirection.Ascending),
                new SortDescription(nameof(RoomStatusReactive.RoomUrl), ListSortDirection.Ascending),
            ]
            :
            [
                new SortDescription(nameof(RoomStatusReactive.AddedOrder), ListSortDirection.Ascending),
                new SortDescription(nameof(RoomStatusReactive.RoomUrl), ListSortDirection.Ascending),
            ];
    }

    [RelayCommand]
    private async Task RefreshRoomCardsAsync()
    {
        RoomStatusReactive[] rooms = [.. RoomStatuses.Where(room => !string.IsNullOrWhiteSpace(room.RoomUrl))];

        if (rooms.Length == 0)
        {
            ReloadRoomStatus();
            Toast.Warning("FailOp".Tr());
            return;
        }

        if (!TryBeginManualRefresh())
        {
            Toast.Warning("RefreshTooFrequently".Tr());
            return;
        }

        using SemaphoreSlim semaphore = new(Math.Clamp(Environment.ProcessorCount, 2, 6));
        bool hasUpdated = false;

        Task[] tasks = rooms.Select(async room =>
        {
            await semaphore.WaitAsync();
            try
            {
                string preferredQuality = RoomRecordingSettings.GetPreferredStreamQuality(room.RoomUrl);
                bool updated = await GlobalMonitor.RunRoomUpdateAsync(room.RoomUrl, async () =>
                {
                    ISpiderResult? result = await Task.Run(() => Spider.GetResult(room.RoomUrl, preferredQuality));
                    if (result == null)
                    {
                        return false;
                    }

                    string avatarLocalPath = await CacheAvatarAsync(room.RoomUrl, result);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ApplyRoomInfoResult(room, result, avatarLocalPath);
                    });
                    return true;
                });
                if (updated)
                {
                    hasUpdated = true;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        SaveRoomOrder();
        RoomStatusesView.Refresh();
        OnPropertyChanged(nameof(PlatformSummaryText));
        OnPropertyChanged(nameof(CanPreviewSelectedRoom));
        ClosePreviewIfCurrentRoomUnavailable();
        Toast.Success(hasUpdated ? "SuccOp".Tr() : "FailOp".Tr());
    }

    [RelayCommand]
    private async Task RefreshSelectedRoomInfoAsync()
    {
        RoomStatusReactive? selectedRoom = SelectedItem;
        if (selectedRoom == null || string.IsNullOrWhiteSpace(selectedRoom.RoomUrl) || IsRefreshingSelectedRoomInfo)
        {
            return;
        }

        if (!TryBeginManualRefresh())
        {
            Toast.Warning("RefreshTooFrequently".Tr());
            return;
        }

        IsRefreshingSelectedRoomInfo = true;
        try
        {
            string roomUrl = selectedRoom.RoomUrl;
            string preferredQuality = RoomRecordingSettings.GetPreferredStreamQuality(roomUrl);
            bool updated = await GlobalMonitor.RunRoomUpdateAsync(roomUrl, async () =>
            {
                ISpiderResult? result = await Task.Run(() => Spider.GetResult(roomUrl, preferredQuality));
                if (result == null)
                {
                    return false;
                }

                string avatarLocalPath = await CacheAvatarAsync(roomUrl, result);
                ApplyRoomInfoResult(selectedRoom, result, avatarLocalPath);
                return true;
            });
            if (!updated)
            {
                Toast.Error("GetRoomInfoError".Tr());
                return;
            }

            SaveRoomOrder();
            RoomStatusesView.Refresh();
            OnPropertyChanged(nameof(PlatformSummaryText));
            OnPropertyChanged(nameof(CanPreviewSelectedRoom));
            ClosePreviewIfCurrentRoomUnavailable();
            Toast.Success("SuccOp".Tr());
        }
        finally
        {
            IsRefreshingSelectedRoomInfo = false;
        }
    }

    private bool TryBeginManualRefresh()
    {
        long now = Environment.TickCount64;
        lock (manualRefreshCooldownLock)
        {
            if (lastManualRefreshTimestamp != 0 && now - lastManualRefreshTimestamp < ManualRefreshCooldownMilliseconds)
            {
                return false;
            }

            lastManualRefreshTimestamp = now;
            return true;
        }
    }

    [RelayCommand]
    private async Task TestNetworkCapacityAsync()
    {
        if (IsNetworkCapacityTesting)
        {
            return;
        }

        RoomStatusReactive[] estimateRooms = GetNetworkCapacityEstimateRooms();
        if (estimateRooms.Length == 0)
        {
            networkCapacityState = NetworkCapacityState.NoStream;
            RefreshNetworkCapacityLocalization();
            Toast.Warning(NetworkCapacityToolTip);
            return;
        }

        IsNetworkCapacityTesting = true;
        networkCapacityState = NetworkCapacityState.Testing;
        RefreshNetworkCapacityLocalization();
        AppSessionLogger.Write($"network capacity test started, samples={estimateRooms.Length}");

        try
        {
            double perRoomMbps = estimateRooms.Select(EstimateRequiredMbps).DefaultIfEmpty(10d).Average();
            NetworkCapacityMeasurement measurement = await MeasureBestNetworkThroughputMbpsAsync(estimateRooms);
            int capacity = CalculateNetworkCapacity(measurement.CapacityMbps, perRoomMbps)
                ?? throw new InvalidOperationException("Network capacity measurement did not return a valid result.");

            networkCapacityState = NetworkCapacityState.Result;
            networkCapacityPresentation = new NetworkCapacityPresentation(measurement, perRoomMbps, capacity, estimateRooms.Length);
            RefreshNetworkCapacityLocalization();
            AppSessionLogger.Write($"network capacity test completed, measuredMbps={measurement.CapacityMbps:0.##}, domesticMbps={measurement.Domestic?.Mbps:0.##}, overseasMbps={measurement.Overseas?.Mbps:0.##}, samples={measurement.SuccessfulSamples}/{measurement.AttemptedSamples}, confidence={measurement.Confidence}, perRoomMbps={perRoomMbps:0.##}, capacity={capacity}");
            Toast.Success(NetworkCapacityToolTip);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            AppSessionLogger.WriteException(e);
            networkCapacityState = NetworkCapacityState.Failed;
            networkCapacityPresentation = null;
            RefreshNetworkCapacityLocalization();
            Toast.Warning(NetworkCapacityToolTip);
        }
        finally
        {
            IsNetworkCapacityTesting = false;
        }
    }

    internal static int? CalculateNetworkCapacity(double? measuredMbps, double perRoomMbps)
    {
        if (measuredMbps is not > 0 ||
            double.IsNaN(measuredMbps.Value) ||
            double.IsInfinity(measuredMbps.Value) ||
            perRoomMbps <= 0 ||
            double.IsNaN(perRoomMbps) ||
            double.IsInfinity(perRoomMbps))
        {
            return null;
        }

        return Math.Max(1, (int)Math.Floor(measuredMbps.Value * 0.7d / perRoomMbps));
    }

    internal static double? CalculateStableNetworkThroughput(IEnumerable<double> measurements)
    {
        double[] valid = measurements
            .Where(value => value > 0d && !double.IsNaN(value) && !double.IsInfinity(value))
            .Order()
            .ToArray();
        if (valid.Length == 0)
        {
            return null;
        }

        int middle = valid.Length / 2;
        return valid.Length % 2 == 0
            ? (valid[middle - 1] + valid[middle]) / 2d
            : valid[middle];
    }

    private RoomStatusReactive[] GetNetworkCapacityEstimateRooms()
    {
        RoomStatusReactive[] activeRooms = RoomStatuses
            .Where(room => room.StreamStatus == StreamStatus.Streaming || room.RecordStatus == RecordStatus.Recording || room.CanPreview)
            .ToArray();

        if (SelectedItem != null &&
            !string.IsNullOrWhiteSpace(SelectedItem.RoomUrl) &&
            (SelectedItem.StreamStatus == StreamStatus.Streaming || SelectedItem.RecordStatus == RecordStatus.Recording || SelectedItem.CanPreview))
        {
            return [SelectedItem, .. activeRooms.Where(room => !ReferenceEquals(room, SelectedItem))];
        }

        return activeRooms;
    }

    private static string FormatNetworkCapacityResultShort(int capacity)
    {
        string text = "NetworkCapacityResultShort".Tr(capacity);
        return string.IsNullOrWhiteSpace(text) || text == "NetworkCapacityResultShort" ? $"可录 {capacity} 路" : text;
    }

    private void RefreshNetworkCapacityLocalization()
    {
        switch (networkCapacityState)
        {
            case NetworkCapacityState.Testing:
                NetworkCapacityText = "NetworkCapacityTesting".Tr();
                NetworkCapacityToolTip = NetworkCapacityText;
                break;
            case NetworkCapacityState.NoStream:
                NetworkCapacityText = "NetworkCapacityNoStreamShort".Tr();
                NetworkCapacityToolTip = "NetworkCapacityNoStream".Tr();
                break;
            case NetworkCapacityState.Failed:
                NetworkCapacityText = "NetworkCapacityFailed".Tr();
                NetworkCapacityToolTip = NetworkCapacityText;
                break;
            case NetworkCapacityState.Result when networkCapacityPresentation != null:
                NetworkCapacityPresentation result = networkCapacityPresentation;
                NetworkCapacityText = FormatNetworkCapacityResultShort(result.Capacity);
                NetworkCapacityToolTip = "NetworkCapacityResultHint".Tr(
                    result.Measurement.CapacityMbps,
                    result.PerRoomMbps,
                    result.Capacity,
                    result.RoomCount,
                    FormatNetworkRegionMeasurement(result.Measurement.Domestic),
                    FormatNetworkRegionMeasurement(result.Measurement.Overseas),
                    result.Measurement.SuccessfulSamples,
                    GetNetworkMeasurementConfidenceText(result.Measurement.Confidence));
                break;
            default:
                NetworkCapacityText = "NetworkCapacityIdle".Tr();
                NetworkCapacityToolTip = "NetworkCapacityHint".Tr();
                break;
        }
    }

    private static string FormatNetworkRegionMeasurement(NetworkRegionMeasurement? measurement)
    {
        return measurement == null
            ? "NetworkCapacityNotTested".Tr()
            : "NetworkCapacityRegionMeasured".Tr(measurement.Mbps, measurement.SuccessfulSamples);
    }

    private static string GetNetworkMeasurementConfidenceText(NetworkMeasurementConfidence confidence)
    {
        return confidence switch
        {
            NetworkMeasurementConfidence.High => "NetworkCapacityConfidenceHigh".Tr(),
            NetworkMeasurementConfidence.Medium => "NetworkCapacityConfidenceMedium".Tr(),
            _ => "NetworkCapacityConfidenceLow".Tr(),
        };
    }

    private async Task<NetworkThroughputConnection> OpenNetworkThroughputConnectionAsync(
        NetworkThroughputEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };

        string configuredProxy = Configurations.ProxyUrl.Get();
        if (Configurations.IsUseProxy.Get() && !string.IsNullOrWhiteSpace(configuredProxy))
        {
            string proxyUrl = configuredProxy.Contains("://", StringComparison.Ordinal) ? configuredProxy : $"http://{configuredProxy}";
            handler.Proxy = new WebProxy(proxyUrl);
            handler.UseProxy = true;
        }

        HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        HttpResponseMessage? response = null;
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, endpoint.Url);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            if (endpoint.UseRange)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99_999_999);
            }
            string userAgent = Configurations.UserAgent.Get();
            request.Headers.TryAddWithoutValidation("User-Agent", string.IsNullOrWhiteSpace(userAgent) ? "Emerde/1.6.7" : userAgent);

            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new NetworkThroughputConnection(endpoint, handler, client, response, stream);
        }
        catch
        {
            response?.Dispose();
            client.Dispose();
            handler.Dispose();
            throw;
        }
    }

    private async Task<NetworkCapacityMeasurement> MeasureBestNetworkThroughputMbpsAsync(
        IReadOnlyCollection<RoomStatusReactive> estimateRooms)
    {
        bool needsDomestic = estimateRooms.Any(room => !IsOverseasPlatform(room.PlatformName));
        bool needsOverseas = estimateRooms.Any(room => IsOverseasPlatform(room.PlatformName));
        NetworkRegionMeasurement? domestic = needsDomestic
            ? await MeasureNetworkRegionThroughputMbpsAsync(NetworkThroughputRegion.Domestic)
            : null;
        NetworkRegionMeasurement? overseas = needsOverseas
            ? await MeasureNetworkRegionThroughputMbpsAsync(NetworkThroughputRegion.Overseas)
            : null;

        double capacityMbps;
        if (needsDomestic && needsOverseas)
        {
            capacityMbps = Math.Min(
                domestic?.Mbps ?? throw new InvalidOperationException("Domestic network capacity test failed."),
                overseas?.Mbps ?? throw new InvalidOperationException("Overseas network capacity test failed."));
        }
        else if (needsOverseas)
        {
            capacityMbps = overseas?.Mbps
                ?? throw new InvalidOperationException("Overseas network capacity test failed.");
        }
        else
        {
            capacityMbps = domestic?.Mbps
                ?? throw new InvalidOperationException("Domestic network capacity test failed.");
        }

        int successfulSamples = (domestic?.SuccessfulSamples ?? 0) + (overseas?.SuccessfulSamples ?? 0);
        int attemptedSamples = (domestic?.AttemptedSamples ?? 0) + (overseas?.AttemptedSamples ?? 0);
        int successfulEndpoints = (domestic?.SuccessfulEndpoints ?? 0) + (overseas?.SuccessfulEndpoints ?? 0);
        return new NetworkCapacityMeasurement(
            capacityMbps,
            domestic,
            overseas,
            successfulSamples,
            attemptedSamples,
            GetNetworkMeasurementConfidence(successfulSamples, attemptedSamples, successfulEndpoints));
    }

    private async Task<NetworkRegionMeasurement?> MeasureNetworkRegionThroughputMbpsAsync(NetworkThroughputRegion region)
    {
        NetworkThroughputEndpoint[] endpoints = NetworkThroughputTestEndpoints
            .Where(endpoint => endpoint.Region == region)
            .ToArray();
        Task<(NetworkThroughputEndpoint Endpoint, double? Mbps)>[] probeTasks = endpoints
            .Select(async endpoint => (
                endpoint,
                await MeasureNetworkEndpointThroughputMbpsAsync(
                    endpoint,
                    connectionCount: 1,
                    roundCount: 1,
                    warmupBytes: 0,
                    bytesPerRound: NetworkThroughputProbeBytes,
                    timeout: TimeSpan.FromSeconds(12))))
            .ToArray();
        (NetworkThroughputEndpoint Endpoint, double? Mbps)[] probes = await Task.WhenAll(probeTasks);

        NetworkThroughputEndpoint[] candidates = probes
            .Where(probe => probe.Mbps.HasValue)
            .OrderByDescending(probe => probe.Mbps)
            .Select(probe => probe.Endpoint)
            .Take(3)
            .ToArray();
        List<NetworkEndpointMeasurement> results = [];
        foreach (NetworkThroughputEndpoint endpoint in candidates)
        {
            NetworkEndpointMeasurement? result = await MeasureNetworkEndpointAsync(
                endpoint,
                NetworkThroughputConnectionsPerEndpoint,
                NetworkThroughputRoundCount,
                NetworkThroughputWarmupBytesPerConnection,
                NetworkThroughputBytesPerConnectionRound,
                TimeSpan.FromSeconds(45));
            if (result != null)
            {
                results.Add(result);
            }
        }

        double? stableMbps = CalculateStableNetworkThroughput(results.Select(result => result.Mbps));
        return stableMbps.HasValue
            ? new NetworkRegionMeasurement(
                stableMbps.Value,
                results.Sum(result => result.SuccessfulSamples),
                candidates.Length * NetworkThroughputRoundCount,
                results.Count,
                candidates.Length)
            : null;
    }

    private async Task<double?> MeasureNetworkEndpointThroughputMbpsAsync(
        NetworkThroughputEndpoint endpoint,
        int connectionCount,
        int roundCount,
        long warmupBytes,
        long bytesPerRound,
        TimeSpan timeout)
    {
        NetworkEndpointMeasurement? measurement = await MeasureNetworkEndpointAsync(
            endpoint,
            connectionCount,
            roundCount,
            warmupBytes,
            bytesPerRound,
            timeout);
        return measurement?.Mbps;
    }

    private async Task<NetworkEndpointMeasurement?> MeasureNetworkEndpointAsync(
        NetworkThroughputEndpoint endpoint,
        int connectionCount,
        int roundCount,
        long warmupBytes,
        long bytesPerRound,
        TimeSpan timeout)
    {
        using CancellationTokenSource cancellationTokenSource = new(timeout);
        List<NetworkThroughputConnection> connections = [];
        try
        {
            for (int index = 0; index < connectionCount; index++)
            {
                try
                {
                    connections.Add(await OpenNetworkThroughputConnectionAsync(endpoint, cancellationTokenSource.Token));
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    AppSessionLogger.Write($"network capacity connection failed, endpoint={endpoint.Name}, index={index}, error={e.Message}");
                }
            }
            if (connections.Count == 0)
            {
                return null;
            }
            if (warmupBytes > 0)
            {
                long?[] warmupSamples = await Task.WhenAll(connections.Select(connection =>
                    ReadNetworkThroughputBytesSafelyAsync(
                        connection,
                        0,
                        warmupBytes,
                        cancellationTokenSource.Token)));
                for (int index = warmupSamples.Length - 1; index >= 0; index--)
                {
                    if (!warmupSamples[index].HasValue)
                    {
                        await connections[index].DisposeAsync();
                        connections.RemoveAt(index);
                    }
                }
                if (connections.Count == 0)
                {
                    return null;
                }
            }

            List<double> rounds = [];
            for (int round = 1; round <= roundCount; round++)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                long?[] connectionSamples = await Task.WhenAll(connections.Select(connection =>
                    ReadNetworkThroughputBytesSafelyAsync(
                        connection,
                        round,
                        bytesPerRound,
                        cancellationTokenSource.Token)));
                stopwatch.Stop();
                long successfulBytes = connectionSamples.Where(value => value.HasValue).Sum(value => value!.Value);
                if (successfulBytes <= 0)
                {
                    continue;
                }
                double measuredMbps = CalculateNetworkThroughputMbps(
                    successfulBytes,
                    stopwatch.Elapsed.TotalSeconds)
                    ?? 0d;
                if (measuredMbps <= 0)
                {
                    continue;
                }
                rounds.Add(measuredMbps);
                AppSessionLogger.Write($"network capacity round completed, endpoint={endpoint.Name}, region={endpoint.Region}, round={round}/{roundCount}, connections={connectionSamples.Count(value => value.HasValue)}/{connectionCount}, measuredMbps={measuredMbps:0.##}");
            }

            double? stableMbps = CalculateStableNetworkThroughput(rounds);
            return stableMbps.HasValue
                ? new NetworkEndpointMeasurement(stableMbps.Value, rounds.Count, roundCount)
                : null;
        }
        finally
        {
            foreach (NetworkThroughputConnection connection in connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

    internal static NetworkMeasurementConfidence GetNetworkMeasurementConfidence(
        int successfulSamples,
        int attemptedSamples,
        int successfulEndpoints)
    {
        if (successfulSamples <= 0 || attemptedSamples <= 0 || successfulEndpoints <= 0)
        {
            return NetworkMeasurementConfidence.Low;
        }

        double ratio = (double)successfulSamples / attemptedSamples;
        if (successfulEndpoints >= 2 && successfulSamples >= 6 && ratio >= 0.8d)
        {
            return NetworkMeasurementConfidence.High;
        }
        return successfulSamples >= 3 && ratio >= 0.5d
            ? NetworkMeasurementConfidence.Medium
            : NetworkMeasurementConfidence.Low;
    }

    internal static bool IsOverseasPlatform(string? platformName)
    {
        return !string.IsNullOrWhiteSpace(platformName) && OverseasPlatformNames.Contains(platformName);
    }

    internal static double? CalculateNetworkThroughputMbps(long totalBytes, double elapsedSeconds)
    {
        if (totalBytes < 64 * 1024 || elapsedSeconds <= 0.1d || double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
        {
            return null;
        }

        return totalBytes * 8d / elapsedSeconds / 1_000_000d;
    }

    private async Task<long?> ReadNetworkThroughputBytesSafelyAsync(
        NetworkThroughputConnection connection,
        int round,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            long totalBytes = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            TimeSpan sampleDuration = TimeSpan.FromSeconds(4);
            while (stopwatch.Elapsed < sampleDuration && totalBytes < maxBytes)
            {
                int read = await connection.Stream.ReadAsync(connection.Buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }
                totalBytes += read;
            }

            stopwatch.Stop();
            return totalBytes >= 64 * 1024 ? totalBytes : null;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            AppSessionLogger.Write($"network capacity round connection failed, round={round}, error={e.Message}");
            return null;
        }
    }

    private static double EstimateRequiredMbps(RoomStatusReactive room)
    {
        double? parsedBitrate = ParseBitrateMbps(room.Bitrate);
        if (parsedBitrate is > 0)
        {
            return parsedBitrate.Value;
        }

        double? urlBitrate = ParseStreamUrlBitrateMbps(room.FlvUrl, room.HlsUrl, room.PreviewUrl);
        if (urlBitrate is > 0)
        {
            return urlBitrate.Value;
        }

        string resolution = room.ResolutionText;
        if (resolution.Contains("2160", StringComparison.OrdinalIgnoreCase) || resolution.Contains("4k", StringComparison.OrdinalIgnoreCase))
        {
            return 18d;
        }

        if (resolution.Contains("1440", StringComparison.OrdinalIgnoreCase))
        {
            return 12d;
        }

        if (resolution.Contains("1080", StringComparison.OrdinalIgnoreCase))
        {
            return 8d;
        }

        if (resolution.Contains("720", StringComparison.OrdinalIgnoreCase))
        {
            return 4d;
        }

        return 10d;
    }

    private static double? ParseStreamUrlBitrateMbps(params string[] urls)
    {
        string[] keys = ["origin_bitrate", "bitrate", "bandwidth"];

        foreach (string url in urls.Where(url => !string.IsNullOrWhiteSpace(url)))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                continue;
            }

            NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
            foreach (string key in keys)
            {
                double? value = ParseBitrateMbps(query[key]);
                if (value is > 0)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static double? ParseBitrateMbps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string normalized = text.Trim().ToLowerInvariant();
        string numberText = new(normalized
            .SkipWhile(character => !char.IsDigit(character))
            .TakeWhile(character => char.IsDigit(character) || character == '.' || character == ',')
            .ToArray());

        if (!double.TryParse(numberText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
        {
            return null;
        }

        if (normalized.Contains("kb", StringComparison.OrdinalIgnoreCase) || normalized.Contains("kbit", StringComparison.OrdinalIgnoreCase))
        {
            return value / 1000d;
        }

        if (normalized.Contains("mb", StringComparison.OrdinalIgnoreCase) || normalized.Contains("m", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (value >= 100_000d)
        {
            return value / 1_000_000d;
        }

        if (value >= 1000d)
        {
            return value / 1000d;
        }

        return value;
    }

    internal static void ApplyRoomInfoResult(RoomStatusReactive room, ISpiderResult result, string? avatarLocalPath = null)
    {
        string? title = SpiderResultMetadata.GetTitle(result);
        string? quality = SpiderResultMetadata.GetQuality(result);
        string? resolution = SpiderResultMetadata.GetResolution(result);
        string? bitrate = SpiderResultMetadata.GetBitrate(result);
        string? headers = SpiderResultMetadata.GetHeaders(result);
        if (!string.IsNullOrWhiteSpace(result.Nickname))
        {
            room.NickName = result.Nickname;
        }

        if (!string.IsNullOrWhiteSpace(result.AvatarThumbUrl))
        {
            room.AvatarThumbUrl = result.AvatarThumbUrl;
            room.AvatarLocalPath = string.IsNullOrWhiteSpace(avatarLocalPath)
                ? AvatarCache.GetCachedAvatarSource(room.RoomUrl)
                : avatarLocalPath;
        }

        if (!string.IsNullOrWhiteSpace(result.PlatformName))
        {
            room.PlatformName = result.PlatformName;
        }
        else if (!string.IsNullOrWhiteSpace(result.RoomUrl))
        {
            room.PlatformName = Spider.GetPlatformName(result.RoomUrl);
        }

        if (result.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(title))
        {
            room.LiveTitle = title;
        }
        else if (result.IsLiveStreaming == false)
        {
            room.LiveTitle = string.Empty;
        }

        if (result.IsLiveStreaming == true)
        {
            room.Quality = quality ?? room.Quality;
            room.Resolution = resolution ?? room.Resolution;
            room.Bitrate = bitrate ?? room.Bitrate;
        }
        else if (result.IsLiveStreaming == false)
        {
            room.Quality = string.Empty;
            room.Resolution = string.Empty;
            room.Bitrate = string.Empty;
        }

        bool hasStreamUrl = !string.IsNullOrWhiteSpace(result.RecordUrl)
            || !string.IsNullOrWhiteSpace(result.FlvUrl)
            || !string.IsNullOrWhiteSpace(result.HlsUrl);
        if (result.IsLiveStreaming.HasValue || hasStreamUrl)
        {
            room.FlvUrl = result.FlvUrl ?? string.Empty;
            room.HlsUrl = result.HlsUrl ?? string.Empty;
            room.RecordUrl = result.RecordUrl ?? string.Empty;
        }
        if (result.IsLiveStreaming.HasValue)
        {
            room.Headers = headers ?? string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(headers))
        {
            room.Headers = headers;
        }

        if (!string.IsNullOrWhiteSpace(result.Uid))
        {
            room.Uid = result.Uid;
        }
        room.StreamStatus = GlobalMonitor.ResolveStreamStatus(room.StreamStatus, result.IsLiveStreaming, hasStreamUrl);

        RoomStatus status = GlobalMonitor.RoomStatus.GetOrAdd(room.RoomUrl, _ => new RoomStatus()
        {
            NickName = room.NickName,
            RoomUrl = room.RoomUrl,
            PlatformName = room.PlatformName,
            StreamStatus = StreamStatus.Initialized,
        });
        status.NickName = room.NickName;
        status.AvatarThumbUrl = room.AvatarThumbUrl;
        status.AvatarLocalPath = room.AvatarLocalPath;
        status.PlatformName = room.PlatformName;
        status.LiveTitle = room.LiveTitle;
        status.Uid = room.Uid;
        status.Quality = room.Quality;
        status.Resolution = room.Resolution;
        status.Bitrate = room.Bitrate;
        status.Headers = room.Headers;
        status.FlvUrl = room.FlvUrl;
        status.HlsUrl = room.HlsUrl;
        status.RecordUrl = room.RecordUrl;
        status.StreamStatus = room.StreamStatus;
        if (room.StreamStatus != StreamStatus.Streaming)
        {
            GlobalMonitor.ResetLiveSessionMetadata(status);
        }
        SaveRoomInfo(room);
        room.FlashRefresh();
    }

    private static void SaveRoomInfo(RoomStatusReactive source)
    {
        Room[] rooms = Configurations.Rooms.Get();
        Room? room = rooms.FirstOrDefault(item => string.Equals(item.RoomUrl, source.RoomUrl, StringComparison.OrdinalIgnoreCase));
        if (room == null)
        {
            return;
        }

        ApplyRoomStatusToRoom(source, room);
        Configurations.Rooms.Set(rooms);
        ConfigurationSaveScheduler.Request();
    }

    private static void ApplyRoomStatusToRoom(RoomStatusReactive source, Room target)
    {
        target.NickName = source.NickName;
        target.RoomUrl = NormalizeRoomUrl(source.RoomUrl);
        target.AvatarThumbUrl = source.AvatarThumbUrl;
        target.PlatformName = source.PlatformName;
        target.LiveTitle = source.LiveTitle;
        target.Uid = source.Uid;
        target.Quality = source.Quality;
        target.Resolution = source.Resolution;
        target.Bitrate = source.Bitrate;
        target.Headers = source.Headers;
        target.FlvUrl = source.FlvUrl;
        target.HlsUrl = source.HlsUrl;
        target.RecordUrl = source.RecordUrl;
        target.IsToNotify = source.IsToNotify;
        target.IsToRecord = source.IsToRecord;
        target.IsToMonitor = source.IsToMonitor;
        target.IsFollowGlobalSettings = source.IsFollowGlobalSettings;
    }

    private static Task<string> CacheAvatarAsync(string roomUrl, ISpiderResult result, CancellationToken token = default)
    {
        return string.IsNullOrWhiteSpace(result.AvatarThumbUrl)
            ? Task.FromResult(AvatarCache.GetCachedAvatarSource(roomUrl))
            : AvatarCache.UpdateAsync(roomUrl, result.AvatarThumbUrl, token);
    }

    public void MoveRoom(RoomStatusReactive source, int newVisibleIndex)
    {
        MoveRooms([source], newVisibleIndex);
    }

    public void MoveRooms(IReadOnlyCollection<RoomStatusReactive> sources, int newVisibleIndex)
    {
        if (sources.Count == 0)
        {
            return;
        }

        List<RoomStatusReactive> visibleRooms = RoomStatusesView.Cast<RoomStatusReactive>().ToList();
        RoomStatusReactive[] movingRooms = visibleRooms.Where(sources.Contains).ToArray();
        if (movingRooms.Length == 0)
        {
            return;
        }

        RoomStatusReactive[] nextOrder = BuildMovedRoomOrder(RoomStatuses.ToArray(), visibleRooms, movingRooms, newVisibleIndex);
        if (RoomStatuses.SequenceEqual(nextOrder))
        {
            return;
        }

        RoomStatusReactive selected = SelectedItem;
        RoomStatuses.Reset(nextOrder);
        RestoreSelectedRoom(selected);
        SaveRoomOrder();
        RoomStatusesView.Refresh();
    }

    internal static RoomStatusReactive[] BuildMovedRoomOrder(
        IReadOnlyList<RoomStatusReactive> allRooms,
        IReadOnlyList<RoomStatusReactive> visibleRooms,
        IReadOnlyCollection<RoomStatusReactive> movingRooms,
        int insertionIndex)
    {
        HashSet<RoomStatusReactive> moving = movingRooms.ToHashSet();
        RoomStatusReactive[] orderedMoving = visibleRooms.Where(moving.Contains).ToArray();
        RoomStatusReactive[] remainingVisible = visibleRooms.Where(room => !moving.Contains(room)).ToArray();
        if (orderedMoving.Length == 0 || remainingVisible.Length == 0)
        {
            return allRooms.ToArray();
        }

        insertionIndex = Math.Clamp(insertionIndex, 0, visibleRooms.Count);
        int removedBeforeInsertion = visibleRooms.Take(insertionIndex).Count(moving.Contains);
        int adjustedInsertionIndex = Math.Clamp(insertionIndex - removedBeforeInsertion, 0, remainingVisible.Length);
        RoomStatusReactive? target = remainingVisible.ElementAtOrDefault(adjustedInsertionIndex);
        List<RoomStatusReactive> result = allRooms.Where(room => !moving.Contains(room)).ToList();
        int targetIndex = target == null
            ? result.IndexOf(remainingVisible[^1]) + 1
            : result.IndexOf(target);
        result.InsertRange(Math.Clamp(targetIndex, 0, result.Count), orderedMoving);
        return result.ToArray();
    }

    internal RoomStatusReactive[] GetRoomsForMove(RoomStatusReactive source)
    {
        if (source.IsSelected)
        {
            RoomStatusReactive[] selected = RoomStatusesView.Cast<RoomStatusReactive>().Where(room => room.IsSelected).ToArray();
            if (selected.Length > 0)
            {
                return selected;
            }
        }

        return [source];
    }

    internal void BeginRoomMultiSelect()
    {
        IsRoomMultiSelectMode = true;
        RefreshRoomSelectionSummary();
    }

    internal void SelectRoom(RoomStatusReactive room, bool toggleSelection, bool selectRange)
    {
        BeginRoomMultiSelect();
        ApplyRoomSelectionChange(() =>
        {
            RoomStatusReactive[] visibleRooms = RoomStatusesView.Cast<RoomStatusReactive>().ToArray();
            if (selectRange && lastSelectedRoom != null)
            {
                int start = Array.IndexOf(visibleRooms, lastSelectedRoom);
                int end = Array.IndexOf(visibleRooms, room);
                if (start >= 0 && end >= 0)
                {
                    if (start > end)
                    {
                        (start, end) = (end, start);
                    }

                    for (int index = start; index <= end; index++)
                    {
                        visibleRooms[index].IsSelected = true;
                    }
                    lastSelectedRoom = room;
                    return;
                }
            }

            if (!toggleSelection)
            {
                foreach (RoomStatusReactive candidate in RoomStatuses)
                {
                    candidate.IsSelected = ReferenceEquals(candidate, room);
                }
            }
            else
            {
                room.IsSelected = !room.IsSelected;
            }
            lastSelectedRoom = room.IsSelected ? room : null;
        });
    }

    internal void SelectRooms(IEnumerable<RoomStatusReactive> rooms)
    {
        RoomStatusReactive[] targets = rooms.Distinct().ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        BeginRoomMultiSelect();
        ApplyRoomSelectionChange(() =>
        {
            foreach (RoomStatusReactive room in targets)
            {
                room.IsSelected = true;
            }
            lastSelectedRoom = targets[^1];
        });
    }

    [RelayCommand]
    private void SelectAllRoomCards()
    {
        BeginRoomMultiSelect();
        ApplyRoomSelectionChange(() =>
        {
            foreach (RoomStatusReactive room in RoomStatusesView.Cast<RoomStatusReactive>())
            {
                room.IsSelected = true;
            }
        });
    }

    [RelayCommand]
    private void InvertRoomCardSelection()
    {
        BeginRoomMultiSelect();
        ApplyRoomSelectionChange(() =>
        {
            foreach (RoomStatusReactive room in RoomStatusesView.Cast<RoomStatusReactive>())
            {
                room.IsSelected = !room.IsSelected;
            }
        });
    }

    [RelayCommand]
    internal void CancelRoomMultiSelect()
    {
        ApplyRoomSelectionChange(() =>
        {
            foreach (RoomStatusReactive room in RoomStatuses)
            {
                room.IsSelected = false;
            }
        });
        IsRoomMultiSelectMode = false;
        lastSelectedRoom = null;
        RefreshRoomSelectionSummary();
    }

    [RelayCommand]
    internal void UndoRoomSelection()
    {
        if (roomHistoryUndoStack.Count == 0)
        {
            return;
        }

        RoomHistoryEntry entry = roomHistoryUndoStack.Pop();
        roomHistoryRedoStack.Push(entry);
        RestoreRoomHistoryEntry(entry, restoreBefore: true);
        RefreshRoomSelectionSummary();
    }

    [RelayCommand]
    internal void RedoRoomSelection()
    {
        if (roomHistoryRedoStack.Count == 0)
        {
            return;
        }

        RoomHistoryEntry entry = roomHistoryRedoStack.Pop();
        roomHistoryUndoStack.Push(entry);
        RestoreRoomHistoryEntry(entry, restoreBefore: false);
        RefreshRoomSelectionSummary();
    }

    private void ApplyRoomSelectionChange(Action change)
    {
        HashSet<string> before = CaptureSelectedRoomUrls();
        change();
        HashSet<string> after = CaptureSelectedRoomUrls();
        if (!before.SetEquals(after))
        {
            PushRoomHistory(new RoomSelectionHistoryEntry(before, after));
        }

        RefreshRoomSelectionSummary();
    }

    private HashSet<string> CaptureSelectedRoomUrls()
    {
        return RoomStatuses.Where(room => room.IsSelected)
            .Select(room => room.RoomUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RestoreRoomSelection(ISet<string> selectedRoomUrls)
    {
        foreach (RoomStatusReactive room in RoomStatuses)
        {
            room.IsSelected = selectedRoomUrls.Contains(room.RoomUrl);
        }

        IsRoomMultiSelectMode = selectedRoomUrls.Count > 0;
        lastSelectedRoom = RoomStatusesView.Cast<RoomStatusReactive>().LastOrDefault(room => room.IsSelected);
        RefreshRoomSelectionSummary();
    }

    private void ClearRoomSelection()
    {
        foreach (RoomStatusReactive room in RoomStatuses)
        {
            room.IsSelected = false;
        }

        IsRoomMultiSelectMode = false;
        lastSelectedRoom = null;
        RefreshRoomSelectionSummary();
    }

    private RoomListHistoryState CaptureRoomListHistoryState()
    {
        return new RoomListHistoryState(
            Configurations.Rooms.Get().Select(CloneRoom).ToArray(),
            CaptureSelectedRoomUrls(),
            SelectedItem?.RoomUrl ?? string.Empty);
    }

    private void PushRoomHistory(RoomHistoryEntry entry)
    {
        roomHistoryUndoStack.Push(entry);
        while (roomHistoryUndoStack.Count > RoomHistoryLimit)
        {
            RoomHistoryEntry[] entries = roomHistoryUndoStack.Reverse().Skip(1).ToArray();
            roomHistoryUndoStack.Clear();
            foreach (RoomHistoryEntry historyEntry in entries)
            {
                roomHistoryUndoStack.Push(historyEntry);
            }
        }
        roomHistoryRedoStack.Clear();
        RefreshRoomSelectionSummary();
    }

    private void RestoreRoomHistoryEntry(RoomHistoryEntry entry, bool restoreBefore)
    {
        switch (entry)
        {
            case RoomSelectionHistoryEntry selection:
                RestoreRoomSelection(restoreBefore ? selection.Before : selection.After);
                break;
            case RoomListHistoryEntry roomList:
                RestoreRoomListHistoryState(restoreBefore ? roomList.Before : roomList.After);
                break;
        }
    }

    private void RestoreRoomListHistoryState(RoomListHistoryState state)
    {
        HashSet<string> targetUrls = state.Rooms
            .Select(room => room.RoomUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (RoomStatusReactive room in RoomStatuses.Where(room => !targetUrls.Contains(room.RoomUrl)).ToArray())
        {
            StopAndRemoveMonitoredRoom(room.RoomUrl);
        }

        Room[] restoredConfiguration = BuildRestoredRoomConfiguration(Configurations.Rooms.Get(), state.Rooms);
        Dictionary<string, RoomStatusReactive> currentRooms = RoomStatuses
            .Where(room => !string.IsNullOrWhiteSpace(room.RoomUrl))
            .GroupBy(room => room.RoomUrl, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        RoomStatusReactive[] restoredRooms = state.Rooms
            .Select((room, index) =>
            {
                if (currentRooms.TryGetValue(room.RoomUrl, out RoomStatusReactive? existing))
                {
                    existing.AddedOrder = index;
                    return existing;
                }
                return CreateRoomStatusReactive(room, index);
            })
            .ToArray();

        Configurations.Rooms.Set(restoredConfiguration);
        ConfigurationSaveScheduler.Request();
        RoomStatuses.Reset(restoredRooms);
        RoomStatusesView.Refresh();
        RestoreRoomSelection(state.SelectedRoomUrls);
        SelectedItem = RoomStatuses.FirstOrDefault(room => string.Equals(room.RoomUrl, state.SelectedRoomUrl, StringComparison.OrdinalIgnoreCase))
            ?? RoomStatuses.FirstOrDefault()
            ?? new RoomStatusReactive();
        OnPropertyChanged(nameof(PlatformSummaryText));
        OnPropertyChanged(nameof(PlatformFilterOptions));
    }

    internal static Room[] BuildRestoredRoomConfiguration(IEnumerable<Room> currentRooms, IReadOnlyList<Room> targetRooms)
    {
        Dictionary<string, Room> currentConfiguration = currentRooms
            .Where(room => !string.IsNullOrWhiteSpace(room.RoomUrl))
            .GroupBy(room => room.RoomUrl, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return targetRooms
            .Select(room => currentConfiguration.TryGetValue(room.RoomUrl, out Room? current)
                ? CloneRoom(current)
                : CloneRoom(room))
            .ToArray();
    }

    private void StopAndRemoveMonitoredRoom(string roomUrl)
    {
        if (GlobalMonitor.RoomStatus.TryGetValue(roomUrl, out RoomStatus? roomStatus))
        {
            roomStatus.Recorder.Stop();
            _ = GlobalMonitor.RoomStatus.TryRemove(roomUrl, out _);
        }
        GlobalMonitor.ClearTemporaryRoomOverrides(roomUrl);
    }

    private void RefreshRoomSelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedRoomCount));
        OnPropertyChanged(nameof(HasSelectedRooms));
        OnPropertyChanged(nameof(CanUndoRoomSelection));
        OnPropertyChanged(nameof(CanRedoRoomSelection));
        OnPropertyChanged(nameof(SelectedRoomSummary));
    }

    private void RestoreSelectedRoom(RoomStatusReactive? selected)
    {
        if (selected == null || string.IsNullOrWhiteSpace(selected.RoomUrl))
        {
            SelectedItem = RoomStatuses.FirstOrDefault() ?? new RoomStatusReactive();
            return;
        }

        SelectedItem = RoomStatuses.FirstOrDefault(room => room.RoomUrl == selected.RoomUrl)
            ?? RoomStatuses.FirstOrDefault()
            ?? new RoomStatusReactive();
    }

    private static async Task CopyTextToClipboardAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.Warning("FailOp".Tr());
            return;
        }

        if (await ClipboardService.SetTextAsync(text))
        {
            Toast.Success("SuccOp".Tr());
        }
        else
        {
            Toast.Warning("FailOp".Tr());
        }
    }

    [RelayCommand]
    private async Task PlayRecordAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        if (GlobalMonitor.RoomStatus.TryGetValue(SelectedItem.RoomUrl, out RoomStatus? roomStatus)
         && File.Exists(roomStatus.Recorder.FileName))
        {
            await Player.PlayAsync(roomStatus.Recorder.FileName, isSeekable: roomStatus.RecordStatus == RecordStatus.Recording);
        }
        else
        {
            Toast.Warning("PlayerErrorOfNoFile".Tr());
        }
    }

    [RelayCommand]
    private void RowUpRoomUrl()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        RoomStatusReactive? roomStatusReactive = RoomStatuses.FirstOrDefault(roomStatus => roomStatus.RoomUrl == SelectedItem.RoomUrl);

        if (roomStatusReactive == null)
        {
            return;
        }

        RoomStatuses.MoveUp(roomStatusReactive);
        SaveRoomOrder();
        RestoreSelectedRoomAfterReorder(roomStatusReactive);
    }

    [RelayCommand]
    private void RowDownRoomUrl()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        RoomStatusReactive? roomStatusReactive = RoomStatuses.FirstOrDefault(roomStatus => roomStatus.RoomUrl == SelectedItem.RoomUrl);

        if (roomStatusReactive == null)
        {
            return;
        }

        RoomStatuses.MoveDown(roomStatusReactive);
        SaveRoomOrder();
        RestoreSelectedRoomAfterReorder(roomStatusReactive);
    }

    private void RestoreSelectedRoomAfterReorder(RoomStatusReactive room)
    {
        RoomStatusesView.Refresh();
        SelectRoom(room, false, false);
        SelectedItem = room;
        OnPropertyChanged(nameof(SelectedItem));
    }

    private void SaveRoomOrder()
    {
        Dictionary<string, Room> roomsByUrl = [];

        for (int index = 0; index < RoomStatuses.Count; index++)
        {
            RoomStatuses[index].AddedOrder = index;
        }

        foreach (Room room in Configurations.Rooms.Get().Where(room => !string.IsNullOrWhiteSpace(room.RoomUrl)))
        {
            string normalizedRoomUrl = NormalizeRoomUrl(room.RoomUrl);
            if (string.IsNullOrWhiteSpace(normalizedRoomUrl))
            {
                continue;
            }

            room.RoomUrl = normalizedRoomUrl;
            roomsByUrl[normalizedRoomUrl] = room;
        }

        Room[] rooms = RoomStatuses
            .Where(roomStatus => !string.IsNullOrWhiteSpace(roomStatus.RoomUrl))
            .Select(roomStatus =>
            {
                if (roomsByUrl.TryGetValue(roomStatus.RoomUrl, out Room? room))
                {
                    ApplyRoomStatusToRoom(roomStatus, room);
                    return room;
                }

                Room newRoom = new()
                {
                    NickName = roomStatus.NickName,
                    RoomUrl = NormalizeRoomUrl(roomStatus.RoomUrl),
                };
                ApplyRoomStatusToRoom(roomStatus, newRoom);
                return newRoom;
            })
            .ToArray();

        Configurations.Rooms.Set(rooms);
        ConfigurationSaveScheduler.Request();
    }

    [RelayCommand]
    private async Task ToggleSelectedRoomMonitorAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        if (SelectedItem.IsFollowGlobalSettings)
        {
            bool enabled = !GlobalMonitor.GetEffectiveRoomMonitor(SelectedItem.RoomUrl, SelectedItem.IsToMonitor, true);
            GlobalMonitor.SetTemporaryRoomMonitor(SelectedItem.RoomUrl, enabled);
            SelectedItem.RefreshStatus();

            if (enabled)
            {
                GlobalMonitor.Start();
                await GlobalMonitor.RunRoomAsync(SelectedItem.RoomUrl);
            }

            RefreshRoomEffectiveStates();
            return;
        }

        SelectedItem.IsToMonitor = !SelectedItem.IsToMonitor;
        SaveSelectedRoomSettings();
        SelectedItem.RefreshStatus();

        if (SelectedItem.IsToMonitor)
        {
            if (!Configurations.IsMonitorRunning.Get())
            {
                Configurations.IsMonitorRunning.Set(true);
                ConfigurationSaveScheduler.Request();
                StatusOfIsMonitorRunning = true;
            }

            GlobalMonitor.Start();
            await GlobalMonitor.RunRoomAsync(SelectedItem.RoomUrl);
        }

        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private async Task ToggleSelectedRoomRecordAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        if (SelectedItem.IsFollowGlobalSettings)
        {
            bool enabled = !GlobalMonitor.GetEffectiveRoomRecord(SelectedItem.RoomUrl, SelectedItem.IsToRecord, true);
            GlobalMonitor.SetTemporaryRoomRecord(SelectedItem.RoomUrl, enabled);
            SelectedItem.RefreshStatus();

            if (enabled && SelectedItem.EffectiveIsToMonitor)
            {
                GlobalMonitor.Start();
                await GlobalMonitor.RunRoomAsync(SelectedItem.RoomUrl);
            }
            else if (!enabled)
            {
                StopSelectedRoomRecording();
            }

            RefreshRoomEffectiveStates();
            return;
        }

        SelectedItem.IsToRecord = !SelectedItem.IsToRecord;
        SaveSelectedRoomSettings();
        SelectedItem.RefreshStatus();

        if (SelectedItem.IsToRecord && SelectedItem.EffectiveIsToMonitor)
        {
            GlobalMonitor.ClearTemporaryRoomRecord(SelectedItem.RoomUrl);

            if (!Configurations.IsMonitorRunning.Get())
            {
                Configurations.IsMonitorRunning.Set(true);
                ConfigurationSaveScheduler.Request();
                StatusOfIsMonitorRunning = true;
            }

            GlobalMonitor.Start();
            await GlobalMonitor.RunRoomAsync(SelectedItem.RoomUrl);
        }

        if (!SelectedItem.IsToRecord)
        {
            StopSelectedRoomRecording();
        }

        RefreshRoomEffectiveStates();
    }

    private void StopGlobalFollowRecorders()
    {
        Room[] rooms = Configurations.Rooms.Get();
        foreach (RoomStatus roomStatus in GlobalMonitor.RoomStatus.Values)
        {
            Room? room = rooms.FirstOrDefault(room => string.Equals(room.RoomUrl, roomStatus.RoomUrl, StringComparison.OrdinalIgnoreCase));
            if (room is { IsFollowGlobalSettings: false } && GlobalMonitor.GetEffectiveRoomRecord(room))
            {
                continue;
            }

            if (roomStatus.RecordStatus == RecordStatus.Recording)
            {
                roomStatus.Recorder.Stop();
            }

            roomStatus.RecordStatus = RecordStatus.Disabled;
        }
    }

    private void StopSelectedRoomRecording()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        if (GlobalMonitor.RoomStatus.TryGetValue(SelectedItem.RoomUrl, out RoomStatus? roomStatus)
            && roomStatus.RecordStatus == RecordStatus.Recording)
        {
            GlobalMonitor.SetTemporaryRoomRecord(SelectedItem.RoomUrl, false);
            roomStatus.Recorder.Stop();
            roomStatus.RecordStatus = RecordStatus.Disabled;
            SelectedItem.RecordStatus = RecordStatus.Disabled;
            SelectedItem.RefreshStatus();
        }
    }

    [RelayCommand]
    private void IsFollowGlobalSettings()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        SaveSelectedRoomSettings();
        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private Task OpenLocalSettingsAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return Task.CompletedTask;
        }

        return OpenLocalSettingsDialogAsync();
    }

    private async Task OpenLocalSettingsDialogAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        LocalSettingsContentDialog content = new(SelectedItem);
        Window? owner = Application.Current?.MainWindow;
        ContentDialog dialog = new()
        {
            Title = "SingleSettings".Tr(),
            Content = content,
            PrimaryButtonText = "Save".Tr(),
            CloseButtonText = "ButtonOfCancel".Tr(),
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current?.TryFindResource("DefaultVioletaContentDialogStyle") as Style,
        };
        content.ApplyDialogVisualSize(dialog, owner);

        using DialogBlurScope blurScope = DialogBlurScope.ForLightDismiss(owner, dialog);
        ContentDialogResult result = await ShowMainContentDialogAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        SelectedItem.IsFollowGlobalSettings = content.IsFollowGlobalSettings;
        SelectedItem.IsToNotify = content.IsToNotify;
        SelectedItem.IsToMonitor = content.IsToMonitor;
        SelectedItem.IsToRecord = content.IsToRecord;
        SaveSelectedRoomSettings(content.GetRecordingOptions());
        RefreshRoomEffectiveStates();
        Toast.Success("SuccOp".Tr());
    }

    [RelayCommand]
    private void ExitApplication()
    {
        TrayIconManager.GetInstance().ShutdownApplication();
    }

    [RelayCommand]
    private async Task RemoveRoomUrlAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        RoomStatusReactive[] targets = IsRoomMultiSelectMode && SelectedItem.IsSelected
            ? RoomStatuses.Where(room => room.IsSelected).ToArray()
            : [SelectedItem];
        if (targets.Length == 0)
        {
            return;
        }

        string prompt = targets.Length == 1
            ? "SureRemoveRoom".Tr(targets[0].NickName)
            : $"确定移除选中的 {targets.Length} 个直播间吗？";
        using DialogBlurScope blurScope = new(Application.Current.MainWindow);
        MessageBoxResult result = await MessageBox.QuestionAsync(prompt);

        if (result == MessageBoxResult.Yes)
        {
            RoomListHistoryState before = CaptureRoomListHistoryState();
            HashSet<string> roomUrls = targets.Select(room => room.RoomUrl).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int removedIndex = targets
                .Select(RoomStatuses.IndexOf)
                .Where(index => index >= 0)
                .DefaultIfEmpty(0)
                .Min();

            foreach (RoomStatusReactive target in targets)
            {
                StopAndRemoveMonitoredRoom(target.RoomUrl);
                RoomStatuses.Remove(target);
            }

            ClearRoomSelection();
            RoomStatusesView.Refresh();
            OnPropertyChanged(nameof(PlatformSummaryText));
            OnPropertyChanged(nameof(PlatformFilterOptions));

            List<Room> rooms = [.. Configurations.Rooms.Get()];
            rooms.RemoveAll(room => roomUrls.Contains(room.RoomUrl));
            Configurations.Rooms.Set([.. rooms]);
            ConfigurationSaveScheduler.Request();
            SelectedItem = RoomStatuses.Count == 0
                ? new RoomStatusReactive()
                : RoomStatuses[Math.Clamp(removedIndex, 0, RoomStatuses.Count - 1)];
            PushRoomHistory(new RoomListHistoryEntry(before, CaptureRoomListHistoryState()));

            Toast.Success("SuccOp".Tr());
        }
    }

    [RelayCommand]
    private async Task GotoRoomUrlAsync(RoomStatusReactive? roomStatus = null)
    {
        RoomStatusReactive? targetRoom = roomStatus ?? SelectedItem;
        if (targetRoom == null || string.IsNullOrWhiteSpace(targetRoom.RoomUrl))
        {
            return;
        }

        // TODO: Implement for other platforms
        await Launcher.LaunchUriAsync(new Uri(targetRoom.RoomUrl));
    }

    [RelayCommand]
    private async Task StopRecordAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        if (GlobalMonitor.RoomStatus.TryGetValue(SelectedItem.RoomUrl, out RoomStatus? roomStatus))
        {
            if (roomStatus.RecordStatus == RecordStatus.Recording)
            {
                // https://github.com/qzj1472/Emerde/issues/13
                // https://github.com/qzj1472/Emerde/issues/19

                StackPanel content = new();
                CheckBox checkBox = new()
                {
                    Content = "EnableRecord".Tr(),
                    DataContext = SelectedItem,
                };

                // Do not use `CheckBox::Checked`, because it will be triggered when the CheckBox is loaded
                checkBox.Click += (_, _) =>
                {
                    IsToRecord();
                    Toast.Success("SuccOp".Tr());
                };

                // We not need to binding with two way, because we update the config through method `IsToRecord()`.
                checkBox.SetBinding(CheckBox.IsCheckedProperty, nameof(RoomStatusReactive.IsToRecord));

                content.Children.Add(new TextBlock()
                {
                    Text = "SureStopRecord".Tr(roomStatus.NickName)
                });
                content.Children.Add(checkBox);

                ContentDialog dialog = new()
                {
                    Title = "StopRecord".Tr(),
                    Content = content,
                    CloseButtonText = "ButtonOfCancel".Tr(),
                    PrimaryButtonText = "StopRecord".Tr(),
                    DefaultButton = ContentDialogButton.Primary,
                };

                using DialogBlurScope blurScope = DialogBlurScope.ForDialog(Application.Current.MainWindow, dialog);
                ContentDialogResult result = await ShowMainContentDialogAsync(dialog);

                if (result == ContentDialogResult.Primary)
                {
                    roomStatus.Recorder.Stop();
                    Toast.Success("SuccOp".Tr());
                }
            }
            else
            {
                Toast.Warning("NoRecordTask".Tr());
            }
        }
        else
        {
            Toast.Warning("NoRecordTask".Tr());
        }
    }

    [RelayCommand]
    private void ShowRecordLog()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        // TODO
        Toast.Warning("ComingSoon".Tr() + " ...");
    }

    [RelayCommand]
    private void IsToNotify()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        RoomStatusReactive? roomStatusReactive = RoomStatuses.Where(room => room.RoomUrl == SelectedItem.RoomUrl).FirstOrDefault();

        if (roomStatusReactive != null)
        {
            roomStatusReactive.IsToNotify = SelectedItem.IsToNotify;
        }

        Room[] rooms = Configurations.Rooms.Get();
        Room? room = rooms.Where(room => room.RoomUrl == SelectedItem.RoomUrl).FirstOrDefault();

        if (room != null)
        {
            room.IsToNotify = SelectedItem.IsToNotify;
        }
        Configurations.Rooms.Set(rooms);
        ConfigurationSaveScheduler.Request();
        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private void IsToRecord()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        RoomStatusReactive? roomStatusReactive = RoomStatuses.Where(room => room.RoomUrl == SelectedItem.RoomUrl).FirstOrDefault();

        if (roomStatusReactive != null)
        {
            roomStatusReactive.IsToRecord = SelectedItem.IsToRecord;
        }

        Room[] rooms = Configurations.Rooms.Get();
        Room? room = rooms.Where(room => room.RoomUrl == SelectedItem.RoomUrl).FirstOrDefault();

        if (room != null)
        {
            room.IsToRecord = SelectedItem.IsToRecord;
        }
        Configurations.Rooms.Set(rooms);
        ConfigurationSaveScheduler.Request();
        RefreshRoomEffectiveStates();
    }

    private void SaveSelectedRoomSettings(RoomRecordingOptions? recordingOptions = null)
    {
        RoomStatusReactive? roomStatusReactive = RoomStatuses.Where(room => room.RoomUrl == SelectedItem.RoomUrl).FirstOrDefault();

        if (roomStatusReactive != null)
        {
            roomStatusReactive.IsToNotify = SelectedItem.IsToNotify;
            roomStatusReactive.IsToRecord = SelectedItem.IsToRecord;
            roomStatusReactive.IsToMonitor = SelectedItem.IsToMonitor;
            roomStatusReactive.IsFollowGlobalSettings = SelectedItem.IsFollowGlobalSettings;
        }

        Room[] rooms = Configurations.Rooms.Get();
        Room? room = rooms.Where(room => room.RoomUrl == SelectedItem.RoomUrl).FirstOrDefault();

        if (room != null)
        {
            ApplyRoomStatusToRoom(SelectedItem, room);
            if (recordingOptions != null)
            {
                RoomRecordingSettings.Apply(room, recordingOptions);
            }
        }

        Configurations.Rooms.Set(rooms);
        ConfigurationSaveScheduler.Request();
        GlobalMonitor.RefreshRoutineInterval();
    }

    private void RefreshRoomEffectiveStates()
    {
        foreach (RoomStatusReactive room in RoomStatuses)
        {
            room.RefreshStatus();
        }
    }

    [RelayCommand]
    private void OnContextMenuLoaded(RelayEventParameter param)
    {
        ContextMenu sender = (ContextMenu)param.Deconstruct().Sender;

        sender.Opened -= ContextMenuOpened;
        sender.Opened += ContextMenuOpened;

        // Closure method
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu { } contextMenu
             && contextMenu.Parent is Popup { } popup
             && popup.PlacementTarget is DataGrid { } dataGrid)
            {
                if (dataGrid.InputHitTest(Mouse.GetPosition(dataGrid)) is FrameworkElement { } element)
                {
                    if (GetDataGridRow(element) is DataGridRow { } row)
                    {
                        if (row.DataContext is RoomStatusReactive { } data)
                        {
                            SelectedItem = data;

                            foreach (UIElement d in ((ContextMenu)sender).Items.OfType<UIElement>())
                            {
                                d.Visibility = Visibility.Visible;
                            }
                        }
                    }
                    else
                    {
                        ((ContextMenu)sender).IsOpen = false;
                        SelectedItem = new RoomStatusReactive();

                        foreach (UIElement d in ((ContextMenu)sender).Items.OfType<UIElement>())
                        {
                            d.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static DataGridRow? GetDataGridRow(FrameworkElement? element)
            {
                while (element != null && element is not DataGridRow)
                {
                    element = VisualTreeHelper.GetParent(element) as FrameworkElement;
                }
                return element as DataGridRow;
            }
        }
    }

    private bool FilterRoomStatus(object item)
    {
        if (item is not RoomStatusReactive room)
        {
            return false;
        }

        return SelectedPlatformFilter == AllPlatformFilter
            || string.Equals(room.PlatformName, SelectedPlatformFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        foreach (RoomStatusReactive roomStatusReactive in RoomStatuses)
        {
            roomStatusReactive.RefreshStatus();
        }
        OnPropertyChanged(nameof(PlatformSummaryText));
        OnPropertyChanged(nameof(PreviewPlaybackToolTip));
        OnPropertyChanged(nameof(PreviewMuteToolTip));
        RefreshNetworkCapacityLocalization();
    }

    public void Dispose()
    {
        AbortAutoShutdownCountdown();
        AutoShutdownDispatcherTimer.Stop();
        DispatcherTimer.Stop();
        lock (previewTransitionSync)
        {
            previewTransitionCancellation?.Cancel();
            previewTransitionCancellation?.Dispose();
            previewTransitionCancellation = null;
        }

        Locale.CultureChanged -= OnCultureChanged;
        livePreviewPlayer.PlaybackFailed -= OnLivePreviewPlaybackFailed;
        livePreviewPlayer.PlaybackEnded -= OnLivePreviewPlaybackEnded;
        livePreviewPlayer.Dispose();
    }
}

internal abstract record RoomHistoryEntry;

internal sealed record RoomSelectionHistoryEntry(HashSet<string> Before, HashSet<string> After) : RoomHistoryEntry;

internal sealed record RoomListHistoryState(Room[] Rooms, HashSet<string> SelectedRoomUrls, string SelectedRoomUrl);

internal sealed record RoomListHistoryEntry(RoomListHistoryState Before, RoomListHistoryState After) : RoomHistoryEntry;

internal sealed class NetworkThroughputConnection(
    NetworkThroughputEndpoint endpoint,
    HttpClientHandler handler,
    HttpClient client,
    HttpResponseMessage response,
    Stream stream) : IAsyncDisposable
{
    public NetworkThroughputEndpoint Endpoint { get; } = endpoint;

    public byte[] Buffer { get; } = new byte[128 * 1024];

    public Stream Stream { get; } = stream;

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        response.Dispose();
        client.Dispose();
        handler.Dispose();
    }
}

internal sealed record NetworkThroughputEndpoint(
    string Name,
    NetworkThroughputRegion Region,
    string Url,
    bool UseRange);

internal sealed record NetworkCapacityMeasurement(
    double CapacityMbps,
    NetworkRegionMeasurement? Domestic,
    NetworkRegionMeasurement? Overseas,
    int SuccessfulSamples,
    int AttemptedSamples,
    NetworkMeasurementConfidence Confidence);

internal sealed record NetworkRegionMeasurement(
    double Mbps,
    int SuccessfulSamples,
    int AttemptedSamples,
    int SuccessfulEndpoints,
    int AttemptedEndpoints);

internal sealed record NetworkEndpointMeasurement(
    double Mbps,
    int SuccessfulSamples,
    int AttemptedSamples);

internal sealed record NetworkCapacityPresentation(
    NetworkCapacityMeasurement Measurement,
    double PerRoomMbps,
    int Capacity,
    int RoomCount);

internal enum NetworkCapacityState
{
    Idle,
    Testing,
    NoStream,
    Result,
    Failed,
}

internal enum NetworkThroughputRegion
{
    Domestic,
    Overseas,
}

internal enum NetworkMeasurementConfidence
{
    Low,
    Medium,
    High,
}
