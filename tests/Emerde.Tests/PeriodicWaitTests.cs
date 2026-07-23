using Emerde.Threading;
using System.Diagnostics;

namespace Emerde.Tests;

public sealed class PeriodicWaitTests
{
    [Fact]
    public async Task FirstTick_WithZeroInitialDelay_CompletesImmediately()
    {
        PeriodicWait wait = new(TimeSpan.FromSeconds(1));

        bool result = await wait.WaitForNextTickAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task InitialDelay_StopsPromptlyWhenCancelled()
    {
        PeriodicWait wait = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        using CancellationTokenSource source = new(TimeSpan.FromMilliseconds(50));
        Stopwatch stopwatch = Stopwatch.StartNew();

        bool result = await wait.WaitForNextTickAsync(source.Token);

        Assert.False(result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LaterTick_UsesUpdatedPeriod()
    {
        PeriodicWait wait = new(TimeSpan.FromSeconds(5));
        Assert.True(await wait.WaitForNextTickAsync(CancellationToken.None));
        wait.Period = TimeSpan.FromMilliseconds(40);
        Stopwatch stopwatch = Stopwatch.StartNew();

        bool result = await wait.WaitForNextTickAsync(CancellationToken.None);

        Assert.True(result);
        Assert.InRange(stopwatch.ElapsedMilliseconds, 20, 1000);
    }

    [Fact]
    public async Task ActiveWait_RespondsToPeriodChange()
    {
        PeriodicWait wait = new(TimeSpan.FromSeconds(5));
        Assert.True(await wait.WaitForNextTickAsync(CancellationToken.None));
        Stopwatch stopwatch = Stopwatch.StartNew();
        ValueTask<bool> pendingTick = wait.WaitForNextTickAsync(CancellationToken.None);

        await Task.Delay(50);
        wait.Period = TimeSpan.FromMilliseconds(40);
        bool result = await pendingTick;

        Assert.True(result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }
}
