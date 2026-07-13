using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fischless.Configuration;
using MediaInfoLib;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Web;
using Emerde.Models;
using Emerde.Threading;
using Windows.System;
using Wpf.Ui.Violeta.Resources;

namespace Emerde.Core;

internal static class GlobalMonitor
{
    private const int DefaultSchedulerPeriodMilliseconds = MonitorTiming.DefaultRoutineIntervalMilliseconds;
    private const int MinimumBatchSize = 1;
    private const int MaximumBatchSize = MonitorTiming.MonitorBatchLimit;
    private static readonly TimeSpan StreamingCycleInterval = TimeSpan.FromMilliseconds(MonitorTiming.LiveRoutineIntervalMilliseconds);
    private static readonly TimeSpan RecentlyClosedInterval = TimeSpan.FromMilliseconds(MonitorTiming.RecentlyClosedRoutineIntervalMilliseconds);
    private static readonly TimeSpan RecentlyClosedWindow = MonitorTiming.RecentlyClosedWindow;

    /// <summary>
    /// ConcurrentDictionary{RoomUrl: string, RoomStatus: RoomStatus>}
    /// </summary>
    public static ConcurrentDictionary<string, RoomStatus> RoomStatus { get; } = new();

    private static readonly ConcurrentDictionary<string, bool> TemporaryRoomMonitorOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, bool> TemporaryRoomRecordOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RoomCheckLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, RoomCheckScheduleState> RoomCheckSchedules = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> RecordStartBlocks = new(StringComparer.OrdinalIgnoreCase);

    public static PeriodicWait RoutinePeriodicWait = new(GetRoutinePeriod(), TimeSpan.Zero);

    public static CancellationTokenSource? TokenSource { get; private set; } = null;

    private static readonly object MonitorLock = new();

    private static Task? MonitorTask = null;

    private static long monitorGeneration;

    private sealed class RoomCheckScheduleState
    {
        public DateTime NextCheckAt { get; set; } = DateTime.MinValue;
        public DateTime? LiveCycleEndsAt { get; set; }
        public DateTime? LastClosedAt { get; set; }
        public Queue<DateTime> PendingLiveChecks { get; } = [];
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
                        ConfigurationManager.Save();
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
        lock (MonitorLock)
        {
            Interlocked.Increment(ref monitorGeneration);
            TokenSource?.Cancel();
            TokenSource = null;
            AppSessionLogger.Event("info", "monitor", "monitor_stopped", "global monitor stopped");
        }
    }

    public static void StopAllRecorders()
    {
        RoomStatus[] roomStatuses = RoomStatus.Values.ToArray();
        foreach (RoomStatus roomStatus in roomStatuses)
        {
            roomStatus.Recorder.Stop();
        }

        AppSessionLogger.Event("info", "monitor", "all_recorders_stopped", "all active recorders were asked to stop", new
        {
            recorderCount = roomStatuses.Count(roomStatus => roomStatus.Recorder.IsBusy),
        });
    }

    public static bool HasActiveRecorders => RoomStatus.Values.Any(roomStatus => roomStatus.Recorder.IsBusy);

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
        SemaphoreSlim roomLock = GetRoomCheckLock(roomUrl);
        await roomLock.WaitAsync(token);

