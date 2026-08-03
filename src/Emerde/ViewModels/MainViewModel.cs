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
using VLCState = LibVLCSharp.Shared.VLCState;

namespace Emerde.ViewModels;

[ObservableObject]
public partial class MainViewModel : ReactiveObject, IDisposable
{
    internal const string AllPlatformFilter = "";
    internal const int RoomHistoryLimit = 200;
    private const long ManualRefreshCooldownMilliseconds = 5000;
    internal const long PreviewRefreshCooldownMilliseconds = 2000;
    private const long PreviewQualityRefreshCooldownMilliseconds = 30000;
    private const int NetworkThroughputRoundCount = 3;
    private const int NetworkThroughputConnectionsPerEndpoint = 2;
    private const int NetworkThroughputMeasuredEndpointCount = 2;
    private const double NetworkThroughputSingleOverseasProbeMbps = 20d;
    private const double DefaultNetworkCapacityPerRoomMbps = 6.345d;
    private const double NetworkCapacitySafetyRatio = 0.85d;
    private const long NetworkThroughputWarmupBytesPerConnection = 256L * 1024L;
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
    private readonly object previewRefreshCooldownLock = new();
    private readonly Dictionary<string, long> previewQualityRefreshTimestamps = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> stalePreviewStreamRooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<RoomHistoryEntry> roomHistoryUndoStack = [];
    private readonly Stack<RoomHistoryEntry> roomHistoryRedoStack = [];
    private CancellationTokenSource? previewTransitionCancellation;
    private PreviewFirstFrameLogContext? pendingPreviewFirstFrameLog;
    private CancellationTokenSource? networkCapacityTestCancellation;
    private long previewTransitionRequestSequence;
    private long lastManualRefreshTimestamp;
    private readonly Dictionary<string, long> previewRefreshTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private PreviewRefreshSuppression? previewRefreshSuppression;
    private CancellationTokenSource? previewRefreshSuppressionCancellation;
    private RoomStatusReactive? lastSelectedRoom;
    private readonly AutoShutdownSchedule autoShutdownSchedule = new();
    private AutoShutdownContentDialog? autoShutdownDialog;
    private bool forceShutdownAfterTranscode;
    private bool isPreviewPausedByPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomePageSelected))]
    [NotifyPropertyChangedFor(nameof(IsVideoListPageSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPageSelected))]
    [NotifyPropertyChangedFor(nameof(IsExtensionsPageSelected))]
    [NotifyPropertyChangedFor(nameof(IsAboutPageSelected))]
    private int selectedMainPageIndex;

    public bool IsHomePageSelected => SelectedMainPageIndex == 0;

    public bool IsVideoListPageSelected => SelectedMainPageIndex == 1;

    public bool IsSettingsPageSelected => SelectedMainPageIndex == 2;

    public bool IsExtensionsPageSelected => SelectedMainPageIndex == 3;

    public bool IsAboutPageSelected => SelectedMainPageIndex == 4;

    partial void OnSelectedMainPageIndexChanged(int value)
    {
        if (value < 0)
        {
            SelectedMainPageIndex = 0;
            return;
        }

        if (value != 2)
        {
            ReloadConfigurationStatus();
        }

        if (IsPreviewing)
        {
            AppSessionLogger.Event("info", "preview", "preview_page_changed", "application page changed while preview was active", new
            {
                selectedPageIndex = value,
                IsPreviewPaused,
                IsPreviewTransitioning,
                room = CreatePreviewRoomLogContext(PreviewingRoom),
            });
        }

        UpdatePreviewPageVisibility();
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
    [NotifyPropertyChangedFor(nameof(PreviewVolumeToolTip))]
    private int previewVolume;

    private int previewVolumeBeforeMute = 10;

    private bool isSynchronizingPreviewVolume;

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

    public LivePreviewFrameSource LivePreviewFrameSource => livePreviewPlayer.FrameSource;

    internal event EventHandler<PreviewControlFeedbackEventArgs>? PreviewControlFeedbackRequested;

    public bool IsPreviewIdle => !IsPreviewing;

    public bool IsPreviewPlaying => IsPreviewing && !IsPreviewPaused;

    public bool CanPreviewSelectedRoom => SelectedItem?.CanPreview ?? false;

    public string PreviewPlaybackToolTip => $"{(IsPreviewPlaying ? "PreviewPause".Tr() : "ButtonOfPlay".Tr())} (Space)";

    public string PreviewMuteToolTip => $"{(IsPreviewMuted ? "PreviewUnmute".Tr() : "PreviewMute".Tr())} (M)";

    public string PreviewVolumeToolTip => $"音量 {PreviewVolume}% (-/=)";

    public string LivePreviewStatusText => LivePreviewStatus switch
    {
        LivePreviewStatus.Idle => "LivePreviewIdle".Tr(),
        LivePreviewStatus.Ready => "LivePreviewReady".Tr(),
        LivePreviewStatus.Playing => "LivePreviewPlaying".Tr(),
        LivePreviewStatus.Unavailable => "LivePreviewUnavailable".Tr(),
        LivePreviewStatus.Error => "LivePreviewError".Tr(),
        _ => "LivePreviewIdle".Tr(),
    };

    private static Room[] NormalizeStoredRooms(Room[]? rooms)
    {
        List<Room> normalizedRooms = [];
        HashSet<string> seenUrls = new(StringComparer.OrdinalIgnoreCase);
        bool changed = rooms == null;

        foreach (Room? room in rooms ?? [])
        {
            if (room == null)
            {
                changed = true;
                continue;
            }

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
            IsOptimizeAudio = room.IsOptimizeAudio,
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
        previewVolumeBeforeMute = Math.Clamp(Configurations.PreviewVolume.Get(), 1, 100);
        isPreviewMuted = Configurations.IsPreviewMuted.Get();
        previewVolume = isPreviewMuted ? 0 : previewVolumeBeforeMute;
        livePreviewPlayer.SetVolume(previewVolume);
        livePreviewPlayer.SetMuted(isPreviewMuted);
        livePreviewPlayer.PlaybackFailed += OnLivePreviewPlaybackFailed;
        livePreviewPlayer.PlaybackEnded += OnLivePreviewPlaybackEnded;
        livePreviewPlayer.FrameSourceChanged += OnLivePreviewFrameSourceChanged;
        livePreviewPlayer.FirstFramePresented += OnLivePreviewFirstFramePresented;
        DispatcherTimer = new(TimeSpan.FromSeconds(3), ReloadRoomStatus);
        AutoShutdownDispatcherTimer = new(TimeSpan.FromSeconds(1), UpdateOneSecondState);
        Room[] configuredRooms = NormalizeStoredRooms(Configurations.Rooms.Get());
        AvatarCache.Prune(configuredRooms.Select(room => room.RoomUrl));

        RoomStatuses.Reset(configuredRooms.Select(CreateRoomStatusReactive));
        RoomStatusesView = CollectionViewSource.GetDefaultView(RoomStatuses);
        RoomStatusesView.Filter = FilterRoomStatus;
        ConfigureRoomStatusesViewLiveShaping();
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
        WeakReferenceMessenger.Default.Register<RoomRecordingStateChangedMessage>(this, (_, message) =>
        {
            ApplicationDispatcher.BeginInvoke(() => ReloadRoomStatus(message.RoomUrl));
        });
        WeakReferenceMessenger.Default.Register<RuntimeConfigurationChangedMessage>(this, (_, message) =>
        {
            ApplicationDispatcher.BeginInvoke(async () =>
            {
                ReloadConfigurationStatus();
                if (message.RecheckRooms)
                {
                    await GlobalMonitor.ApplyRuntimeConfigurationAsync();
                    ReloadRoomStatus();
                }
            });
        });

        ChildProcessTracerPeriodicTimer.Default.WhiteList = [];
        ChildProcessTracerPeriodicTimer.Default.Start();
        if (ShouldRunMonitorLoop())
        {
            GlobalMonitor.Start();
        }
        DispatcherTimer.Start();
        AutoShutdownDispatcherTimer.Start();
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
                GlobalMonitor.SyncRecordStatus(roomStatus);
                StreamStatus previousStreamStatus = roomStatusReactive.StreamStatus;
                bool previousCanPreview = roomStatusReactive.CanPreview;

                CopyRoomStatus(roomStatusReactive, roomStatus);

                refreshPlatformSummary |= (previousStreamStatus == StreamStatus.Streaming)
                    != (roomStatusReactive.StreamStatus == StreamStatus.Streaming);
                refreshSelectedPreview |= ReferenceEquals(roomStatusReactive, SelectedItem) && previousCanPreview != roomStatusReactive.CanPreview;
            }
        }

        if (refreshPlatformSummary)
        {
            OnPropertyChanged(nameof(PlatformSummaryText));
        }

        IsRecording = RoomStatuses.Any(roomStatusReactive => roomStatusReactive.IsRecording);

        if (refreshSelectedPreview)
        {
            OnPropertyChanged(nameof(CanPreviewSelectedRoom));
        }

        ClosePreviewIfCurrentRoomUnavailable();
    }

    private void ReloadRoomStatus(string roomUrl)
    {
        if (!GlobalMonitor.RoomStatus.TryGetValue(roomUrl, out RoomStatus? roomStatus))
        {
            return;
        }

        RoomStatusReactive? roomStatusReactive = RoomStatuses.FirstOrDefault(room => room.RoomUrl == roomStatus.RoomUrl);
        if (roomStatusReactive == null)
        {
            return;
        }

        StreamStatus previousStreamStatus = roomStatusReactive.StreamStatus;
        bool previousCanPreview = roomStatusReactive.CanPreview;
        GlobalMonitor.SyncRecordStatus(roomStatus);
        CopyRoomStatus(roomStatusReactive, roomStatus);

        if ((previousStreamStatus == StreamStatus.Streaming) != (roomStatusReactive.StreamStatus == StreamStatus.Streaming))
        {
            OnPropertyChanged(nameof(PlatformSummaryText));
        }

        IsRecording = RoomStatuses.Any(room => room.IsRecording);
        if (ReferenceEquals(roomStatusReactive, SelectedItem) && previousCanPreview != roomStatusReactive.CanPreview)
        {
            OnPropertyChanged(nameof(CanPreviewSelectedRoom));
        }

        ClosePreviewIfCurrentRoomUnavailable();
    }

    internal static void CopyRoomStatus(RoomStatusReactive target, RoomStatus source)
    {
        target.NickName = source.NickName;
        target.AvatarThumbUrl = source.AvatarThumbUrl;
        target.AvatarLocalPath = source.AvatarLocalPath;
        target.PlatformName = source.PlatformName;
        target.Uid = source.Uid;
        target.LiveTitle = source.LiveTitle;
        target.Quality = source.Quality;
        target.Resolution = source.Resolution;
        target.Bitrate = source.Bitrate;
        target.Headers = source.Headers;
        target.StreamStatus = source.StreamStatus;
        target.IsStreamCheckFailed = source.IsStreamCheckFailed;
        target.RecordStatus = source.RecordStatus;
        target.FlvUrl = source.FlvUrl;
        target.HlsUrl = source.HlsUrl;
        target.RecordUrl = source.RecordUrl;
        target.StartTime = source.Recorder.StartTime;
        target.EndTime = source.Recorder.EndTime;
        target.IsRecordingConfirmed = source.Recorder.HasMediaProgress;
        target.MediaWorkerProcessId = source.Recorder.MediaWorkerProcessId;
        target.MediaWorkerProcessName = source.Recorder.MediaWorkerProcessName;
        target.MediaWorkerWriteBytesPerSecond = source.Recorder.MediaWorkerWriteBytesPerSecond;
        target.MediaWorkerReadBytesPerSecond = source.Recorder.MediaWorkerReadBytesPerSecond;
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
        AppSessionLogger.Event("info", "preview", "preview_room_action_requested", "preview room action was requested", new
        {
            targetRoom = CreatePreviewRoomLogContext(targetRoom),
            currentRoom = CreatePreviewRoomLogContext(PreviewingRoom),
            IsPreviewing,
            IsPreviewTransitioning,
            canPreview = targetRoom?.CanPreview ?? false,
        });
        if (targetRoom == null || !targetRoom.CanPreview)
        {
            LogPreviewActionIgnored("open_or_switch", targetRoom, targetRoom == null ? "no_target_room" : "stream_unavailable");
            LivePreviewStatus = LivePreviewStatus.Unavailable;
            Toast.Warning("LivePreviewUnavailable".Tr());
            return;
        }

        if (IsPreviewing && IsSameRoom(PreviewingRoom, targetRoom))
        {
            await RequestPreviewTransitionAsync(null, PreviewTransitionReason.SameRoomToggleClose);
            return;
        }

        if (!ReferenceEquals(SelectedItem, targetRoom))
        {
            SelectedItem = targetRoom;
        }

        await RequestPreviewTransitionAsync(targetRoom, IsPreviewing ? PreviewTransitionReason.SwitchRoom : PreviewTransitionReason.Open);
    }

    private async Task RequestPreviewTransitionAsync(RoomStatusReactive? targetRoom, PreviewTransitionReason reason)
    {
        if (targetRoom != null)
        {
            CancelNetworkCapacityTest();
        }

        RoomStatusReactive? previousRoom = PreviewingRoom;
        bool replaceCurrentPlayback = targetRoom != null && IsPreviewing && PreviewingRoom != null;
        long requestId = Interlocked.Increment(ref previewTransitionRequestSequence);
        long requestStartedAt = Stopwatch.GetTimestamp();
        PreviewFirstFrameLogContext? firstFrameLog = targetRoom == null
            ? null
            : new PreviewFirstFrameLogContext(
                requestId,
                reason.ToString(),
                requestStartedAt,
                reason == PreviewTransitionReason.SwitchRoom ? null : livePreviewPlayer.FrameSource,
                reason == PreviewTransitionReason.SwitchRoom ? 0 : livePreviewPlayer.FrameSource.PresentedGeneration,
                CreatePreviewRoomLogContext(targetRoom)!);
        CancellationTokenSource cancellation = BeginPreviewTransition(firstFrameLog, out bool supersededPreviousRequest);
        AppSessionLogger.Event("info", "preview", "preview_transition_requested", "preview transition was requested", new
        {
            requestId,
            reason = reason.ToString(),
            replaceCurrentPlayback,
            supersededPreviousRequest,
            previousRoom = CreatePreviewRoomLogContext(previousRoom),
            targetRoom = CreatePreviewRoomLogContext(targetRoom),
            playerState = GetPreviewPlayerState(),
            IsPreviewPaused,
        });
        ApplyPreviewRequestState(targetRoom);
        bool enteredGate = false;
        bool completedCurrentTransition = false;
        bool streamRefreshRequired = false;
        bool resolutionWasMissing = false;
        bool playbackAttempted = false;
        long? initialGateWaitMilliseconds = null;
        long? streamRefreshMilliseconds = null;
        long? playbackGateWaitMilliseconds = null;
        long? playerStopMilliseconds = null;
        long? playerAcceptMilliseconds = null;
        long? resolutionMilliseconds = null;
        bool playerSessionReused = false;
        string outcome = "cancelled";
        string? failureType = null;

        try
        {
            long stageStartedAt = Stopwatch.GetTimestamp();
            await previewTransitionGate.WaitAsync(cancellation.Token);
            initialGateWaitMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
            enteredGate = true;
            cancellation.Token.ThrowIfCancellationRequested();

            if (targetRoom == null)
            {
                stageStartedAt = Stopwatch.GetTimestamp();
                await livePreviewPlayer.StopAsync();
                playerStopMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
                outcome = "closed";
                return;
            }

            previewTransitionGate.Release();
            enteredGate = false;
            bool stalePreviewStream;
            lock (previewTransitionSync)
            {
                stalePreviewStream = stalePreviewStreamRooms.Contains(targetRoom.RoomUrl);
            }
            streamRefreshRequired = ShouldRefreshPreviewStreamBeforePlayback(targetRoom, stalePreviewStream);
            if (streamRefreshRequired)
            {
                stageStartedAt = Stopwatch.GetTimestamp();
                bool refreshed = await RefreshPreviewStreamQualityAsync(targetRoom, cancellation.Token, stalePreviewStream);
                streamRefreshMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
                if (stalePreviewStream && !refreshed)
                {
                    stageStartedAt = Stopwatch.GetTimestamp();
                    await previewTransitionGate.WaitAsync(cancellation.Token);
                    playbackGateWaitMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
                    enteredGate = true;
                    cancellation.Token.ThrowIfCancellationRequested();
                    stageStartedAt = Stopwatch.GetTimestamp();
                    await livePreviewPlayer.StopAsync();
                    playerStopMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
                    ApplyPreviewClosedState();
                    LivePreviewStatus = LivePreviewStatus.Unavailable;
                    outcome = "stream_refresh_failed";
                    Toast.Warning("LivePreviewUnavailable".Tr());
                    return;
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();

            stageStartedAt = Stopwatch.GetTimestamp();
            await previewTransitionGate.WaitAsync(cancellation.Token);
            playbackGateWaitMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
            enteredGate = true;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!targetRoom.CanPreview)
            {
                stageStartedAt = Stopwatch.GetTimestamp();
                await livePreviewPlayer.StopAsync();
                playerStopMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
                ApplyPreviewClosedState();
                LivePreviewStatus = LivePreviewStatus.Unavailable;
                outcome = "stream_unavailable";
                Toast.Warning("LivePreviewUnavailable".Tr());
                return;
            }

            string proxyUrl = Configurations.IsUseProxy.Get() ? Configurations.ProxyUrl.Get() : string.Empty;
            string previewUrl = GetPreviewPlaybackUrl(targetRoom);
            if (string.IsNullOrWhiteSpace(previewUrl))
            {
                stageStartedAt = Stopwatch.GetTimestamp();
                await livePreviewPlayer.StopAsync();
                playerStopMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
                ApplyPreviewClosedState();
                LivePreviewStatus = LivePreviewStatus.Unavailable;
                outcome = "missing_playback_url";
                Toast.Warning("LivePreviewUnavailable".Tr());
                return;
            }

            livePreviewPlayer.SetVolume(PreviewVolume);
            livePreviewPlayer.SetMuted(IsPreviewMuted);
            stageStartedAt = Stopwatch.GetTimestamp();
            string previewSessionKey = CreatePreviewSessionKey(targetRoom, previewUrl, proxyUrl);
            playbackAttempted = true;
            playerSessionReused = await livePreviewPlayer.PlayAsync(
                previewSessionKey,
                previewUrl,
                Configurations.UserAgent.Get(),
                proxyUrl,
                targetRoom.Headers,
                cancellation.Token,
                restartCurrentPlayback: reason is PreviewTransitionReason.ManualRefresh or PreviewTransitionReason.UserResume,
                allowStandbyReuse: true);
            playerAcceptMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
            cancellation.Token.ThrowIfCancellationRequested();
            lock (previewTransitionSync)
            {
                _ = stalePreviewStreamRooms.Remove(targetRoom.RoomUrl);
            }
            BindPendingPreviewFirstFrame(requestId, livePreviewPlayer.FrameSource);
            if (IsCurrentPreviewTransition(cancellation))
            {
                LivePreviewStatus = LivePreviewStatus.Playing;
            }
            previewTransitionGate.Release();
            enteredGate = false;
            resolutionWasMissing = string.IsNullOrWhiteSpace(targetRoom.Resolution);
            stageStartedAt = Stopwatch.GetTimestamp();
            await ResolvePreviewResolutionAsync(targetRoom, cancellation.Token);
            resolutionMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
            outcome = "playing";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = "superseded";
        }
        catch (Exception e)
        {
            if (playbackAttempted && targetRoom is { RoomUrl.Length: > 0 })
            {
                lock (previewTransitionSync)
                {
                    stalePreviewStreamRooms.Add(targetRoom.RoomUrl);
                }
            }
            Debug.WriteLine(e);
            outcome = "error";
            failureType = e.GetType().FullName;
            AppSessionLogger.Event("error", "preview", "preview_transition_failed", e.Message, new
            {
                requestId,
                reason = reason.ToString(),
                elapsedMilliseconds = GetPreviewElapsedMilliseconds(requestStartedAt),
                failureType,
                targetRoom = CreatePreviewRoomLogContext(targetRoom),
            });
            if (IsCurrentPreviewTransition(cancellation))
            {
                long stageStartedAt = Stopwatch.GetTimestamp();
                await livePreviewPlayer.StopAsync();
                playerStopMilliseconds = GetPreviewElapsedMilliseconds(stageStartedAt);
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

            bool isCurrentTransition = IsCurrentPreviewTransition(cancellation);
            if (isCurrentTransition)
            {
                IsPreviewTransitioning = false;
                completedCurrentTransition = true;
            }

            if (outcome != "playing")
            {
                ClearPendingPreviewFirstFrameLog(requestId);
            }
            CompletePreviewTransition(cancellation);
            AppSessionLogger.Event("info", "preview", "preview_transition_summary", "preview transition timing summary", new
            {
                requestId,
                reason = reason.ToString(),
                outcome,
                totalMilliseconds = GetPreviewElapsedMilliseconds(requestStartedAt),
                initialGateWaitMilliseconds,
                streamRefreshRequired,
                streamRefreshMilliseconds,
                playbackGateWaitMilliseconds,
                playerStopMilliseconds,
                playerAcceptMilliseconds,
                playerSessionReused,
                standbySessionCount = livePreviewPlayer.StandbySessionCount,
                resolutionWasMissing,
                resolutionMilliseconds,
                failureType,
                isCurrentTransition,
                playerState = GetPreviewPlayerState(),
                targetRoom = CreatePreviewRoomLogContext(targetRoom),
            });
            if (completedCurrentTransition)
            {
                UpdatePreviewPageVisibility();
            }
        }
    }

    private CancellationTokenSource BeginPreviewTransition(PreviewFirstFrameLogContext? firstFrameLog, out bool supersededPreviousRequest)
    {
        CancellationTokenSource current = new();
        CancellationTokenSource? previous;
        lock (previewTransitionSync)
        {
            previous = previewTransitionCancellation;
            previewTransitionCancellation = current;
            pendingPreviewFirstFrameLog = firstFrameLog;
        }

        supersededPreviousRequest = previous != null;
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
        isPreviewPausedByPage = false;
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
        isPreviewPausedByPage = false;
        IsPreviewDetached = false;
        PreviewingRoom = null;
        IsPreviewing = false;
        IsPreviewPaused = false;
        LivePreviewStatus = CanPreviewSelectedRoom ? LivePreviewStatus.Ready : LivePreviewStatus.Idle;
    }

    private void UpdatePreviewPageVisibility()
    {
        if (ShouldPausePreviewForPage(IsHomePageSelected, IsPreviewing, IsPreviewTransitioning, IsPreviewPaused, isPreviewPausedByPage))
        {
            PausePreviewForHiddenPage();
            return;
        }

        if (ShouldRefreshPreviewForHomePage(IsHomePageSelected, IsPreviewing, IsPreviewTransitioning, isPreviewPausedByPage))
        {
            QueuePreviewRefreshForVisibleHomePage();
        }
    }

    private void QueuePreviewRefreshForVisibleHomePage()
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _ = RefreshPreviewForVisibleHomePageAsync();
            return;
        }

        _ = dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() => _ = RefreshPreviewForVisibleHomePageAsync()));
    }

    private void PausePreviewForHiddenPage()
    {
        isPreviewPausedByPage = true;
        IsPreviewPaused = true;
        livePreviewPlayer.DiscardStandbySessions();
        livePreviewPlayer.SetPaused(true);
        LivePreviewStatus = LivePreviewStatus.Ready;
        AppSessionLogger.Event("info", "preview", "preview_paused_for_hidden_page", "preview playback was paused because the home page was hidden", new
        {
            selectedPageIndex = SelectedMainPageIndex,
            playerState = GetPreviewPlayerState(),
            room = CreatePreviewRoomLogContext(PreviewingRoom),
        });
    }

    private async Task RefreshPreviewForVisibleHomePageAsync()
    {
        if (!ShouldRefreshPreviewForHomePage(IsHomePageSelected, IsPreviewing, IsPreviewTransitioning, isPreviewPausedByPage))
        {
            return;
        }

        RoomStatusReactive? targetRoom = PreviewingRoom;
        if (targetRoom == null)
        {
            isPreviewPausedByPage = false;
            ApplyPreviewClosedState();
            return;
        }

        if (!targetRoom.CanPreview)
        {
            isPreviewPausedByPage = false;
            ClosePreviewIfCurrentRoomUnavailable();
            return;
        }

        await RequestPreviewTransitionAsync(targetRoom, PreviewTransitionReason.PageResume);
    }

    internal static bool ShouldPausePreviewForPage(
        bool isHomePageSelected,
        bool isPreviewing,
        bool isPreviewTransitioning,
        bool isPreviewPaused,
        bool isPreviewPausedByPage)
    {
        return !isHomePageSelected
            && isPreviewing
            && !isPreviewTransitioning
            && !isPreviewPaused
            && !isPreviewPausedByPage;
    }

    internal static bool ShouldRefreshPreviewForHomePage(
        bool isHomePageSelected,
        bool isPreviewing,
        bool isPreviewTransitioning,
        bool isPreviewPausedByPage)
    {
        return isHomePageSelected
            && isPreviewing
            && !isPreviewTransitioning
            && isPreviewPausedByPage;
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
        _ = RequestPreviewTransitionAsync(null, PreviewTransitionReason.RoomUnavailable);
    }

    private void OnLivePreviewPlaybackFailed(object? sender, LivePreviewPlaybackTerminatedEventArgs e)
    {
        HandleLivePreviewPlaybackTerminated(e.SessionKey, LivePreviewStatus.Error, "LivePreviewError", PreviewTransitionReason.PlaybackFailed);
    }

    private void OnLivePreviewPlaybackEnded(object? sender, LivePreviewPlaybackTerminatedEventArgs e)
    {
        HandleLivePreviewPlaybackTerminated(e.SessionKey, LivePreviewStatus.Unavailable, "LivePreviewUnavailable", PreviewTransitionReason.PlaybackEnded);
    }

    private void OnLivePreviewFirstFramePresented(object? sender, EventArgs e)
    {
        if (sender is LivePreviewFrameSource frameSource)
        {
            TryCompletePendingPreviewFirstFrame(frameSource);
        }
    }

    private void OnLivePreviewFrameSourceChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(LivePreviewMediaPlayer));
        OnPropertyChanged(nameof(LivePreviewFrameSource));
    }

    private void BindPendingPreviewFirstFrame(long requestId, LivePreviewFrameSource frameSource)
    {
        bool shouldComplete;
        lock (previewTransitionSync)
        {
            PreviewFirstFrameLogContext? context = pendingPreviewFirstFrameLog;
            if (context == null || context.RequestId != requestId)
            {
                return;
            }

            if (!ReferenceEquals(context.FrameSource, frameSource))
            {
                context.FrameSource = frameSource;
                context.BaselinePresentedGeneration = 0;
            }

            shouldComplete = frameSource.HasPresentedFrame
                && frameSource.PresentedGeneration > context.BaselinePresentedGeneration;
        }

        if (shouldComplete)
        {
            TryCompletePendingPreviewFirstFrame(frameSource);
        }
    }

    private void TryCompletePendingPreviewFirstFrame(LivePreviewFrameSource frameSource)
    {
        PreviewFirstFrameLogContext? context;
        int generation = frameSource.PresentedGeneration;
        lock (previewTransitionSync)
        {
            context = pendingPreviewFirstFrameLog;
            if (context == null
                || !ReferenceEquals(context.FrameSource, frameSource)
                || generation <= context.BaselinePresentedGeneration)
            {
                return;
            }

            pendingPreviewFirstFrameLog = null;
        }

        AppSessionLogger.Event("info", "preview", "preview_first_frame_presented", "preview first frame was presented", new
        {
            context.RequestId,
            reason = context.Reason,
            elapsedMilliseconds = GetPreviewElapsedMilliseconds(context.StartedAt),
            generation,
            room = context.Room,
        });
    }

    private static string CreatePreviewSessionKey(RoomStatusReactive room, string previewUrl, string proxyUrl)
    {
        return string.Join('\u001f',
            room.RoomUrl,
            previewUrl,
            room.Headers,
            Configurations.UserAgent.Get(),
            ProxyAddress.Normalize(proxyUrl));
    }

    private void ClearPendingPreviewFirstFrameLog(long requestId)
    {
        lock (previewTransitionSync)
        {
            if (pendingPreviewFirstFrameLog?.RequestId == requestId)
            {
                pendingPreviewFirstFrameLog = null;
            }
        }
    }

    private static long GetPreviewElapsedMilliseconds(long startedAt)
    {
        return (long)Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private string GetPreviewPlayerState()
    {
        try
        {
            return livePreviewPlayer.MediaPlayer.State.ToString();
        }
        catch (ObjectDisposedException)
        {
            return "Disposed";
        }
    }

    private static PreviewRoomLogContext? CreatePreviewRoomLogContext(RoomStatusReactive? room)
    {
        return room == null
            ? null
            : new PreviewRoomLogContext(room.RoomUrl, room.NickName, room.PlatformName);
    }

    private void LogPreviewActionIgnored(string action, RoomStatusReactive? room, string reason)
    {
        AppSessionLogger.Event("info", "preview", "preview_action_ignored", "preview action was ignored", new
        {
            action,
            reason,
            IsPreviewing,
            IsPreviewTransitioning,
            IsPreviewPaused,
            playerState = GetPreviewPlayerState(),
            room = CreatePreviewRoomLogContext(room),
        });
    }

    private void HandleLivePreviewPlaybackTerminated(string sessionKey, LivePreviewStatus status, string messageKey, PreviewTransitionReason reason)
    {
        string terminatedRoomUrl = GetPreviewSessionRoomUrl(sessionKey);
        if (!string.IsNullOrWhiteSpace(terminatedRoomUrl))
        {
            lock (previewTransitionSync)
            {
                stalePreviewStreamRooms.Add(terminatedRoomUrl);
            }
        }
        AppSessionLogger.Event("info", "preview", "preview_playback_terminated", "preview playback terminated", new
        {
            reason = reason.ToString(),
            status = status.ToString(),
            terminatedRoomUrl,
            playerState = GetPreviewPlayerState(),
            room = CreatePreviewRoomLogContext(PreviewingRoom),
        });
        ApplicationDispatcher.BeginInvoke(async () =>
        {
            if (!ShouldHandlePreviewTermination(terminatedRoomUrl, PreviewingRoom?.RoomUrl, IsPreviewing))
            {
                LogPreviewActionIgnored("playback_terminated", PreviewingRoom, "preview_session_changed_or_closed");
                return;
            }

            await RequestPreviewTransitionAsync(null, reason);
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

    internal static string GetPreviewSessionRoomUrl(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            return string.Empty;
        }
        int separatorIndex = sessionKey.IndexOf('\u001f');
        return separatorIndex < 0 ? sessionKey : sessionKey[..separatorIndex];
    }

    internal static bool ShouldHandlePreviewTermination(string? terminatedRoomUrl, string? currentRoomUrl, bool isPreviewing)
    {
        return isPreviewing
            && !string.IsNullOrWhiteSpace(terminatedRoomUrl)
            && string.Equals(terminatedRoomUrl, currentRoomUrl, StringComparison.OrdinalIgnoreCase);
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

    private async Task<bool> RefreshPreviewStreamQualityAsync(RoomStatusReactive targetRoom, CancellationToken cancellationToken, bool force = false)
    {
        string roomUrl = targetRoom.RoomUrl;
        if (string.IsNullOrWhiteSpace(roomUrl))
        {
            return false;
        }

        string preferredQuality = PreviewStreamQualityPreference;
        if (!force && !ShouldRefreshPreviewStreamQuality(targetRoom, preferredQuality))
        {
            return false;
        }

        bool refreshed;
        try
        {
            refreshed = await GlobalMonitor.RunRoomUpdateAsync(roomUrl, async () =>
            {
                ISpiderResult? result = await GlobalMonitor.GetManualSpiderResultAsync(roomUrl, preferredQuality, cancellationToken);
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
            return false;
        }

        if (!refreshed)
        {
            return false;
        }

        OnPropertyChanged(nameof(CanPreviewSelectedRoom));
        OnPropertyChanged(nameof(PlatformSummaryText));
        OnPropertyChanged(nameof(PlatformFilterOptions));
        ClosePreviewIfCurrentRoomUnavailable();
        return true;
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
        return ShouldRefreshPreviewStreamBeforePlayback(room, false);
    }

    internal static bool ShouldRefreshPreviewStreamBeforePlayback(RoomStatusReactive room, bool streamInvalidated)
    {
        return streamInvalidated || string.IsNullOrWhiteSpace(GetPreviewPlaybackUrl(room));
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
        await RequestPreviewTransitionAsync(null, PreviewTransitionReason.ManualStop);
    }

    internal async Task<bool> PlayPreviewForExtensionAsync(string roomUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RoomStatusReactive? room = RoomStatuses.FirstOrDefault(item => string.Equals(item.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase));
        if (room == null)
        {
            return false;
        }
        if (IsPreviewing && IsSameRoom(PreviewingRoom, room))
        {
            return true;
        }
        await PreviewLiveRoomAsync(room);
        cancellationToken.ThrowIfCancellationRequested();
        return IsPreviewing && string.Equals(PreviewingRoom?.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase);
    }

    internal async Task StopPreviewForExtensionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopPreviewAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal async Task RefreshPreviewForExtensionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshPreviewAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal async Task SetPreviewPausedForExtensionAsync(bool paused, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsPreviewPaused != paused)
        {
            await TogglePreviewPauseAsync();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void SetPreviewMutedForExtension(bool muted)
    {
        if (IsPreviewMuted != muted)
        {
            TogglePreviewMute();
        }
    }

    internal void SetPreviewVolumeForExtension(int volume)
    {
        PreviewVolume = LivePreviewPlayer.NormalizeVolume(volume);
    }

    [RelayCommand]
    private async Task TogglePreviewPlaybackAsync()
    {
        if (IsPreviewing)
        {
            await RequestPreviewTransitionAsync(null, PreviewTransitionReason.ToggleClose);
            return;
        }

        RoomStatusReactive? targetRoom = SelectedItem;
        if (targetRoom != null && targetRoom.CanPreview)
        {
            await RequestPreviewTransitionAsync(targetRoom, PreviewTransitionReason.Open);
            return;
        }

        LogPreviewActionIgnored("toggle_playback", targetRoom, targetRoom == null ? "no_target_room" : "stream_unavailable");
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
            LogPreviewActionIgnored("toggle_pause", PreviewingRoom, "transition_in_progress");
            return;
        }

        if (!IsPreviewPaused)
        {
            IsPreviewPaused = true;
            livePreviewPlayer.SetPaused(true);
            LivePreviewStatus = LivePreviewStatus.Ready;
            AppSessionLogger.Event("info", "preview", "preview_paused", "preview playback was paused by the user", new
            {
                playerState = GetPreviewPlayerState(),
                room = CreatePreviewRoomLogContext(PreviewingRoom),
            });
            return;
        }

        await ReloadPreviewAsync(PreviewTransitionReason.UserResume);
    }

    [RelayCommand]
    private async Task RefreshPreviewAsync()
    {
        RoomStatusReactive? targetRoom = PreviewingRoom;
        if (targetRoom == null || !targetRoom.CanPreview || IsPreviewTransitioning)
        {
            string ignoredReason = targetRoom == null
                ? "no_preview_room"
                : !targetRoom.CanPreview
                    ? "stream_unavailable"
                    : "transition_in_progress";
            LogPreviewActionIgnored(PreviewTransitionReason.ManualRefresh.ToString(), targetRoom, ignoredReason);
            return;
        }

        if (!TryBeginPreviewRefresh(targetRoom, out long remainingMilliseconds, out bool shouldNotify))
        {
            if (shouldNotify)
            {
                Toast.Warning("PreviewRefreshTooFrequently".Tr(GetPreviewRefreshRemainingSeconds(remainingMilliseconds)));
            }
            return;
        }

        AppSessionLogger.Event("info", "preview", "preview_manual_refresh_requested", "manual preview refresh was requested", new
        {
            IsPreviewing,
            IsPreviewTransitioning,
            IsPreviewPaused,
            playerState = GetPreviewPlayerState(),
            room = CreatePreviewRoomLogContext(PreviewingRoom),
        });
        await ReloadPreviewAsync(PreviewTransitionReason.ManualRefresh);
    }

    private bool TryBeginPreviewRefresh(RoomStatusReactive room, out long remainingMilliseconds, out bool shouldNotify)
    {
        remainingMilliseconds = 0;
        shouldNotify = false;
        if (!ShouldApplyPreviewRefreshCooldown(livePreviewPlayer.MediaPlayer.State))
        {
            FlushPreviewRefreshSuppression();
            return true;
        }

        long now = Environment.TickCount64;
        lock (previewRefreshCooldownLock)
        {
            if (!TryRegisterPreviewRefresh(
                previewRefreshTimestamps,
                room.RoomUrl,
                now,
                PreviewRefreshCooldownMilliseconds,
                out remainingMilliseconds))
            {
                shouldNotify = RegisterPreviewRefreshSuppression(room, now);
                return false;
            }
        }

        FlushPreviewRefreshSuppression();
        return true;
    }

    internal static bool ShouldApplyPreviewRefreshCooldown(VLCState state)
    {
        return state is VLCState.Playing or VLCState.Opening or VLCState.Buffering or VLCState.Paused;
    }

    internal static int GetPreviewRefreshRemainingSeconds(long remainingMilliseconds)
    {
        return Math.Max(1, (int)Math.Ceiling(remainingMilliseconds / 1000d));
    }

    internal static bool TryRegisterPreviewRefresh(
        IDictionary<string, long> timestamps,
        string roomUrl,
        long now,
        long cooldownMilliseconds,
        out long remainingMilliseconds)
    {
        foreach (string expiredRoomUrl in timestamps
            .Where(entry => now - entry.Value >= cooldownMilliseconds)
            .Select(entry => entry.Key)
            .ToArray())
        {
            timestamps.Remove(expiredRoomUrl);
        }

        if (timestamps.TryGetValue(roomUrl, out long lastRefreshTimestamp))
        {
            remainingMilliseconds = cooldownMilliseconds - (now - lastRefreshTimestamp);
            return false;
        }

        timestamps[roomUrl] = now;
        remainingMilliseconds = 0;
        return true;
    }

    private bool RegisterPreviewRefreshSuppression(RoomStatusReactive room, long now)
    {
        if (previewRefreshSuppression == null)
        {
            previewRefreshSuppression = new PreviewRefreshSuppression(
                now,
                now,
                long.MaxValue,
                1,
                CreatePreviewRoomLogContext(room));
            previewRefreshSuppressionCancellation = new CancellationTokenSource();
            _ = FlushPreviewRefreshSuppressionAfterCooldownAsync(previewRefreshSuppressionCancellation.Token);
            return true;
        }

        long interval = now - previewRefreshSuppression.LastAttemptAt;
        previewRefreshSuppression.LastAttemptAt = now;
        previewRefreshSuppression.MinimumIntervalMilliseconds = Math.Min(
            previewRefreshSuppression.MinimumIntervalMilliseconds,
            interval);
        previewRefreshSuppression.AttemptCount++;
        return false;
    }

    private async Task FlushPreviewRefreshSuppressionAfterCooldownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(PreviewRefreshCooldownMilliseconds), cancellationToken);
            FlushPreviewRefreshSuppression();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void FlushPreviewRefreshSuppression()
    {
        PreviewRefreshSuppression? suppression;
        CancellationTokenSource? cancellation;
        lock (previewRefreshCooldownLock)
        {
            suppression = previewRefreshSuppression;
            cancellation = previewRefreshSuppressionCancellation;
            previewRefreshSuppression = null;
            previewRefreshSuppressionCancellation = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        if (suppression == null)
        {
            return;
        }

        AppSessionLogger.Event("info", "preview", "preview_refresh_throttled_summary", "repeated preview refresh attempts were throttled", new
        {
            suppression.AttemptCount,
            windowMilliseconds = suppression.LastAttemptAt - suppression.FirstAttemptAt,
            minimumIntervalMilliseconds = suppression.MinimumIntervalMilliseconds == long.MaxValue
                ? (long?)null
                : suppression.MinimumIntervalMilliseconds,
            cooldownMilliseconds = PreviewRefreshCooldownMilliseconds,
            suppression.Room,
        });
    }

    private async Task ReloadPreviewAsync(PreviewTransitionReason reason)
    {
        RoomStatusReactive? targetRoom = PreviewingRoom;
        if (targetRoom == null || !targetRoom.CanPreview || IsPreviewTransitioning)
        {
            string ignoredReason = targetRoom == null
                ? "no_preview_room"
                : !targetRoom.CanPreview
                    ? "stream_unavailable"
                    : "transition_in_progress";
            LogPreviewActionIgnored(reason.ToString(), targetRoom, ignoredReason);
            return;
        }

        await RequestPreviewTransitionAsync(targetRoom, reason);
    }

    [RelayCommand]
    private void TogglePreviewMute()
    {
        bool wasMuted = IsPreviewMuted;
        int previousVolume = PreviewVolume;
        if (IsPreviewMuted)
        {
            SetPreviewVolumeState(previewVolumeBeforeMute > 0 ? previewVolumeBeforeMute : 10, false);
            RequestPreviewControlFeedback(PreviewControlFeedbackKind.Volume, PreviewVolume);
            LogPreviewAudioAction("mute_toggle", wasMuted, previousVolume);
            return;
        }

        previewVolumeBeforeMute = PreviewVolume > 0 ? PreviewVolume : 10;
        SetPreviewVolumeState(0, true);
        RequestPreviewControlFeedback(PreviewControlFeedbackKind.Volume, 0);
        LogPreviewAudioAction("mute_toggle", wasMuted, previousVolume);
    }

    partial void OnPreviewVolumeChanged(int value)
    {
        int normalizedVolume = LivePreviewPlayer.NormalizeVolume(value);
        if (normalizedVolume != value)
        {
            PreviewVolume = normalizedVolume;
            return;
        }

        if (isSynchronizingPreviewVolume)
        {
            return;
        }

        livePreviewPlayer.SetVolume(normalizedVolume);
        if (normalizedVolume == 0)
        {
            previewVolumeBeforeMute = 10;
            IsPreviewMuted = true;
            livePreviewPlayer.SetMuted(true);
            SavePreviewAudioState();
            RequestPreviewControlFeedback(PreviewControlFeedbackKind.Volume, 0);
            LogPreviewAudioAction("volume_changed", null, null);
            return;
        }

        previewVolumeBeforeMute = normalizedVolume;
        if (IsPreviewMuted)
        {
            IsPreviewMuted = false;
            livePreviewPlayer.SetMuted(false);
        }

        SavePreviewAudioState();
        RequestPreviewControlFeedback(PreviewControlFeedbackKind.Volume, normalizedVolume);
        LogPreviewAudioAction("volume_changed", null, null);
    }

    internal void AdjustPreviewVolume(int delta)
    {
        AppSessionLogger.Event("info", "preview", "preview_volume_adjust_requested", "preview volume adjustment was requested", new
        {
            delta,
            currentVolume = PreviewVolume,
            IsPreviewMuted,
        });
        PreviewVolume = LivePreviewPlayer.NormalizeVolume(PreviewVolume + delta);
    }

    private void LogPreviewAudioAction(string action, bool? previousMuted, int? previousVolume)
    {
        object data = previousMuted.HasValue && previousVolume.HasValue
            ? new
            {
                action,
                previousMuted = previousMuted.Value,
                previousVolume = previousVolume.Value,
                currentMuted = IsPreviewMuted,
                currentVolume = PreviewVolume,
            }
            : new
            {
                action,
                currentMuted = IsPreviewMuted,
                currentVolume = PreviewVolume,
            };
        AppSessionLogger.Event("info", "preview", "preview_audio_changed", "preview audio state changed", data);
    }

    private void SetPreviewVolumeState(int volume, bool muted)
    {
        int normalizedVolume = muted ? 0 : LivePreviewPlayer.NormalizeVolume(volume);
        isSynchronizingPreviewVolume = true;
        try
        {
            PreviewVolume = normalizedVolume;
        }
        finally
        {
            isSynchronizingPreviewVolume = false;
        }

        IsPreviewMuted = muted;
        livePreviewPlayer.SetVolume(normalizedVolume);
        livePreviewPlayer.SetMuted(muted);
        SavePreviewAudioState();
    }

    private void RequestPreviewControlFeedback(PreviewControlFeedbackKind kind, int volume = 0)
    {
        PreviewControlFeedbackRequested?.Invoke(this, new PreviewControlFeedbackEventArgs(kind, volume));
    }

    private void SavePreviewAudioState()
    {
        Configurations.PreviewVolume.Set(previewVolumeBeforeMute);
        Configurations.IsPreviewMuted.Set(IsPreviewMuted);
        ConfigurationSaveScheduler.Request();
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

        await AddConfirmedRoomAsync(
            dialog.NickName,
            dialog.RoomUrl,
            dialog.SpiderResult,
            dialog.IsFollowGlobalSettings,
            dialog.SettingsEditor.IsToNotify,
            dialog.SettingsEditor.IsToMonitor,
            dialog.SettingsEditor.IsToRecord,
            dialog.RecordingOptions);
    }

    private static async Task<ContentDialogResult> ShowMainContentDialogAsync(ContentDialog dialog)
    {
        Window? owner = Application.Current?.MainWindow;
        return await WindowSizing.ShowContentDialogAsync(dialog, owner);
    }

    private async Task AddConfirmedRoomAsync(
        string nickName,
        string roomUrl,
        ISpiderResult? spiderResult,
        bool isFollowGlobalSettings,
        bool isToNotify,
        bool isToMonitor,
        bool isToRecord,
        RoomRecordingOptions recordingOptions)
    {
        RoomListHistoryState before = CaptureRoomListHistoryState();
        List<Room> rooms = [.. Configurations.Rooms.Get() ?? []];

        rooms.RemoveAll(room => string.Equals(room.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase));
        Room room = new()
        {
            NickName = nickName,
            RoomUrl = roomUrl,
            AvatarThumbUrl = spiderResult?.AvatarThumbUrl ?? string.Empty,
            PlatformName = Spider.GetPlatformName(roomUrl),
            IsToNotify = isToNotify,
            IsToMonitor = isToMonitor,
            IsToRecord = isToRecord,
            IsFollowGlobalSettings = isFollowGlobalSettings,
        };
        RoomRecordingSettings.Apply(room, recordingOptions);
        rooms.Add(room);
        Configurations.Rooms.Set([.. rooms]);
        ConfigurationSaveScheduler.Request();

        RoomStatusReactive roomStatusReactive = new()
        {
            NickName = nickName,
            RoomUrl = roomUrl,
            AvatarLocalPath = AvatarCache.GetCachedAvatarSource(roomUrl),
            PlatformName = Spider.GetPlatformName(roomUrl),
            IsToNotify = isToNotify,
            IsToMonitor = isToMonitor,
            IsToRecord = isToRecord,
            IsFollowGlobalSettings = isFollowGlobalSettings,
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

        if (spiderResult == null && roomStatusReactive.EffectiveIsToMonitor)
        {
            GlobalMonitor.Start();
            _ = GlobalMonitor.RunRoomAsync(roomUrl);
        }
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
    private void OpenExtensions()
    {
        SelectedMainPageIndex = 3;
    }

    [RelayCommand]
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
            Toast.Warning($"无法打开保存目录：{e.Message}");
        }
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
        SelectedMainPageIndex = 4;
    }

    [RelayCommand]
    private async Task CopySelectedRoomUrlAsync()
    {
        await CopyTextToClipboardAsync(SelectedItem?.RoomUrl);
    }

    [RelayCommand]
    private async Task CopySelectedPreviewUrlAsync()
    {
        AppSessionLogger.Event("info", "preview", "preview_stream_url_copy_requested", "preview stream URL copy was requested", new
        {
            hasPreviewUrl = !string.IsNullOrWhiteSpace(SelectedItem?.PreviewUrl),
            room = CreatePreviewRoomLogContext(SelectedItem),
        });
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

    private void ConfigureRoomStatusesViewLiveShaping()
    {
        if (RoomStatusesView is not ICollectionViewLiveShaping liveView)
        {
            return;
        }

        if (liveView.CanChangeLiveSorting)
        {
            liveView.LiveSortingProperties.Add(nameof(RoomStatusReactive.NickName));
            liveView.IsLiveSorting = true;
        }

        if (liveView.CanChangeLiveFiltering)
        {
            liveView.LiveFilteringProperties.Add(nameof(RoomStatusReactive.PlatformName));
            liveView.IsLiveFiltering = true;
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
                    ISpiderResult? result = await GlobalMonitor.GetManualSpiderResultAsync(room.RoomUrl, preferredQuality);
                    if (result == null)
                    {
                        GlobalMonitor.SetRoomStreamCheckFailed(room.RoomUrl, true);
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
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
                GlobalMonitor.SetRoomStreamCheckFailed(room.RoomUrl, true);
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
                ISpiderResult? result = await GlobalMonitor.GetManualSpiderResultAsync(roomUrl, preferredQuality);
                if (result == null)
                {
                    GlobalMonitor.SetRoomStreamCheckFailed(roomUrl, true);
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
            OnPropertyChanged(nameof(PlatformSummaryText));
            OnPropertyChanged(nameof(PlatformFilterOptions));
            OnPropertyChanged(nameof(CanPreviewSelectedRoom));
            ClosePreviewIfCurrentRoomUnavailable();
            Toast.Success("SuccOp".Tr());
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            GlobalMonitor.SetRoomStreamCheckFailed(selectedRoom.RoomUrl, true);
            Toast.Error("GetRoomInfoError".Tr());
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
        CancellationTokenSource testCancellation = new();
        networkCapacityTestCancellation = testCancellation;
        IsNetworkCapacityTesting = true;
        networkCapacityState = NetworkCapacityState.Testing;
        RefreshNetworkCapacityLocalization();
        AppSessionLogger.Write($"network capacity test started, samples={estimateRooms.Length}");

        try
        {
            NetworkCapacityMeasurement measurement = await MeasureBestNetworkThroughputMbpsAsync(testCancellation.Token);
            NetworkCapacityPresentation presentation = CreateNetworkCapacityPresentation(measurement, estimateRooms);

            networkCapacityState = NetworkCapacityState.Result;
            networkCapacityPresentation = presentation;
            RefreshNetworkCapacityLocalization();
            AppSessionLogger.Write($"network capacity test completed, domesticMbps={measurement.Domestic?.Mbps:0.##}, overseasMbps={measurement.Overseas?.Mbps:0.##}, samples={measurement.SuccessfulSamples}/{measurement.AttemptedSamples}, confidence={measurement.Confidence}, domesticPerRoomMbps={presentation.DomesticPerRoomMbps:0.##}, overseasPerRoomMbps={presentation.OverseasPerRoomMbps:0.##}, domesticCapacity={presentation.DomesticCapacity}, overseasCapacity={presentation.OverseasCapacity}");
            Toast.Success(NetworkCapacityToolTip);
        }
        catch (OperationCanceledException) when (testCancellation.IsCancellationRequested)
        {
            networkCapacityState = networkCapacityPresentation == null
                ? NetworkCapacityState.Idle
                : NetworkCapacityState.Result;
            AppSessionLogger.Event("info", "network", "network_capacity_test_cancelled", "network capacity test was cancelled for higher priority network activity");
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
            if (ReferenceEquals(networkCapacityTestCancellation, testCancellation))
            {
                networkCapacityTestCancellation = null;
            }
            testCancellation.Dispose();
            IsNetworkCapacityTesting = false;
            RefreshNetworkCapacityLocalization();
        }
    }

    private void CancelNetworkCapacityTest()
    {
        networkCapacityTestCancellation?.Cancel();
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

        return Math.Max(1, (int)Math.Floor(measuredMbps.Value * NetworkCapacitySafetyRatio / perRoomMbps));
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

    internal static NetworkCapacityPresentation CreateNetworkCapacityPresentation(
        NetworkCapacityMeasurement measurement,
        IReadOnlyCollection<RoomStatusReactive> estimateRooms)
    {
        double domesticPerRoomMbps = DefaultNetworkCapacityPerRoomMbps;
        double overseasPerRoomMbps = DefaultNetworkCapacityPerRoomMbps;
        int? domesticCapacity = CalculateNetworkCapacity(measurement.Domestic?.Mbps, domesticPerRoomMbps);
        int? overseasCapacity = CalculateNetworkCapacity(measurement.Overseas?.Mbps, overseasPerRoomMbps);
        if (!domesticCapacity.HasValue && !overseasCapacity.HasValue)
        {
            throw new InvalidOperationException("Network capacity measurement did not return a valid result.");
        }

        return new NetworkCapacityPresentation(
            measurement,
            domesticPerRoomMbps,
            overseasPerRoomMbps,
            domesticCapacity,
            overseasCapacity,
            estimateRooms.Count);
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

    internal static string FormatNetworkCapacityResultShort(int? domesticCapacity, int? overseasCapacity)
    {
        List<string> parts = [];
        if (domesticCapacity.HasValue)
        {
            parts.Add($"国内可录 {domesticCapacity.Value} 路");
        }
        if (overseasCapacity.HasValue)
        {
            parts.Add($"国外可录 {overseasCapacity.Value} 路");
        }

        return parts.Count == 0 ? "测速失败" : string.Join("，", parts);
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
                NetworkCapacityText = FormatNetworkCapacityResultShort(result.DomesticCapacity, result.OverseasCapacity);
                NetworkCapacityToolTip = FormatNetworkCapacityResultToolTip(result);
                break;
            default:
                NetworkCapacityText = "NetworkCapacityIdle".Tr();
                NetworkCapacityToolTip = "NetworkCapacityHint".Tr();
                break;
        }
    }

    private static string FormatNetworkCapacityResultToolTip(NetworkCapacityPresentation result)
    {
        string domestic = FormatNetworkRegionCapacity(
            "国内",
            result.Measurement.Domestic,
            result.DomesticCapacity,
            result.DomesticPerRoomMbps);
        string overseas = FormatNetworkRegionCapacity(
            "国外",
            result.Measurement.Overseas,
            result.OverseasCapacity,
            result.OverseasPerRoomMbps);
        return $"多节点三轮实测：{domestic}，{overseas}。单路统一按默认平均 {DefaultNetworkCapacityPerRoomMbps:0.##} Mbps 估算，不依赖当前直播流；共 {result.Measurement.SuccessfulSamples} 个有效样本，可信度 {GetNetworkMeasurementConfidenceText(result.Measurement.Confidence)}。";
    }

    private static string FormatNetworkRegionCapacity(
        string regionName,
        NetworkRegionMeasurement? measurement,
        int? capacity,
        double perRoomMbps)
    {
        if (measurement == null || !capacity.HasValue)
        {
            return $"{regionName}未测通";
        }

        return $"{regionName} {measurement.Mbps:0.##} Mbps，可录 {capacity.Value} 路，单路 {perRoomMbps:0.##} Mbps";
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

    private async Task<NetworkCapacityMeasurement> MeasureBestNetworkThroughputMbpsAsync(CancellationToken cancellationToken)
    {
        Task<NetworkRegionMeasurement?> domesticTask = MeasureNetworkRegionThroughputMbpsAsync(NetworkThroughputRegion.Domestic, cancellationToken);
        Task<NetworkRegionMeasurement?> overseasTask = MeasureNetworkRegionThroughputMbpsAsync(NetworkThroughputRegion.Overseas, cancellationToken);
        await Task.WhenAll(domesticTask, overseasTask);

        NetworkRegionMeasurement? domestic = await domesticTask;
        NetworkRegionMeasurement? overseas = await overseasTask;

        int successfulSamples = (domestic?.SuccessfulSamples ?? 0) + (overseas?.SuccessfulSamples ?? 0);
        int attemptedSamples = (domestic?.AttemptedSamples ?? 0) + (overseas?.AttemptedSamples ?? 0);
        int successfulEndpoints = (domestic?.SuccessfulEndpoints ?? 0) + (overseas?.SuccessfulEndpoints ?? 0);
        if (successfulSamples <= 0)
        {
            throw new InvalidOperationException("Network capacity test failed.");
        }

        return new NetworkCapacityMeasurement(
            domestic,
            overseas,
            successfulSamples,
            attemptedSamples,
            GetNetworkMeasurementConfidence(successfulSamples, attemptedSamples, successfulEndpoints));
    }

    private async Task<NetworkRegionMeasurement?> MeasureNetworkRegionThroughputMbpsAsync(NetworkThroughputRegion region, CancellationToken cancellationToken)
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
                    timeout: TimeSpan.FromSeconds(12),
                    cancellationToken)))
            .ToArray();
        (NetworkThroughputEndpoint Endpoint, double? Mbps)[] probes = await Task.WhenAll(probeTasks);

        (NetworkThroughputEndpoint Endpoint, double Mbps)[] successfulProbes = probes
            .Where(probe => probe.Mbps.HasValue)
            .Select(probe => (probe.Endpoint, probe.Mbps!.Value))
            .ToArray();
        double bestProbeMbps = successfulProbes.Select(probe => probe.Mbps).DefaultIfEmpty(0d).Max();
        int measuredEndpointCount = region == NetworkThroughputRegion.Overseas && bestProbeMbps < NetworkThroughputSingleOverseasProbeMbps
            ? 1
            : NetworkThroughputMeasuredEndpointCount;
        NetworkThroughputEndpoint[] candidates = successfulProbes
            .OrderByDescending(probe => probe.Mbps)
            .Select(probe => probe.Endpoint)
            .Take(measuredEndpointCount)
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
                TimeSpan.FromSeconds(45),
                cancellationToken);
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
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        NetworkEndpointMeasurement? measurement = await MeasureNetworkEndpointAsync(
            endpoint,
            connectionCount,
            roundCount,
            warmupBytes,
            bytesPerRound,
            timeout,
            cancellationToken);
        return measurement?.Mbps;
    }

    private async Task<NetworkEndpointMeasurement?> MeasureNetworkEndpointAsync(
        NetworkThroughputEndpoint endpoint,
        int connectionCount,
        int roundCount,
        long warmupBytes,
        long bytesPerRound,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationTokenSource.CancelAfter(timeout);
        List<NetworkThroughputConnection> connections = [];
        try
        {
            for (int index = 0; index < connectionCount; index++)
            {
                try
                {
                    connections.Add(await OpenNetworkThroughputConnectionAsync(endpoint, cancellationTokenSource.Token));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
        bool hasStreamUrl = !string.IsNullOrWhiteSpace(result.RecordUrl)
            || !string.IsNullOrWhiteSpace(result.FlvUrl)
            || !string.IsNullOrWhiteSpace(result.HlsUrl);
        bool isConclusive = StreamResolver.HasConclusiveData(result);
        room.IsStreamCheckFailed = !isConclusive;
        bool deferOffline = GlobalMonitor.ReconcileManualRefreshResult(room.RoomUrl, result.IsLiveStreaming, hasStreamUrl);
        bool? resolvedLiveState = deferOffline ? null : result.IsLiveStreaming;
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

        if (resolvedLiveState == true && !string.IsNullOrWhiteSpace(title))
        {
            room.LiveTitle = title;
        }
        else if (resolvedLiveState == false)
        {
            room.LiveTitle = string.Empty;
        }

        if (resolvedLiveState == true)
        {
            room.Quality = quality ?? room.Quality;
            room.Resolution = resolution ?? room.Resolution;
            room.Bitrate = bitrate ?? room.Bitrate;
        }
        else if (resolvedLiveState == false)
        {
            room.Quality = string.Empty;
            room.Resolution = string.Empty;
            room.Bitrate = string.Empty;
        }

        if (resolvedLiveState.HasValue || hasStreamUrl)
        {
            room.FlvUrl = result.FlvUrl ?? string.Empty;
            room.HlsUrl = result.HlsUrl ?? string.Empty;
            room.RecordUrl = result.RecordUrl ?? string.Empty;
        }
        if (resolvedLiveState.HasValue)
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
        room.StreamStatus = GlobalMonitor.ResolveStreamStatus(room.StreamStatus, resolvedLiveState, hasStreamUrl);

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
        status.IsStreamCheckFailed = room.IsStreamCheckFailed;
        room.RecordStatus = status.RecordStatus;
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
            await Player.PlayAsync(roomStatus.Recorder.FileName);
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

        RecordStatus currentRecordStatus = SelectedItem.RecordStatus;
        if (GlobalMonitor.RoomStatus.TryGetValue(SelectedItem.RoomUrl, out RoomStatus? runtimeStatus))
        {
            GlobalMonitor.SyncRecordStatus(runtimeStatus);
            currentRecordStatus = runtimeStatus.RecordStatus;
        }
        bool enabled = ShouldEnableSelectedRoomRecord(currentRecordStatus, SelectedItem.EffectiveIsToRecord);
        if (enabled)
        {
            GlobalMonitor.ClearRoomRecordStartPause(SelectedItem.RoomUrl);
        }
        AppSessionLogger.Event("info", "business", "manual_room_record_state_changed", "manual room recording state changed", new
        {
            SelectedItem.RoomUrl,
            SelectedItem.NickName,
            currentRecordStatus,
            enabled,
            SelectedItem.IsFollowGlobalSettings,
        });

        if (SelectedItem.IsFollowGlobalSettings)
        {
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

        SelectedItem.IsToRecord = enabled;
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

    internal static bool ShouldEnableSelectedRoomRecord(RecordStatus recordStatus, bool effectiveIsToRecord)
    {
        return recordStatus != RecordStatus.Recording && !effectiveIsToRecord;
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
            FocusVisualStyle = null,
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
        await GlobalMonitor.RunRoomAsync(SelectedItem.RoomUrl);
        ReloadRoomStatus(SelectedItem.RoomUrl);
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
        using DialogBlurScope blurScope = DialogBlurScope.ForMessageBox(Application.Current.MainWindow);
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

        Task refreshTask = targetRoom.EffectiveIsToMonitor
            ? GlobalMonitor.RunRoomAsync(targetRoom.RoomUrl)
            : Task.CompletedTask;
        await Launcher.LaunchUriAsync(new Uri(targetRoom.RoomUrl));
        await refreshTask;
        ReloadRoomStatus(targetRoom.RoomUrl);
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
        CancelNetworkCapacityTest();
        FlushPreviewRefreshSuppression();
        AutoShutdownDispatcherTimer.Stop();
        DispatcherTimer.Stop();
        CancellationTokenSource? transitionCancellation;
        lock (previewTransitionSync)
        {
            transitionCancellation = previewTransitionCancellation;
            previewTransitionCancellation = null;
            pendingPreviewFirstFrameLog = null;
        }
        transitionCancellation?.Cancel();

        Locale.CultureChanged -= OnCultureChanged;
        livePreviewPlayer.PlaybackFailed -= OnLivePreviewPlaybackFailed;
        livePreviewPlayer.PlaybackEnded -= OnLivePreviewPlaybackEnded;
        livePreviewPlayer.FrameSourceChanged -= OnLivePreviewFrameSourceChanged;
        livePreviewPlayer.FirstFramePresented -= OnLivePreviewFirstFramePresented;
        livePreviewPlayer.Dispose();
    }
}

internal enum PreviewControlFeedbackKind
{
    Volume,
}

internal enum PreviewTransitionReason
{
    Open,
    SwitchRoom,
    SameRoomToggleClose,
    ManualStop,
    ToggleClose,
    ManualRefresh,
    UserResume,
    PageResume,
    RoomUnavailable,
    PlaybackFailed,
    PlaybackEnded,
}

internal sealed record PreviewRoomLogContext(string RoomUrl, string NickName, string PlatformName);

internal sealed class PreviewFirstFrameLogContext(
    long requestId,
    string reason,
    long startedAt,
    LivePreviewFrameSource? frameSource,
    int baselinePresentedGeneration,
    PreviewRoomLogContext room)
{
    public long RequestId { get; } = requestId;
    public string Reason { get; } = reason;
    public long StartedAt { get; } = startedAt;
    public LivePreviewFrameSource? FrameSource { get; set; } = frameSource;
    public int BaselinePresentedGeneration { get; set; } = baselinePresentedGeneration;
    public PreviewRoomLogContext Room { get; } = room;
}

internal sealed class PreviewRefreshSuppression(
    long firstAttemptAt,
    long lastAttemptAt,
    long minimumIntervalMilliseconds,
    int attemptCount,
    PreviewRoomLogContext? room)
{
    public long FirstAttemptAt { get; } = firstAttemptAt;
    public long LastAttemptAt { get; set; } = lastAttemptAt;
    public long MinimumIntervalMilliseconds { get; set; } = minimumIntervalMilliseconds;
    public int AttemptCount { get; set; } = attemptCount;
    public PreviewRoomLogContext? Room { get; } = room;
}

internal sealed class PreviewControlFeedbackEventArgs(PreviewControlFeedbackKind kind, int volume) : EventArgs
{
    public PreviewControlFeedbackKind Kind { get; } = kind;

    public int Volume { get; } = volume;
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
    double DomesticPerRoomMbps,
    double OverseasPerRoomMbps,
    int? DomesticCapacity,
    int? OverseasCapacity,
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
