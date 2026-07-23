using Emerde.Core;

namespace Emerde.Tests;

public sealed class RuntimeResourceLoggerTests
{
    [Fact]
    public void Stop_AllowsSamplerToRestart()
    {
        RuntimeResourceLogger.Start();
        RuntimeResourceLogger.Stop();
        RuntimeResourceLogger.Start();
        RuntimeResourceLogger.Stop();
    }

    [Fact]
    public void ShouldWriteSnapshot_SuppressesRepeatedStableSnapshots()
    {
        DateTime now = new(2026, 7, 24, 2, 0, 0);
        try
        {
            RuntimeResourceLogger.SetSnapshotStateForTest(now, "ffmpeg:record:1", 300);

            Assert.False(RuntimeResourceLogger.ShouldWriteSnapshot(now + TimeSpan.FromSeconds(30), "ffmpeg:record:1", 301));
            Assert.True(RuntimeResourceLogger.ShouldWriteSnapshot(now + TimeSpan.FromSeconds(30), "ffmpeg:record:2", 301));
            Assert.True(RuntimeResourceLogger.ShouldWriteSnapshot(now + RuntimeResourceLogger.SnapshotForceInterval, "ffmpeg:record:1", 301));
            Assert.True(RuntimeResourceLogger.ShouldWriteSnapshot(now + RuntimeResourceLogger.SnapshotMinimumInterval, "ffmpeg:record:1", 430));
        }
        finally
        {
            RuntimeResourceLogger.Stop();
        }
    }
}
