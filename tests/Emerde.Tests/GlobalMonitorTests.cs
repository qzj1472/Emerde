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

        try
        {
            Configurations.RoutineInterval.Set(60_000);
            Configurations.IsToMonitor.Set(true);
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
        }
    }
}
