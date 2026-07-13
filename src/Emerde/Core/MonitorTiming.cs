namespace Emerde.Core;

internal static class MonitorTiming
{
    public const int MinimumRoutineIntervalMilliseconds = 1000;
    public const int DefaultRoutineIntervalMilliseconds = 5000;
    public const int LiveRoutineIntervalMilliseconds = 60000;
    public const int RecentlyClosedRoutineIntervalMilliseconds = 20000;
    public const int MonitorBatchLimit = 5;
    public static readonly TimeSpan RecentlyClosedWindow = TimeSpan.FromMinutes(30);

    public static int NormalizeRoutineInterval(int milliseconds)
    {
        return Math.Max(MinimumRoutineIntervalMilliseconds, milliseconds);
    }
}
