using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Emerde.Core;

internal enum MediaOperationKind
{
    Recording,
    Conversion,
    Split,
    Merge,
}

internal static class MediaOperationRegistry
{
    private static readonly ConcurrentDictionary<Guid, OperationState> Operations = new();

    public static event EventHandler<MediaOperationsChangedEventArgs>? OperationsChanged;

    public static int ActiveCount => Operations.Count;

    public static bool HasActiveOperations => !Operations.IsEmpty;

    public static IDisposable Register(
        MediaOperationKind kind,
        Func<IEnumerable<string?>> protectedPaths,
        Action? cancel = null)
    {
        Guid id = Guid.NewGuid();
        OperationState state = new(kind, protectedPaths, cancel);
        Operations[id] = state;
        RaiseOperationsChanged(kind, true, GetPaths(state));
        return new Registration(id, state);
    }

    public static int Count(MediaOperationKind kind)
    {
        return Operations.Values.Count(operation => operation.Kind == kind);
    }

    public static bool HasActive(MediaOperationKind kind)
    {
        return Operations.Values.Any(operation => operation.Kind == kind);
    }

    public static bool IsPathProtected(string path)
    {
        if (!TryNormalizePath(path, out string normalizedPath))
        {
            return false;
        }

        foreach (OperationState operation in Operations.Values)
        {
            foreach (string pattern in GetPaths(operation))
            {
                if (PathMatches(normalizedPath, pattern))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsPathProtectedBy(MediaOperationKind kind, string path)
    {
        if (!TryNormalizePath(path, out string normalizedPath))
        {
            return false;
        }

        foreach (OperationState operation in Operations.Values.Where(operation => operation.Kind == kind))
        {
            if (GetPaths(operation).Any(pattern => PathMatches(normalizedPath, pattern)))
            {
                return true;
            }
        }

        return false;
    }

    public static void CancelAll()
    {
        CancelWhere(static _ => true);
    }

    public static int Cancel(MediaOperationKind kind)
    {
        return CancelWhere(operation => operation.Kind == kind);
    }

    public static int Cancel(MediaOperationKind kind, string path)
    {
        if (!TryNormalizePath(path, out string normalizedPath))
        {
            return 0;
        }

        return CancelWhere(operation => operation.Kind == kind && OperationProtectsPath(operation, normalizedPath));
    }

    public static int Cancel(MediaOperationKind kind, IEnumerable<string> paths)
    {
        string[] normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => TryNormalizePath(path, out string normalizedPath) ? normalizedPath : string.Empty)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return 0;
        }

        return CancelWhere(operation => operation.Kind == kind
            && normalizedPaths.Any(path => OperationProtectsPath(operation, path)));
    }

    public static async Task WaitForCompletionAsync(TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!Operations.IsEmpty)
        {
            Task[] completions = Operations.Values.Select(operation => operation.Completion.Task).ToArray();
            if (completions.Length == 0)
            {
                return;
            }

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            try
            {
                await Task.WhenAll(completions).WaitAsync(remaining);
            }
            catch (TimeoutException)
            {
                return;
            }
        }
    }

    public static async Task<bool> WaitForPathReleaseAsync(MediaOperationKind kind, IEnumerable<string> paths, TimeSpan timeout)
    {
        string[] normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => TryNormalizePath(path, out string normalizedPath) ? normalizedPath : string.Empty)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return true;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            Task[] completions = Operations.Values
                .Where(operation => operation.Kind == kind && normalizedPaths.Any(path => OperationProtectsPath(operation, path)))
                .Select(operation => operation.Completion.Task)
                .ToArray();
            if (completions.Length == 0)
            {
                return true;
            }

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            try
            {
                await Task.WhenAll(completions).WaitAsync(remaining);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }

    internal static bool PathMatches(string path, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || !TryNormalizePath(pattern, out string normalizedPattern))
        {
            return false;
        }

        if (!normalizedPattern.Contains('%') && !normalizedPattern.Contains('*') && !normalizedPattern.Contains('?'))
        {
            return string.Equals(path, normalizedPattern, StringComparison.OrdinalIgnoreCase);
        }

        string regexPattern = Regex.Escape(normalizedPattern)
            .Replace("%03d", @"\d{3,}", StringComparison.Ordinal)
            .Replace(@"\*", ".*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal);
        return Regex.IsMatch(
            path,
            "^" + regexPattern + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(100));
    }

    private static int CancelWhere(Func<OperationState, bool> predicate)
    {
        int cancelled = 0;
        foreach (OperationState operation in Operations.Values.Where(predicate))
        {
            if (!operation.TryBeginCancel())
            {
                continue;
            }

            try
            {
                operation.Cancel!();
                cancelled++;
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
        }

        return cancelled;
    }

    private static bool OperationProtectsPath(OperationState operation, string normalizedPath)
    {
        return GetPaths(operation).Any(pattern => PathMatches(normalizedPath, pattern));
    }

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static string[] GetPaths(OperationState state)
    {
        try
        {
            return state.ProtectedPaths()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static void RaiseOperationsChanged(MediaOperationKind kind, bool isActive, IReadOnlyList<string> paths)
    {
        MediaOperationsChangedEventArgs eventArgs = new(kind, isActive, paths);
        foreach (EventHandler<MediaOperationsChangedEventArgs> handler in OperationsChanged?.GetInvocationList().Cast<EventHandler<MediaOperationsChangedEventArgs>>() ?? [])
        {
            try
            {
                handler(null, eventArgs);
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
        }
    }

    private sealed record OperationState(
        MediaOperationKind Kind,
        Func<IEnumerable<string?>> ProtectedPaths,
        Action? Cancel)
    {
        private int cancelRequested;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryBeginCancel()
        {
            return Cancel != null && Interlocked.Exchange(ref cancelRequested, 1) == 0;
        }
    }

    private sealed class Registration(Guid id, OperationState state) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            _ = Operations.TryRemove(new KeyValuePair<Guid, OperationState>(id, state));
            state.Completion.TrySetResult();
            RaiseOperationsChanged(state.Kind, false, GetPaths(state));
        }
    }
}

internal sealed class MediaOperationsChangedEventArgs(
    MediaOperationKind kind,
    bool isActive,
    IReadOnlyList<string> paths) : EventArgs
{
    public MediaOperationKind Kind { get; } = kind;
    public bool IsActive { get; } = isActive;
    public IReadOnlyList<string> Paths { get; } = paths;
}
