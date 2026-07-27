using Emerde.Threading;

namespace Emerde.Tests;

public sealed class ChildProcessTracerPeriodicTimerTests
{
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
}
