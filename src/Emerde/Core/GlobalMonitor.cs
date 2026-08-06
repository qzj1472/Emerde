using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fischless.Configuration;
using MediaInfoLib;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web;
using Emerde.Models;
using Emerde.Threading;
using Emerde.Plugins;
using Windows.System;
using Wpf.Ui.Violeta.Resources;

namespace Emerde.Core;

internal static class GlobalMonitor
{
    private const int DefaultSchedulerPeriodMilliseconds = MonitorTiming.MinimumRoutineIntervalMilliseconds;
    private const int MaximumMonitorConcurrency = MonitorTiming.MonitorBatchLimit;
    private const int MaximumBatchSize = 20;
    private const int MaximumRecordingBatchSize = 10;
    private const int MaximumRecordingConcurrency = 4;
    private static readonly TimeSpan MaximumSchedulerDelay = TimeSpan.FromDays(1);
    internal const long FixedRoomMetadataRefreshIntervalMilliseconds = 60 * 60 * 1000;
    internal const long InconclusiveLogIntervalMilliseconds = 60 * 60 * 1000;
    private static readonly TimeSpan StreamingCycleInterval = TimeSpan.FromMilliseconds(MonitorTiming.LiveRoutineIntervalMilliseconds);
    private static readonly TimeSpan RecentlyClosedInterval = TimeSpan.FromMilliseconds(MonitorTiming.RecentlyClosedRoutineIntervalMilliseconds);
    private static readonly TimeSpan RecentlyClosedWindow = MonitorTiming.RecentlyClosedWindow;
    internal static readonly TimeSpan RoutineRoomCheckTimeout = TimeSpan.FromSeconds(6);
    internal static readonly TimeSpan ForcedRoomCheckTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan RoomCheckDispatchDelayWarning = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan RecordingStartupOfflineGuardWindow = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan RoomRecordStartPause = TimeSpan.FromMinutes(2);

    /// <summary>
    /// ConcurrentDictionary{RoomUrl: string, RoomStatus: RoomStatus>}
    /// </summary>
    public static ConcurrentDictionary<string, RoomStatus> RoomStatus { get; } = new();

    private static readonly ConcurrentDictionary<string, bool> TemporaryRoomMonitorOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, bool> TemporaryRoomRecordOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object RoomCheckLocksSync = new();

    private static readonly Dictionary<string, RoomCheckGate> RoomCheckLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, RoomCheckScheduleState> RoomCheckSchedules = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, int> OfflineConfirmationChecks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> RecordStartBlocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, DateTime> RoomRecordStartPausedUntil = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, long> InconclusiveLogTimestamps = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> ScheduledRoomChecks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly SemaphoreSlim RoutineRoomCheckConcurrency = new(MaximumMonitorConcurrency, MaximumMonitorConcurrency);

    private static readonly SemaphoreSlim RecordingRoomCheckConcurrency = new(MaximumRecordingConcurrency, MaximumRecordingConcurrency);

    private static readonly Dictionary<string, ActiveSpiderResultTask> ActiveSpiderResultTasks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object ActiveSpiderResultTasksSync = new();

    private static int invalidEmailConfigurationLogged;

    public static PeriodicWait RoutinePeriodicWait = new(GetRoutinePeriod(), TimeSpan.Zero);

    public static CancellationTokenSource? TokenSource { get; private set; } = null;

    private static readonly object MonitorLock = new();

    private static Task? MonitorTask = null;

    private static long monitorGeneration;

    private sealed class RoomCheckScheduleState
    {
        public DateTime NextCheckAt { get; set; } = DateTime.MinValue;
        public DateTime? LastClosedAt { get; set; }
    }

    private sealed record PendingRoomCheck(
        Room Room,
        RoomStatus RoomStatus,
        bool ShouldNotify,
        bool ShouldRecord,
        RoomRecordingOptions Settings,
        DateTime DueAt);

