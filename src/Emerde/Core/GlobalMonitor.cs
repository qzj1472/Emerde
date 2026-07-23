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
using Windows.System;
using Wpf.Ui.Violeta.Resources;

namespace Emerde.Core;

internal static class GlobalMonitor
{
    private const int DefaultSchedulerPeriodMilliseconds = MonitorTiming.MinimumRoutineIntervalMilliseconds;
    private const int MaximumBatchSize = MonitorTiming.MonitorBatchLimit;
    internal const long FixedRoomMetadataRefreshIntervalMilliseconds = 60 * 60 * 1000;
    internal const long InconclusiveLogIntervalMilliseconds = 60 * 60 * 1000;
    private static readonly TimeSpan StreamingCycleInterval = TimeSpan.FromMilliseconds(MonitorTiming.LiveRoutineIntervalMilliseconds);
    private static readonly TimeSpan RecentlyClosedInterval = TimeSpan.FromMilliseconds(MonitorTiming.RecentlyClosedRoutineIntervalMilliseconds);
    private static readonly TimeSpan RecentlyClosedWindow = MonitorTiming.RecentlyClosedWindow;
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
        lock (MonitorLock)
        {
            if (TokenSource != null && !TokenSource.IsCancellationRequested && MonitorTask is { IsCompleted: false })
            {
                return;
            }

            CancellationTokenSource source = tokenSource ?? new CancellationTokenSource();
            long generation = Interlocked.Increment(ref monitorGeneration);
            PeriodicWait periodicWait = new(GetRoutinePeriod(), TimeSpan.Zero);
            TokenSource = source;
            RoutinePeriodicWait = periodicWait;
            MonitorTask = Task.Factory.StartNew(
                () => StartAsync(source.Token, generation, periodicWait),
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
    }

    public static void Stop()
    {
        CancellationTokenSource? source;
        Task? task;
        lock (MonitorLock)
        {
            Interlocked.Increment(ref monitorGeneration);
            source = TokenSource;
            task = MonitorTask;
            TokenSource = null;
            MonitorTask = null;
            source?.Cancel();
            AppSessionLogger.Event("info", "monitor", "monitor_stopped", "global monitor stopped");
        }

        if (source != null)
        {
            _ = DisposeMonitorSourceAsync(source, task);
        }
    }

    private static async Task DisposeMonitorSourceAsync(CancellationTokenSource source, Task? task)
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
        PeriodicWait priorityPeriodicWait = new(GetRoutinePeriod(), TimeSpan.Zero);
        await Task.WhenAll(
            StartScheduledChecksAsync(token, generation, periodicWait, priorityOnly: false),
            StartScheduledChecksAsync(token, generation, priorityPeriodicWait, priorityOnly: true));
    }

    private static async Task StartScheduledChecksAsync(CancellationToken token, long generation, PeriodicWait periodicWait, bool priorityOnly)
    {
        while (!token.IsCancellationRequested && generation == Volatile.Read(ref monitorGeneration))
        {
            periodicWait.Period = GetRoutinePeriod();

            if (!await periodicWait.WaitForNextTickAsync(token)
                || generation != Volatile.Read(ref monitorGeneration))
            {
                break;
            }

            await RunRoomsAsync(Configurations.Rooms.Get() ?? [], token, priorityOnly: priorityOnly);
        }
    }

