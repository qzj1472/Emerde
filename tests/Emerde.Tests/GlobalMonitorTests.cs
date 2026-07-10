using Emerde.Core;

namespace Emerde.Tests;

public sealed class GlobalMonitorTests
{
    [Fact]
    public void GetEffectiveRoutineInterval_UsesShortestEnabledRoomInterval()
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

            Assert.Equal(500, GlobalMonitor.GetEffectiveRoutineInterval());
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
}
