namespace Emerde.Core;

internal readonly record struct ConversionConcurrencyProfile(
    int TotalLimit);

internal static class ConversionWorkScheduler
{
    private static readonly ConversionConcurrencyProfile IdleProfile = new(4);
    private static readonly ConversionConcurrencyProfile RecordingProfile = new(2);
    private static readonly SemaphoreSlim TotalConcurrency = new(IdleProfile.TotalLimit, IdleProfile.TotalLimit);
    private static readonly SemaphoreSlim RecordingTotalConcurrency = new(RecordingProfile.TotalLimit, RecordingProfile.TotalLimit);

    public static async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        List<SemaphoreSlim> acquired = new(2);
        try
        {
            await AcquireAsync(TotalConcurrency, acquired, token);
            if (GlobalMonitor.HasActiveRecorders)
            {
                await AcquireAsync(RecordingTotalConcurrency, acquired, token);
            }
            return new Lease(acquired);
        }
        catch
        {
            Release(acquired);
            throw;
        }
    }

    internal static ConversionConcurrencyProfile GetProfile(bool hasActiveRecorders)
    {
        return hasActiveRecorders ? RecordingProfile : IdleProfile;
    }

    private static async Task AcquireAsync(
        SemaphoreSlim semaphore,
        ICollection<SemaphoreSlim> acquired,
        CancellationToken token)
    {
        await semaphore.WaitAsync(token);
        acquired.Add(semaphore);
    }

    private static void Release(IReadOnlyList<SemaphoreSlim> acquired)
    {
        for (int index = acquired.Count - 1; index >= 0; index--)
        {
            acquired[index].Release();
        }
    }

    private sealed class Lease(IReadOnlyList<SemaphoreSlim> acquired) : IDisposable
    {
        private IReadOnlyList<SemaphoreSlim>? held = acquired;

        public void Dispose()
        {
            IReadOnlyList<SemaphoreSlim>? released = Interlocked.Exchange(ref held, null);
            if (released != null)
            {
                Release(released);
            }
        }
    }
}
