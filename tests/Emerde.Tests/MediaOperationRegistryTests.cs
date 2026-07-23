using Emerde.Core;

namespace Emerde.Tests;

public sealed class MediaOperationRegistryTests
{
    [Fact]
    public void Register_ProtectsExactAndSegmentPathsUntilDisposed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-operation-{Guid.NewGuid():N}");
        string exact = Path.Combine(directory, "record.ts");
        string pattern = Path.Combine(directory, "segment_%03d.ts");
        int initialCount = MediaOperationRegistry.ActiveCount;

        using (MediaOperationRegistry.Register(MediaOperationKind.Recording, () => [exact, pattern]))
        {
            Assert.Equal(initialCount + 1, MediaOperationRegistry.ActiveCount);
            Assert.True(MediaOperationRegistry.IsPathProtected(exact));
            Assert.True(MediaOperationRegistry.IsPathProtected(Path.Combine(directory, "segment_001.ts")));
            Assert.True(MediaOperationRegistry.IsPathProtected(Path.Combine(directory, "segment_1000.ts")));
            Assert.False(MediaOperationRegistry.IsPathProtected(Path.Combine(directory, "other.ts")));
        }

        Assert.Equal(initialCount, MediaOperationRegistry.ActiveCount);
        Assert.False(MediaOperationRegistry.IsPathProtected(exact));
    }

    [Fact]
    public void Cancel_InvokesMatchingOperationOnly()
    {
        int recordingsCancelled = 0;
        int conversionsCancelled = 0;
        using IDisposable recording = MediaOperationRegistry.Register(
            MediaOperationKind.Recording,
            () => [],
            () => recordingsCancelled++);
        using IDisposable conversion = MediaOperationRegistry.Register(
            MediaOperationKind.Conversion,
            () => [],
            () => conversionsCancelled++);

        MediaOperationRegistry.Cancel(MediaOperationKind.Conversion);

        Assert.Equal(0, recordingsCancelled);
        Assert.Equal(1, conversionsCancelled);
    }

    [Fact]
    public async Task WaitForCompletionAsync_WaitsForRegistrationToDispose()
    {
        IDisposable operation = MediaOperationRegistry.Register(MediaOperationKind.Split, () => []);
        Task waitTask = MediaOperationRegistry.WaitForCompletionAsync(TimeSpan.FromSeconds(2));

        Assert.False(waitTask.IsCompleted);
        operation.Dispose();
        await waitTask;

        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void Register_RaisesStartedAndCompletedNotifications()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-operation-{Guid.NewGuid():N}.ts");
        List<bool> states = [];
        EventHandler<MediaOperationsChangedEventArgs> handler = (_, e) =>
        {
            if (e.Kind == MediaOperationKind.Merge && e.Paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                states.Add(e.IsActive);
            }
        };
        MediaOperationRegistry.OperationsChanged += handler;
        try
        {
            using (MediaOperationRegistry.Register(MediaOperationKind.Merge, () => [path]))
            {
            }
        }
        finally
        {
            MediaOperationRegistry.OperationsChanged -= handler;
        }

        Assert.Equal([true, false], states);
    }
}
