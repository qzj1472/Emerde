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

    public static PeriodicWait RoutinePeriodicWait = new(TimeSpan.FromMilliseconds(int.Max(Configurations.RoutineInterval.Get(), 500)), TimeSpan.Zero);

    public static CancellationTokenSource? TokenSource { get; private set; } = null;

    private static readonly object MonitorLock = new();

    private static Task? MonitorTask = null;

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
            TokenSource = source;
            RoutinePeriodicWait = new PeriodicWait(TimeSpan.FromMilliseconds(int.Max(Configurations.RoutineInterval.Get(), 500)), TimeSpan.Zero);
            MonitorTask = Task.Factory.StartNew(
                () => StartAsync(source.Token),
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
            TokenSource?.Cancel();
        }
    }

    public static async Task StartAsync(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            // Delay Routine Interval
            _ = await RoutinePeriodicWait.WaitForNextTickAsync(token);

            // Routine can not be stopped from throwables
            try
            {
                Room[] rooms = Configurations.Rooms.Get();

                // Check Global Settings
                bool isGlobalToMonitor = Configurations.IsToMonitor.Get() && IsRoutineScheduleActive(DateTime.Now);
                bool isGlobalToNotify = Configurations.IsToNotify.Get();
                bool isGlobalToRecord = Configurations.IsToRecord.Get();

                foreach (Room room in rooms)
                {
                    if (TryGetRoomStatus(room) is RoomStatus roomStatus)
                    {
                        bool shouldNotify = isGlobalToNotify && room.IsToNotify;
                        bool shouldRecord = room.IsFollowGlobalSettings ? isGlobalToRecord : room.IsToRecord;
                        bool shouldMonitor = room.IsFollowGlobalSettings ? isGlobalToMonitor : room.IsToMonitor;

                        if (shouldMonitor)
                        {
                            // Spider Room Status
                            ISpiderResult? spiderResult = Spider.GetResult(room.RoomUrl);

                            if (spiderResult == null)
                            {
                                // Not supported streaming live or error
                                continue;
                            }

                            StreamStatus prevStreamStatus = roomStatus.StreamStatus;

                            // Update Room Status
                            if (string.IsNullOrWhiteSpace(roomStatus.AvatarThumbUrl) && !string.IsNullOrWhiteSpace(spiderResult.AvatarThumbUrl))
                            {
                                roomStatus.AvatarThumbUrl = spiderResult.AvatarThumbUrl;
                            }
                            roomStatus.PlatformName = spiderResult.PlatformName ?? Spider.GetPlatformName(room.RoomUrl);
                            string? liveTitle = SpiderResultMetadata.GetTitle(spiderResult);
                            if (spiderResult.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(liveTitle))
                            {
                                roomStatus.LiveTitle = liveTitle;
                            }
                            else if (spiderResult.IsLiveStreaming == false)
                            {
                                roomStatus.LiveTitle = string.Empty;
                            }
                            roomStatus.FlvUrl = spiderResult.FlvUrl ?? string.Empty;
                            roomStatus.HlsUrl = spiderResult.HlsUrl ?? string.Empty;

                            // If current status is `StreamStatus.Streaming`, don't update it in 30s.
                            if (roomStatus.StreamStatus == StreamStatus.Streaming
                                && roomStatus.RecordStatus == RecordStatus.Recording
                                && (roomStatus.Recorder.EndTime - roomStatus.Recorder.StartTime).TotalSeconds < 30)
                            {
                                // Update stream status next 30s.
                                // https://github.com/qzj1472/Emerde/issues/20
                            }
                            else
                            {
                                roomStatus.StreamStatus = spiderResult.IsLiveStreaming switch
                                {
                                    true => StreamStatus.Streaming,
                                    false => StreamStatus.NotStreaming,
                                    null or _ => roomStatus.StreamStatus,
                                };
                            }

                            // If current status is `StreamStatus.Streaming`, don't update it in 30s.
                            if (roomStatus.StreamStatus == StreamStatus.Streaming
                                && (roomStatus.Recorder.EndTime - roomStatus.Recorder.StartTime).TotalSeconds >= 30)
                            {
                            }

                            // Start Streaming Recording
                            if (shouldRecord)
                            {
                                if (spiderResult.IsLiveStreaming ?? false)
                                {
                                    _ = roomStatus.Recorder.Start(new RecorderStartInfo()
                                    {
                                        NickName = room.NickName,
                                        FlvUrl = roomStatus.FlvUrl,
                                        HlsUrl = roomStatus.HlsUrl,
                                    });
                                }
                            }
                            else
                            {
                                if (roomStatus.RecordStatus != RecordStatus.Recording)
                                {
                                    roomStatus.RecordStatus = RecordStatus.Disabled;
                                }
                            }

                            // Start Broadcast Notification
                            if (shouldNotify)
                            {
                                // Only to notify when first detected
                                if (prevStreamStatus != StreamStatus.Streaming)
                                {
                                    if (spiderResult.IsLiveStreaming ?? false)
                                    {
                                        await Notify(room, token);
                                    }
                                }
                            }
                            if (shouldRecord && roomStatus.RecordStatus == RecordStatus.Disabled)
                            {
                                // Restore to initialized
                                roomStatus.RecordStatus = RecordStatus.Initialized;
                            }
                        }
                        else
                        {
                            if (roomStatus.RecordStatus != RecordStatus.Recording)
                            {
                                roomStatus.RecordStatus = RecordStatus.Disabled;
                            }
                            roomStatus.StreamStatus = StreamStatus.Disabled;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }
    }

    private static bool IsRoutineScheduleActive(DateTime now)
    {
        switch (Math.Clamp(Configurations.RoutineScheduleMode.Get(), 0, 4))
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

        HashSet<string> enabledDays = Configurations.RoutineScheduleDays.Get()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!enabledDays.Contains(now.DayOfWeek.ToString()))
        {
            return false;
        }

        TimeSpan start = new(
            Math.Clamp(Configurations.RoutineScheduleStartHour.Get(), 0, 23),
            Math.Clamp(Configurations.RoutineScheduleStartMinute.Get(), 0, 59),
            0);
        TimeSpan end = new(
            Math.Clamp(Configurations.RoutineScheduleEndHour.Get(), 0, 23),
            Math.Clamp(Configurations.RoutineScheduleEndMinute.Get(), 0, 59),
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
                RoomUrl = room.RoomUrl,
                PlatformName = Spider.GetPlatformName(room.RoomUrl),
                FlvUrl = null!,
                HlsUrl = null!,
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
