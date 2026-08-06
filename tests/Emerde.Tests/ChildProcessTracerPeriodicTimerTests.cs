using Emerde.Threading;

namespace Emerde.Tests;

public sealed class ChildProcessTracerPeriodicTimerTests
{
    [Fact]
    public void DefaultFallbackPeriod_IsLowFrequency()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), ChildProcessTracerPeriodicTimer.DefaultFallbackPeriod);
    }

    [Fact]
    public void Dispose_StopsAnActiveWorkerAndPreventsRestart()
    {
        for (int index = 0; index < 20; index++)
        {
            ChildProcessTracerPeriodicTimer activeTimer = new(TimeSpan.FromMilliseconds(10));
            activeTimer.Start();
            activeTimer.Dispose();
        }

        ChildProcessTracerPeriodicTimer timer = new(TimeSpan.FromMilliseconds(10));
        timer.Dispose();


        Assert.Throws<ObjectDisposedException>(() => timer.Start());
    }

    [Fact]
    public void Dispose_DoesNotDisposeCallerOwnedCancellationSource()
    {
        using CancellationTokenSource source = new();
        ChildProcessTracerPeriodicTimer timer = new(TimeSpan.FromMilliseconds(10));
        timer.Start(source);

        timer.Dispose();

        Assert.True(source.IsCancellationRequested);
        source.Token.Register(static () => { }).Dispose();
    }

    [Fact]
    public void Stop_AllowsRestartWithAFreshCancellationSource()
    {
        using ChildProcessTracerPeriodicTimer timer = new(TimeSpan.FromSeconds(5));
        timer.Start();
        CancellationTokenSource firstSource = timer.TokenSource!;

        timer.Stop();
        timer.Start();

        Assert.True(firstSource.IsCancellationRequested);
        Assert.NotSame(firstSource, timer.TokenSource);
        Assert.False(timer.TokenSource!.IsCancellationRequested);
    }
}
