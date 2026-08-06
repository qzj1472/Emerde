using System.Diagnostics;
using Emerde.Core;

namespace Emerde.Tests;

public sealed class RuntimeResourceLoggerTests
{
    [Fact]
    public void CalculateCpuPercent_ClampsRegressedCpuTime()
    {
        double cpuPercent = RuntimeResourceLogger.CalculateCpuPercent(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            30,
            8);

        Assert.Equal(0, cpuPercent);
    }

    [Fact]
    public void CalculateCpuPercent_NormalizesInvalidSamplingBounds()
    {
        double cpuPercent = RuntimeResourceLogger.CalculateCpuPercent(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.Zero,
            0,
            0);

        Assert.Equal(100, cpuPercent);
    }

    [Fact]
    public void Stop_AllowsSamplerToRestart()
    {
        for (int index = 0; index < 20; index++)
        {
            RuntimeResourceLogger.Start();
            RuntimeResourceLogger.Stop();
        }

        RuntimeResourceLogger.Start();
        RuntimeResourceLogger.Stop();
    }

    [Fact]
    public void RegisterAndUnregister_StartsAndStopsSamplerOnDemand()
    {
        using Process process = Process.GetCurrentProcess();
        try
        {
            RuntimeResourceLogger.Register(process, "test", "lifecycle");

            Assert.True(RuntimeResourceLogger.IsRunningForTest);
            Assert.Equal(1, RuntimeResourceLogger.RegisteredProcessCountForTest);

            RuntimeResourceLogger.Unregister(process.Id);

            Assert.False(RuntimeResourceLogger.IsRunningForTest);
            Assert.Equal(0, RuntimeResourceLogger.RegisteredProcessCountForTest);
        }
        finally
        {
            RuntimeResourceLogger.Stop();
        }
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
