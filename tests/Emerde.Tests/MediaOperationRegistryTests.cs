using Emerde.Core;

namespace Emerde.Tests;

[Collection("MediaOperationRegistry")]
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
    public void Cancel_ByPathInvokesOnlyMatchingOperation()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-operation-{Guid.NewGuid():N}");
        string first = Path.Combine(directory, "first.ts");
        string second = Path.Combine(directory, "second.ts");
        int firstCancelled = 0;
        int secondCancelled = 0;
        using IDisposable firstConversion = MediaOperationRegistry.Register(
            MediaOperationKind.Conversion,
            () => [first],
            () => firstCancelled++);
        using IDisposable secondConversion = MediaOperationRegistry.Register(
            MediaOperationKind.Conversion,
            () => [second],
            () => secondCancelled++);

        int cancelled = MediaOperationRegistry.Cancel(MediaOperationKind.Conversion, first);

        Assert.Equal(1, cancelled);
        Assert.Equal(1, firstCancelled);
        Assert.Equal(0, secondCancelled);
    }

    [Fact]
    public void Cancel_ByMultiplePathsCancelsEachMatchingOperationOnce()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-operation-{Guid.NewGuid():N}");
        string first = Path.Combine(directory, "first.ts");
        string second = Path.Combine(directory, "second.ts");
        string unrelated = Path.Combine(directory, "unrelated.ts");
        int matchingCancelled = 0;
        int unrelatedCancelled = 0;
        using IDisposable matching = MediaOperationRegistry.Register(
            MediaOperationKind.Conversion,
            () => [first, second],
            () => matchingCancelled++);
        using IDisposable other = MediaOperationRegistry.Register(
            MediaOperationKind.Conversion,
            () => [unrelated],
            () => unrelatedCancelled++);

        int cancelled = MediaOperationRegistry.Cancel(MediaOperationKind.Conversion, [first, second]);

        Assert.Equal(1, cancelled);
        Assert.Equal(1, matchingCancelled);
        Assert.Equal(0, unrelatedCancelled);
    }

    [Fact]
    public async Task WaitForPathReleaseAsync_WaitsForMatchingPathOnly()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-operation-{Guid.NewGuid():N}");
        string first = Path.Combine(directory, "first.ts");
        string second = Path.Combine(directory, "second.ts");
        using IDisposable firstConversion = MediaOperationRegistry.Register(MediaOperationKind.Conversion, () => [first]);
        using IDisposable secondConversion = MediaOperationRegistry.Register(MediaOperationKind.Conversion, () => [second]);

        Task<bool> waitTask = MediaOperationRegistry.WaitForPathReleaseAsync(MediaOperationKind.Conversion, [first], TimeSpan.FromSeconds(2));

        Assert.False(waitTask.IsCompleted);
        firstConversion.Dispose();

        Assert.True(await waitTask);
        Assert.True(MediaOperationRegistry.IsPathProtected(second));
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

    [Fact]
    public void ProtectedPathEnumerationFailure_DoesNotEscapeRegistryQueries()
    {
        static IEnumerable<string?> ThrowDuringEnumeration()
        {
            yield return null;
            throw new InvalidOperationException("enumeration failed");
        }

        string path = Path.Combine(Path.GetTempPath(), "emerde-enumeration-failure.ts");
        using IDisposable operation = MediaOperationRegistry.Register(MediaOperationKind.Recording, ThrowDuringEnumeration);

        Assert.False(MediaOperationRegistry.IsPathProtected(path));
        Assert.False(MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Recording, path));
        Assert.Equal(0, MediaOperationRegistry.Cancel(MediaOperationKind.Recording, path));
    }
}
