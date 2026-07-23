using System.Diagnostics;

namespace Emerde.Threading;

public class PeriodicWait
{
    private readonly object periodLock = new();
    private CancellationTokenSource periodChanged = new();
    private TimeSpan period;
    private bool initialized;

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
            CancellationTokenSource previous;
            lock (periodLock)
            {
                period = value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : value;
                previous = periodChanged;
                periodChanged = new CancellationTokenSource();
            }
            previous.Cancel();
            previous.Dispose();
        }
    }

    public PeriodicWait(TimeSpan period, TimeSpan initialDelay = default)
    {
        InitialDelay = initialDelay;
        this.period = period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period;
    }

    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
    {
        if (!initialized)
        {
            initialized = true;
            return InitialDelay <= TimeSpan.Zero
                ? !cancellationToken.IsCancellationRequested
                : await DelayAsync(InitialDelay, cancellationToken, default);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan currentPeriod;
            CancellationToken changeToken;
            lock (periodLock)
            {
                currentPeriod = period;
                changeToken = periodChanged.Token;
            }

            TimeSpan remaining = currentPeriod - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return true;
            }

            if (!await DelayAsync(remaining, cancellationToken, changeToken) && cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        return false;
    }

    private static async ValueTask<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken, CancellationToken changeToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, changeToken);
        try
        {
            await Task.Delay(delay, linked.Token);
            return true;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return false;
        }
    }
}
