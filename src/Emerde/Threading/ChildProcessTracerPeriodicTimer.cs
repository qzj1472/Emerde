using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Emerde.Core;
using Vanara.PInvoke;

namespace Emerde.Threading;

public partial class ChildProcessTracerPeriodicTimer(TimeSpan period) : IDisposable
{
    public static ChildProcessTracerPeriodicTimer Default { get; } = new(TimeSpan.FromMilliseconds(500));

    public ChildProcessTracer Tracer { get; } = new ChildProcessTracer();
    public PeriodicTimer PeriodicTimer { get; } = new PeriodicTimer(period);
    public CancellationTokenSource? TokenSource { get; protected set; } = null;
    public HashSet<int> TracedChildProcessIds { get; } = [];
    private string[]? whiteList;

    public IEnumerable<string>? WhiteList
    {
        get => Volatile.Read(ref whiteList)?.ToArray();
        set => Volatile.Write(ref whiteList, value?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    private readonly object syncRoot = new();
    private Task? workerTask = null;
    private bool ownsTokenSource;
    private bool disposed;

    public void Start(CancellationTokenSource? tokenSource = null)
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (workerTask is { IsCompleted: false })
            {
                return;
            }

            if (ownsTokenSource)
            {
                TokenSource?.Dispose();
            }
            CancellationTokenSource activeSource = tokenSource ?? new CancellationTokenSource();
            TokenSource = activeSource;
            ownsTokenSource = tokenSource == null;
            workerTask = Task.Factory.StartNew(
                () => StartAsync(activeSource.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            ).Unwrap();
        }
    }

    private async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            lock (syncRoot)
            {
                TracedChildProcessIds.Clear();
            }

            while (await PeriodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                (int Id, string ProcessName)[] children = Interop.GetChildProcessIdAndName(Environment.ProcessId);

                foreach ((int childId, string childProcessName) in children)
                {
                    if (!IsAllowedProcess(childProcessName))
                    {
                        continue;
                    }

                    TryTraceProcess(childId);
                }

                lock (syncRoot)
                {
                    TracedChildProcessIds.RemoveWhere(tracedChildProcessId =>
                       !children.Any(child => child.Id == tracedChildProcessId)
                    );
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (disposed)
        {
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    public bool TryTraceProcess(Process process)
    {
        try
        {
            if (process.HasExited || !IsAllowedProcess(process.ProcessName))
            {
                return false;
            }

            return TryTraceProcess(process.Id, process);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or Win32Exception)
        {
            return false;
        }
    }

    private bool TryTraceProcess(int processId, Process? process = null)
    {
        try
        {
            using Process? ownedProcess = process == null ? Process.GetProcessById(processId) : null;
            Process childProcess = process ?? ownedProcess!;

            if (childProcess.HasExited || childProcess.Handle == nint.Zero || !IsAllowedProcess(childProcess.ProcessName))
            {
                return false;
            }

            lock (syncRoot)
            {
                if (TracedChildProcessIds.Contains(childProcess.Id))
                {
                    return true;
                }

                if (!Tracer.TryAddChildProcess(childProcess.Handle))
                {
                    return false;
                }

                TracedChildProcessIds.Add(childProcess.Id);
            }
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or Win32Exception)
        {
            lock (syncRoot)
            {
                _ = TracedChildProcessIds.Remove(processId);
            }
            return false;
        }
    }

    private bool IsAllowedProcess(string processName)
    {
        if (processName.Equals("conhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[]? allowedProcesses = Volatile.Read(ref whiteList);
        return allowedProcesses == null
            || allowedProcesses.Length == 0
            || allowedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }

    public void Stop(bool killChildren = false)
    {
        try
        {
            TokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (killChildren)
        {
            KillTracedProcesses();
        }
    }

    public void KillTracedProcesses()
    {
        int[] processIds;
        lock (syncRoot)
        {
            processIds = [.. TracedChildProcessIds];
        }

        foreach (int processId in processIds)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (!process.HasExited && IsAllowedProcess(process.ProcessName))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or Win32Exception)
            {
            }
        }

        lock (syncRoot)
        {
            TracedChildProcessIds.Clear();
        }
    }

    public HashSet<int> GetTracedProcessIds()
    {
        lock (syncRoot)
        {
            return [.. TracedChildProcessIds];
        }
    }

    [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize")]
    [SuppressMessage("CodeQuality", "IDE0079:Remove unnecessary suppression")]
    public void Dispose()
    {
        Task? stoppingWorker;
        CancellationTokenSource? stoppingSource;
        bool disposeSource;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stoppingWorker = workerTask;
            stoppingSource = TokenSource;
            disposeSource = ownsTokenSource;
            workerTask = null;
            TokenSource = null;
            ownsTokenSource = false;
        }

        try
        {
            stoppingSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        PeriodicTimer.Dispose();
        try
        {
            _ = stoppingWorker?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        if (disposeSource)
        {
            stoppingSource?.Dispose();
        }
        Tracer.Dispose();
    }
}

/// <summary>
/// Make sures that child processes are automatically terminated
/// if the parent process exits unexpectedly.
/// </summary>
public partial class ChildProcessTracer : IDisposable
{
    private readonly Kernel32.SafeHJOB? hJob;

    public ChildProcessTracer()
    {
        if (Environment.OSVersion.Version < new Version(6, 2))
        {
            return;
        }

        hJob = Kernel32.CreateJobObject(null, $"{nameof(ChildProcessTracer)}-{Environment.ProcessId}");

        Kernel32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION extendedInfo = new()
        {
            BasicLimitInformation = new Kernel32.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = Kernel32.JOBOBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int length = Marshal.SizeOf(extendedInfo);
        nint extendedInfoPtr = Marshal.AllocHGlobal(length);

        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

            if (!Kernel32.SetInformationJobObject(hJob, Kernel32.JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length))
            {
                Debug.WriteLine($"Failed to set information for job object. Error: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            // Free allocated memory after job object is set.
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    public void Dispose()
    {
        hJob?.Dispose();
    }

    /// <summary>
    /// Adds a process to the tracking job. If the parent process is terminated, this process will also be automatically terminated.
    /// </summary>
    /// <param name="hProcess">The child process to be tracked.</param>
    /// <exception cref="ArgumentNullException">Thrown when the process argument is null.</exception>
    public void AddChildProcess(nint hProcess)
    {
        _ = TryAddChildProcess(hProcess);
    }

    public bool TryAddChildProcess(nint hProcess)
    {
        if (hJob != null && !hJob.IsInvalid)
        {
            if (!Kernel32.AssignProcessToJobObject(hJob, hProcess))
            {
                Debug.WriteLine($"Failed to assign process to job object. Error: {Marshal.GetLastWin32Error()}");
                return false;
            }

            return true;
        }

        return false;
    }
}
