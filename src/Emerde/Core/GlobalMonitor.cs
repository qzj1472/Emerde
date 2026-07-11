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
    /// <summary>
    /// ConcurrentDictionary{RoomUrl: string, RoomStatus: RoomStatus>}
    /// </summary>
    public static ConcurrentDictionary<string, RoomStatus> RoomStatus { get; } = new();

    private static readonly ConcurrentDictionary<string, bool> TemporaryRoomMonitorOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, bool> TemporaryRoomRecordOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RoomCheckLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, DateTime> LastRoomCheckTimes = new(StringComparer.OrdinalIgnoreCase);

    public static PeriodicWait RoutinePeriodicWait = new(GetRoutinePeriod(), TimeSpan.Zero);

    public static CancellationTokenSource? TokenSource { get; private set; } = null;

    private static readonly object MonitorLock = new();

    private static Task? MonitorTask = null;

    private static long monitorGeneration;

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
        }
    }

    public static void Stop()
    {
        lock (MonitorLock)
        {
            Interlocked.Increment(ref monitorGeneration);
            TokenSource?.Cancel();
            TokenSource = null;
        }
    }

    public static void StopAllRecorders()
    {
        foreach (RoomStatus roomStatus in RoomStatus.Values)
        {
            roomStatus.Recorder.Stop();
        }
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
            _ = LastRoomCheckTimes.TryRemove(roomUrl, out _);
            _ = RoomCheckLocks.TryRemove(roomUrl, out _);
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

            using SemaphoreSlim semaphore = new(Math.Clamp(Environment.ProcessorCount, 4, 8));
            List<Task> tasks = [];

            foreach (Room room in DistinctRoomsByUrl(rooms))
            {
                token.ThrowIfCancellationRequested();

                if (TryGetRoomStatus(room) is not RoomStatus roomStatus)
                {
                    continue;
                }

                RoomRecordingOptions settings = RoomRecordingSettings.Get(room);
                bool shouldNotify = isGlobalToNotify && room.IsToNotify;
                bool shouldRecord = GetEffectiveRoomRecord(room);
                bool shouldMonitor = GetEffectiveRoomMonitor(room) && IsRoutineScheduleActive(now, settings);

                if (shouldMonitor)
                {
                    if (!force && !ShouldCheckRoom(room.RoomUrl, settings.RoutineInterval, now))
                    {
                        continue;
                    }

                    LastRoomCheckTimes[room.RoomUrl] = now;
                    tasks.Add(RunRoomCheckWithSemaphoreAsync(semaphore, room, roomStatus, shouldNotify, shouldRecord, settings, token));
                }
                else
                {
                    StopRecordingBecauseMonitoringDisabled(room, roomStatus);
                    roomStatus.RecordStatus = RecordStatus.Disabled;
                    roomStatus.StreamStatus = StreamStatus.Disabled;
                }
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

    private static async Task RunRoomCheckWithSemaphoreAsync(SemaphoreSlim semaphore, Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, CancellationToken token)
    {
        await semaphore.WaitAsync(token);
        SemaphoreSlim roomLock = RoomCheckLocks.GetOrAdd(room.RoomUrl, _ => new SemaphoreSlim(1, 1));
        bool roomLockTaken = false;

        try
        {
            await roomLock.WaitAsync(token);
            roomLockTaken = true;
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
                roomLock.Release();
            }
            semaphore.Release();
        }
    }

    private static async Task RunRoomCheckAsync(Room room, RoomStatus roomStatus, bool shouldNotify, bool shouldRecord, RoomRecordingOptions settings, CancellationToken token)
    {
        SyncRecordStatus(roomStatus);
        ISpiderResult? spiderResult = await Task.Run(() => Spider.GetResult(room.RoomUrl, settings.PreferredStreamQuality), token);
        shouldRecord = GetEffectiveRoomRecord(room);

        if (!GetEffectiveRoomMonitor(room))
        {
            StopRecordingBecauseMonitoringDisabled(room, roomStatus);
            roomStatus.RecordStatus = RecordStatus.Disabled;
            roomStatus.StreamStatus = StreamStatus.Disabled;
            return;
        }

        if (spiderResult == null)
        {
            if (!shouldRecord)
            {
                StopRecordingBecauseDisabled(room, roomStatus);
                roomStatus.RecordStatus = RecordStatus.Disabled;
                roomStatus.StreamStatus = StreamStatus.Disabled;
                return;
            }

            SyncRecordStatus(roomStatus);
            if (roomStatus.RecordStatus == RecordStatus.Recording)
            {
                AppSessionLogger.Event("warn", "business", "room_check_failed_while_recording", "room check failed while recorder is still running", new
                {
                    room.RoomUrl,
                    room.NickName,
                    roomStatus.PlatformName,
                    hasRecordUrl = !string.IsNullOrWhiteSpace(roomStatus.RecordUrl),
                    hasFlvUrl = !string.IsNullOrWhiteSpace(roomStatus.FlvUrl),
                    hasHlsUrl = !string.IsNullOrWhiteSpace(roomStatus.HlsUrl),
                });

                if (HasRecordableStream(roomStatus))
                {
                    roomStatus.StreamStatus = StreamStatus.Streaming;
                }

                return;
            }

            roomStatus.RecordStatus = RecordStatus.NotRecording;
            roomStatus.StreamStatus = StreamStatus.NotStreaming;
            AppSessionLogger.Event("warn", "business", "room_check_no_result", "room check returned no spider result", new
            {
                room.RoomUrl,
                room.NickName,
                roomStatus.PlatformName,
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
        if (HasRecordableStream(spiderResult) || roomStatus.RecordStatus != RecordStatus.Recording)
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

        bool isLiveStreaming = IsLiveStreaming(spiderResult);
        SyncRecordStatus(roomStatus);

        if (roomStatus.StreamStatus == StreamStatus.Streaming
            && roomStatus.RecordStatus == RecordStatus.Recording
            && (DateTime.Now - roomStatus.Recorder.StartTime).TotalSeconds < 30)
        {
        }
        else
        {
            roomStatus.StreamStatus = spiderResult.IsLiveStreaming switch
            {
                true => StreamStatus.Streaming,
                false => StreamStatus.NotStreaming,
                null or _ => isLiveStreaming ? StreamStatus.Streaming : StreamStatus.NotStreaming,
            };
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
    }

    internal static TimeSpan GetRoutinePeriod()
    {
        return TimeSpan.FromMilliseconds(GetEffectiveRoutineInterval());
    }

    internal static int GetEffectiveRoutineInterval()
    {
        Room[] rooms = Configurations.Rooms.Get() ?? [];
        int interval = Math.Max(500, Configurations.RoutineInterval.Get());

        foreach (Room room in rooms)
        {
            if (room == null || !GetEffectiveRoomMonitor(room))
            {
                continue;
            }

            RoomRecordingOptions settings = RoomRecordingSettings.Get(room);
            interval = Math.Min(interval, Math.Max(500, settings.RoutineInterval));
        }

        return interval;
    }

    private static bool ShouldCheckRoom(string roomUrl, int routineInterval, DateTime now)
    {
        if (!LastRoomCheckTimes.TryGetValue(roomUrl, out DateTime lastCheckTime))
        {
            LastRoomCheckTimes[roomUrl] = now;
            return true;
        }

        if ((now - lastCheckTime).TotalMilliseconds < Math.Max(500, routineInterval))
        {
            return false;
        }

        LastRoomCheckTimes[roomUrl] = now;
        return true;
    }

    private static bool IsLiveStreaming(ISpiderResult spiderResult)
    {
        return spiderResult.IsLiveStreaming switch
        {
            true => true,
            false => false,
            null => HasRecordableStream(spiderResult),
        };
    }

    private static bool HasRecordableStream(ISpiderResult spiderResult)
    {
        return !string.IsNullOrWhiteSpace(spiderResult.RecordUrl)
            || !string.IsNullOrWhiteSpace(spiderResult.HlsUrl)
            || !string.IsNullOrWhiteSpace(spiderResult.FlvUrl);
    }

    private static bool HasRecordableStream(RoomStatus roomStatus)
    {
        return !string.IsNullOrWhiteSpace(roomStatus.RecordUrl)
            || !string.IsNullOrWhiteSpace(roomStatus.HlsUrl)
            || !string.IsNullOrWhiteSpace(roomStatus.FlvUrl);
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
