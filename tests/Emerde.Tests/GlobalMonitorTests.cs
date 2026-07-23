using Emerde.Core;
using Emerde.Models;

namespace Emerde.Tests;

public sealed class GlobalMonitorTests
{
    [Theory]
    [InlineData("Douyin", true)]
    [InlineData("Twitch", true)]
    [InlineData("twitch", true)]
    [InlineData("Bilibili", true)]
    [InlineData(null, false)]
    public void RecorderStreamRefresh_IsEnabledForExpiringPlatformUrls(string? platformName, bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.SupportsRecorderStreamRefresh(platformName));
    }

    [Theory]
    [InlineData(null, null, "https://example.test/master.m3u8", true)]
    [InlineData("https://example.test/master.m3u8", null, "https://example.test/master.m3u8", true)]
    [InlineData("https://example.test/live.flv", null, "https://example.test/master.m3u8", false)]
    [InlineData(null, "https://example.test/live.flv", "https://example.test/master.m3u8", false)]
    [InlineData(null, null, null, false)]
    public void RecorderHlsPreparation_OnlyProbesWhenHlsIsSelected(string? recordUrl, string? flvUrl, string? hlsUrl, bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.ShouldProbeHlsBeforeRecording(recordUrl, flvUrl, hlsUrl));
    }

    [Fact]
    public void RecorderStreamRefresh_CoversEverySupportedPlatform()
    {
        Assert.All(Spider.SupportedPlatformNames, platform => Assert.True(GlobalMonitor.SupportsRecorderStreamRefresh(platform)));
    }

    [Theory]
    [InlineData(null, 1000L, true)]
    [InlineData(1000L, 1000L, false)]
    [InlineData(1000L, 3600999L, false)]
    [InlineData(1000L, 3601000L, true)]
    [InlineData(3601000L, 1000L, true)]
    public void RoomCheckInconclusiveLog_UsesHourlyInterval(long? lastTimestamp, long currentTimestamp, bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.ShouldLogRoomCheckInconclusive(lastTimestamp, currentTimestamp));
    }

    [Fact]
    public void RoomCheckInconclusiveLog_LogsAgainAfterSuccessfulCheck()
    {
        string roomUrl = $"https://example.test/{Guid.NewGuid():N}";

        Assert.True(GlobalMonitor.TryAcquireInconclusiveLog(roomUrl, 1000));
        Assert.False(GlobalMonitor.TryAcquireInconclusiveLog(roomUrl, 2000));

        GlobalMonitor.ResetRoomCheckInconclusiveLog(roomUrl);

        Assert.True(GlobalMonitor.TryAcquireInconclusiveLog(roomUrl, 3000));
        GlobalMonitor.ResetRoomCheckInconclusiveLog(roomUrl);
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void RoomCheckConclusion_RequiresStateOrFreshStream(
        bool? resolvedLiveState,
        bool hasFreshStream,
        bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.IsConclusiveRoomCheck(resolvedLiveState, hasFreshStream));
    }

    [Fact]
    public void StreamConnectionMetadata_PreservesWorkingStreamWhenLiveResultIsPartial()
    {
        RoomStatus status = new()
        {
            FlvUrl = "https://example.test/old.flv",
            HlsUrl = "https://example.test/old.m3u8",
            RecordUrl = "https://example.test/old-record.flv",
            Headers = "Referer: https://example.test/",
        };

        GlobalMonitor.ApplyStreamConnectionMetadata(status, null, null, null, null, true, false);

        Assert.Equal("https://example.test/old.flv", status.FlvUrl);
        Assert.Equal("https://example.test/old.m3u8", status.HlsUrl);
        Assert.Equal("https://example.test/old-record.flv", status.RecordUrl);
        Assert.Equal("Referer: https://example.test/", status.Headers);
    }

    [Fact]
    public void StreamConnectionMetadata_ClearsWorkingStreamWhenOfflineIsConfirmed()
    {
        RoomStatus status = new()
        {
            FlvUrl = "https://example.test/old.flv",
            HlsUrl = "https://example.test/old.m3u8",
            RecordUrl = "https://example.test/old-record.flv",
            Headers = "Referer: https://example.test/",
        };

        GlobalMonitor.ApplyStreamConnectionMetadata(status, null, null, null, null, false, false);

        Assert.Equal(string.Empty, status.FlvUrl);
        Assert.Equal(string.Empty, status.HlsUrl);
        Assert.Equal(string.Empty, status.RecordUrl);
        Assert.Equal(string.Empty, status.Headers);
    }

    [Fact]
    public void StreamConnectionMetadata_IgnoresStaleStreamAttachedToOfflineResult()
    {
        RoomStatus status = new()
        {
            FlvUrl = "https://example.test/old.flv",
            Headers = "old",
        };

        GlobalMonitor.ApplyStreamConnectionMetadata(
            status,
            "https://example.test/stale.flv",
            "https://example.test/stale.m3u8",
            "https://example.test/stale-record.flv",
            "stale",
            false,
            true);

        Assert.Equal(string.Empty, status.FlvUrl);
        Assert.Equal(string.Empty, status.HlsUrl);
        Assert.Equal(string.Empty, status.RecordUrl);
        Assert.Equal(string.Empty, status.Headers);
    }

    [Fact]
    public void StreamConnectionMetadata_ReplacesWorkingStreamWhenFreshStreamIsAvailable()
    {
        RoomStatus status = new()
        {
            FlvUrl = "https://example.test/old.flv",
            Headers = "old",
        };

        GlobalMonitor.ApplyStreamConnectionMetadata(
            status,
            "https://example.test/new.flv",
            null,
            "https://example.test/new-record.flv",
            "new",
            true,
            true);

        Assert.Equal("https://example.test/new.flv", status.FlvUrl);
        Assert.Equal(string.Empty, status.HlsUrl);
        Assert.Equal("https://example.test/new-record.flv", status.RecordUrl);
        Assert.Equal("new", status.Headers);
    }

    [Fact]
    public void LiveSessionMetadata_ClearsStaleValuesAndFillsMissingFieldsLater()
    {
        RoomStatus status = new()
        {
            LiveTitle = "stale title",
            Quality = "stale quality",
            Resolution = "stale resolution",
        };

        GlobalMonitor.ApplyLiveSessionMetadata(status, "first title", null, string.Empty);

        Assert.Equal("first title", status.LiveTitle);
        Assert.Equal(string.Empty, status.Quality);
        Assert.Equal(string.Empty, status.Resolution);

        GlobalMonitor.ApplyLiveSessionMetadata(status, "changed title", "原画", "1920x1080");

        Assert.Equal("first title", status.LiveTitle);
        Assert.Equal("原画", status.Quality);
        Assert.Equal("1920x1080", status.Resolution);

        GlobalMonitor.ResetLiveSessionMetadata(status);
        GlobalMonitor.ApplyLiveSessionMetadata(status, "next title", "高清", "1280x720");

        Assert.Equal("next title", status.LiveTitle);
        Assert.Equal("高清", status.Quality);
        Assert.Equal("1280x720", status.Resolution);
    }

    [Fact]
    public void RoomRecordingOptions_DefaultRoutineIntervalIsFiveSeconds()
    {
        Assert.Equal(5000, new RoomRecordingOptions().RoutineInterval);
    }

    [Fact]
    public void GetRoutinePeriod_UsesOneSecondSchedulerResolution()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), GlobalMonitor.GetRoutinePeriod());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(100, 5)]
    public void GetMonitorConcurrency_LimitsOnlyConcurrentChecks(int roomCount, int expected)
    {
        Assert.Equal(expected, GlobalMonitor.GetMonitorConcurrency(roomCount));
    }

    [Theory]
    [InlineData(22, false, 22)]
    [InlineData(3, false, 3)]
    [InlineData(22, true, 22)]
    public void GetRoutineBatchSize_QueuesEveryDueRoomAndLimitsOnlyExecutionConcurrency(int roomCount, bool force, int expected)
    {
        Assert.Equal(expected, GlobalMonitor.GetRoutineBatchSize(roomCount, force));
    }

    [Theory]
    [InlineData(StreamStatus.Streaming, RecordStatus.Recording, 0)]
    [InlineData(StreamStatus.NotStreaming, RecordStatus.Recording, 0)]
    [InlineData(StreamStatus.Streaming, RecordStatus.NotRecording, 1)]
    [InlineData(StreamStatus.NotStreaming, RecordStatus.NotRecording, 2)]
    public void GetRoomCheckPriority_PrioritizesRecordingAndLiveRooms(StreamStatus streamStatus, RecordStatus recordStatus, int expected)
    {
        Assert.Equal(expected, GlobalMonitor.GetRoomCheckPriority(streamStatus, recordStatus));
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 1)]
    public void GetStreamingFollowUpInterval_AcceleratesOfflineConfirmation(bool offlineConfirmationPending, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), GlobalMonitor.GetStreamingFollowUpInterval(offlineConfirmationPending));
    }

    [Fact]
    public void GetFallbackInterval_UsesLiveRecentlyClosedAndRoutineRules()
    {
        DateTime now = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Local);

        Assert.Equal(
            TimeSpan.FromSeconds(10),
            GlobalMonitor.GetFallbackInterval(StreamStatus.Streaming, 9000, null, now));
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            GlobalMonitor.GetFallbackInterval(StreamStatus.NotStreaming, 9000, now.AddMinutes(-29), now));
        Assert.Equal(
            TimeSpan.FromSeconds(9),
            GlobalMonitor.GetFallbackInterval(StreamStatus.NotStreaming, 9000, now.AddMinutes(-30), now));
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            GlobalMonitor.GetFallbackInterval(StreamStatus.NotStreaming, 500, null, now));
    }

    [Fact]
    public void GetEffectiveRoutineInterval_UsesOneSecondMinimumForShortestEnabledRoomInterval()
    {
        Room[] oldRooms = Configurations.Rooms.Get();
        int oldRoutineInterval = Configurations.RoutineInterval.Get();
        bool oldIsToMonitor = Configurations.IsToMonitor.Get();
        bool oldIsMonitorRunning = Configurations.IsMonitorRunning.Get();

        try
        {
            Configurations.RoutineInterval.Set(60_000);
            Configurations.IsToMonitor.Set(true);
            Configurations.IsMonitorRunning.Set(true);
            Configurations.Rooms.Set(
            [
                new Room
                {
                    NickName = "global",
                    RoomUrl = "https://example.test/global",
                    IsFollowGlobalSettings = true,
                    IsToMonitor = true,
                },
                new Room
                {
                    NickName = "local",
                    RoomUrl = "https://example.test/local",
                    IsFollowGlobalSettings = false,
                    IsToMonitor = true,
                    RoutineInterval = 500,
                },
                new Room
                {
                    NickName = "disabled",
                    RoomUrl = "https://example.test/disabled",
                    IsFollowGlobalSettings = false,
                    IsToMonitor = false,
                    RoutineInterval = 250,
                },
            ]);

            Assert.Equal(1000, GlobalMonitor.GetEffectiveRoutineInterval());
        }
        finally
        {
            Configurations.Rooms.Set(oldRooms);
            Configurations.RoutineInterval.Set(oldRoutineInterval);
            Configurations.IsToMonitor.Set(oldIsToMonitor);
            Configurations.IsMonitorRunning.Set(oldIsMonitorRunning);
        }
    }

    [Fact]
    public void GetEffectiveRoomRecord_FollowGlobalUsesGlobalRecordSwitch()
    {
        bool oldIsToRecord = Configurations.IsToRecord.Get();

        try
        {
            Configurations.IsToRecord.Set(false);

            Assert.False(GlobalMonitor.GetEffectiveRoomRecord("https://example.test/room", true, true));

            Configurations.IsToRecord.Set(true);

            Assert.True(GlobalMonitor.GetEffectiveRoomRecord("https://example.test/room", false, true));
        }
        finally
        {
            Configurations.IsToRecord.Set(oldIsToRecord);
        }
    }

    [Fact]
    public void GetEffectiveRoomRecord_LocalRoomOverridesGlobalRecordSwitch()
    {
        bool oldIsToRecord = Configurations.IsToRecord.Get();

        try
        {
            Configurations.IsToRecord.Set(false);

            Assert.True(GlobalMonitor.GetEffectiveRoomRecord("https://example.test/room", true, false));

            Configurations.IsToRecord.Set(true);

            Assert.False(GlobalMonitor.GetEffectiveRoomRecord("https://example.test/room", false, false));
        }
        finally
        {
            Configurations.IsToRecord.Set(oldIsToRecord);
        }
    }

    [Fact]
    public void GetEffectiveRoomMonitor_FollowGlobalUsesGlobalMonitorAndLocalOverrides()
    {
        bool oldIsMonitorRunning = Configurations.IsMonitorRunning.Get();
        bool oldIsToMonitor = Configurations.IsToMonitor.Get();

        try
        {
            Configurations.IsMonitorRunning.Set(false);
            Configurations.IsToMonitor.Set(true);

            Assert.False(GlobalMonitor.GetEffectiveRoomMonitor("https://example.test/room", true, true));
            Assert.True(GlobalMonitor.GetEffectiveRoomMonitor("https://example.test/room", true, false));

            Configurations.IsMonitorRunning.Set(true);
            Configurations.IsToMonitor.Set(false);

            Assert.False(GlobalMonitor.GetEffectiveRoomMonitor("https://example.test/room", true, true));
            Assert.False(GlobalMonitor.GetEffectiveRoomMonitor("https://example.test/room", false, false));
        }
        finally
        {
            Configurations.IsMonitorRunning.Set(oldIsMonitorRunning);
            Configurations.IsToMonitor.Set(oldIsToMonitor);
        }
    }

    [Fact]
    public void TemporaryRoomOverrides_AreRemovedTogether()
    {
        const string roomUrl = "https://example.test/temporary-room";
        bool oldIsToRecord = Configurations.IsToRecord.Get();
        bool oldIsToMonitor = Configurations.IsToMonitor.Get();
        bool oldIsMonitorRunning = Configurations.IsMonitorRunning.Get();

        try
        {
            Configurations.IsToRecord.Set(false);
            Configurations.IsToMonitor.Set(false);
            Configurations.IsMonitorRunning.Set(false);
            GlobalMonitor.SetTemporaryRoomRecord(roomUrl, true);
            GlobalMonitor.SetTemporaryRoomMonitor(roomUrl, true);

            Assert.True(GlobalMonitor.GetEffectiveRoomRecord(roomUrl, false, true));
            Assert.True(GlobalMonitor.GetEffectiveRoomMonitor(roomUrl, false, true));

            GlobalMonitor.ClearTemporaryRoomOverrides(roomUrl);

            Assert.False(GlobalMonitor.GetEffectiveRoomRecord(roomUrl, false, true));
            Assert.False(GlobalMonitor.GetEffectiveRoomMonitor(roomUrl, false, true));
        }
        finally
        {
            GlobalMonitor.ClearTemporaryRoomOverrides(roomUrl);
            Configurations.IsToRecord.Set(oldIsToRecord);
            Configurations.IsToMonitor.Set(oldIsToMonitor);
            Configurations.IsMonitorRunning.Set(oldIsMonitorRunning);
        }
    }

    [Fact]
    public async Task RoomCheckLock_IsReleasedAfterRoomUpdate()
    {
        const string roomUrl = "https://example.test/locked-room";
        int before = GlobalMonitor.RoomCheckLockCount;

        int result = await GlobalMonitor.RunRoomUpdateAsync(roomUrl, () => Task.FromResult(1));

        Assert.Equal(1, result);
        Assert.Equal(before, GlobalMonitor.RoomCheckLockCount);
    }

    [Fact]
    public void IsCurrentRoomStatus_RejectsReplacedStatusInstance()
    {
        const string roomUrl = "https://example.test/replaced-room";
        RoomStatus first = new() { RoomUrl = roomUrl };
        RoomStatus second = new() { RoomUrl = roomUrl };

        try
        {
            GlobalMonitor.RoomStatus[roomUrl] = first;
            Assert.True(GlobalMonitor.IsCurrentRoomStatus(roomUrl, first));

            GlobalMonitor.RoomStatus[roomUrl] = second;

            Assert.False(GlobalMonitor.IsCurrentRoomStatus(roomUrl, first));
            Assert.True(GlobalMonitor.IsCurrentRoomStatus(roomUrl, second));
        }
        finally
        {
            _ = GlobalMonitor.RoomStatus.TryRemove(roomUrl, out _);
        }
    }

    [Fact]
    public void RecordStartBlock_IsReferenceCountedByReason()
    {
        const string firstReason = "test-first";
        const string secondReason = "test-second";

        try
        {
            GlobalMonitor.SetRecordStartBlock(firstReason, true);
            GlobalMonitor.SetRecordStartBlock(secondReason, true);

            Assert.True(GlobalMonitor.IsRecordStartBlocked);

            GlobalMonitor.SetRecordStartBlock(firstReason, false);

            Assert.True(GlobalMonitor.IsRecordStartBlocked);

            GlobalMonitor.SetRecordStartBlock(secondReason, false);

            Assert.False(GlobalMonitor.IsRecordStartBlocked);
        }
        finally
        {
            GlobalMonitor.SetRecordStartBlock(firstReason, false);
            GlobalMonitor.SetRecordStartBlock(secondReason, false);
        }
    }

    [Theory]
    [InlineData(StreamStatus.Streaming, null, false, StreamStatus.Streaming)]
    [InlineData(StreamStatus.NotStreaming, null, false, StreamStatus.NotStreaming)]
    [InlineData(StreamStatus.NotStreaming, null, true, StreamStatus.Streaming)]
    [InlineData(StreamStatus.Streaming, false, false, StreamStatus.NotStreaming)]
    [InlineData(StreamStatus.NotStreaming, true, false, StreamStatus.Streaming)]
    public void ResolveStreamStatus_OnlyExplicitOfflineOverridesConfirmedState(
        StreamStatus currentStatus,
        bool? isLiveStreaming,
        bool hasRecordableStream,
        StreamStatus expected)
    {
        Assert.Equal(expected, GlobalMonitor.ResolveStreamStatus(currentStatus, isLiveStreaming, hasRecordableStream));
    }

    [Theory]
    [InlineData(StreamStatus.Streaming, RecordStatus.NotRecording, false, false, 1, true)]
    [InlineData(StreamStatus.Streaming, RecordStatus.NotRecording, false, false, 2, false)]
    [InlineData(StreamStatus.NotStreaming, RecordStatus.Recording, false, false, 1, true)]
    [InlineData(StreamStatus.NotStreaming, RecordStatus.NotRecording, false, false, 1, false)]
    [InlineData(StreamStatus.Streaming, RecordStatus.Recording, true, true, 1, false)]
    public void ShouldDeferOffline_RequiresASecondOfflineConfirmationForActiveRooms(
        StreamStatus streamStatus,
        RecordStatus recordStatus,
        bool? isLiveStreaming,
        bool hasFreshStream,
        int offlineChecks,
        bool expected)
    {
        Assert.Equal(
            expected,
            GlobalMonitor.ShouldDeferOffline(streamStatus, recordStatus, isLiveStreaming, hasFreshStream, offlineChecks));
    }

    [Theory]
    [InlineData(true, "Douyin", StreamStatus.Streaming, true, true, true)]
    [InlineData(true, "Douyin", StreamStatus.Streaming, true, false, false)]
    [InlineData(false, "Douyin", StreamStatus.Streaming, true, true, false)]
    [InlineData(true, "TikTok", StreamStatus.Streaming, true, true, false)]
    [InlineData(true, "Douyin", StreamStatus.NotStreaming, true, true, false)]
    [InlineData(true, "Douyin", StreamStatus.Streaming, false, true, false)]
    public void ShouldStartFromPreservedDouyinStream_RequiresConfirmedLivePreviewStream(
        bool shouldRecord,
        string platformName,
        StreamStatus streamStatus,
        bool hasRecordableStream,
        bool isReachable,
        bool expected)
    {
        Assert.Equal(
            expected,
            GlobalMonitor.ShouldStartFromPreservedDouyinStream(shouldRecord, platformName, streamStatus, hasRecordableStream, isReachable));
    }

    [Theory]
    [InlineData("Douyin", StreamStatus.Initialized, true, false)]
    [InlineData("Douyin", StreamStatus.NotStreaming, true, false)]
    [InlineData("Douyin", StreamStatus.Streaming, true, true)]
    [InlineData("TikTok", StreamStatus.Initialized, true, false)]
    [InlineData("Douyin", StreamStatus.Initialized, false, false)]
    public void ShouldProbePreservedDouyinStream_RequiresConfirmedStreamingState(
        string platformName,
        StreamStatus streamStatus,
        bool hasRecordableStream,
        bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.ShouldProbePreservedDouyinStream(platformName, streamStatus, hasRecordableStream));
    }

    [Theory]
    [InlineData("Douyin", StreamStatus.Streaming, RecordStatus.NotRecording, true, true)]
    [InlineData("Douyin", StreamStatus.NotStreaming, RecordStatus.Recording, true, true)]
    [InlineData("Douyin", StreamStatus.NotStreaming, RecordStatus.NotRecording, true, false)]
    [InlineData("Twitch", StreamStatus.Streaming, RecordStatus.Recording, true, false)]
    [InlineData("Douyin", StreamStatus.Streaming, RecordStatus.Recording, false, false)]
    public void PreservedStreamProbeOnInconclusive_IsOnlyUsedForActiveDouyinRooms(
        string platformName,
        StreamStatus streamStatus,
        RecordStatus recordStatus,
        bool hasRecordableStream,
        bool expected)
    {
        Assert.Equal(
            expected,
            GlobalMonitor.ShouldProbePreservedStreamOnInconclusive(platformName, streamStatus, recordStatus, hasRecordableStream));
    }

    [Theory]
    [InlineData("Douyin", "https://example.test/live.flv?t_id=037-20260722073816ABC", 0, StreamStatus.Initialized)]
    [InlineData("Douyin", "https://example.test/live.flv?t_id=037-20260722073816ABC", 121, StreamStatus.Initialized)]
    [InlineData("Douyin", "https://example.test/live.flv", 0, StreamStatus.Initialized)]
    [InlineData("TikTok", "https://example.test/live.flv?t_id=037-20260722073816ABC", 0, StreamStatus.Initialized)]
    public void ResolveInitialStreamStatus_DoesNotTrustPersistedStreamUrls(
        string platformName,
        string streamUrl,
        int elapsedMinutes,
        StreamStatus expected)
    {
        DateTime issuedAt = new(2026, 7, 22, 7, 38, 16, DateTimeKind.Local);

        Assert.Equal(
            expected,
            GlobalMonitor.ResolveInitialStreamStatus(platformName, streamUrl, string.Empty, string.Empty, issuedAt.AddMinutes(elapsedMinutes)));
    }

    [Theory]
    [InlineData(true, RecordStatus.Recording, false)]
    [InlineData(false, RecordStatus.Recording, true)]
    [InlineData(false, RecordStatus.NotRecording, false)]
    public void ShouldStopRecorderAfterRoomCheck_RequiresConfirmedOffline(bool isLiveStreaming, RecordStatus recordStatus, bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.ShouldStopRecorderAfterRoomCheck(isLiveStreaming, recordStatus));
    }

    [Fact]
    public void SyncRecordStatus_ClearsStaleRecordingStateWhenRecorderFinished()
    {
        RoomStatus status = new()
        {
            RoomUrl = "https://example.test/finished",
            NickName = "finished",
            RecordStatus = RecordStatus.Recording,
        };

        GlobalMonitor.SyncRecordStatus(status);

        Assert.Equal(RecordStatus.NotRecording, status.RecordStatus);
    }
}