        try
        {
            return await update();
        }
        finally
        {
            roomLock.Release();
        }
    }

    public static async Task StartAsync(CancellationToken token = default)
    {
        await StartAsync(token, Volatile.Read(ref monitorGeneration), RoutinePeriodicWait);
    }

    private static async Task StartAsync(CancellationToken token, long generation, PeriodicWait periodicWait)
    {
        while (!token.IsCancellationRequested && generation == Volatile.Read(ref monitorGeneration))
        {
            periodicWait.Period = GetRoutinePeriod();

            if (!await periodicWait.WaitForNextTickAsync(token)
                || generation != Volatile.Read(ref monitorGeneration))
            {
                break;
            }

            await RunOnceAsync(token);
        }
    }

    private static async Task RunRoomsAsync(IEnumerable<Room> rooms, CancellationToken token = default, bool force = false)
    {
        try
        {
            bool isGlobalToNotify = Configurations.IsToNotify.Get();
            DateTime now = DateTime.Now;

            List<(Room Room, RoomStatus RoomStatus, bool ShouldNotify, bool ShouldRecord, RoomRecordingOptions Settings)> dueRooms = [];

            foreach (Room room in DistinctRoomsByUrl(rooms))
            {
                token.ThrowIfCancellationRequested();

                if (TryGetRoomStatus(room) is not RoomStatus roomStatus)
                {
                    continue;
                }

                RoomRecordingOptions settings = RoomRecordingSettings.Get(room);
                bool shouldNotify = isGlobalToNotify && room.IsToNotify;
                bool shouldRecord = GetEffectiveRoomRecord(room) && !IsRecordStartBlocked;
                bool shouldMonitor = GetEffectiveRoomMonitor(room) && IsRoutineScheduleActive(now, settings);

                if (shouldMonitor)
                {
                    if (!force && !ShouldCheckRoom(room.RoomUrl, settings, roomStatus.StreamStatus, now))
                    {
                        continue;
                    }

                    dueRooms.Add((room, roomStatus, shouldNotify, shouldRecord, settings));
                }
                else
                {
                    _ = RoomCheckSchedules.TryRemove(room.RoomUrl, out _);
                    StopRecordingBecauseMonitoringDisabled(room, roomStatus);
                    roomStatus.RecordStatus = RecordStatus.Disabled;
                    roomStatus.StreamStatus = StreamStatus.Disabled;
                }
            }

            if (dueRooms.Count == 0)
            {
                return;
            }

            int offset = 0;
            foreach (int batchSize in CreateMonitorBatchSizes(dueRooms.Count))
            {
                token.ThrowIfCancellationRequested();
                using SemaphoreSlim semaphore = new(batchSize);
                List<Task> tasks = new(batchSize);

                for (int index = offset; index < offset + batchSize; index++)
                {
                    (Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings) = dueRooms[index];
                    ReserveRoomCheck(room.RoomUrl, settings, roomStatus.StreamStatus, now);
                    tasks.Add(RunRoomCheckWithSemaphoreAsync(semaphore, room, roomStatus, shouldNotify, shouldRecord, settings, token));
                }

                await Task.WhenAll(tasks);
                offset += batchSize;
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

    internal static IReadOnlyList<int> CreateMonitorBatchSizes(int roomCount, Random? random = null)
    {
        List<int> batchSizes = [];
        int remaining = Math.Max(0, roomCount);
        Random generator = random ?? Random.Shared;

        while (remaining > 0)
        {
            int batchSize = Math.Min(remaining, generator.Next(MinimumBatchSize, MaximumBatchSize + 1));
            batchSizes.Add(batchSize);
            remaining -= batchSize;
        }

        return batchSizes;
    }

    internal static SemaphoreSlim GetRoomCheckLock(string roomUrl)
    {
        return RoomCheckLocks.GetOrAdd(roomUrl, _ => new SemaphoreSlim(1, 1));
    }

    internal static bool IsCurrentRoomStatus(string roomUrl, RoomStatus roomStatus)
    {
        return RoomStatus.TryGetValue(roomUrl, out RoomStatus? current)
            && ReferenceEquals(current, roomStatus);
    }

    private static async Task RunRoomCheckWithSemaphoreAsync(SemaphoreSlim semaphore, Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, CancellationToken token)
    {
        await semaphore.WaitAsync(token);
        SemaphoreSlim roomLock = GetRoomCheckLock(room.RoomUrl);
        bool roomLockTaken = false;
        StreamStatus previousStreamStatus = default;

        try
        {
            await roomLock.WaitAsync(token);
            roomLockTaken = true;
            previousStreamStatus = roomStatus.StreamStatus;
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
            if (roomLockTaken)
            {
                if (GetEffectiveRoomMonitor(room) && IsCurrentRoomStatus(room.RoomUrl, roomStatus))
                {
                    UpdateRoomCheckSchedule(room.RoomUrl, previousStreamStatus, roomStatus.StreamStatus, settings, DateTime.Now);
                }
                roomLock.Release();
            }
            semaphore.Release();
        }
    }

    private static async Task RunRoomCheckAsync(Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, CancellationToken token)
    {
        SyncRecordStatus(roomStatus);
        ISpiderResult? spiderResult = await Task.Run(() => Spider.GetResult(room.RoomUrl, settings.PreferredStreamQuality), token);
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
            return;
        }

        if (spiderResult == null)
        {
            SyncRecordStatus(roomStatus);
            if (!shouldRecord)
            {
                StopRecordingBecauseDisabled(room, roomStatus);
                roomStatus.RecordStatus = RecordStatus.Disabled;
            }
            else if (roomStatus.RecordStatus != RecordStatus.Recording)
            {
                roomStatus.RecordStatus = RecordStatus.NotRecording;
            }

            AppSessionLogger.Event("warn", "business", "room_check_inconclusive", "room check returned no result and the previous stream state was preserved", new
            {
                room.RoomUrl,
                room.NickName,
                roomStatus.PlatformName,
                roomStatus.StreamStatus,
                roomStatus.RecordStatus,
                resolverError = ExternalStreamResolver.GetLastError(room.RoomUrl),
            });
            return;
        }

        StreamStatus prevStreamStatus = roomStatus.StreamStatus;

        if (string.IsNullOrWhiteSpace(roomStatus.AvatarThumbUrl) && !string.IsNullOrWhiteSpace(spiderResult.AvatarThumbUrl))
        {
            roomStatus.AvatarThumbUrl = spiderResult.AvatarThumbUrl;
        }
        roomStatus.PlatformName = string.IsNullOrWhiteSpace(spiderResult.PlatformName)
            ? Spider.GetPlatformName(room.RoomUrl)
            : spiderResult.PlatformName;
        string? liveTitle = SpiderResultMetadata.GetTitle(spiderResult);
        string? quality = SpiderResultMetadata.GetQuality(spiderResult);
        string? resolution = SpiderResultMetadata.GetResolution(spiderResult);
        string? bitrate = SpiderResultMetadata.GetBitrate(spiderResult);
        string? headers = SpiderResultMetadata.GetHeaders(spiderResult);
        if (spiderResult.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(liveTitle))
        {
            roomStatus.LiveTitle = liveTitle;
        }
        else if (spiderResult.IsLiveStreaming == false)
        {
            roomStatus.LiveTitle = string.Empty;
        }
        if (spiderResult.IsLiveStreaming.HasValue)
        {
            roomStatus.Quality = quality ?? string.Empty;
            roomStatus.Resolution = resolution ?? string.Empty;
            roomStatus.Bitrate = bitrate ?? string.Empty;
        }
        bool hasFreshStream = HasRecordableStream(spiderResult);
        if (spiderResult.IsLiveStreaming.HasValue || hasFreshStream)
        {
            roomStatus.FlvUrl = spiderResult.FlvUrl ?? string.Empty;
            roomStatus.HlsUrl = spiderResult.HlsUrl ?? string.Empty;
            roomStatus.RecordUrl = spiderResult.RecordUrl ?? string.Empty;
        }
        if (!string.IsNullOrWhiteSpace(spiderResult.Uid))
        {
            roomStatus.Uid = spiderResult.Uid;
        }
        if (spiderResult.IsLiveStreaming.HasValue)
        {
            roomStatus.Headers = headers ?? string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(headers))
        {
            roomStatus.Headers = headers;
        }

        SyncRecordStatus(roomStatus);
        roomStatus.StreamStatus = ResolveStreamStatus(roomStatus.StreamStatus, spiderResult.IsLiveStreaming, hasFreshStream);
        bool isLiveStreaming = roomStatus.StreamStatus == StreamStatus.Streaming;

        if (prevStreamStatus != roomStatus.StreamStatus)
        {
            AppSessionLogger.Event("info", "business", "room_stream_state_changed", "room stream state changed", new
            {
                room.RoomUrl,
                room.NickName,
                previous = prevStreamStatus,
                current = roomStatus.StreamStatus,
                result = spiderResult.IsLiveStreaming,
                hasFreshStream,
                roomStatus.RecordStatus,
            });
        }

        if (shouldRecord)
        {
            if (isLiveStreaming && HasRecordableStream(roomStatus))
            {
                if (roomStatus.Recorder.IsBusy && roomStatus.RecordStatus != RecordStatus.Recording)
                {
                    AppSessionLogger.Event("info", "business", "record_start_waiting_for_cleanup", "record start delayed while recorder cleanup is still running", new
                    {
                        room.RoomUrl,
                        room.NickName,
                        roomStatus.PlatformName,
                    });
                    return;
                }

                if (!IsRoomRecording(roomStatus))
                {
                    if (HasActiveRecorderForRoom(room.RoomUrl, roomStatus))
                    {
                        AppSessionLogger.Event("warn", "business", "record_start_skipped_duplicate", "record start skipped because another recorder is active for the same room", new
                        {
                            room.RoomUrl,
                            room.NickName,
                        });
                        return;
                    }

                    AppSessionLogger.Event("info", "business", "record_start_requested", "record start requested", new
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
                    });
                }
            }
            else if (roomStatus.RecordStatus == RecordStatus.Recording)
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
            else
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

    private static bool ShouldCheckRoom(string roomUrl, RoomRecordingOptions settings, StreamStatus streamStatus, DateTime now)
    {
        RoomCheckScheduleState state = RoomCheckSchedules.GetOrAdd(roomUrl, _ => new RoomCheckScheduleState());
        lock (state)
        {
            if (state.NextCheckAt == DateTime.MinValue)
            {
                state.NextCheckAt = now;
                return true;
            }

            return now >= state.NextCheckAt;
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
                AdvanceLiveSchedule(state, now);
                return;
            }

            state.PendingLiveChecks.Clear();
            state.LiveCycleEndsAt = null;

            if (previousStatus == StreamStatus.Streaming && currentStatus == StreamStatus.NotStreaming)
            {
                state.LastClosedAt = now;
            }

            state.NextCheckAt = now + GetFallbackInterval(currentStatus, settings.RoutineInterval, state.LastClosedAt, now);
        }
    }

    private static void AdvanceLiveSchedule(RoomCheckScheduleState state, DateTime now)
    {
        while (state.PendingLiveChecks.Count > 0 && state.PendingLiveChecks.Peek() <= now)
        {
            state.PendingLiveChecks.Dequeue();
        }

        if (state.PendingLiveChecks.Count == 0 || state.LiveCycleEndsAt == null || state.LiveCycleEndsAt <= now)
        {
            ScheduleLiveCycle(state, now);
            return;
        }

        state.NextCheckAt = state.PendingLiveChecks.Peek();
    }

    private static void ScheduleLiveCycle(RoomCheckScheduleState state, DateTime now)
    {
        state.PendingLiveChecks.Clear();
        IReadOnlyList<TimeSpan> offsets = CreateStreamingCycleOffsets();
        foreach (TimeSpan offset in offsets)
        {
            state.PendingLiveChecks.Enqueue(now + offset);
        }
        state.LiveCycleEndsAt = now + offsets[^1];
        state.NextCheckAt = state.PendingLiveChecks.Peek();
    }

    internal static IReadOnlyList<TimeSpan> CreateStreamingCycleOffsets(Random? random = null)
    {
        Random generator = random ?? Random.Shared;
        int firstOffset = generator.Next(12, 24);
        int secondOffset = generator.Next(34, 51);
        return
        [
            TimeSpan.FromSeconds(firstOffset),
            TimeSpan.FromSeconds(Math.Max(firstOffset + 5, secondOffset)),
            StreamingCycleInterval,
        ];
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

    private static bool HasRecordableStream(RoomStatus roomStatus)
    {
        return !string.IsNullOrWhiteSpace(roomStatus.RecordUrl)
            || !string.IsNullOrWhiteSpace(roomStatus.HlsUrl)
            || !string.IsNullOrWhiteSpace(roomStatus.FlvUrl);
    }

    private static bool HasRecordableStream(ISpiderResult spiderResult)
    {
        return !string.IsNullOrWhiteSpace(spiderResult.RecordUrl)
            || !string.IsNullOrWhiteSpace(spiderResult.HlsUrl)
            || !string.IsNullOrWhiteSpace(spiderResult.FlvUrl);
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

    private static void SyncRecordStatus(RoomStatus roomStatus)
    {
        if (roomStatus.RecordStatus == RecordStatus.Recording && !roomStatus.Recorder.IsBusy)
        {
            roomStatus.Recorder.EndNowIfRecording();
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
                AvatarLocalPath = room.AvatarLocalPath,
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
                StreamStatus = StreamStatus.Initialized,
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
            }, token);
        }

        if (Configurations.IsToNotifyWithEmail.Get())
        {
            string smtpServer = Configurations.ToNotifyWithEmailSmtp.Get();
            string userName = Configurations.ToNotifyWithEmailUserName.Get();
            string password = Configurations.ToNotifyWithEmailPassword.Get();

            _ = Task.Run(() =>
            {
                _ = Notifier.SendEmail(smtpServer, userName, password, room.NickName, room.RoomUrl);
            }, token);
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
