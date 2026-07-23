using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Emerde.Core;

internal static class RuntimeResourceLogger
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan SnapshotMinimumInterval = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan SnapshotForceInterval = TimeSpan.FromMinutes(5);
    internal const double SnapshotRamDeltaMb = 128d;
    private static readonly ConcurrentDictionary<int, RuntimeProcessContext> Processes = new();
    private static readonly object SyncRoot = new();
    private static CancellationTokenSource? tokenSource;
    private static Task? workerTask;
    private static DateTime lastNetworkSampleAt = DateTime.MinValue;
    private static long lastNetworkReceivedBytes;
    private static long lastNetworkSentBytes;
    private static DateTime lastSnapshotAt = DateTime.MinValue;
    private static string lastSnapshotProcessSignature = string.Empty;
    private static double lastSnapshotRamMb;

    public static void Start()
    {
        lock (SyncRoot)
        {
            if (workerTask is { IsCompleted: false })
            {
                return;
            }

            tokenSource = new CancellationTokenSource();
            workerTask = Task.Run(() => RunAsync(tokenSource.Token));
        }
    }

    public static void Stop()
    {
        CancellationTokenSource? stoppingTokenSource;
        Task? stoppingWorkerTask;

        lock (SyncRoot)
        {
            stoppingTokenSource = tokenSource;
            stoppingWorkerTask = workerTask;
            stoppingTokenSource?.Cancel();
        }

        try
        {
            _ = stoppingWorkerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        lock (SyncRoot)
        {
            if (ReferenceEquals(tokenSource, stoppingTokenSource))
            {
                tokenSource = null;
            }
            if (ReferenceEquals(workerTask, stoppingWorkerTask))
            {
                workerTask = null;
            }

            Processes.Clear();
            lastNetworkSampleAt = DateTime.MinValue;
            lastNetworkReceivedBytes = 0;
            lastNetworkSentBytes = 0;
            lastSnapshotAt = DateTime.MinValue;
            lastSnapshotProcessSignature = string.Empty;
            lastSnapshotRamMb = 0;
        }

        stoppingTokenSource?.Dispose();
    }

    public static void Register(Process process, string processKind, string purpose, string roomUrl = "", string? nickName = null, object? extra = null)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            RuntimeProcessContext context = new(
                process.Id,
                process.ProcessName,
                processKind,
                purpose,
                roomUrl,
                nickName ?? string.Empty,
                DateTime.Now,
                process.TotalProcessorTime,
                DateTime.Now);
            Processes[process.Id] = context;
            AppSessionLogger.Event("info", "runtime", "process_registered", "runtime process registered", new
            {
                context.ProcessId,
                context.ProcessName,
                context.ProcessKind,
                context.Purpose,
                context.RoomUrl,
                context.NickName,
                extra,
            });
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
        }
    }

    private static async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(SampleInterval, token);
                try
                {
                    Sample();
                }
                catch (Exception e)
                {
                    AppSessionLogger.WriteException(e);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void Sample()
    {
        RuntimeProcessContext[] contexts = Processes.Values.ToArray();
        if (contexts.Length == 0)
        {
            return;
        }

        NetworkSample network = GetNetworkSample();
        List<RuntimeProcessSample> samples = [];

        foreach (RuntimeProcessContext context in contexts)
        {
            try
            {
                using Process process = Process.GetProcessById(context.ProcessId);
                if (process.HasExited)
                {
                    _ = Processes.TryRemove(context.ProcessId, out _);
                    continue;
                }

                DateTime now = DateTime.Now;
                TimeSpan totalCpu = process.TotalProcessorTime;
                double elapsedSeconds = Math.Max(0.001d, (now - context.LastSampleAt).TotalSeconds);
                double cpuPercent = Math.Round((totalCpu - context.LastCpuTime).TotalMilliseconds / (elapsedSeconds * Environment.ProcessorCount * 10d), 2);

                Processes[context.ProcessId] = context with
                {
                    LastCpuTime = totalCpu,
                    LastSampleAt = now,
                };

                samples.Add(new RuntimeProcessSample(
                    context.RoomUrl,
                    context.NickName,
                    context.ProcessKind,
                    context.Purpose,
                    context.ProcessName,
                    context.ProcessId,
                    cpuPercent,
                    Math.Round(process.WorkingSet64 / 1024d / 1024d, 2),
                    context.StartedAt,
                    Math.Round((now - context.StartedAt).TotalSeconds, 1)));
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException)
            {
                _ = Processes.TryRemove(context.ProcessId, out _);
            }
        }

        if (samples.Count == 0)
        {
            return;
        }

        using Process current = Process.GetCurrentProcess();
        double ramMb = Math.Round(current.WorkingSet64 / 1024d / 1024d, 2);
        DateTime snapshotAt = DateTime.Now;
        string processSignature = BuildProcessSignature(samples);
        if (!ShouldWriteSnapshot(snapshotAt, processSignature, ramMb))
        {
            return;
        }

        lastSnapshotAt = snapshotAt;
        lastSnapshotProcessSignature = processSignature;
        lastSnapshotRamMb = ramMb;
        AppSessionLogger.Event("info", "runtime", "resource_snapshot", "runtime resource snapshot", new
        {
            application = new
            {
                processId = Environment.ProcessId,
                cpuTimeSeconds = Math.Round(current.TotalProcessorTime.TotalSeconds, 2),
                ramMb,
                threadCount = current.Threads.Count,
            },
            network = network.IsValid ? new
            {
                receiveMbps = network.ReceiveMbps,
                sendMbps = network.SendMbps,
                intervalSeconds = network.IntervalSeconds,
            } : null,
            gpu = new
            {
                available = false,
                reason = "gpu sampling is skipped to avoid extra runtime overhead and compatibility issues",
            },
            processes = samples.Select(sample => new
            {
                sample.RoomUrl,
                sample.NickName,
                sample.ProcessKind,
                sample.Purpose,
                sample.ProcessName,
                sample.ProcessId,
                cpuPercent = sample.CpuPercent,
                ramMb = sample.RamMb,
                startedAt = sample.StartedAt,
                runningSeconds = sample.RunningSeconds,
            }).ToArray(),
        });
    }

    internal static bool ShouldWriteSnapshot(DateTime now, string processSignature, double ramMb)
    {
        if (lastSnapshotAt == DateTime.MinValue)
        {
            return true;
        }

        if (!string.Equals(lastSnapshotProcessSignature, processSignature, StringComparison.Ordinal))
        {
            return true;
        }

        TimeSpan elapsed = now - lastSnapshotAt;
        if (elapsed >= SnapshotForceInterval)
        {
            return true;
        }

        return elapsed >= SnapshotMinimumInterval
            && Math.Abs(ramMb - lastSnapshotRamMb) >= SnapshotRamDeltaMb;
    }

    internal static void SetSnapshotStateForTest(DateTime snapshotAt, string processSignature, double ramMb)
    {
        lastSnapshotAt = snapshotAt;
        lastSnapshotProcessSignature = processSignature;
        lastSnapshotRamMb = ramMb;
    }

    private static string BuildProcessSignature(IEnumerable<RuntimeProcessSample> samples)
    {
        return string.Join("|", samples
            .Select(sample => $"{sample.ProcessKind}:{sample.Purpose}:{sample.ProcessId}")
            .OrderBy(value => value, StringComparer.Ordinal));
    }

    private static NetworkSample GetNetworkSample()
    {
        try
        {
            long received = 0;
            long sent = 0;
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                IPv4InterfaceStatistics stats = networkInterface.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }

            DateTime now = DateTime.Now;
            if (lastNetworkSampleAt == DateTime.MinValue)
            {
                lastNetworkSampleAt = now;
                lastNetworkReceivedBytes = received;
                lastNetworkSentBytes = sent;
                return NetworkSample.Empty;
            }

            double seconds = Math.Max(0.001d, (now - lastNetworkSampleAt).TotalSeconds);
            long receivedDelta = Math.Max(0, received - lastNetworkReceivedBytes);
            long sentDelta = Math.Max(0, sent - lastNetworkSentBytes);

            lastNetworkSampleAt = now;
            lastNetworkReceivedBytes = received;
            lastNetworkSentBytes = sent;

            return new NetworkSample(
                true,
                Math.Round(receivedDelta * 8d / seconds / 1_000_000d, 3),
                Math.Round(sentDelta * 8d / seconds / 1_000_000d, 3),
                Math.Round(seconds, 1));
        }
        catch
        {
            return NetworkSample.Empty;
        }
    }

    private sealed record RuntimeProcessContext(
        int ProcessId,
        string ProcessName,
        string ProcessKind,
        string Purpose,
        string RoomUrl,
        string NickName,
        DateTime StartedAt,
        TimeSpan LastCpuTime,
        DateTime LastSampleAt);

    private sealed record RuntimeProcessSample(
        string RoomUrl,
        string NickName,
        string ProcessKind,
        string Purpose,
        string ProcessName,
        int ProcessId,
        double CpuPercent,
        double RamMb,
        DateTime StartedAt,
        double RunningSeconds);

    private sealed record NetworkSample(bool IsValid, double ReceiveMbps, double SendMbps, double IntervalSeconds)
    {
        public static NetworkSample Empty { get; } = new(false, 0, 0, 0);
    }
}