    private sealed class RoomCheckGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private sealed class RoomCheckLease(string roomUrl, RoomCheckGate gate) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            gate.Semaphore.Release();
            lock (RoomCheckLocksSync)
            {
                gate.ReferenceCount--;
                if (gate.ReferenceCount == 0
                    && RoomCheckLocks.TryGetValue(roomUrl, out RoomCheckGate? current)
                    && ReferenceEquals(current, gate))
                {
                    RoomCheckLocks.Remove(roomUrl);
                    gate.Semaphore.Dispose();
                }
            }
        }
    }

    private sealed class GlobalMonitorRecipient : ObservableRecipient
    {
        public static GlobalMonitorRecipient Instance { get; } = new();
    }

    static GlobalMonitor()
    {
        WeakReferenceMessenger.Default.Register<ToastNotificationActivatedMessage>(GlobalMonitorRecipient.Instance, async (_, msg) =>
        {
            string arguments = msg.EventArgs.Argument;

            if (!string.IsNullOrEmpty(arguments))
            {
                NameValueCollection parsedArgs = HttpUtility.ParseQueryString(arguments);

                if (parsedArgs["RoomUrl"] != null)
                {
                    try
                    {
                        // TODO: Implement for other platforms
                        await Launcher.LaunchUriAsync(new Uri(parsedArgs["RoomUrl"]!));
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                else if (parsedArgs["OffRemindTheCloseToTrayHint"] != null)
                {
                    try
                    {
                        Configurations.IsOffRemindCloseToTray.Set(true);
                        ConfigurationSaveScheduler.SaveNow();
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
            }
        });
    }

    public static void Start(CancellationTokenSource? tokenSource = null)
    {
        CancellationTokenSource? previousSource;
        Task? previousTask;
        PeriodicWait previousPeriodicWait;
        CancellationTokenSource activeSource;
        lock (MonitorLock)
        {
            if (TokenSource != null && !TokenSource.IsCancellationRequested && MonitorTask is { IsCompleted: false })
            {
                WakeMonitorScheduler();
                return;
            }

            activeSource = tokenSource ?? new CancellationTokenSource();
            long generation = Interlocked.Increment(ref monitorGeneration);
            PeriodicWait periodicWait = new(GetRoutinePeriod(), TimeSpan.Zero);
            previousSource = TokenSource;
            previousTask = MonitorTask;
            previousPeriodicWait = RoutinePeriodicWait;
            TokenSource = activeSource;
            RoutinePeriodicWait = periodicWait;
            MonitorTask = Task.Factory.StartNew(
                () => StartAsync(activeSource.Token, generation, periodicWait),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            ).Unwrap();
            AppSessionLogger.Event("info", "monitor", "monitor_started", "global monitor started", new
            {
                generation,
                routineMilliseconds = periodicWait.Period.TotalMilliseconds,
            });
        }

        if (previousSource != null)
        {
            if (ReferenceEquals(previousSource, activeSource))
            {
                _ = DisposeMonitorTaskAsync(previousTask, previousPeriodicWait);
            }
            else
            {
                TryCancel(previousSource);
                _ = DisposeMonitorSourceAsync(previousSource, previousTask, previousPeriodicWait);
            }
        }
        else
        {
            previousPeriodicWait.Dispose();
        }
    }

    public static void Stop()
    {
        CancellationTokenSource? source;
        Task? task;
        PeriodicWait? periodicWait;
        lock (MonitorLock)
        {
            Interlocked.Increment(ref monitorGeneration);
            source = TokenSource;
            task = MonitorTask;
            periodicWait = RoutinePeriodicWait;
            RoutinePeriodicWait = new(GetRoutinePeriod(), TimeSpan.Zero);
            TokenSource = null;
            MonitorTask = null;
            AppSessionLogger.Event("info", "monitor", "monitor_stopped", "global monitor stopped");
        }

        TryCancel(source);

        if (source != null)
        {
            _ = DisposeMonitorSourceAsync(source, task, periodicWait);
        }
        else
        {
            periodicWait?.Dispose();
        }
    }

    private static async Task DisposeMonitorSourceAsync(CancellationTokenSource source, Task? task, PeriodicWait? periodicWait)
    {
        try
        {
            await DisposeMonitorTaskAsync(task, periodicWait);
        }
        finally
        {
            source.Dispose();
        }
    }

    public static void StopAllRecorders(bool deferPostProcessing = false)
    {
        RoomStatus[] roomStatuses = RoomStatus.Values.ToArray();
        foreach (RoomStatus roomStatus in roomStatuses)
        {
            roomStatus.Recorder.Stop(deferPostProcessing);
        }

        MediaOperationRegistry.Cancel(MediaOperationKind.Recording);

        AppSessionLogger.Event("info", "monitor", "all_recorders_stopped", "all active recorders were asked to stop", new
        {
            recorderCount = roomStatuses.Count(roomStatus => roomStatus.Recorder.IsBusy),
        });
    }

    public static async Task WaitForRecordersAsync(TimeSpan timeout)
    {
        await MediaOperationRegistry.WaitForCompletionAsync(timeout);
    }

    public static bool HasActiveRecorders => MediaOperationRegistry.HasActive(MediaOperationKind.Recording);

    public static bool IsRecordStartBlocked => !RecordStartBlocks.IsEmpty;

    public static void SetRecordStartBlock(string reason, bool blocked)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        bool changed = blocked
            ? RecordStartBlocks.TryAdd(reason, 1)
            : RecordStartBlocks.TryRemove(reason, out _);
        if (!changed)
        {
            return;
        }

        AppSessionLogger.Event("info", "monitor", blocked ? "record_start_blocked" : "record_start_unblocked", reason, new
        {
            reason,
            activeBlocks = RecordStartBlocks.Keys.OrderBy(static value => value).ToArray(),
        });
    }

    public static bool GetEffectiveRoomRecord(Room room)
    {
        if (room == null || string.IsNullOrWhiteSpace(room.RoomUrl))
        {
            return false;
        }

        return GetEffectiveRoomRecord(room.RoomUrl, room.IsToRecord, room.IsFollowGlobalSettings);
    }

    public static bool GetEffectiveRoomRecord(string roomUrl, bool roomValue, bool followsGlobal)
    {
        if (string.IsNullOrWhiteSpace(roomUrl))
        {
            return false;
        }

        bool value = followsGlobal ? Configurations.IsToRecord.Get() : roomValue;
        return TemporaryRoomRecordOverrides.TryGetValue(roomUrl, out bool temporaryValue) ? temporaryValue : value;
    }

    public static bool GetEffectiveRoomMonitor(Room room)
    {
        if (room == null || string.IsNullOrWhiteSpace(room.RoomUrl))
        {
            return false;
        }

        return GetEffectiveRoomMonitor(room.RoomUrl, room.IsToMonitor, room.IsFollowGlobalSettings);
    }

    public static bool GetEffectiveRoomMonitor(string roomUrl, bool roomValue, bool followsGlobal)
    {
        if (string.IsNullOrWhiteSpace(roomUrl))
        {
            return false;
        }

        bool value = followsGlobal ? Configurations.IsMonitorRunning.Get() && Configurations.IsToMonitor.Get() : roomValue;
        value = TemporaryRoomMonitorOverrides.TryGetValue(roomUrl, out bool temporaryValue) ? temporaryValue : value;
        return value;
    }

    public static void SetTemporaryRoomRecord(string roomUrl, bool enabled)
    {
        if (!string.IsNullOrWhiteSpace(roomUrl))
        {
            TemporaryRoomRecordOverrides[roomUrl] = enabled;
        }
    }

    public static void ClearTemporaryRoomRecord(string roomUrl)
    {
        if (!string.IsNullOrWhiteSpace(roomUrl))
        {
            _ = TemporaryRoomRecordOverrides.TryRemove(roomUrl, out _);
        }
    }

    public static void ClearTemporaryRecordOverrides()
    {
        TemporaryRoomRecordOverrides.Clear();
    }

    public static void ClearTemporaryMonitorOverrides()
    {
        TemporaryRoomMonitorOverrides.Clear();
    }

    public static async Task ApplyRuntimeConfigurationAsync()
    {
        RefreshRoutineInterval();
        Room[] rooms = Configurations.Rooms.Get() ?? [];
        if (Configurations.IsMonitorRunning.Get() || rooms.Any(room => !room.IsFollowGlobalSettings && GetEffectiveRoomMonitor(room)))
        {
            Start();
        }

        await RunRoomsAsync(rooms, force: true);
    }

    public static void SetTemporaryRoomMonitor(string roomUrl, bool enabled)
    {
        if (!string.IsNullOrWhiteSpace(roomUrl))
        {
            TemporaryRoomMonitorOverrides[roomUrl] = enabled;
            WakeMonitorScheduler();
        }
    }

    public static void ClearTemporaryRoomOverrides(string roomUrl)
    {
        if (!string.IsNullOrWhiteSpace(roomUrl))
        {
            _ = TemporaryRoomRecordOverrides.TryRemove(roomUrl, out _);
            _ = TemporaryRoomMonitorOverrides.TryRemove(roomUrl, out _);
            _ = RoomCheckSchedules.TryRemove(roomUrl, out _);
            ResetOfflineConfirmation(roomUrl);
            _ = InconclusiveLogTimestamps.TryRemove(roomUrl, out _);
            ExternalStreamResolver.ClearRoomState(roomUrl);
            WakeMonitorScheduler();
        }
    }

    public static async Task RunOnceAsync(CancellationToken token = default)
    {
        await RunRoomsAsync(Configurations.Rooms.Get() ?? [], token);
    }

    public static async Task RunRoomAsync(string roomUrl, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(roomUrl))
        {
            return;
        }

        Room[] rooms = (Configurations.Rooms.Get() ?? [])
            .Where(room => string.Equals(room.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await RunRoomsAsync(rooms, token, force: true);
    }

    internal static async Task<T> RunRoomUpdateAsync<T>(string roomUrl, Func<Task<T>> update, CancellationToken token = default)
    {
        using IDisposable roomLock = await AcquireRoomCheckLockAsync(roomUrl, token);
        return await update();
    }

    public static async Task StartAsync(CancellationToken token = default)
    {
        await StartAsync(token, Volatile.Read(ref monitorGeneration), RoutinePeriodicWait);
    }

    private static async Task StartAsync(CancellationToken token, long generation, PeriodicWait periodicWait)
    {
        await StartScheduledChecksAsync(token, generation, periodicWait);
    }

    private static async Task DisposeMonitorTaskAsync(Task? task, PeriodicWait? periodicWait)
    {
        try
        {
            if (task != null)
            {
                await task;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }
        finally
        {
            periodicWait?.Dispose();
        }
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (AggregateException e)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    private sealed record ActiveSpiderResultTask(
        Task<ISpiderResult?> Task,
        CancellationTokenSource Cancellation,
        int Priority,
        string PreferredQuality);

    private static async Task StartScheduledChecksAsync(CancellationToken token, long generation, PeriodicWait periodicWait)
    {
        while (!token.IsCancellationRequested && generation == Volatile.Read(ref monitorGeneration))
        {
            if (!await periodicWait.WaitForNextTickAsync(token)
                || generation != Volatile.Read(ref monitorGeneration))
            {
                break;
            }

            Room[] rooms = Configurations.Rooms.Get() ?? [];
            await RunRoomsAsync(rooms, token);
            periodicWait.Period = GetNextSchedulerDelay(rooms, DateTime.Now);
        }
    }

    private static async Task RunRoomsAsync(IEnumerable<Room> rooms, CancellationToken token = default, bool force = false, bool? recordingLaneOnly = null)
    {
        Room[] roomArray = rooms as Room[] ?? rooms.ToArray();
        Lazy<Task> defaultMonitoring = new(
            () => RunRoomsCoreAsync(roomArray, token, force, recordingLaneOnly),
            LazyThreadSafetyMode.ExecutionAndPublication);
        ExtensionMonitorRequest request = new(roomArray, force, recordingLaneOnly, token);
        await ExtensionHostRuntime.InvokeOverrideChainAsync<ExtensionMonitorOverride>(
            ExtensionContractNames.Monitor,
            (monitor, next) => monitor(request, next),
            () => defaultMonitoring.Value,
            AppSessionLogger.WriteException,
            exception => exception is not OperationCanceledException || !token.IsCancellationRequested);
    }

    private static async Task RunRoomsCoreAsync(IEnumerable<Room> rooms, CancellationToken token = default, bool force = false, bool? recordingLaneOnly = null)
    {
        try
        {
            bool isGlobalToNotify = Configurations.IsToNotify.Get();
            DateTime now = DateTime.Now;

            List<PendingRoomCheck> dueRooms = [];

            foreach (Room room in DistinctRoomsByUrl(rooms))
            {
                token.ThrowIfCancellationRequested();

                if (TryGetRoomStatus(room) is not RoomStatus roomStatus)
                {
                    continue;
                }

                bool isRecordingLaneRoom = UsesRecordingCheckLane(roomStatus.RecordStatus);
                if (recordingLaneOnly.HasValue && recordingLaneOnly.Value != isRecordingLaneRoom)
                {
                    continue;
                }

                RoomRecordingOptions settings = RoomRecordingSettings.Get(room);
                bool shouldNotify = isGlobalToNotify && room.IsToNotify;
                bool shouldRecord = GetEffectiveRoomRecord(room) && !IsRecordStartBlocked;
                bool shouldMonitor = GetEffectiveRoomMonitor(room) && IsRoutineScheduleActive(now, settings);

                if (shouldMonitor)
                {
                    DateTime dueAt = GetRoomCheckDueAt(room.RoomUrl, now);
                    if (!force && now < dueAt)
                    {
                        continue;
                    }

                    dueRooms.Add(new PendingRoomCheck(room, roomStatus, shouldNotify, shouldRecord, settings, dueAt));
                }
                else
                {
                    bool stateChanged = roomStatus.RecordStatus != RecordStatus.Disabled
                        || roomStatus.StreamStatus != StreamStatus.Disabled
                        || roomStatus.IsStreamCheckFailed;
                    _ = RoomCheckSchedules.TryRemove(room.RoomUrl, out _);
                    StopRecordingBecauseMonitoringDisabled(room, roomStatus);
                    roomStatus.RecordStatus = RecordStatus.Disabled;
                    roomStatus.StreamStatus = StreamStatus.Disabled;
                    roomStatus.IsStreamCheckFailed = false;
                    ResetLiveSessionMetadata(roomStatus);
                    ResetRoomCheckInconclusiveLog(room.RoomUrl);
                    if (stateChanged)
                    {
                        _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(room.RoomUrl));
                    }
                }
            }

            if (dueRooms.Count == 0)
            {
                return;
            }

            if (!force && !recordingLaneOnly.HasValue)
            {
                DispatchScheduledRoomChecks(SelectDueRooms(dueRooms, recordingLane: false), RoutineRoomCheckConcurrency, token);
                DispatchScheduledRoomChecks(SelectDueRooms(dueRooms, recordingLane: true), RecordingRoomCheckConcurrency, token);
                return;
            }

            PendingRoomCheck[] selectedRooms = dueRooms
                .Where(item => force || !ScheduledRoomChecks.ContainsKey(item.Room.RoomUrl))
                .OrderBy(item => GetRoomCheckPriority(item.RoomStatus.StreamStatus, item.RoomStatus.RecordStatus))
                .ThenBy(item => item.DueAt)
                .Take(GetRoutineBatchSize(dueRooms.Count, force, recordingLaneOnly == true))
                .ToArray();
            SemaphoreSlim semaphore = force
                ? new SemaphoreSlim(GetMonitorConcurrency(selectedRooms.Length, recordingLaneOnly == true))
                : recordingLaneOnly == true
                    ? RecordingRoomCheckConcurrency
                    : RoutineRoomCheckConcurrency;
            List<Task> tasks = new(selectedRooms.Length);
            foreach (PendingRoomCheck pending in selectedRooms)
            {
                if (!force && !ScheduledRoomChecks.TryAdd(pending.Room.RoomUrl, 1))
                {
                    continue;
                }

                Task task = RunRoomCheckWithSemaphoreAsync(
                    semaphore,
                    pending.Room,
                    pending.RoomStatus,
                    pending.ShouldNotify,
                    pending.ShouldRecord,
                    pending.Settings,
                    force,
                    token);
                tasks.Add(force ? task : CompleteScheduledRoomCheckAsync(pending.Room.RoomUrl, task));
            }

            if (force)
            {
                try
                {
                    await Task.WhenAll(tasks);
                }
                finally
                {
                    semaphore.Dispose();
                }
            }
            else
            {
                ObserveRoomCheckBatch(tasks);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            AppSessionLogger.WriteException(e);
        }
    }

    private static async Task CompleteScheduledRoomCheckAsync(string roomUrl, Task task)
    {
        try
        {
            await task;
        }
        finally
        {
            _ = ScheduledRoomChecks.TryRemove(roomUrl, out _);
            WakeMonitorScheduler();
        }
    }

    private static PendingRoomCheck[] SelectDueRooms(IEnumerable<PendingRoomCheck> dueRooms, bool recordingLane)
    {
        PendingRoomCheck[] laneRooms = dueRooms
            .Where(item => UsesRecordingCheckLane(item.RoomStatus.RecordStatus) == recordingLane)
            .Where(item => !ScheduledRoomChecks.ContainsKey(item.Room.RoomUrl))
            .OrderBy(item => GetRoomCheckPriority(item.RoomStatus.StreamStatus, item.RoomStatus.RecordStatus))
            .ThenBy(item => item.DueAt)
            .ToArray();
        return laneRooms
            .Take(GetRoutineBatchSize(laneRooms.Length, force: false, recordingLane))
            .ToArray();
    }

    private static void DispatchScheduledRoomChecks(IEnumerable<PendingRoomCheck> rooms, SemaphoreSlim semaphore, CancellationToken token)
    {
        List<Task> tasks = [];
        foreach (PendingRoomCheck pending in rooms)
        {
            if (!ScheduledRoomChecks.TryAdd(pending.Room.RoomUrl, 1))
            {
                continue;
            }

            Task task = RunRoomCheckWithSemaphoreAsync(
                semaphore,
                pending.Room,
                pending.RoomStatus,
                pending.ShouldNotify,
                pending.ShouldRecord,
                pending.Settings,
                force: false,
                token);
            tasks.Add(CompleteScheduledRoomCheckAsync(pending.Room.RoomUrl, task));
        }

        ObserveRoomCheckBatch(tasks);
    }

    private static void ObserveRoomCheckBatch(IEnumerable<Task> tasks)
    {
        foreach (Task task in tasks)
        {
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    internal static int GetMonitorConcurrency(int roomCount)
    {
        return Math.Clamp(roomCount, 1, MaximumMonitorConcurrency);
    }

    internal static int GetMonitorConcurrency(int roomCount, bool recordingLane)
    {
        return recordingLane
            ? Math.Clamp(roomCount, 1, MaximumRecordingConcurrency)
            : GetMonitorConcurrency(roomCount);
    }

    internal static int GetRoutineBatchSize(int dueRoomCount, bool force)
    {
        return GetRoutineBatchSize(dueRoomCount, force, false);
    }

    internal static int GetRoutineBatchSize(int dueRoomCount, bool force, bool recordingLane)
    {
        if (dueRoomCount <= 0)
        {
            return 0;
        }

        int maximumBatchSize = recordingLane ? MaximumRecordingBatchSize : MaximumBatchSize;
        return force ? dueRoomCount : Math.Min(dueRoomCount, maximumBatchSize);
    }

    internal static bool UsesRecordingCheckLane(RecordStatus recordStatus)
    {
        return recordStatus == RecordStatus.Recording;
    }

    internal static bool ShouldAllowDouyinWebViewFallback(bool force, bool prioritizeDouyin)
    {
        return force || prioritizeDouyin;
    }

    internal static TimeSpan GetRoomCheckTimeout(bool force)
    {
        return force ? ForcedRoomCheckTimeout : RoutineRoomCheckTimeout;
    }

    internal static bool ShouldRunSelectedRoomCheck(DateTime dueAt, bool force, DateTime now)
    {
        return force || now >= dueAt;
    }

    internal static bool ShouldLogRoomCheckDispatchDelay(DateTime dueAt, DateTime startedAt)
    {
        return startedAt - dueAt > RoomCheckDispatchDelayWarning;
    }

    internal static int GetRoomCheckPriority(StreamStatus streamStatus, RecordStatus recordStatus)
    {
        if (recordStatus == RecordStatus.Recording)
        {
            return 0;
        }

        return streamStatus == StreamStatus.Streaming ? 1 : 2;
    }

    internal static int RoomCheckLockCount
    {
        get
        {
            lock (RoomCheckLocksSync)
            {
                return RoomCheckLocks.Count;
            }
        }
    }

    private static async Task<IDisposable> AcquireRoomCheckLockAsync(string roomUrl, CancellationToken token)
    {
        RoomCheckGate gate;
        lock (RoomCheckLocksSync)
        {
            if (!RoomCheckLocks.TryGetValue(roomUrl, out gate!))
            {
                gate = new RoomCheckGate();
                RoomCheckLocks[roomUrl] = gate;
            }
            gate.ReferenceCount++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(token);
            return new RoomCheckLease(roomUrl, gate);
        }
        catch
        {
            lock (RoomCheckLocksSync)
            {
                gate.ReferenceCount--;
                if (gate.ReferenceCount == 0
                    && RoomCheckLocks.TryGetValue(roomUrl, out RoomCheckGate? current)
                    && ReferenceEquals(current, gate))
                {
                    RoomCheckLocks.Remove(roomUrl);
                    gate.Semaphore.Dispose();
                }
            }
            throw;
        }
    }

    internal static bool IsCurrentRoomStatus(string roomUrl, RoomStatus roomStatus)
    {
        return RoomStatus.TryGetValue(roomUrl, out RoomStatus? current)
            && ReferenceEquals(current, roomStatus);
    }

    private static async Task RunRoomCheckWithSemaphoreAsync(SemaphoreSlim semaphore, Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, bool force, CancellationToken token)
    {
        await semaphore.WaitAsync(token);
        IDisposable? roomLock = null;
        StreamStatus previousStreamStatus = default;
        bool ranCheck = false;

        try
        {
            roomLock = await AcquireRoomCheckLockAsync(room.RoomUrl, token);
            DateTime startedAt = DateTime.Now;
            DateTime dueAt = GetRoomCheckDueAt(room.RoomUrl, startedAt);
            if (!ShouldRunSelectedRoomCheck(dueAt, force, startedAt))
            {
                return;
            }

            LogRoomCheckDispatchDelay(room, dueAt, startedAt, force);
            previousStreamStatus = roomStatus.StreamStatus;
            ReserveRoomCheck(room.RoomUrl, settings, roomStatus.StreamStatus, startedAt);
            ranCheck = true;
            await RunRoomCheckAsync(room, roomStatus, shouldNotify, shouldRecord, settings, force, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            AppSessionLogger.WriteException(e);
            AppSessionLogger.Event("error", "business", "room_check_failed", e.Message, new
            {
                room.RoomUrl,
                room.NickName,
            });
        }
        finally
        {
            if (roomLock != null)
            {
                if (ranCheck && IsCurrentRoomStatus(room.RoomUrl, roomStatus))
                {
                    DateTime completedAt = DateTime.Now;
                    if (GetEffectiveRoomMonitor(room) && IsRoutineScheduleActive(completedAt, settings))
                    {
                        UpdateRoomCheckSchedule(room.RoomUrl, previousStreamStatus, roomStatus.StreamStatus, settings, completedAt);
                    }
                    else
                    {
                        _ = RoomCheckSchedules.TryRemove(room.RoomUrl, out _);
                    }
                    _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(room.RoomUrl));
                }
                roomLock.Dispose();
            }
            semaphore.Release();
        }
    }

    private static async Task RunRoomCheckAsync(Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, bool force, CancellationToken token)
    {
        SyncRecordStatus(roomStatus);
        bool prioritizeDouyin = roomStatus.StreamStatus == StreamStatus.Streaming
            || roomStatus.RecordStatus == RecordStatus.Recording;
        long currentTimestamp = Environment.TickCount64;
        ISpiderResult? spiderResult;
        if (ShouldReuseResolvedDouyinStream(
                force,
                roomStatus.PlatformName,
                roomStatus.StreamStatus,
                HasRecordableStream(roomStatus))
            && await IsPreservedStreamReachableAsync(roomStatus, token))
        {
            spiderResult = CreatePreservedStreamResult(room, roomStatus);
        }
        else
        {
            spiderResult = await GetSpiderResultAsync(room, settings, prioritizeDouyin, force, token);
        }
        token.ThrowIfCancellationRequested();
        shouldRecord = GetEffectiveRoomRecord(room) && !IsRecordStartBlocked;

        if (!IsCurrentRoomStatus(room.RoomUrl, roomStatus))
        {
            return;
        }

        if (!GetEffectiveRoomMonitor(room) || !IsRoutineScheduleActive(DateTime.Now, settings))
        {
            _ = RoomCheckSchedules.TryRemove(room.RoomUrl, out _);
            StopRecordingBecauseMonitoringDisabled(room, roomStatus);
            roomStatus.RecordStatus = RecordStatus.Disabled;
            roomStatus.StreamStatus = StreamStatus.Disabled;
            roomStatus.IsStreamCheckFailed = false;
            ResetLiveSessionMetadata(roomStatus);
            ResetRoomCheckInconclusiveLog(room.RoomUrl);
            return;
        }

        if (spiderResult == null)
        {
            roomStatus.IsStreamCheckFailed = true;
            SyncRecordStatus(roomStatus);
            bool preservedStreamReachable = false;
            if (ShouldProbePreservedStreamOnInconclusive(roomStatus.PlatformName, roomStatus.StreamStatus, roomStatus.RecordStatus, HasRecordableStream(roomStatus)))
            {
                if (await IsPreservedStreamReachableAsync(roomStatus, token))
                {
                    preservedStreamReachable = true;
                    roomStatus.StreamStatus = StreamStatus.Streaming;
                    roomStatus.IsStreamCheckFailed = false;
                    ResetOfflineConfirmation(room.RoomUrl);
                    ResetRoomCheckInconclusiveLog(room.RoomUrl);
                }
            }
            if (roomStatus.StreamStatus != StreamStatus.Streaming)
            {
                ResetLiveSessionMetadata(roomStatus);
            }
            if (!shouldRecord)
            {
                StopRecordingBecauseDisabled(room, roomStatus);
                roomStatus.RecordStatus = RecordStatus.Disabled;
            }
            else if (ShouldStartFromPreservedDouyinStream(
                shouldRecord,
                roomStatus.PlatformName,
                roomStatus.StreamStatus,
                HasRecordableStream(roomStatus),
                preservedStreamReachable))
            {
                _ = StartRecorderIfNeeded(room, roomStatus, settings, isLiveStreaming: true, usingPreservedStream: true);
            }
            else if (roomStatus.RecordStatus != RecordStatus.Recording)
            {
                roomStatus.RecordStatus = RecordStatus.NotRecording;
            }

            if (TryAcquireInconclusiveLog(room.RoomUrl, Environment.TickCount64))
            {
                AppSessionLogger.Event("warn", "business", "room_check_inconclusive", "room check returned no result and the previous stream state was preserved", new
                {
                    room.RoomUrl,
                    room.NickName,
                    roomStatus.PlatformName,
                    roomStatus.StreamStatus,
                    roomStatus.RecordStatus,
                    resolverError = ExternalStreamResolver.GetLastError(room.RoomUrl),
                });
            }
            return;
        }

        roomStatus.IsStreamCheckFailed = !StreamResolver.HasConclusiveData(spiderResult);

        StreamStatus prevStreamStatus = roomStatus.StreamStatus;

        if (ShouldRefreshFixedRoomMetadata(roomStatus.FixedMetadataRefreshTimestamp, currentTimestamp))
        {
            bool fixedMetadataChanged = false;
            if (!string.IsNullOrWhiteSpace(spiderResult.Nickname))
            {
                roomStatus.NickName = spiderResult.Nickname;
                if (!string.Equals(room.NickName, spiderResult.Nickname, StringComparison.Ordinal))
                {
                    room.NickName = spiderResult.Nickname;
                    fixedMetadataChanged = true;
                }
            }
            if (!string.IsNullOrWhiteSpace(spiderResult.AvatarThumbUrl))
            {
                bool updateAvatar = string.IsNullOrWhiteSpace(roomStatus.AvatarLocalPath) ||
                    !string.Equals(roomStatus.AvatarThumbUrl, spiderResult.AvatarThumbUrl, StringComparison.Ordinal);
                roomStatus.AvatarThumbUrl = spiderResult.AvatarThumbUrl;
                if (!string.Equals(room.AvatarThumbUrl, spiderResult.AvatarThumbUrl, StringComparison.Ordinal))
                {
                    room.AvatarThumbUrl = spiderResult.AvatarThumbUrl;
                    fixedMetadataChanged = true;
                }
                if (updateAvatar)
                {
                    roomStatus.AvatarLocalPath = await AvatarCache.UpdateAsync(room.RoomUrl, spiderResult.AvatarThumbUrl, token);
                }
            }
            else if (string.IsNullOrWhiteSpace(roomStatus.AvatarLocalPath))
            {
                roomStatus.AvatarLocalPath = AvatarCache.GetCachedAvatarSource(room.RoomUrl);
            }
            roomStatus.PlatformName = string.IsNullOrWhiteSpace(spiderResult.PlatformName)
                ? Spider.GetPlatformName(room.RoomUrl)
                : spiderResult.PlatformName;
            if (!string.Equals(room.PlatformName, roomStatus.PlatformName, StringComparison.Ordinal))
            {
                room.PlatformName = roomStatus.PlatformName;
                fixedMetadataChanged = true;
            }
            if (!string.IsNullOrWhiteSpace(spiderResult.Uid))
            {
                roomStatus.Uid = spiderResult.Uid;
                if (!string.Equals(room.Uid, spiderResult.Uid, StringComparison.Ordinal))
                {
                    room.Uid = spiderResult.Uid;
                    fixedMetadataChanged = true;
                }
            }
            roomStatus.FixedMetadataRefreshTimestamp = currentTimestamp;
            if (fixedMetadataChanged)
            {
                ConfigurationSaveScheduler.Request();
            }
        }
        string? liveTitle = SpiderResultMetadata.GetTitle(spiderResult);
        string? quality = SpiderResultMetadata.GetQuality(spiderResult);
        string? resolution = SpiderResultMetadata.GetResolution(spiderResult);
        string? bitrate = SpiderResultMetadata.GetBitrate(spiderResult);
        string? headers = SpiderResultMetadata.GetHeaders(spiderResult);
        bool hasFreshStream = HasRecordableStream(spiderResult);
        bool deferOffline = ShouldDeferOffline(room, roomStatus, spiderResult.IsLiveStreaming, hasFreshStream);
        bool? resolvedLiveState = deferOffline ? null : spiderResult.IsLiveStreaming;
        StreamStatus nextStreamStatus = ResolveStreamStatus(roomStatus.StreamStatus, resolvedLiveState, hasFreshStream);
        if (IsConclusiveRoomCheck(resolvedLiveState, hasFreshStream))
        {
            ResetRoomCheckInconclusiveLog(room.RoomUrl);
        }
        if (nextStreamStatus == StreamStatus.Streaming)
        {
            ApplyLiveSessionMetadata(roomStatus, liveTitle, quality, resolution);
        }
        else if (resolvedLiveState == false)
        {
            roomStatus.LiveTitle = string.Empty;
            roomStatus.Quality = string.Empty;
            roomStatus.Resolution = string.Empty;
            roomStatus.Bitrate = string.Empty;
            ResetLiveSessionMetadata(roomStatus);
        }
        if (nextStreamStatus == StreamStatus.Streaming)
        {
            roomStatus.Bitrate = bitrate ?? roomStatus.Bitrate;
        }
        ApplyStreamConnectionMetadata(
            roomStatus,
            spiderResult.FlvUrl,
            spiderResult.HlsUrl,
            spiderResult.RecordUrl,
            headers,
            resolvedLiveState,
            hasFreshStream);
        ApplyReferenceStream(roomStatus, spiderResult.ReferenceUrl, resolvedLiveState, hasFreshStream);

        SyncRecordStatus(roomStatus);
        roomStatus.StreamStatus = nextStreamStatus;
        if (resolvedLiveState == false && roomStatus.StreamStatus == StreamStatus.NotStreaming)
        {
            ResetRoomRecordSessionState(room.RoomUrl);
            shouldRecord = GetEffectiveRoomRecord(room) && !IsRecordStartBlocked;
        }
        bool isLiveStreaming = roomStatus.StreamStatus == StreamStatus.Streaming;

        if (prevStreamStatus != roomStatus.StreamStatus)
        {
            AppSessionLogger.Event("info", "business", "room_stream_state_changed", "room stream state changed", new
            {
                room.RoomUrl,
                room.NickName,
                previous = prevStreamStatus,
                current = roomStatus.StreamStatus,
                result = resolvedLiveState,
                hasFreshStream,
                roomStatus.RecordStatus,
            });
        }

        if (shouldRecord)
        {
            if (isLiveStreaming && hasFreshStream && HasRecordableStream(roomStatus))
            {
                if (!StartRecorderIfNeeded(room, roomStatus, settings, isLiveStreaming, usingPreservedStream: false))
                {
                    return;
                }
            }
            else if (ShouldStopRecorderAfterRoomCheck(isLiveStreaming, roomStatus.RecordStatus))
            {
                AppSessionLogger.Event("info", "business", "record_stop_requested", "record stop requested because live ended", new
                {
                    room.RoomUrl,
                    room.NickName,
                    roomStatus.PlatformName,
                    roomStatus.RecordStatus,
                    isLiveStreaming,
                    hasRecordUrl = !string.IsNullOrWhiteSpace(roomStatus.RecordUrl),
                    hasFlvUrl = !string.IsNullOrWhiteSpace(roomStatus.FlvUrl),
                    hasHlsUrl = !string.IsNullOrWhiteSpace(roomStatus.HlsUrl),
                });
                roomStatus.Recorder.Stop();
                roomStatus.RecordStatus = RecordStatus.NotRecording;
            }
            else if (roomStatus.RecordStatus != RecordStatus.Recording)
            {
                roomStatus.RecordStatus = RecordStatus.NotRecording;
            }
        }
        else
        {
            StopRecordingBecauseDisabled(room, roomStatus);
            roomStatus.RecordStatus = RecordStatus.Disabled;
        }

        if (shouldNotify && prevStreamStatus != StreamStatus.Streaming && isLiveStreaming)
        {
            await Notify(room, token);
        }
    }

    private static async Task<ISpiderResult?> GetSpiderResultAsync(Room room, RoomRecordingOptions settings, bool prioritizeDouyin, bool force, CancellationToken token)
    {
        TimeSpan timeout = GetRoomCheckTimeout(force);
        int requestPriority = force ? 1 : 0;
        using CancellationTokenSource timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCancellation.CancelAfter(timeout);
        bool allowDouyinWebViewFallback = ShouldAllowDouyinWebViewFallback(force, prioritizeDouyin);
        ActiveSpiderResultTask activeTask = StartSpiderResultTask(
            room.RoomUrl,
            settings.PreferredStreamQuality,
            bypassDouyinThrottle: false,
            prioritizeDouyin,
            allowDouyinWebViewFallback,
            requestPriority,
            token);
        try
        {
            return await activeTask.Task.WaitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timeoutCancellation.IsCancellationRequested)
        {
            CancelSpiderResultTask(room.RoomUrl, activeTask, requestPriority);
            AppSessionLogger.Event("warn", "business", "room_check_timeout", "room check timed out and the room lock was released", new
            {
                room.RoomUrl,
                room.NickName,
                timeoutSeconds = timeout.TotalSeconds,
                force,
            });
            return null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static async Task<ISpiderResult?> GetManualSpiderResultAsync(string roomUrl, string? preferredQuality, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(roomUrl))
        {
            return null;
        }

        using CancellationTokenSource timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCancellation.CancelAfter(ForcedRoomCheckTimeout);
        ActiveSpiderResultTask activeTask = StartSpiderResultTask(
            roomUrl,
            preferredQuality,
            bypassDouyinThrottle: true,
            prioritizeDouyin: true,
            allowDouyinWebViewFallback: true,
            priority: 2,
            token);
        try
        {
            return await activeTask.Task.WaitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timeoutCancellation.IsCancellationRequested)
        {
            CancelSpiderResultTask(roomUrl, activeTask, 2);
            AppSessionLogger.Event("warn", "business", "manual_room_check_timeout", "manual room check timed out", new
            {
                RoomUrl = roomUrl,
                timeoutSeconds = ForcedRoomCheckTimeout.TotalSeconds,
            });
            return null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }

    private static ActiveSpiderResultTask StartSpiderResultTask(
        string roomUrl,
        string? preferredQuality,
        bool bypassDouyinThrottle,
        bool prioritizeDouyin,
        bool allowDouyinWebViewFallback,
        int priority,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string normalizedQuality = preferredQuality ?? string.Empty;
        ActiveSpiderResultTask createdTask;
        lock (ActiveSpiderResultTasksSync)
        {
            if (ActiveSpiderResultTasks.TryGetValue(roomUrl, out ActiveSpiderResultTask? activeTask))
            {
                if (CanReuseSpiderResultTask(activeTask.Priority, activeTask.PreferredQuality, priority, normalizedQuality))
                {
                    return activeTask;
                }

                activeTask.Cancellation.Cancel();
                ActiveSpiderResultTasks.Remove(roomUrl);
            }

            CancellationTokenSource workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<ISpiderResult?> workerTask = Task.Factory.StartNew(
                () => Spider.GetResult(roomUrl, preferredQuality, bypassDouyinThrottle, prioritizeDouyin, allowDouyinWebViewFallback, workerCancellation.Token),
                workerCancellation.Token,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            createdTask = new ActiveSpiderResultTask(workerTask, workerCancellation, priority, normalizedQuality);
            ActiveSpiderResultTasks[roomUrl] = createdTask;
        }

        _ = createdTask.Task.ContinueWith(
            completed => CompleteSpiderResultTask(roomUrl, createdTask, completed),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return createdTask;
    }

    internal static bool CanReuseSpiderResultTask(int activePriority, string? activeQuality, int requestedPriority, string? requestedQuality)
    {
        return activePriority >= requestedPriority
            && string.Equals(activeQuality ?? string.Empty, requestedQuality ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static void CancelSpiderResultTask(string roomUrl, ActiveSpiderResultTask activeTask, int requestPriority)
    {
        lock (ActiveSpiderResultTasksSync)
        {
            if (ActiveSpiderResultTasks.TryGetValue(roomUrl, out ActiveSpiderResultTask? current)
                && ReferenceEquals(current, activeTask)
                && current.Priority <= requestPriority)
            {
                activeTask.Cancellation.Cancel();
            }
        }
    }

    private static void CompleteSpiderResultTask(string roomUrl, ActiveSpiderResultTask activeTask, Task<ISpiderResult?> completedTask)
    {
        if (completedTask.IsFaulted)
        {
            _ = completedTask.Exception;
        }
        lock (ActiveSpiderResultTasksSync)
        {
            if (ActiveSpiderResultTasks.TryGetValue(roomUrl, out ActiveSpiderResultTask? current)
                && ReferenceEquals(current, activeTask))
            {
                ActiveSpiderResultTasks.Remove(roomUrl);
            }
        }
        activeTask.Cancellation.Dispose();
    }

    private static void LogRoomCheckDispatchDelay(Room room, DateTime dueAt, DateTime startedAt, bool force)
    {
        if (!ShouldLogRoomCheckDispatchDelay(dueAt, startedAt))
        {
            return;
        }

        AppSessionLogger.Event("warn", "business", "room_check_dispatch_delayed", "room check started later than its due time", new
        {
            room.RoomUrl,
            room.NickName,
            delaySeconds = (startedAt - dueAt).TotalSeconds,
            force,
        });
    }

    public static void RefreshRoutineInterval()
    {
        RefreshRoutineSchedules(DateTime.Now);
        WakeMonitorScheduler();
    }

    private static void WakeMonitorScheduler()
    {
        try
        {
            RoutinePeriodicWait.Wake();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void RefreshRoutineSchedules(DateTime now)
    {
        Dictionary<string, Room> rooms = (Configurations.Rooms.Get() ?? [])
            .Where(room => room != null && !string.IsNullOrWhiteSpace(room.RoomUrl))
            .GroupBy(room => room.RoomUrl, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach ((string roomUrl, RoomCheckScheduleState state) in RoomCheckSchedules)
        {
            if (!rooms.TryGetValue(roomUrl, out Room? room) || !GetEffectiveRoomMonitor(room))
            {
                _ = RoomCheckSchedules.TryRemove(roomUrl, out _);
                continue;
            }

            if (!RoomStatus.TryGetValue(roomUrl, out RoomStatus? roomStatus))
            {
                continue;
            }

            RoomRecordingOptions settings = RoomRecordingSettings.Get(room);
            lock (state)
            {
                if (state.NextCheckAt == DateTime.MinValue || roomStatus.StreamStatus == StreamStatus.Streaming)
                {
                    continue;
                }

                if (state.LastClosedAt is DateTime closedAt && now - closedAt < RecentlyClosedWindow)
                {
                    DateTime recentlyClosedNextCheck = now + RecentlyClosedInterval;
                    if (state.NextCheckAt > recentlyClosedNextCheck)
                    {
                        state.NextCheckAt = recentlyClosedNextCheck;
                    }
                    continue;
                }

                state.NextCheckAt = now + TimeSpan.FromMilliseconds(MonitorTiming.NormalizeRoutineInterval(settings.RoutineInterval));
            }
        }
    }

    internal static TimeSpan GetRoutinePeriod()
    {
        return TimeSpan.FromMilliseconds(Math.Min(DefaultSchedulerPeriodMilliseconds, GetEffectiveRoutineInterval()));
    }

    internal static TimeSpan GetNextSchedulerDelay(IEnumerable<Room> rooms, DateTime now)
    {
        DateTime? nextCheckAt = null;
        foreach (Room room in DistinctRoomsByUrl(rooms))
        {
            if (!GetEffectiveRoomMonitor(room))
            {
                continue;
            }

            RoomRecordingOptions settings = RoomRecordingSettings.Get(room);
            DateTime? scheduleTransition = GetNextRoutineScheduleTransition(now, settings);
            if (scheduleTransition.HasValue
                && (!nextCheckAt.HasValue || scheduleTransition.Value < nextCheckAt.Value))
            {
                nextCheckAt = scheduleTransition;
            }

            if (!IsRoutineScheduleActive(now, settings))
            {
                continue;
            }

            if (ScheduledRoomChecks.ContainsKey(room.RoomUrl))
            {
                continue;
            }

            DateTime dueAt = GetRoomCheckDueAt(room.RoomUrl, now);
            if (!nextCheckAt.HasValue || dueAt < nextCheckAt.Value)
            {
                nextCheckAt = dueAt;
            }
        }

        if (!nextCheckAt.HasValue)
        {
            return MaximumSchedulerDelay;
        }

        TimeSpan delay = nextCheckAt.Value - now;
        if (delay <= TimeSpan.Zero)
        {
            return TimeSpan.FromMilliseconds(1);
        }

        return delay > MaximumSchedulerDelay ? MaximumSchedulerDelay : delay;
    }

    internal static DateTime? GetNextRoutineScheduleActivation(DateTime now, RoomRecordingOptions settings)
    {
        int mode = Math.Clamp(settings.RoutineScheduleMode, 0, 4);
        if (mode == 0 || IsRoutineScheduleActive(now, settings))
        {
            return now;
        }

        if (mode is 1 or 2)
        {
            for (int offset = 1; offset <= 7; offset++)
            {
                DateTime candidate = now.Date.AddDays(offset);
                bool enabled = mode == 1
                    ? candidate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                    : candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                if (enabled)
                {
                    return candidate;
                }
            }
        }

        if (mode == 3)
        {
            DateTime evening = now.Date.AddHours(18);
            return evening > now ? evening : evening.AddDays(1);
        }

        HashSet<string> enabledDays = settings.RoutineScheduleDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        TimeSpan start = new(
            Math.Clamp(settings.RoutineScheduleStartHour, 0, 23),
            Math.Clamp(settings.RoutineScheduleStartMinute, 0, 59),
            0);
        TimeSpan end = new(
            Math.Clamp(settings.RoutineScheduleEndHour, 0, 23),
            Math.Clamp(settings.RoutineScheduleEndMinute, 0, 59),
            0);

        for (int offset = 0; offset <= 7; offset++)
        {
            DateTime date = now.Date.AddDays(offset);
            if (!enabledDays.Contains(date.DayOfWeek.ToString()))
            {
                continue;
            }

            if (start <= end)
            {
                DateTime candidate = date.Add(start);
                if (candidate > now)
                {
                    return candidate;
                }
                continue;
            }

            if (offset > 0)
            {
                return date;
            }

            DateTime eveningCandidate = date.Add(start);
            if (eveningCandidate > now)
            {
                return eveningCandidate;
            }
        }

        return null;
    }

    internal static DateTime? GetNextRoutineScheduleTransition(DateTime now, RoomRecordingOptions settings)
    {
        int mode = Math.Clamp(settings.RoutineScheduleMode, 0, 4);
        if (mode == 0)
        {
            return null;
        }

        if (!IsRoutineScheduleActive(now, settings))
        {
            return GetNextRoutineScheduleActivation(now, settings);
        }

        if (mode is 1 or 2)
        {
            for (int offset = 1; offset <= 7; offset++)
            {
                DateTime candidate = now.Date.AddDays(offset);
                bool enabled = mode == 1
                    ? candidate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                    : candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                if (!enabled)
                {
                    return candidate;
                }
            }
        }

        if (mode == 3)
        {
            DateTime end = now.TimeOfDay >= TimeSpan.FromHours(18)
                ? now.Date.AddDays(1).AddHours(8)
                : now.Date.AddHours(8);
            return end.AddTicks(1);
        }

        HashSet<string> enabledDays = settings.RoutineScheduleDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        TimeSpan start = new(
            Math.Clamp(settings.RoutineScheduleStartHour, 0, 23),
            Math.Clamp(settings.RoutineScheduleStartMinute, 0, 59),
            0);
        TimeSpan endTime = new(
            Math.Clamp(settings.RoutineScheduleEndHour, 0, 23),
            Math.Clamp(settings.RoutineScheduleEndMinute, 0, 59),
            0);

        if (start <= endTime || now.TimeOfDay <= endTime)
        {
            return now.Date.Add(endTime).AddTicks(1);
        }

        DateTime tomorrow = now.Date.AddDays(1);
        return enabledDays.Contains(tomorrow.DayOfWeek.ToString())
            ? tomorrow.Add(endTime).AddTicks(1)
            : tomorrow;
    }

    internal static int GetEffectiveRoutineInterval()
    {
        Room[] rooms = Configurations.Rooms.Get() ?? [];
        int interval = MonitorTiming.NormalizeRoutineInterval(Configurations.RoutineInterval.Get());

        foreach (Room room in rooms)
        {
            if (room == null || !GetEffectiveRoomMonitor(room))
            {
                continue;
            }

            RoomRecordingOptions settings = RoomRecordingSettings.Get(room);
            interval = Math.Min(interval, MonitorTiming.NormalizeRoutineInterval(settings.RoutineInterval));
        }

        return interval;
    }

    private static DateTime GetRoomCheckDueAt(string roomUrl, DateTime now)
    {
        RoomCheckScheduleState state = RoomCheckSchedules.GetOrAdd(roomUrl, _ => new RoomCheckScheduleState());
        lock (state)
        {
            if (state.NextCheckAt == DateTime.MinValue)
            {
                state.NextCheckAt = now;
            }

            return state.NextCheckAt;
        }
    }

    private static void ReserveRoomCheck(string roomUrl, RoomRecordingOptions settings, StreamStatus streamStatus, DateTime now)
    {
        RoomCheckScheduleState state = RoomCheckSchedules.GetOrAdd(roomUrl, _ => new RoomCheckScheduleState());
        lock (state)
        {
            state.NextCheckAt = now + GetFallbackInterval(streamStatus, settings.RoutineInterval, state.LastClosedAt, now);
        }
    }

    private static void UpdateRoomCheckSchedule(string roomUrl, StreamStatus previousStatus, StreamStatus currentStatus, RoomRecordingOptions settings, DateTime now)
    {
        RoomCheckScheduleState state = RoomCheckSchedules.GetOrAdd(roomUrl, _ => new RoomCheckScheduleState());
        lock (state)
        {
            if (currentStatus == StreamStatus.Streaming)
            {
                state.LastClosedAt = null;
                state.NextCheckAt = now + GetStreamingFollowUpInterval(OfflineConfirmationChecks.ContainsKey(roomUrl));
                return;
            }

            if (previousStatus == StreamStatus.Streaming && currentStatus == StreamStatus.NotStreaming)
            {
                state.LastClosedAt = now;
            }

            state.NextCheckAt = now + GetFallbackInterval(currentStatus, settings.RoutineInterval, state.LastClosedAt, now);
        }
    }

    internal static TimeSpan GetFallbackInterval(StreamStatus streamStatus, int routineInterval, DateTime? lastClosedAt, DateTime now)
    {
        if (streamStatus == StreamStatus.Streaming)
        {
            return StreamingCycleInterval;
        }

        if (lastClosedAt is DateTime closedAt && now - closedAt < RecentlyClosedWindow)
        {
            return RecentlyClosedInterval;
        }

        return TimeSpan.FromMilliseconds(MonitorTiming.NormalizeRoutineInterval(routineInterval));
    }

    internal static TimeSpan GetStreamingFollowUpInterval(bool offlineConfirmationPending)
    {
        return StreamingCycleInterval;
    }

    private static bool HasRecordableStream(RoomStatus roomStatus)
    {
        return !string.IsNullOrWhiteSpace(roomStatus.RecordUrl)
            || !string.IsNullOrWhiteSpace(roomStatus.HlsUrl)
            || !string.IsNullOrWhiteSpace(roomStatus.FlvUrl);
    }

    internal static bool ShouldRefreshFixedRoomMetadata(long? lastRefreshTimestamp, long currentTimestamp)
    {
        return !lastRefreshTimestamp.HasValue
            || currentTimestamp < lastRefreshTimestamp.Value
            || currentTimestamp - lastRefreshTimestamp.Value >= FixedRoomMetadataRefreshIntervalMilliseconds;
    }

    internal static bool ShouldLogRoomCheckInconclusive(long? lastLogTimestamp, long currentTimestamp)
    {
        return !lastLogTimestamp.HasValue
            || currentTimestamp < lastLogTimestamp.Value
            || currentTimestamp - lastLogTimestamp.Value >= InconclusiveLogIntervalMilliseconds;
    }

    internal static bool IsConclusiveRoomCheck(bool? resolvedLiveState, bool hasFreshStream)
    {
        return resolvedLiveState.HasValue || hasFreshStream;
    }

    internal static void ApplyLiveSessionMetadata(
        RoomStatus roomStatus,
        string? liveTitle,
        string? quality,
        string? resolution)
    {
        if (!roomStatus.IsLiveSessionMetadataInitialized)
        {
            roomStatus.LiveTitle = string.Empty;
            roomStatus.Quality = string.Empty;
            roomStatus.Resolution = string.Empty;
            roomStatus.IsLiveTitleLoaded = false;
            roomStatus.IsQualityLoaded = false;
            roomStatus.IsResolutionLoaded = false;
            roomStatus.IsLiveSessionMetadataInitialized = true;
        }

        if (!roomStatus.IsLiveTitleLoaded && !string.IsNullOrWhiteSpace(liveTitle))
        {
            roomStatus.LiveTitle = liveTitle;
            roomStatus.IsLiveTitleLoaded = true;
        }
        if (!roomStatus.IsQualityLoaded && !string.IsNullOrWhiteSpace(quality))
        {
            roomStatus.Quality = quality;
            roomStatus.IsQualityLoaded = true;
        }
        if (!roomStatus.IsResolutionLoaded && !string.IsNullOrWhiteSpace(resolution))
        {
            roomStatus.Resolution = resolution;
            roomStatus.IsResolutionLoaded = true;
        }
    }

    internal static void ResetLiveSessionMetadata(RoomStatus roomStatus)
    {
        roomStatus.IsLiveSessionMetadataInitialized = false;
        roomStatus.IsLiveTitleLoaded = false;
        roomStatus.IsQualityLoaded = false;
        roomStatus.IsResolutionLoaded = false;
    }

    internal static void ResetRoomCheckInconclusiveLog(string roomUrl)
    {
        _ = InconclusiveLogTimestamps.TryRemove(roomUrl, out _);
    }

    internal static bool TryAcquireInconclusiveLog(string roomUrl, long currentTimestamp)
    {
        while (true)
        {
            if (!InconclusiveLogTimestamps.TryGetValue(roomUrl, out long previousTimestamp))
            {
                if (InconclusiveLogTimestamps.TryAdd(roomUrl, currentTimestamp))
                {
                    return true;
                }
                continue;
            }

            if (!ShouldLogRoomCheckInconclusive(previousTimestamp, currentTimestamp))
            {
                return false;
            }

            if (InconclusiveLogTimestamps.TryUpdate(roomUrl, currentTimestamp, previousTimestamp))
            {
                return true;
            }
        }
    }

    private static bool HasRecordableStream(ISpiderResult spiderResult)
    {
        return !string.IsNullOrWhiteSpace(spiderResult.RecordUrl)
            || !string.IsNullOrWhiteSpace(spiderResult.HlsUrl)
            || !string.IsNullOrWhiteSpace(spiderResult.FlvUrl);
    }

    internal static bool ShouldStartFromPreservedDouyinStream(
        bool shouldRecord,
        string platformName,
        StreamStatus streamStatus,
        bool hasRecordableStream,
        bool isReachable)
    {
        return shouldRecord
            && IsDouyinPlatform(platformName)
            && streamStatus == StreamStatus.Streaming
            && hasRecordableStream
            && isReachable;
    }

    internal static bool ShouldReuseResolvedDouyinStream(
        bool force,
        string platformName,
        StreamStatus streamStatus,
        bool hasRecordableStream)
    {
        return !force
            && IsDouyinPlatform(platformName)
            && streamStatus == StreamStatus.Streaming
            && hasRecordableStream;
    }

    internal static StreamResolverResult CreatePreservedStreamResult(Room room, RoomStatus roomStatus)
    {
        return new StreamResolverResult
        {
            RoomUrl = room.RoomUrl,
            PlatformName = roomStatus.PlatformName,
            IsLiveStreaming = true,
            Nickname = roomStatus.NickName,
            AvatarThumbUrl = roomStatus.AvatarThumbUrl,
            FlvUrl = roomStatus.FlvUrl,
            HlsUrl = roomStatus.HlsUrl,
            RecordUrl = roomStatus.RecordUrl,
            ReferenceUrl = roomStatus.ReferenceUrl,
            Title = roomStatus.LiveTitle,
            Quality = roomStatus.Quality,
            Uid = roomStatus.Uid,
            Resolution = roomStatus.Resolution,
            Bitrate = roomStatus.Bitrate,
            Headers = roomStatus.Headers,
        };
    }

    internal static bool ShouldProbePreservedDouyinStream(string platformName, StreamStatus streamStatus, bool hasRecordableStream)
    {
        return IsDouyinPlatform(platformName)
            && streamStatus == StreamStatus.Streaming
            && hasRecordableStream;
    }

    internal static bool ShouldProbePreservedStreamOnInconclusive(string platformName, StreamStatus streamStatus, RecordStatus recordStatus, bool hasRecordableStream)
    {
        return IsDouyinPlatform(platformName)
            && hasRecordableStream
            && (streamStatus == StreamStatus.Streaming || recordStatus == RecordStatus.Recording);
    }

    private static async Task<bool> IsPreservedStreamReachableAsync(RoomStatus roomStatus, CancellationToken token)
    {
        string url = FirstNonEmpty(roomStatus.RecordUrl, roomStatus.FlvUrl, roomStatus.HlsUrl);
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", Configurations.UserAgent.Get());
        request.Headers.Referrer = new Uri("https://live.douyin.com/");

        try
        {
            using HttpResponseMessage response = await ProxyHttpClientPool.GetCurrent().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    internal static StreamStatus ResolveInitialStreamStatus(string platformName, string recordUrl, string flvUrl, string hlsUrl, DateTime now)
    {
        return StreamStatus.Initialized;
    }

    private static bool ShouldDeferOffline(Room room, RoomStatus roomStatus, bool? isLiveStreaming, bool hasFreshStream)
    {
        return ShouldDeferOffline(
            room.RoomUrl,
            room.NickName,
            roomStatus,
            isLiveStreaming,
            hasFreshStream);
    }

    private static bool ShouldDeferOffline(
        string roomUrl,
        string nickName,
        RoomStatus roomStatus,
        bool? isLiveStreaming,
        bool hasFreshStream)
    {
        if (!ShouldConfirmOffline(roomStatus.StreamStatus, roomStatus.RecordStatus)
            || isLiveStreaming != false
            || hasFreshStream)
        {
            ResetOfflineConfirmation(roomUrl);
            return false;
        }

        if (IsWithinRecordingStartupOfflineGuard(roomStatus, DateTime.Now))
        {
            ResetOfflineConfirmation(roomUrl);
            AppSessionLogger.Event("info", "business", "room_startup_offline_deferred", "offline result was deferred during recording startup", new
            {
                RoomUrl = roomUrl,
                NickName = nickName,
                roomStatus.PlatformName,
                roomStatus.StreamStatus,
                roomStatus.RecordStatus,
                roomStatus.Recorder.RequestedAt,
            });
            return true;
        }

        int offlineChecks = OfflineConfirmationChecks.AddOrUpdate(roomUrl, 1, static (_, current) => current + 1);
        bool defer = ShouldDeferOffline(roomStatus.StreamStatus, roomStatus.RecordStatus, isLiveStreaming, hasFreshStream, offlineChecks);
        if (defer)
        {
            AppSessionLogger.Event("info", "business", "room_offline_confirmation_pending", "the first offline result was deferred to avoid a transient live-state flap", new
            {
                RoomUrl = roomUrl,
                NickName = nickName,
                roomStatus.PlatformName,
                roomStatus.StreamStatus,
                roomStatus.RecordStatus,
                offlineChecks,
            });
        }
        else
        {
            ResetOfflineConfirmation(roomUrl);
        }
        return defer;
    }

    internal static bool ReconcileManualRefreshResult(string roomUrl, bool? isLiveStreaming, bool hasFreshStream)
    {
        if (!RoomStatus.TryGetValue(roomUrl, out RoomStatus? roomStatus))
        {
            return false;
        }

        SyncRecordStatus(roomStatus);
        ResetOfflineConfirmation(roomUrl);

        if (isLiveStreaming == false)
        {
            ResetRoomRecordSessionState(roomUrl);
        }

        if (isLiveStreaming == false && roomStatus.RecordStatus == RecordStatus.Recording)
        {
            AppSessionLogger.Event("info", "business", "record_stop_requested", "record stop requested because manual refresh confirmed that live ended", new
            {
                RoomUrl = roomUrl,
                roomStatus.NickName,
                roomStatus.PlatformName,
                roomStatus.RecordStatus,
                source = "manual_refresh",
            });
            roomStatus.Recorder.Stop();
            roomStatus.RecordStatus = RecordStatus.NotRecording;
        }

        return false;
    }

    internal static void SetRoomStreamCheckFailed(string roomUrl, bool failed)
    {
        if (!RoomStatus.TryGetValue(roomUrl, out RoomStatus? roomStatus))
        {
            return;
        }
        roomStatus.IsStreamCheckFailed = failed;
        _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(roomUrl));
    }

    internal static bool ShouldDeferOffline(StreamStatus streamStatus, RecordStatus recordStatus, bool? isLiveStreaming, bool hasFreshStream, int offlineChecks)
    {
        return ShouldConfirmOffline(streamStatus, recordStatus)
            && isLiveStreaming == false
            && !hasFreshStream
            && offlineChecks < 2;
    }

    internal static bool IsWithinRecordingStartupOfflineGuard(RecordStatus recordStatus, DateTime requestedAt, DateTime startedAt, DateTime now)
    {
        if (recordStatus != RecordStatus.Recording || requestedAt <= DateTime.MinValue || now < requestedAt)
        {
            return false;
        }

        if (now - requestedAt < RecordingStartupOfflineGuardWindow)
        {
            return true;
        }

        return startedAt > DateTime.MinValue
            && now >= startedAt
            && now - startedAt < RecordingStartupOfflineGuardWindow;
    }

    private static bool IsWithinRecordingStartupOfflineGuard(RoomStatus roomStatus, DateTime now)
    {
        return IsWithinRecordingStartupOfflineGuard(roomStatus.RecordStatus, roomStatus.Recorder.RequestedAt, roomStatus.Recorder.StartTime, now);
    }

    private static bool ShouldConfirmOffline(StreamStatus streamStatus, RecordStatus recordStatus)
    {
        return streamStatus == StreamStatus.Streaming || recordStatus == RecordStatus.Recording;
    }

    private static void ResetOfflineConfirmation(string roomUrl)
    {
        _ = OfflineConfirmationChecks.TryRemove(roomUrl, out _);
    }

    private static bool IsDouyinPlatform(string platformName)
    {
        return platformName.Equals("Douyin", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldStopRecorderAfterRoomCheck(bool isLiveStreaming, RecordStatus recordStatus)
    {
        return !isLiveStreaming && recordStatus == RecordStatus.Recording;
    }

    private static Task<RecorderStreamRefreshResult?> RefreshRecorderStreamAsync(
        Room room,
        RoomStatus roomStatus,
        RoomRecordingOptions settings,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.Factory.StartNew(
            () =>
            {
                ISpiderResult? result = Spider.GetResult(
                    room.RoomUrl,
                    settings.PreferredStreamQuality,
                    bypassDouyinThrottle: false,
                    prioritizeDouyin: true);
                if (result == null)
                {
                    return null;
                }

                RecorderStreamRefreshResult refreshResult = new()
                {
                    IsLiveStreaming = result.IsLiveStreaming,
                    RecordUrl = result.RecordUrl ?? string.Empty,
                    ReferenceUrl = result.ReferenceUrl ?? string.Empty,
                    HlsUrl = result.HlsUrl ?? string.Empty,
                    FlvUrl = result.FlvUrl ?? string.Empty,
                    Headers = SpiderResultMetadata.GetHeaders(result) ?? string.Empty,
                    Title = SpiderResultMetadata.GetTitle(result) ?? string.Empty,
                    Resolution = SpiderResultMetadata.GetResolution(result) ?? string.Empty,
                    Bitrate = SpiderResultMetadata.GetBitrate(result) ?? string.Empty,
                };
                ApplyRecorderStreamRefresh(room, roomStatus, refreshResult);
                return refreshResult;
            },
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static void ApplyRecorderStreamRefresh(Room room, RoomStatus roomStatus, RecorderStreamRefreshResult result)
    {
        bool hasFreshStream = !string.IsNullOrWhiteSpace(result.RecordUrl)
            || !string.IsNullOrWhiteSpace(result.HlsUrl)
            || !string.IsNullOrWhiteSpace(result.FlvUrl);
        if ((!hasFreshStream && result.IsLiveStreaming != true) || !IsCurrentRoomStatus(room.RoomUrl, roomStatus))
        {
            return;
        }

        ApplyStreamConnectionMetadata(
            roomStatus,
            result.FlvUrl,
            result.HlsUrl,
            result.RecordUrl,
            result.Headers,
            result.IsLiveStreaming,
            hasFreshStream);
        ApplyReferenceStream(roomStatus, result.ReferenceUrl, result.IsLiveStreaming, hasFreshStream);
        ApplyLiveSessionMetadata(roomStatus, result.Title, roomStatus.Quality, result.Resolution);
        if (!string.IsNullOrWhiteSpace(result.Bitrate))
        {
            roomStatus.Bitrate = result.Bitrate;
        }
        roomStatus.StreamStatus = StreamStatus.Streaming;
        roomStatus.IsStreamCheckFailed = false;
        ResetOfflineConfirmation(room.RoomUrl);
        ResetRoomCheckInconclusiveLog(room.RoomUrl);
    }

    private static void ConfirmRecorderOffline(Room room, RoomStatus roomStatus, RoomRecordingOptions settings)
    {
        if (!IsCurrentRoomStatus(room.RoomUrl, roomStatus))
        {
            return;
        }

        StreamStatus previous = roomStatus.StreamStatus;
        ApplyStreamConnectionMetadata(roomStatus, null, null, null, null, false, false);
        roomStatus.StreamStatus = StreamStatus.NotStreaming;
        roomStatus.RecordStatus = RecordStatus.NotRecording;
        roomStatus.IsStreamCheckFailed = false;
        roomStatus.LiveTitle = string.Empty;
        roomStatus.Quality = string.Empty;
        roomStatus.Resolution = string.Empty;
        roomStatus.Bitrate = string.Empty;
        ResetLiveSessionMetadata(roomStatus);
        ResetRoomRecordSessionState(room.RoomUrl);
        UpdateRoomCheckSchedule(room.RoomUrl, previous, roomStatus.StreamStatus, settings, DateTime.Now);
        AppSessionLogger.Event("info", "business", "recorder_offline_confirmed", "recorder confirmed that the live stream ended", new
        {
            room.RoomUrl,
            room.NickName,
            previous,
            current = roomStatus.StreamStatus,
        });
    }

    private static bool StartRecorderIfNeeded(Room room, RoomStatus roomStatus, RoomRecordingOptions settings, bool isLiveStreaming, bool usingPreservedStream)
    {
        if (IsRoomRecordStartPaused(room.RoomUrl, DateTime.Now))
        {
            return false;
        }

        if (roomStatus.Recorder.IsBusy && roomStatus.RecordStatus != RecordStatus.Recording)
        {
            AppSessionLogger.Event("info", "business", "record_start_waiting_for_cleanup", "record start delayed while recorder cleanup is still running", new
            {
                room.RoomUrl,
                room.NickName,
                roomStatus.PlatformName,
                usingPreservedStream,
            });
            return false;
        }

        if (IsRoomRecording(roomStatus))
        {
            return true;
        }

        if (HasActiveRecorderForRoom(room.RoomUrl, roomStatus))
        {
            AppSessionLogger.Event("warn", "business", "record_start_skipped_duplicate", "record start skipped because another recorder is active for the same room", new
            {
                room.RoomUrl,
                room.NickName,
                usingPreservedStream,
            });
            return false;
        }

        PrepareHlsStreamForRecording(room.RoomUrl, roomStatus, settings.PreferredStreamQuality);

        AppSessionLogger.Event("info", "business", "record_start_requested", "record start requested", new
        {
            room.RoomUrl,
            room.NickName,
            roomStatus.PlatformName,
            roomStatus.RecordStatus,
            isLiveStreaming,
            usingPreservedStream,
            hasRecordUrl = !string.IsNullOrWhiteSpace(roomStatus.RecordUrl),
            hasFlvUrl = !string.IsNullOrWhiteSpace(roomStatus.FlvUrl),
            hasHlsUrl = !string.IsNullOrWhiteSpace(roomStatus.HlsUrl),
        });

        RecorderStartInfo startInfo = new()
        {
            NickName = room.NickName,
            RoomUrl = room.RoomUrl,
            PlatformName = roomStatus.PlatformName,
            Resolution = roomStatus.Resolution,
            FlvUrl = roomStatus.FlvUrl,
            HlsUrl = roomStatus.HlsUrl,
            RecordUrl = roomStatus.RecordUrl,
            ReferenceUrl = roomStatus.ReferenceUrl,
            Headers = roomStatus.Headers,
            Title = roomStatus.LiveTitle,
            Bitrate = roomStatus.Bitrate,
            CoverPath = string.IsNullOrWhiteSpace(roomStatus.AvatarLocalPath) ? roomStatus.AvatarThumbUrl : roomStatus.AvatarLocalPath,
            Options = settings,
            ResolveCurrentOptions = () => RoomRecordingSettings.GetCurrent(room.RoomUrl, settings),
            RefreshStreamAsync = SupportsRecorderStreamRefresh(roomStatus.PlatformName)
                ? refreshToken => RefreshRecorderStreamAsync(
                    room,
                    roomStatus,
                    RoomRecordingSettings.GetCurrent(room.RoomUrl, settings),
                    refreshToken)
                : null,
            OfflineConfirmed = SupportsRecorderStreamRefresh(roomStatus.PlatformName)
                ? () => ConfirmRecorderOffline(
                    room,
                    roomStatus,
                    RoomRecordingSettings.GetCurrent(room.RoomUrl, settings))
                : null,
            ReconnectExhausted = () => PauseRoomRecordStart(room.RoomUrl, room.NickName, "reconnect_exhausted"),
            RapidExitDetected = () => PauseRoomRecordStart(room.RoomUrl, room.NickName, "rapid_exit"),
        };

        Lazy<bool> defaultRecordingStart = new(() =>
        {
            _ = roomStatus.Recorder.Start(startInfo);
            return true;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        ExtensionRecorderStartRequest request = new(room, roomStatus, settings, startInfo);
        return ExtensionHostRuntime.InvokeOverrideChain<ExtensionRecorderOverride, bool>(
            ExtensionContractNames.Recorder,
            (recorder, next) => recorder(request, next),
            () => defaultRecordingStart.Value,
            AppSessionLogger.WriteException);
    }

    internal static bool IsRoomRecordStartPaused(string roomUrl, DateTime now)
    {
        if (!RoomRecordStartPausedUntil.TryGetValue(roomUrl, out DateTime pausedUntil))
        {
            return false;
        }

        if (pausedUntil > now)
        {
            return true;
        }

        _ = RoomRecordStartPausedUntil.TryRemove(roomUrl, out _);
        return false;
    }

    internal static void SetRoomRecordStartPause(string roomUrl, DateTime pausedUntil)
    {
        RoomRecordStartPausedUntil[roomUrl] = pausedUntil;
    }

    internal static void ClearRoomRecordStartPause(string roomUrl)
    {
        if (!string.IsNullOrWhiteSpace(roomUrl))
        {
            _ = RoomRecordStartPausedUntil.TryRemove(roomUrl, out _);
        }
    }

    internal static void ResetRoomRecordSessionState(string roomUrl)
    {
        ClearTemporaryRoomRecord(roomUrl);
        ClearRoomRecordStartPause(roomUrl);
    }

    private static void PauseRoomRecordStart(string roomUrl, string nickName, string reason)
    {
        DateTime pausedUntil = DateTime.Now + RoomRecordStartPause;
        SetRoomRecordStartPause(roomUrl, pausedUntil);
        AppSessionLogger.Event("warn", "business", "room_record_start_paused", "room recording was paused after unstable media startup", new
        {
            RoomUrl = roomUrl,
            NickName = nickName,
            reason,
            pausedUntil,
        });
    }

    internal static bool SupportsRecorderStreamRefresh(string? platformName)
    {
        return !string.IsNullOrWhiteSpace(platformName);
    }

    internal static bool ShouldProbeHlsBeforeRecording(string? recordUrl, string? flvUrl, string? hlsUrl)
    {
        if (string.IsNullOrWhiteSpace(hlsUrl))
        {
            return false;
        }

        string selectedUrl = FirstNonEmpty(recordUrl ?? string.Empty, flvUrl ?? string.Empty, hlsUrl);
        return string.Equals(selectedUrl, hlsUrl, StringComparison.Ordinal)
            && Uri.TryCreate(hlsUrl, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrepareHlsStreamForRecording(string roomUrl, RoomStatus roomStatus, string preferredQuality)
    {
        if (!ShouldProbeHlsBeforeRecording(roomStatus.RecordUrl, roomStatus.FlvUrl, roomStatus.HlsUrl))
        {
            return;
        }

        StreamResolverResult result = new()
        {
            HlsUrl = roomStatus.HlsUrl,
            RecordUrl = roomStatus.RecordUrl,
            Resolution = roomStatus.Resolution,
            Bitrate = roomStatus.Bitrate,
        };
        StreamResolver.EnrichHighestHlsVariant(
            result,
            preferredQuality,
            roomUrl,
            null,
            Configurations.UserAgent.Get());
        if (string.Equals(result.HlsUrl, roomStatus.HlsUrl, StringComparison.Ordinal))
        {
            return;
        }

        roomStatus.HlsUrl = result.HlsUrl ?? roomStatus.HlsUrl;
        roomStatus.RecordUrl = result.RecordUrl ?? roomStatus.RecordUrl;
        roomStatus.Resolution = result.Resolution ?? roomStatus.Resolution;
        roomStatus.Bitrate = result.Bitrate ?? roomStatus.Bitrate;
    }

    private static bool HasActiveRecorderForRoom(string roomUrl, RoomStatus current)
    {
        return RoomStatus.Values.Any(item =>
            !ReferenceEquals(item, current)
            && string.Equals(item.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase)
            && IsRoomRecording(item));
    }

    private static bool IsRoomRecording(RoomStatus roomStatus)
    {
        return roomStatus.RecordStatus == RecordStatus.Recording && roomStatus.Recorder.IsBusy;
    }

    internal static void SyncRecordStatus(RoomStatus roomStatus)
    {
        if (roomStatus.RecordStatus == RecordStatus.Recording && !roomStatus.Recorder.IsBusy)
        {
            roomStatus.Recorder.EndNowIfRecording();
            roomStatus.RecordStatus = RecordStatus.NotRecording;
            AppSessionLogger.Event("info", "business", "room_record_status_synced", "room recording status synced from recorder task", new
            {
                roomStatus.RoomUrl,
                roomStatus.NickName,
                roomStatus.PlatformName,
            });
        }
    }

    internal static StreamStatus ResolveStreamStatus(StreamStatus currentStatus, bool? isLiveStreaming, bool hasRecordableStream)
    {
        return isLiveStreaming switch
        {
            true => StreamStatus.Streaming,
            false => StreamStatus.NotStreaming,
            null when hasRecordableStream => StreamStatus.Streaming,
            _ => currentStatus,
        };
    }

    internal static void ApplyStreamConnectionMetadata(
        RoomStatus roomStatus,
        string? flvUrl,
        string? hlsUrl,
        string? recordUrl,
        string? headers,
        bool? resolvedLiveState,
        bool hasFreshStream)
    {
        if (resolvedLiveState == false)
        {
            roomStatus.FlvUrl = string.Empty;
            roomStatus.HlsUrl = string.Empty;
            roomStatus.RecordUrl = string.Empty;
            roomStatus.Headers = string.Empty;
            return;
        }

        if (hasFreshStream)
        {
            roomStatus.FlvUrl = flvUrl ?? string.Empty;
            roomStatus.HlsUrl = hlsUrl ?? string.Empty;
            roomStatus.RecordUrl = recordUrl ?? string.Empty;
            if (resolvedLiveState.HasValue || !string.IsNullOrWhiteSpace(headers))
            {
                roomStatus.Headers = headers ?? string.Empty;
            }
            return;
        }

    }

    internal static void ApplyReferenceStream(
        RoomStatus roomStatus,
        string? referenceUrl,
        bool? resolvedLiveState,
        bool hasFreshStream)
    {
        if (resolvedLiveState == false)
        {
            roomStatus.ReferenceUrl = string.Empty;
            return;
        }

        if (hasFreshStream)
        {
            roomStatus.ReferenceUrl = referenceUrl ?? string.Empty;
        }
    }

    private static void StopRecordingBecauseDisabled(Room room, RoomStatus roomStatus)
    {
        SyncRecordStatus(roomStatus);
        if (roomStatus.RecordStatus != RecordStatus.Recording)
        {
            return;
        }

        AppSessionLogger.Event("info", "business", "record_stop_requested", "record stop requested because recording is disabled", new
        {
            room.RoomUrl,
            room.NickName,
            roomStatus.PlatformName,
            roomStatus.RecordStatus,
        });
        roomStatus.Recorder.Stop();
    }

    private static void StopRecordingBecauseMonitoringDisabled(Room room, RoomStatus roomStatus)
    {
        SyncRecordStatus(roomStatus);
        if (roomStatus.RecordStatus != RecordStatus.Recording)
        {
            return;
        }

        AppSessionLogger.Event("info", "business", "record_stop_requested", "record stop requested because monitoring is disabled", new
        {
            room.RoomUrl,
            room.NickName,
            roomStatus.PlatformName,
            roomStatus.RecordStatus,
        });
        roomStatus.Recorder.Stop();
    }

    private static IEnumerable<Room> DistinctRoomsByUrl(IEnumerable<Room> rooms)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Room? room in rooms)
        {
            if (room == null || string.IsNullOrWhiteSpace(room.RoomUrl))
            {
                continue;
            }

            if (seen.Add(room.RoomUrl))
            {
                yield return room;
            }
        }
    }

    private static bool IsRoutineScheduleActive(DateTime now, RoomRecordingOptions settings)
    {
        switch (Math.Clamp(settings.RoutineScheduleMode, 0, 4))
        {
            case 0:
                return true;
            case 1:
                return now.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
            case 2:
                return now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            case 3:
                return now.TimeOfDay >= TimeSpan.FromHours(18) || now.TimeOfDay <= TimeSpan.FromHours(8);
        }

        HashSet<string> enabledDays = settings.RoutineScheduleDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!enabledDays.Contains(now.DayOfWeek.ToString()))
        {
            return false;
        }

        TimeSpan start = new(
            Math.Clamp(settings.RoutineScheduleStartHour, 0, 23),
            Math.Clamp(settings.RoutineScheduleStartMinute, 0, 59),
            0);
        TimeSpan end = new(
            Math.Clamp(settings.RoutineScheduleEndHour, 0, 23),
            Math.Clamp(settings.RoutineScheduleEndMinute, 0, 59),
            0);
        TimeSpan current = now.TimeOfDay;

        return start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;
    }

    /// <summary>
    /// Get Room Status
    /// </summary>
    private static RoomStatus? TryGetRoomStatus(Room room)
    {
        // First insert
        if (!RoomStatus.ContainsKey(room.RoomUrl))
        {
            RoomStatus.TryAdd(room.RoomUrl, new RoomStatus()
            {
                NickName = room.NickName,
                AvatarThumbUrl = room.AvatarThumbUrl,
                AvatarLocalPath = AvatarCache.GetCachedAvatarSource(room.RoomUrl),
                RoomUrl = room.RoomUrl,
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
                StreamStatus = ResolveInitialStreamStatus(
                    string.IsNullOrWhiteSpace(room.PlatformName) ? Spider.GetPlatformName(room.RoomUrl) : room.PlatformName,
                    room.RecordUrl,
                    room.FlvUrl,
                    room.HlsUrl,
                    DateTime.Now),
            });
        }

        if (RoomStatus.TryGetValue(room.RoomUrl, out RoomStatus? roomStatus))
        {
            ///
        }

        return roomStatus;
    }

    /// <summary>
    /// Notification Runnable
    /// </summary>
    private static async Task Notify(Room room, CancellationToken token = default)
    {
        if (Configurations.IsToNotifyWithSystem.Get())
        {
            Notifier.AddNoticeWithButton("LiveNotification".Tr(), room.NickName, [
                new ToastContentButtonOption()
                {
                    Content = "GotoLiveRoom".Tr(),
                    Arguments = [("RoomUrl", room.RoomUrl)],
                    ActivationType = ToastActivationType.Background,
                },
                new ToastContentButtonOption()
                {
                    Content = "ButtonOfClose".Tr(),
                    ActivationType = ToastActivationType.Foreground,
                },
            ]);
        }

        if (Configurations.IsToNotifyWithMusic.Get())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    const string musicPack = "pack://application:,,,/Emerde;component/Assets/b_101.f1304dc4.mp3";
                    string? musicPath = Configurations.ToNotifyWithMusicPath.Get();

                    if (File.Exists(musicPath))
                    {
                        using MediaInfo lib = new();
                        lib.Open(musicPath);
                        string audioTrackCount = lib.Get(StreamKind.Audio, 0, "StreamCount");

                        if (int.TryParse(audioTrackCount, out int count) && count > 0)
                        {
                            using FileStream stream = File.OpenRead(musicPath);
                            await Notifier.PlayMusicAsync(stream);
                        }
                        else
                        {
                            using Stream stream = ResourcesProvider.GetStream(musicPack);
                            await Notifier.PlayMusicAsync(stream);
                        }
                    }
                    else
                    {
                        using Stream stream = ResourcesProvider.GetStream(musicPack);
                        await Notifier.PlayMusicAsync(stream);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception e)
                {
                    AppSessionLogger.WriteException(e);
                }
            }, token);
        }

        if (Configurations.IsToNotifyWithEmail.Get())
        {
            string smtpServer = Configurations.ToNotifyWithEmailSmtp.Get();
            int port = Configurations.ToNotifyWithEmailPort.Get();
            string userName = Configurations.ToNotifyWithEmailUserName.Get();
            string password = SecretProtector.Unprotect(Configurations.ToNotifyWithEmailPassword.Get());

            if (Notifier.IsEmailConfigurationComplete(smtpServer, userName, password))
            {
                Interlocked.Exchange(ref invalidEmailConfigurationLogged, 0);
                _ = Notifier.SendEmailAsync(smtpServer, port, userName, password, room.NickName, room.RoomUrl, token);
            }
            else if (Interlocked.Exchange(ref invalidEmailConfigurationLogged, 1) == 0)
            {
                AppSessionLogger.Event("warn", "notification", "email_configuration_incomplete", "email notification was skipped because its configuration is incomplete");
            }
        }

        if (Configurations.IsToNotifyGotoRoomUrl.Get())
        {
            // TODO: Implement for other platforms
            _ = await Launcher.LaunchUriAsync(new Uri(room.RoomUrl));

            if (Configurations.IsToNotifyGotoRoomUrlAndMute.Get())
            {
                SystemVolume.SetMasterVolumeMute(true);
            }
        }
    }
}
