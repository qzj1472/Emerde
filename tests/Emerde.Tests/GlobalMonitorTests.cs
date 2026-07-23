using Emerde.Core;
using Emerde.Models;

namespace Emerde.Tests;

public sealed class GlobalMonitorTests
{
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(17)]
    [InlineData(100)]
    public void CreateMonitorBatchSizes_CoversEveryRoomWithOneToFivePerBatch(int roomCount)
    {
        IReadOnlyList<int> batchSizes = GlobalMonitor.CreateMonitorBatchSizes(roomCount, new Random(20260713));

        Assert.Equal(roomCount, batchSizes.Sum());
        Assert.All(batchSizes, batchSize => Assert.InRange(batchSize, 1, MonitorTiming.MonitorBatchLimit));
    }

    [Fact]
    public void CreateStreamingCycleOffsets_UsesTwoRandomChecksAndOneMinuteBoundary()
    {
        IReadOnlyList<TimeSpan> offsets = GlobalMonitor.CreateStreamingCycleOffsets(new Random(20260713));

        Assert.Equal(3, offsets.Count);
        Assert.InRange(offsets[0], TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(23));
        Assert.InRange(offsets[1], TimeSpan.FromSeconds(34), TimeSpan.FromSeconds(50));
        Assert.Equal(TimeSpan.FromMinutes(1), offsets[2]);
        Assert.True(offsets[0] < offsets[1]);
        Assert.True(offsets[1] < offsets[2]);
    }

    [Fact]
    public void GetFallbackInterval_UsesLiveRecentlyClosedAndRoutineRules()
    {
        DateTime now = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Local);

        Assert.Equal(
            TimeSpan.FromMinutes(1),
            GlobalMonitor.GetFallbackInterval(StreamStatus.Streaming, 9000, null, now));
        Assert.Equal(
            TimeSpan.FromSeconds(20),
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
    public void ClearTemporaryRoomOverrides_PreservesRoomCheckLock()
    {
        const string roomUrl = "https://example.test/locked-room";
        SemaphoreSlim firstLock = GlobalMonitor.GetRoomCheckLock(roomUrl);

        GlobalMonitor.ClearTemporaryRoomOverrides(roomUrl);

        Assert.Same(firstLock, GlobalMonitor.GetRoomCheckLock(roomUrl));
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
    [InlineData("Douyin", true, false, false, 1, true)]
    [InlineData("Douyin", true, false, false, 2, false)]
    [InlineData("Douyin", false, false, false, 1, false)]
    [InlineData("Douyin", true, true, true, 1, false)]
    [InlineData("TikTok", true, false, false, 1, false)]
    public void ShouldDeferDouyinOffline_RequiresASecondOfflineConfirmation(
        string platformName,
        bool isRecording,
        bool? isLiveStreaming,
        bool hasFreshStream,
        int offlineChecks,
        bool expected)
    {
        Assert.Equal(
            expected,
            GlobalMonitor.ShouldDeferDouyinOffline(platformName, isRecording, isLiveStreaming, hasFreshStream, offlineChecks));
    }

    [Theory]
    [InlineData(true, "Douyin", StreamStatus.Streaming, true, true)]
    [InlineData(false, "Douyin", StreamStatus.Streaming, true, false)]
    [InlineData(true, "TikTok", StreamStatus.Streaming, true, false)]
    [InlineData(true, "Douyin", StreamStatus.NotStreaming, true, false)]
    [InlineData(true, "Douyin", StreamStatus.Streaming, false, false)]
    public void ShouldStartFromPreservedDouyinStream_RequiresConfirmedLivePreviewStream(
        bool shouldRecord,
        string platformName,
        StreamStatus streamStatus,
        bool hasRecordableStream,
        bool expected)
    {
        Assert.Equal(
            expected,
            GlobalMonitor.ShouldStartFromPreservedDouyinStream(shouldRecord, platformName, streamStatus, hasRecordableStream));
    }

    [Theory]
    [InlineData("Douyin", StreamStatus.Initialized, true, true)]
    [InlineData("Douyin", StreamStatus.NotStreaming, true, true)]
    [InlineData("Douyin", StreamStatus.Streaming, true, true)]
    [InlineData("TikTok", StreamStatus.Initialized, true, false)]
    [InlineData("Douyin", StreamStatus.Initialized, false, false)]
    public void ShouldProbePreservedDouyinStream_UsesAnyDouyinCache(
        string platformName,
        StreamStatus streamStatus,
        bool hasRecordableStream,
        bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.ShouldProbePreservedDouyinStream(platformName, streamStatus, hasRecordableStream));
    }

    [Theory]
    [InlineData("Douyin", StreamStatus.Streaming, 1, true)]
    [InlineData("Douyin", StreamStatus.Streaming, 2, false)]
    [InlineData("Douyin", StreamStatus.Initialized, 1, false)]
    [InlineData("TikTok", StreamStatus.Streaming, 1, false)]
    public void ShouldPreserveDouyinStreamingAfterProbeFailure_RequiresFirstStreamingFailure(
        string platformName,
        StreamStatus streamStatus,
        int failureCount,
        bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.ShouldPreserveDouyinStreamingAfterProbeFailure(platformName, streamStatus, failureCount));
    }

    [Theory]
    [InlineData("Douyin", StreamStatus.Initialized, true)]
    [InlineData("Douyin", StreamStatus.NotStreaming, false)]
    [InlineData("TikTok", StreamStatus.Initialized, false)]
    public void ShouldLeaveInitializationAfterInconclusiveCheck_OnlyChangesInitialDouyinState(
        string platformName,
        StreamStatus streamStatus,
        bool expected)
    {
        Assert.Equal(expected, GlobalMonitor.ShouldLeaveInitializationAfterInconclusiveCheck(platformName, streamStatus));
    }

    [Theory]
    [InlineData("Douyin", "https://example.test/live.flv?t_id=037-20260722073816ABC", 0, StreamStatus.Streaming)]
    [InlineData("Douyin", "https://example.test/live.flv?t_id=037-20260722073816ABC", 121, StreamStatus.Initialized)]
    [InlineData("Douyin", "https://example.test/live.flv", 0, StreamStatus.Initialized)]
    [InlineData("TikTok", "https://example.test/live.flv?t_id=037-20260722073816ABC", 0, StreamStatus.Initialized)]
    public void ResolveInitialStreamStatus_OnlyTrustsRecentDouyinStreamUrls(
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
}
