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
            IEnumerable<string?> patterns;
            try
            {
                patterns = operation.ProtectedPaths() ?? [];
            }
            catch
            {
                continue;
            }

            foreach (string? pattern in patterns)
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
            IEnumerable<string?> patterns;
            try
            {
                patterns = operation.ProtectedPaths() ?? [];
            }
            catch
            {
                continue;
            }

            if (patterns.Any(pattern => PathMatches(normalizedPath, pattern)))
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

    public static void Cancel(MediaOperationKind kind)
    {
        CancelWhere(operation => operation.Kind == kind);
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
        return Regex.IsMatch(path, "^" + regexPattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void CancelWhere(Func<OperationState, bool> predicate)
    {
        foreach (OperationState operation in Operations.Values.Where(predicate))
        {
            try
            {
                operation.Cancel?.Invoke();
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
        }
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
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
