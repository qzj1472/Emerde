using System.Collections.Concurrent;
using System.ComponentModel;
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
    private static long lastNetworkSampleTimestamp;
    private static long lastNetworkReceivedBytes;
    private static long lastNetworkSentBytes;
    private static DateTime lastSnapshotAt = DateTime.MinValue;
    private static string lastSnapshotProcessSignature = string.Empty;
    private static double lastSnapshotRamMb;

    internal static bool IsRunningForTest
    {
        get
        {
            lock (SyncRoot)
            {
                return workerTask is { IsCompleted: false };
            }
        }
    }

    internal static int RegisteredProcessCountForTest => Processes.Count;

    public static void Start()
    {
        lock (SyncRoot)
        {
            StartLocked();
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
            tokenSource = null;
            workerTask = null;
            Processes.Clear();
            ResetSamplingStateLocked();
        }

        stoppingTokenSource?.Cancel();

        bool completed = true;
        try
        {
            completed = stoppingWorkerTask?.Wait(TimeSpan.FromSeconds(2)) ?? true;
        }
        catch (AggregateException)
        {
        }

        if (!completed && stoppingWorkerTask != null)
        {
            _ = stoppingWorkerTask.ContinueWith(
                _ => stoppingTokenSource?.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
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

            DateTime startedAt = DateTime.Now;
            long startedTimestamp = Stopwatch.GetTimestamp();
            RuntimeProcessContext context = new(
                process.Id,
                process.ProcessName,
                processKind,
                purpose,
                roomUrl,
                nickName ?? string.Empty,
                startedAt,
                startedTimestamp,
                process.TotalProcessorTime,
                startedTimestamp);
            lock (SyncRoot)
            {
                Processes[process.Id] = context;
                StartLocked();
            }
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
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or Win32Exception)
        {
        }
    }

    public static void Unregister(int processId)
    {
        CancellationTokenSource? idleSource = null;
        Task? idleWorker = null;
        lock (SyncRoot)
        {
            _ = Processes.TryRemove(processId, out _);
            if (!Processes.IsEmpty)
            {
                return;
            }

            idleSource = tokenSource;
            idleWorker = workerTask;
            tokenSource = null;
            workerTask = null;
            ResetSamplingStateLocked();
        }

        idleSource?.Cancel();
        if (idleSource != null)
        {
            _ = (idleWorker ?? Task.CompletedTask).ContinueWith(
                _ => idleSource.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static void StartLocked()
    {
        if (workerTask is { IsCompleted: false })
        {
            return;
        }

        tokenSource?.Dispose();
        CancellationTokenSource activeSource = new();
        tokenSource = activeSource;
        workerTask = Task.Run(() => RunAsync(activeSource));
    }

    private static void ResetSamplingStateLocked()
    {
        lastNetworkSampleTimestamp = 0;
        lastNetworkReceivedBytes = 0;
        lastNetworkSentBytes = 0;
        lastSnapshotAt = DateTime.MinValue;
        lastSnapshotProcessSignature = string.Empty;
        lastSnapshotRamMb = 0;
    }

    private static async Task RunAsync(CancellationTokenSource source)
    {
        try
        {
            while (!source.IsCancellationRequested)
            {
                await Task.Delay(SampleInterval, source.Token);
                lock (SyncRoot)
                {
                    if (!ReferenceEquals(tokenSource, source))
                    {
                        return;
                    }
                }
                try
                {
                    Sample();
                }
                catch (Exception e)
                {
                    AppSessionLogger.WriteException(e);
                }

                if (Processes.IsEmpty)
                {
                    Unregister(0);
                    return;
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
                    _ = Processes.TryRemove(new KeyValuePair<int, RuntimeProcessContext>(context.ProcessId, context));
                    continue;
                }

                DateTime now = DateTime.Now;
                long nowTimestamp = Stopwatch.GetTimestamp();
                TimeSpan totalCpu = process.TotalProcessorTime;
                double elapsedSeconds = Math.Max(0.001d, Stopwatch.GetElapsedTime(context.LastSampleTimestamp, nowTimestamp).TotalSeconds);
                double cpuPercent = CalculateCpuPercent(totalCpu, context.LastCpuTime, elapsedSeconds, Environment.ProcessorCount);
                double workingSetMb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 2);

                RuntimeProcessContext updatedContext = context with
                {
                    LastCpuTime = totalCpu,
                    LastSampleTimestamp = nowTimestamp,
                };
                if (!Processes.TryUpdate(context.ProcessId, updatedContext, context))
                {
                    continue;
                }

                samples.Add(new RuntimeProcessSample(
                    context.RoomUrl,
                    context.NickName,
                    context.ProcessKind,
                    context.Purpose,
                    context.ProcessName,
                    context.ProcessId,
                    cpuPercent,
                    workingSetMb,
                    context.StartedAt,
                    Math.Round(Stopwatch.GetElapsedTime(context.StartedTimestamp, nowTimestamp).TotalSeconds, 1)));
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or Win32Exception)
            {
                _ = Processes.TryRemove(new KeyValuePair<int, RuntimeProcessContext>(context.ProcessId, context));
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

    internal static double CalculateCpuPercent(TimeSpan totalCpu, TimeSpan previousCpu, double elapsedSeconds, int processorCount)
    {
        double safeElapsedSeconds = Math.Max(0.001d, elapsedSeconds);
        int safeProcessorCount = Math.Max(1, processorCount);
        double cpuMilliseconds = Math.Max(0d, (totalCpu - previousCpu).TotalMilliseconds);
        return Math.Round(Math.Clamp(cpuMilliseconds / (safeElapsedSeconds * safeProcessorCount * 10d), 0d, 100d), 2);
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

            long nowTimestamp = Stopwatch.GetTimestamp();
            if (lastNetworkSampleTimestamp == 0)
            {
                lastNetworkSampleTimestamp = nowTimestamp;
                lastNetworkReceivedBytes = received;
                lastNetworkSentBytes = sent;
                return NetworkSample.Empty;
            }

            double seconds = Math.Max(0.001d, Stopwatch.GetElapsedTime(lastNetworkSampleTimestamp, nowTimestamp).TotalSeconds);
            long receivedDelta = Math.Max(0, received - lastNetworkReceivedBytes);
            long sentDelta = Math.Max(0, sent - lastNetworkSentBytes);

            lastNetworkSampleTimestamp = nowTimestamp;
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
        long StartedTimestamp,
        TimeSpan LastCpuTime,
        long LastSampleTimestamp);

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