    private static async Task RunRoomsAsync(IEnumerable<Room> rooms, CancellationToken token = default, bool force = false, bool? priorityOnly = null)
    {
        try
        {
            bool isGlobalToNotify = Configurations.IsToNotify.Get();
            DateTime now = DateTime.Now;

            List<(Room Room, RoomStatus RoomStatus, bool ShouldNotify, bool ShouldRecord, RoomRecordingOptions Settings, DateTime DueAt)> dueRooms = [];

            foreach (Room room in DistinctRoomsByUrl(rooms))
            {
                token.ThrowIfCancellationRequested();

                if (TryGetRoomStatus(room) is not RoomStatus roomStatus)
                {
                    continue;
                }

                bool isPriorityRoom = GetRoomCheckPriority(roomStatus.StreamStatus, roomStatus.RecordStatus) < 2;
                if (priorityOnly.HasValue && priorityOnly.Value != isPriorityRoom)
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

                    dueRooms.Add((room, roomStatus, shouldNotify, shouldRecord, settings, dueAt));
                }
                else
                {
                    _ = RoomCheckSchedules.TryRemove(room.RoomUrl, out _);
                    StopRecordingBecauseMonitoringDisabled(room, roomStatus);
                    roomStatus.RecordStatus = RecordStatus.Disabled;
                    roomStatus.StreamStatus = StreamStatus.Disabled;
                    ResetLiveSessionMetadata(roomStatus);
                    ResetRoomCheckInconclusiveLog(room.RoomUrl);
                }
            }

            if (dueRooms.Count == 0)
            {
                return;
            }

            var selectedRooms = dueRooms
                .OrderBy(item => GetRoomCheckPriority(item.RoomStatus.StreamStatus, item.RoomStatus.RecordStatus))
                .ThenBy(item => item.DueAt)
                .Take(GetRoutineBatchSize(dueRooms.Count, force))
                .ToArray();
            using SemaphoreSlim semaphore = new(GetMonitorConcurrency(selectedRooms.Length));
            List<Task> tasks = new(selectedRooms.Length);
            foreach ((Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, _) in selectedRooms)
            {
                ReserveRoomCheck(room.RoomUrl, settings, roomStatus.StreamStatus, now);
                tasks.Add(RunRoomCheckWithSemaphoreAsync(semaphore, room, roomStatus, shouldNotify, shouldRecord, settings, token));
            }

            await Task.WhenAll(tasks);
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

    internal static int GetMonitorConcurrency(int roomCount)
    {
        return Math.Clamp(roomCount, 1, MaximumBatchSize);
    }

    internal static int GetRoutineBatchSize(int dueRoomCount, bool force)
    {
        return Math.Max(0, dueRoomCount);
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

    private static async Task RunRoomCheckWithSemaphoreAsync(SemaphoreSlim semaphore, Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, CancellationToken token)
    {
        await semaphore.WaitAsync(token);
        IDisposable? roomLock = null;
        StreamStatus previousStreamStatus = default;
        RecordStatus previousRecordStatus = default;
        bool previousStreamCheckFailed = false;

        try
        {
            roomLock = await AcquireRoomCheckLockAsync(room.RoomUrl, token);
            previousStreamStatus = roomStatus.StreamStatus;
            previousRecordStatus = roomStatus.RecordStatus;
            previousStreamCheckFailed = roomStatus.IsStreamCheckFailed;
            await RunRoomCheckAsync(room, roomStatus, shouldNotify, shouldRecord, settings, token);
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
                if (GetEffectiveRoomMonitor(room) && IsCurrentRoomStatus(room.RoomUrl, roomStatus))
                {
                    UpdateRoomCheckSchedule(room.RoomUrl, previousStreamStatus, roomStatus.StreamStatus, settings, DateTime.Now);
                    if (previousStreamStatus != roomStatus.StreamStatus
                        || previousRecordStatus != roomStatus.RecordStatus
                        || previousStreamCheckFailed != roomStatus.IsStreamCheckFailed)
                    {
                        _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(room.RoomUrl));
                    }
                }
                roomLock.Dispose();
            }
            semaphore.Release();
        }
    }

    private static async Task RunRoomCheckAsync(Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, CancellationToken token)
    {
        SyncRecordStatus(roomStatus);
        bool prioritizeDouyin = roomStatus.StreamStatus == StreamStatus.Streaming
            || roomStatus.RecordStatus == RecordStatus.Recording;
        ISpiderResult? spiderResult = await Task.Run(
            () => Spider.GetResult(room.RoomUrl, settings.PreferredStreamQuality, bypassDouyinThrottle: false, prioritizeDouyin),
            token);
        token.ThrowIfCancellationRequested();
        shouldRecord = GetEffectiveRoomRecord(room) && !IsRecordStartBlocked;

        if (!IsCurrentRoomStatus(room.RoomUrl, roomStatus))
        {
            return;
        }

        if (!GetEffectiveRoomMonitor(room))
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

        long currentTimestamp = Environment.TickCount64;
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

        SyncRecordStatus(roomStatus);
        roomStatus.StreamStatus = nextStreamStatus;
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

    public static void RefreshRoutineInterval()
    {
        RoutinePeriodicWait.Period = GetRoutinePeriod();
        RefreshRoutineSchedules(DateTime.Now);
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
        return offlineConfirmationPending ? TimeSpan.FromSeconds(1) : StreamingCycleInterval;
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
        return recordStatus == RecordStatus.Recording
            && requestedAt > DateTime.MinValue
            && startedAt == DateTime.MinValue
            && now >= requestedAt
            && now - requestedAt < RecordingStartupOfflineGuardWindow;
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
        return Task.Run(() =>
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
                HlsUrl = result.HlsUrl ?? string.Empty,
                FlvUrl = result.FlvUrl ?? string.Empty,
                Headers = SpiderResultMetadata.GetHeaders(result) ?? string.Empty,
                Title = SpiderResultMetadata.GetTitle(result) ?? string.Empty,
                Resolution = SpiderResultMetadata.GetResolution(result) ?? string.Empty,
                Bitrate = SpiderResultMetadata.GetBitrate(result) ?? string.Empty,
            };
            ApplyRecorderStreamRefresh(room, roomStatus, refreshResult);
            return refreshResult;
        }, token);
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

        _ = roomStatus.Recorder.Start(new RecorderStartInfo()
        {
            NickName = room.NickName,
            RoomUrl = room.RoomUrl,
            PlatformName = roomStatus.PlatformName,
            Resolution = roomStatus.Resolution,
            FlvUrl = roomStatus.FlvUrl,
            HlsUrl = roomStatus.HlsUrl,
            RecordUrl = roomStatus.RecordUrl,
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
        });
        return true;
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

            _ = Notifier.SendEmailAsync(smtpServer, port, userName, password, room.NickName, room.RoomUrl, token);
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
