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
    private const string AllPlatformFilter = "All";
    private const long ManualRefreshCooldownMilliseconds = 5000;
    private static readonly string[] NetworkThroughputTestUrls =
    [
        "https://speed.cloudflare.com/__down?bytes=50000000",
        "https://cachefly.cachefly.net/10mb.test",
        "https://proof.ovh.net/files/10Mb.dat",
    ];

    protected internal ForeverDispatcherTimer DispatcherTimer { get; }

    private readonly LivePreviewPlayer livePreviewPlayer = new();
    private readonly object manualRefreshCooldownLock = new();
    private long lastManualRefreshTimestamp;

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
        }
    }

    [ObservableProperty]
    private ReactiveCollection<RoomStatusReactive> roomStatuses = [];

    public ICollectionView RoomStatusesView { get; }

    public IReadOnlyList<string> PlatformFilterOptions { get; } = [AllPlatformFilter, .. Spider.SupportedPlatformNames];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformSummaryText))]
    private string selectedPlatformFilter = AllPlatformFilter;

    partial void OnSelectedPlatformFilterChanged(string value)
    {
        RoomStatusesView.Refresh();
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
    private bool isCardEditMode = false;

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
    private bool isPreviewing = false;

    [ObservableProperty]
    private RoomStatusReactive? previewingRoom;

    [ObservableProperty]
    private bool isPreviewDetached = false;

    [ObservableProperty]
    private bool isPreviewMuted = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewPlaying))]
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

            normalizedRooms.Add(room);
        }

        if (changed)
        {
            Configurations.Rooms.Set(normalizedRooms.ToArray());
            ConfigurationManager.Save();
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
    private string statusOfAutoShutdownTime = Configurations.AutoShutdownTime.Get();

    [ObservableProperty]
    private string statusOfRecordFormat = Configurations.RecordFormat.Get();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusOfRoutineIntervalWithUnit))]
    private int statusOfRoutineInterval = Configurations.RoutineInterval.Get();

    public string StatusOfRoutineIntervalWithUnit
    {
        get
        {
            if (StatusOfRoutineInterval > 60000d)
            {
                return $"{Math.Round(StatusOfRoutineInterval / 60000d, 1)}min";
            }
            else if (StatusOfRoutineInterval > 1000d)
            {
                return $"{StatusOfRoutineInterval / 1000d}s";
            }
            else
            {
                return $"{StatusOfRoutineInterval}ms";
            }
        }
    }

    [ObservableProperty]
    private bool isNetworkCapacityTesting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkCapacityDisplayText))]
    private string networkCapacityText = "NetworkCapacityIdle".Tr();

    [ObservableProperty]
    private string networkCapacityToolTip = "NetworkCapacityHint".Tr();

    public string NetworkCapacityDisplayText => string.IsNullOrWhiteSpace(NetworkCapacityText) || NetworkCapacityText == "NetworkCapacityIdle" ? "测速" : NetworkCapacityText;

    [ObservableProperty]
    private bool isReadyToShutdown = false;

    public CancellationTokenSource? ShutdownCancellationTokenSource { get; private set; } = null;

    public MainViewModel()
    {
        DispatcherTimer = new(TimeSpan.FromSeconds(3), ReloadRoomStatus);
        Room[] configuredRooms = NormalizeStoredRooms(Configurations.Rooms.Get());

        RoomStatuses.Reset(configuredRooms.Select((room, index) => new RoomStatusReactive()
        {
            NickName = room.NickName,
            RoomUrl = room.RoomUrl,
            AvatarThumbUrl = room.AvatarThumbUrl,
            PlatformName = Spider.GetPlatformName(room.RoomUrl),
            IsToNotify = room.IsToNotify,
            IsToRecord = room.IsToRecord,
            IsToMonitor = room.IsToMonitor,
            IsFollowGlobalSettings = room.IsFollowGlobalSettings,
            AddedOrder = index,
        }));
        RoomStatusesView = CollectionViewSource.GetDefaultView(RoomStatuses);
        RoomStatusesView.Filter = FilterRoomStatus;

        Locale.CultureChanged += (_, _) =>
        {
            foreach (RoomStatusReactive roomStatusReactive in RoomStatuses)
            {
                roomStatusReactive.RefreshStatus();
            }
            OnPropertyChanged(nameof(PlatformSummaryText));
            if (!IsNetworkCapacityTesting && NetworkCapacityText == "NetworkCapacityIdle".Tr())
            {
                NetworkCapacityText = "NetworkCapacityIdle".Tr();
                NetworkCapacityToolTip = "NetworkCapacityHint".Tr();
            }
        };

        WeakReferenceMessenger.Default.Register<ToastNotificationActivatedMessage>(this, (_, msg) =>
        {
            string arguments = msg.EventArgs.Argument;

            if (!string.IsNullOrEmpty(arguments))
            {
                NameValueCollection parsedArgs = HttpUtility.ParseQueryString(arguments);

                if (parsedArgs["AutoShutdownCancel"] != null)
                {
                    ShutdownCancellationTokenSource?.Cancel();
                }
            }
        });

        if (Configurations.IsMonitorRunning.Get())
        {
            GlobalMonitor.Start();
        }
        ChildProcessTracerPeriodicTimer.Default.WhiteList = ["ffmpeg", "ffplay"];
        ChildProcessTracerPeriodicTimer.Default.Start();
        DispatcherTimer.Start();
        RecordingCleanupService.QueueRun();
    }

    private void ReloadRoomStatus()
    {
        foreach (RoomStatus roomStatus in GlobalMonitor.RoomStatus.Values.ToArray())
        {
            RoomStatusReactive? roomStatusReactive = RoomStatuses.Where(room => room.RoomUrl == roomStatus.RoomUrl).FirstOrDefault();

            if (roomStatusReactive != null)
            {
                roomStatusReactive.AvatarThumbUrl = roomStatus.AvatarThumbUrl;
                roomStatusReactive.PlatformName = roomStatus.PlatformName;
                roomStatusReactive.LiveTitle = roomStatus.LiveTitle;
                roomStatusReactive.Quality = roomStatus.Quality;
                roomStatusReactive.Resolution = roomStatus.Resolution;
                roomStatusReactive.Bitrate = roomStatus.Bitrate;
                roomStatusReactive.StreamStatus = roomStatus.StreamStatus;
                roomStatusReactive.RecordStatus = roomStatus.RecordStatus;
                roomStatusReactive.FlvUrl = roomStatus.FlvUrl;
                roomStatusReactive.HlsUrl = roomStatus.HlsUrl;
                roomStatusReactive.StartTime = roomStatus.Recorder.StartTime;
                roomStatusReactive.EndTime = roomStatus.Recorder.EndTime;
                roomStatusReactive.RefreshDuration();
            }
        }
        RoomStatusesView.Refresh();
        OnPropertyChanged(nameof(PlatformSummaryText));

        IsRecording = RoomStatuses.Any(roomStatusReactive => roomStatusReactive.RecordStatus == RecordStatus.Recording);

        StatusOfIsMonitorRunning = Configurations.IsMonitorRunning.Get();
        StatusOfIsToNotify = Configurations.IsToNotify.Get();
        StatusOfIsToMonitor = Configurations.IsToMonitor.Get();
        StatusOfIsToRecord = Configurations.IsToRecord.Get();
        StatusOfIsUseProxy = Configurations.IsUseProxy.Get();
        StatusOfIsUseKeepAwake = Configurations.IsUseKeepAwake.Get();
        StatusOfIsUseAutoShutdown = Configurations.IsUseAutoShutdown.Get();
        StatusOfAutoShutdownTime = Configurations.AutoShutdownTime.Get();
        StatusOfRecordFormat = Configurations.RecordFormat.Get();
        StatusOfRoutineInterval = Configurations.RoutineInterval.Get();
        OnPropertyChanged(nameof(CanPreviewSelectedRoom));

        if (StatusOfIsUseAutoShutdown && TimeSpan.TryParse(StatusOfAutoShutdownTime, out TimeSpan targetTime))
        {
            int timeOffset = (int)(DateTime.Now.TimeOfDay - targetTime).TotalSeconds;

            if (timeOffset >= 0 && timeOffset <= 60)
            {
                IsReadyToShutdown = true;
            }

            if (IsReadyToShutdown && !IsRecording)
            {
                if (ShutdownCancellationTokenSource == null)
                {
                    ShutdownCancellationTokenSource = new();

                    Notifier.AddNoticeWithButton("Title".Tr(), "AutoShutdownInTime".Tr(), [
                        new ToastContentButtonOption()
                            {
                                Content = "ButtonOfCancel".Tr(),
                                Arguments = [("AutoShutdownCancel", string.Empty)],
                                ActivationType = ToastActivationType.Foreground,
                            }
                    ]);

                    ApplicationDispatcher.BeginInvoke(async () =>
                    {
                        await Task.Delay(60000);

                        if (!ShutdownCancellationTokenSource.IsCancellationRequested && !IsRecording)
                        {
                            if (Debugger.IsAttached)
                            {
                                using DialogBlurScope blurScope = new(Application.Current.MainWindow);
                                _ = MessageBox.Information("AutoShutdown".Tr());
                            }
                            else
                            {
                                _ = Interop.ExitWindowsEx(User32.ExitWindowsFlags.EWX_SHUTDOWN | User32.ExitWindowsFlags.EWX_FORCE);
                            }
                        }

                        ShutdownCancellationTokenSource = null;
                        IsReadyToShutdown = false;
                    });
                }
            }
        }
    }

    [RelayCommand]
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
            await StopPreviewAsync();
            return;
        }

        if (!ReferenceEquals(SelectedItem, targetRoom))
        {
            SelectedItem = targetRoom;
        }

        try
        {
            string proxyUrl = Configurations.IsUseProxy.Get() ? Configurations.ProxyUrl.Get() : string.Empty;

            IsPreviewTransitioning = true;
            PreviewingRoom = targetRoom;
            IsPreviewing = true;
            IsPreviewPaused = false;
            LivePreviewStatus = LivePreviewStatus.Ready;
            livePreviewPlayer.SetMuted(IsPreviewMuted);
            await livePreviewPlayer.PlayAsync(targetRoom.PreviewUrl, Configurations.UserAgent.Get(), proxyUrl);
            LivePreviewStatus = LivePreviewStatus.Playing;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            livePreviewPlayer.Stop();
            PreviewingRoom = null;
            IsPreviewing = false;
            IsPreviewPaused = false;
            LivePreviewStatus = LivePreviewStatus.Error;
            Toast.Error("LivePreviewError".Tr());
        }
        finally
        {
            IsPreviewTransitioning = false;
        }
    }

    private static bool IsSameRoom(RoomStatusReactive? current, RoomStatusReactive? next)
    {
        if (current == null || next == null)
        {
            return false;
        }

        return ReferenceEquals(current, next) || string.Equals(current.RoomUrl, next.RoomUrl, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task StopPreviewAsync()
    {
        IsPreviewTransitioning = true;
        try
        {
            await livePreviewPlayer.StopAsync();
            IsPreviewDetached = false;
            PreviewingRoom = null;
            IsPreviewing = false;
            IsPreviewPaused = false;
            LivePreviewStatus = CanPreviewSelectedRoom ? LivePreviewStatus.Ready : LivePreviewStatus.Idle;
        }
        finally
        {
            IsPreviewTransitioning = false;
        }
    }

    [RelayCommand]
    private async Task TogglePreviewPlaybackAsync()
    {
        if (IsPreviewing)
        {
            await StopPreviewAsync();
            return;
        }

        await PreviewLiveRoomAsync();
    }

    [RelayCommand]
    private async Task TogglePreviewPauseAsync()
    {
        if (!IsPreviewing)
        {
            await PreviewLiveRoomAsync();
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
    private void ToggleMonitor()
    {
        bool isMonitorRunning = !Configurations.IsMonitorRunning.Get();
        Configurations.IsMonitorRunning.Set(isMonitorRunning);
        ConfigurationManager.Save();
        StatusOfIsMonitorRunning = isMonitorRunning;

        if (isMonitorRunning)
        {
            GlobalMonitor.Start();
            Toast.Success("SuccOp".Tr());
        }
        else
        {
            GlobalMonitor.Stop();
            Toast.Success("SuccOp".Tr());
        }
    }

    [RelayCommand]
    private void ToggleStatusMonitor()
    {
        StatusOfIsToMonitor = !StatusOfIsToMonitor;
        Configurations.IsToMonitor.Set(StatusOfIsToMonitor);
        ConfigurationManager.Save();
        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private void ToggleStatusNotify()
    {
        StatusOfIsToNotify = !StatusOfIsToNotify;
        Configurations.IsToNotify.Set(StatusOfIsToNotify);
        ConfigurationManager.Save();
        TrayIconManager.GetInstance().UpdateTrayIcon();
        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private void ToggleStatusRecord()
    {
        StatusOfIsToRecord = !StatusOfIsToRecord;
        Configurations.IsToRecord.Set(StatusOfIsToRecord);
        ConfigurationManager.Save();
        TrayIconManager.GetInstance().UpdateTrayIcon();
        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private void ToggleStatusProxy()
    {
        StatusOfIsUseProxy = !StatusOfIsUseProxy;
        Configurations.IsUseProxy.Set(StatusOfIsUseProxy);
        ConfigurationManager.Save();
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
        ConfigurationManager.Save();
    }

    [RelayCommand]
    private void ToggleStatusAutoShutdown()
    {
        StatusOfIsUseAutoShutdown = !StatusOfIsUseAutoShutdown;
        Configurations.IsUseAutoShutdown.Set(StatusOfIsUseAutoShutdown);
        ConfigurationManager.Save();
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

        AddConfirmedRoom(dialog.NickName, dialog.RoomUrl, dialog.SpiderResult);
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

    private void AddConfirmedRoom(string nickName, string roomUrl, ISpiderResult? spiderResult)
    {
        List<Room> rooms = [.. Configurations.Rooms.Get()];

        rooms.RemoveAll(room => room.RoomUrl == roomUrl);
        rooms.Add(new Room()
        {
            NickName = nickName,
            RoomUrl = roomUrl,
            AvatarThumbUrl = spiderResult?.AvatarThumbUrl ?? string.Empty,
            IsToMonitor = Configurations.IsToMonitor.Get(),
            IsToRecord = Configurations.IsToRecord.Get(),
            IsFollowGlobalSettings = true,
        });
        Configurations.Rooms.Set([.. rooms]);
        ConfigurationManager.Save();

        RoomStatusReactive roomStatusReactive = new()
        {
            NickName = nickName,
            RoomUrl = roomUrl,
            PlatformName = Spider.GetPlatformName(roomUrl),
            IsToMonitor = Configurations.IsToMonitor.Get(),
            IsToRecord = Configurations.IsToRecord.Get(),
            IsFollowGlobalSettings = true,
            AddedOrder = RoomStatuses.Count == 0 ? 0 : RoomStatuses.Max(room => room.AddedOrder) + 1,
        };
        if (spiderResult != null)
        {
            ApplyRoomInfoResult(roomStatusReactive, spiderResult);
        }

        RoomStatuses.Add(roomStatusReactive);
        RoomStatusesView.Refresh();
        OnPropertyChanged(nameof(PlatformSummaryText));
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
    private void CopySelectedRoomUrl()
    {
        CopyTextToClipboard(SelectedItem?.RoomUrl);
    }

    [RelayCommand]
    private void CopySelectedPreviewUrl()
    {
        CopyTextToClipboard(SelectedItem?.PreviewUrl);
    }

    [RelayCommand]
    private void SortRoomsByName()
    {
        RoomStatusReactive? selected = SelectedItem;
        RoomStatuses.Reset(RoomStatuses
            .OrderBy(room => room.NickName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(room => room.RoomUrl, StringComparer.OrdinalIgnoreCase)
            .ToArray());
        RestoreSelectedRoom(selected);
        SaveRoomOrder();
        RoomStatusesView.Refresh();
        OnPropertyChanged(nameof(PlatformSummaryText));
    }

    [RelayCommand]
    private void SortRoomsByAddedAt()
    {
        RoomStatusReactive? selected = SelectedItem;
        RoomStatuses.Reset(RoomStatuses
            .OrderBy(room => room.AddedOrder)
            .ThenBy(room => room.RoomUrl, StringComparer.OrdinalIgnoreCase)
            .ToArray());
        RestoreSelectedRoom(selected);
        SaveRoomOrder();
        RoomStatusesView.Refresh();
        OnPropertyChanged(nameof(PlatformSummaryText));
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
                ISpiderResult? result = await Task.Run(() => Spider.GetResult(room.RoomUrl));
                if (result != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ApplyRoomInfoResult(room, result);
                        hasUpdated = true;
                    });
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
        Toast.Success(hasUpdated ? "SuccOp".Tr() : "FailOp".Tr());
    }

    [RelayCommand]
    private async Task RefreshSelectedRoomInfoAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl) || IsRefreshingSelectedRoomInfo)
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
            ISpiderResult? result = await Task.Run(() => Spider.GetResult(SelectedItem.RoomUrl));
            if (result == null)
            {
                Toast.Error("GetRoomInfoError".Tr());
                return;
            }

            ApplyRoomInfoResult(SelectedItem, result);
            SaveRoomOrder();
            RoomStatusesView.Refresh();
            OnPropertyChanged(nameof(PlatformSummaryText));
            OnPropertyChanged(nameof(CanPreviewSelectedRoom));
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
            NetworkCapacityText = "NetworkCapacityNoStreamShort".Tr();
            NetworkCapacityToolTip = "NetworkCapacityNoStream".Tr();
            Toast.Warning(NetworkCapacityToolTip);
            return;
        }

        IsNetworkCapacityTesting = true;
        NetworkCapacityText = "NetworkCapacityTesting".Tr();
        NetworkCapacityToolTip = "NetworkCapacityTesting".Tr();
        AppSessionLogger.Write($"network capacity test started, samples={estimateRooms.Length}");

        try
        {
            RoomStatusReactive[] testRooms = estimateRooms
                .Where(room => !string.IsNullOrWhiteSpace(GetPreferredNetworkTestUrl(room)))
                .ToArray();
            double perRoomMbps = estimateRooms.Select(EstimateRequiredMbps).DefaultIfEmpty(10d).Average();
            bool measuredBroadband = true;
            double measuredMbps;
            try
            {
                measuredMbps = await MeasureBestNetworkThroughputMbpsAsync();
            }
            catch
            {
                measuredBroadband = false;
                measuredMbps = testRooms.Length == 0
                    ? estimateRooms.Sum(EstimateRequiredMbps) * 1.25d
                    : await MeasureNetworkThroughputMbpsAsync(GetPreferredNetworkTestUrl(testRooms[0]));
            }
            int capacity = Math.Max(1, (int)Math.Floor(measuredMbps * 0.7d / Math.Max(1d, perRoomMbps)));

            NetworkCapacityText = FormatNetworkCapacityResultShort(capacity);
            NetworkCapacityToolTip = "NetworkCapacityResultHint".Tr(measuredMbps, perRoomMbps, capacity, estimateRooms.Length);
            AppSessionLogger.Write($"network capacity test completed, measuredMbps={measuredMbps:0.##}, perRoomMbps={perRoomMbps:0.##}, capacity={capacity}, source={(measuredBroadband ? "broadband" : "stream")}");
            Toast.Success(NetworkCapacityToolTip);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            AppSessionLogger.WriteException(e);
            double perRoomMbps = estimateRooms.Select(EstimateRequiredMbps).DefaultIfEmpty(10d).Average();
            double estimatedMbps = estimateRooms.Sum(EstimateRequiredMbps) * 1.25d;
            int capacity = Math.Max(1, (int)Math.Floor(estimatedMbps * 0.7d / Math.Max(1d, perRoomMbps)));
            NetworkCapacityText = FormatNetworkCapacityResultShort(capacity);
            NetworkCapacityToolTip = "NetworkCapacityResultHint".Tr(estimatedMbps, perRoomMbps, capacity, estimateRooms.Length);
            Toast.Warning(NetworkCapacityToolTip);
        }
        finally
        {
            IsNetworkCapacityTesting = false;
        }
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

    private static string GetPreferredNetworkTestUrl(RoomStatusReactive room)
    {
        if (!string.IsNullOrWhiteSpace(room.FlvUrl))
        {
            return room.FlvUrl;
        }

        return room.PreviewUrl;
    }

    private async Task<double> MeasureNetworkThroughputMbpsAsync(string url)
    {
        using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(10));
        using HttpClientHandler handler = new()
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

        using HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        string testUrl = await ResolveNetworkTestUrlAsync(client, url, cancellationTokenSource.Token);
        using HttpRequestMessage request = new(HttpMethod.Get, testUrl);
        string userAgent = Configurations.UserAgent.Get();
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource.Token);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationTokenSource.Token);

        byte[] buffer = new byte[128 * 1024];
        long totalBytes = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan sampleDuration = TimeSpan.FromSeconds(4);
        long maxBytes = 20L * 1024L * 1024L;

        while (stopwatch.Elapsed < sampleDuration && totalBytes < maxBytes)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationTokenSource.Token);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
        }

        stopwatch.Stop();
        if (totalBytes < 64 * 1024 || stopwatch.Elapsed.TotalSeconds <= 0.25d)
        {
            throw new InvalidOperationException("Network sample is too small.");
        }

        return totalBytes * 8d / stopwatch.Elapsed.TotalSeconds / 1_000_000d;
    }

    private async Task<double> MeasureBestNetworkThroughputMbpsAsync()
    {
        List<double> samples = [];

        foreach (string url in NetworkThroughputTestUrls)
        {
            try
            {
                double sample = await MeasureNetworkThroughputMbpsAsync(url);
                samples.Add(sample);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                AppSessionLogger.Write($"network capacity endpoint failed, url={url}, error={e.Message}");
            }
        }

        if (samples.Count == 0)
        {
            throw new InvalidOperationException("No network capacity endpoint returned a valid sample.");
        }

        return samples.Max();
    }

    private async Task<string> ResolveNetworkTestUrlAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        string currentUrl = url;

        for (int depth = 0; depth < 2; depth++)
        {
            if (!currentUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                return currentUrl;
            }

            using HttpRequestMessage request = new(HttpMethod.Get, currentUrl);
            string userAgent = Configurations.UserAgent.Get();
            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            }

            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            string playlist = await response.Content.ReadAsStringAsync(cancellationToken);

            string? entry = playlist
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(line => !line.StartsWith('#') && !string.IsNullOrWhiteSpace(line));

            if (string.IsNullOrWhiteSpace(entry) ||
                !Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri? baseUri) ||
                !Uri.TryCreate(baseUri, entry, out Uri? resolved))
            {
                return currentUrl;
            }

            currentUrl = resolved.ToString();
        }

        return currentUrl;
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

    private static void ApplyRoomInfoResult(RoomStatusReactive room, ISpiderResult result)
    {
        string oldRoomUrl = room.RoomUrl;
        string? title = SpiderResultMetadata.GetTitle(result);
        string? quality = SpiderResultMetadata.GetQuality(result);
        string? resolution = SpiderResultMetadata.GetResolution(result);
        string? bitrate = SpiderResultMetadata.GetBitrate(result);

        if (!string.IsNullOrWhiteSpace(result.Nickname))
        {
            room.NickName = result.Nickname;
        }

        if (!string.IsNullOrWhiteSpace(result.RoomUrl))
        {
            string normalizedRoomUrl = NormalizeRoomUrl(result.RoomUrl);
            if (!string.IsNullOrWhiteSpace(normalizedRoomUrl))
            {
                room.RoomUrl = normalizedRoomUrl;
            }
        }

        if (!string.IsNullOrWhiteSpace(result.AvatarThumbUrl))
        {
            room.AvatarThumbUrl = result.AvatarThumbUrl;
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

        if (!string.IsNullOrWhiteSpace(quality))
        {
            room.Quality = quality;
        }

        if (!string.IsNullOrWhiteSpace(resolution))
        {
            room.Resolution = resolution;
        }

        if (!string.IsNullOrWhiteSpace(bitrate))
        {
            room.Bitrate = bitrate;
        }

        room.FlvUrl = result.FlvUrl ?? string.Empty;
        room.HlsUrl = result.HlsUrl ?? string.Empty;
        room.StreamStatus = result.IsLiveStreaming switch
        {
            true => StreamStatus.Streaming,
            false => StreamStatus.NotStreaming,
            _ => room.StreamStatus,
        };

        if (!string.Equals(oldRoomUrl, room.RoomUrl, StringComparison.OrdinalIgnoreCase))
        {
            _ = GlobalMonitor.RoomStatus.TryRemove(oldRoomUrl, out _);
        }

        RoomStatus status = GlobalMonitor.RoomStatus.GetOrAdd(room.RoomUrl, _ => new RoomStatus()
        {
            NickName = room.NickName,
            RoomUrl = room.RoomUrl,
            PlatformName = room.PlatformName,
            StreamStatus = StreamStatus.Initialized,
        });
        status.NickName = room.NickName;
        status.AvatarThumbUrl = room.AvatarThumbUrl;
        status.PlatformName = room.PlatformName;
        status.LiveTitle = room.LiveTitle;
        status.Quality = room.Quality;
        status.Resolution = room.Resolution;
        status.Bitrate = room.Bitrate;
        status.FlvUrl = room.FlvUrl;
        status.HlsUrl = room.HlsUrl;
        status.StreamStatus = room.StreamStatus;
        room.FlashRefresh();
    }

    [RelayCommand]
    private void ToggleCardEditMode()
    {
        IsCardEditMode = !IsCardEditMode;
        Toast.Success("SuccOp".Tr());
    }

    public void MoveRoom(RoomStatusReactive source, int newVisibleIndex)
    {
        if (!RoomStatuses.Contains(source))
        {
            return;
        }

        List<RoomStatusReactive> visibleRooms = RoomStatusesView.Cast<RoomStatusReactive>().ToList();
        int oldVisibleIndex = visibleRooms.IndexOf(source);

        if (oldVisibleIndex < 0)
        {
            return;
        }

        newVisibleIndex = Math.Clamp(newVisibleIndex, 0, visibleRooms.Count);
        if (oldVisibleIndex < newVisibleIndex)
        {
            newVisibleIndex--;
        }

        if (oldVisibleIndex == newVisibleIndex)
        {
            return;
        }

        RoomStatusReactive? target = visibleRooms
            .Where(room => !ReferenceEquals(room, source))
            .ElementAtOrDefault(newVisibleIndex);

        int oldIndex = RoomStatuses.IndexOf(source);
        int newIndex;

        if (target == null)
        {
            newIndex = RoomStatuses.Count - 1;
        }
        else
        {
            newIndex = RoomStatuses.IndexOf(target);
            if (oldIndex < newIndex)
            {
                newIndex--;
            }
        }

        newIndex = Math.Clamp(newIndex, 0, Math.Max(0, RoomStatuses.Count - 1));
        if (oldIndex == newIndex)
        {
            return;
        }

        RoomStatuses.Move(oldIndex, newIndex);
        SelectedItem = source;
        SaveRoomOrder();
        RoomStatusesView.Refresh();
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

    private static void CopyTextToClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.Warning("FailOp".Tr());
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            Toast.Success("SuccOp".Tr());
        }
        catch (ExternalException)
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
        RoomStatusesView.Refresh();
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
        RoomStatusesView.Refresh();
    }

    private void SaveRoomOrder()
    {
        Dictionary<string, Room> roomsByUrl = [];

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
                    room.NickName = roomStatus.NickName;
                    room.RoomUrl = NormalizeRoomUrl(roomStatus.RoomUrl);
                    room.AvatarThumbUrl = roomStatus.AvatarThumbUrl;
                    room.IsToNotify = roomStatus.IsToNotify;
                    room.IsToRecord = roomStatus.IsToRecord;
                    room.IsToMonitor = roomStatus.IsToMonitor;
                    room.IsFollowGlobalSettings = roomStatus.IsFollowGlobalSettings;
                    return room;
                }

                return new Room()
                {
                    NickName = roomStatus.NickName,
                    RoomUrl = NormalizeRoomUrl(roomStatus.RoomUrl),
                    AvatarThumbUrl = roomStatus.AvatarThumbUrl,
                    IsToNotify = roomStatus.IsToNotify,
                    IsToRecord = roomStatus.IsToRecord,
                    IsToMonitor = roomStatus.IsToMonitor,
                    IsFollowGlobalSettings = roomStatus.IsFollowGlobalSettings,
                };
            })
            .ToArray();

        Configurations.Rooms.Set(rooms);
        ConfigurationManager.Save();
        GlobalMonitor.RefreshRoutineInterval();
    }

    [RelayCommand]
    private void ToggleSelectedRoomMonitor()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        if (SelectedItem.IsFollowGlobalSettings)
        {
            SelectedItem.IsFollowGlobalSettings = false;
            SelectedItem.IsToMonitor = !Configurations.IsToMonitor.Get();
        }
        else
        {
            SelectedItem.IsToMonitor = !SelectedItem.IsToMonitor;
        }

        SaveSelectedRoomSettings();
        RefreshRoomEffectiveStates();
    }

    [RelayCommand]
    private void ToggleSelectedRoomRecord()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.RoomUrl))
        {
            return;
        }

        if (SelectedItem.IsFollowGlobalSettings)
        {
            SelectedItem.IsFollowGlobalSettings = false;
            SelectedItem.IsToRecord = !Configurations.IsToRecord.Get();
        }
        else
        {
            SelectedItem.IsToRecord = !SelectedItem.IsToRecord;
        }

        SaveSelectedRoomSettings();
        RefreshRoomEffectiveStates();
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

        string roomUrl = SelectedItem.RoomUrl;
        string nickName = SelectedItem.NickName;
        using DialogBlurScope blurScope = new(Application.Current.MainWindow);
        MessageBoxResult result = await MessageBox.QuestionAsync("SureRemoveRoom".Tr(nickName));

        if (result == MessageBoxResult.Yes)
        {
            if (GlobalMonitor.RoomStatus.TryGetValue(roomUrl, out RoomStatus? roomStatus))
            {
                roomStatus.Recorder.Stop();
                _ = GlobalMonitor.RoomStatus.TryRemove(roomUrl, out _);
            }

            RoomStatusReactive? roomStatusReactive = RoomStatuses.FirstOrDefault(room => room.RoomUrl == roomUrl);
            if (roomStatusReactive != null)
            {
                RoomStatuses.Remove(roomStatusReactive);
            }
            RoomStatusesView.Refresh();
            OnPropertyChanged(nameof(PlatformSummaryText));

            List<Room> rooms = [.. Configurations.Rooms.Get()];

            rooms.RemoveAll(room => room.RoomUrl == roomUrl);
            Configurations.Rooms.Set([.. rooms]);
            ConfigurationManager.Save();
            _ = SelectedItem.MapFrom(new RoomStatusReactive());

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
        ConfigurationManager.Save();
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
        ConfigurationManager.Save();
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
            room.IsToNotify = SelectedItem.IsToNotify;
            room.IsToRecord = SelectedItem.IsToRecord;
            room.IsToMonitor = SelectedItem.IsToMonitor;
            room.IsFollowGlobalSettings = SelectedItem.IsFollowGlobalSettings;
            if (recordingOptions != null)
            {
                RoomRecordingSettings.Apply(room, recordingOptions);
            }
        }

        Configurations.Rooms.Set(rooms);
        ConfigurationManager.Save();
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
                            _ = data.MapTo(SelectedItem);

                            foreach (UIElement d in ((ContextMenu)sender).Items.OfType<UIElement>())
                            {
                                d.Visibility = Visibility.Visible;
                            }
                        }
                    }
                    else
                    {
                        ((ContextMenu)sender).IsOpen = false;
                        _ = SelectedItem.MapFrom(new RoomStatusReactive());

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

        return SelectedPlatformFilter == AllPlatformFilter || room.PlatformName == SelectedPlatformFilter;
    }

    public void Dispose()
    {
        livePreviewPlayer.Dispose();
    }
}
