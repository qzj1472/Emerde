using System.Diagnostics;

namespace Emerde.Threading;

public sealed class PeriodicWait : IDisposable
{
    private readonly object periodLock = new();
    private TaskCompletionSource periodChanged = CreateSignal();
    private TimeSpan period;
    private int initialized;
    private int disposed;

    public TimeSpan InitialDelay { get; set; }

    public TimeSpan Period
    {
        get
        {
            lock (periodLock)
            {
                return period;
            }
        }
        set
        {
            TaskCompletionSource previous;
            lock (periodLock)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                TimeSpan normalized = value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : value;
                if (period == normalized)
                {
                    return;
                }

                period = normalized;
                previous = periodChanged;
                periodChanged = CreateSignal();
            }
            previous.TrySetResult();
        }
    }

    public PeriodicWait(TimeSpan period, TimeSpan initialDelay = default)
    {
        InitialDelay = initialDelay;
        this.period = period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period;
    }

    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref initialized, 1) == 0)
        {
            return InitialDelay <= TimeSpan.Zero
                ? !cancellationToken.IsCancellationRequested
                : await DelayAsync(InitialDelay, cancellationToken);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan currentPeriod;
            Task changeTask;
            lock (periodLock)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                currentPeriod = period;
                changeTask = periodChanged.Task;
            }

            TimeSpan remaining = currentPeriod - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return true;
            }

            Task delayTask = Task.Delay(remaining, cancellationToken);
            Task completed = await Task.WhenAny(delayTask, changeTask);
            if (completed == delayTask)
            {
                return !cancellationToken.IsCancellationRequested;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        TaskCompletionSource signal;
        lock (periodLock)
        {
            signal = periodChanged;
        }
        signal.TrySetResult();
    }

    private static async ValueTask<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static TaskCompletionSource CreateSignal()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
