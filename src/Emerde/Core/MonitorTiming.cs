namespace Emerde.Core;

internal static class MonitorTiming
{
    public const int MinimumRoutineIntervalMilliseconds = 1000;
    public const int DefaultRoutineIntervalMilliseconds = 5000;
    public const int LiveRoutineIntervalMilliseconds = 10000;
    public const int RecentlyClosedRoutineIntervalMilliseconds = 10000;
    public const int MonitorBatchLimit = 5;
    public static readonly TimeSpan RecentlyClosedWindow = TimeSpan.FromMinutes(30);

    public static int NormalizeRoutineInterval(int milliseconds)
    {
        return Math.Max(MinimumRoutineIntervalMilliseconds, milliseconds);
    }

    public static int ConvertToMilliseconds(double value, int unitIndex)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return MinimumRoutineIntervalMilliseconds;
        }
        double multiplier = unitIndex switch
        {
            3 => 3600000d,
            2 => 60000d,
            1 => 1000d,
            0 => 1d,
            _ => 1000d,
        };
        double milliseconds = Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(milliseconds) || milliseconds >= int.MaxValue)
        {
            return int.MaxValue;
        }
        return Math.Max(1, (int)milliseconds);
    }
}
