namespace Emerde.Core;

internal enum ScheduledCloseTarget
{
    Application,
    Computer,
}

internal sealed class AutoShutdownSchedule
{
    private DateTime? readyTarget;
    private DateTime? cancelledTarget;
    private DateTime? completedTarget;
    private string scheduleTime = string.Empty;

    public bool IsReady { get; private set; }

    public TimeSpan GetRemainingTime(DateTime now)
    {
        return readyTarget is DateTime target && target > now ? target - now : TimeSpan.Zero;
    }

    public static ScheduledCloseTarget ResolveCloseTarget(bool closeComputer)
    {
        return closeComputer ? ScheduledCloseTarget.Computer : ScheduledCloseTarget.Application;
    }

    public static int GetTimePart(string? configuredTime, int index, int maximum)
    {
        string[] parts = (configuredTime ?? string.Empty).Split(':');
        if (index < 0 || parts.Length <= index)
        {
            return 0;
        }

        return int.TryParse(parts[index], out int value)
            ? Math.Clamp(value, 0, maximum)
            : 0;
    }

    public static bool TryParseTime(string? configuredTime, out TimeSpan targetTime)
    {
        string[] parts = (configuredTime ?? string.Empty).Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], out int hour)
            && int.TryParse(parts[1], out int minute)
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59)
        {
            targetTime = new TimeSpan(hour, minute, 0);
            return true;
        }

        targetTime = default;
        return false;
    }

    public bool ShouldStartPrompt(DateTime now, bool enabled, string configuredTime)
    {
        string currentTime = configuredTime ?? string.Empty;

        if (!enabled || !TryParseTime(currentTime, out TimeSpan targetTime))
        {
            ResetAll();
            return false;
        }

        if (!string.Equals(scheduleTime, currentTime, StringComparison.Ordinal))
        {
            ResetAll();
            scheduleTime = currentTime;
        }

        DateTime targetDateTime = ResolveTarget(now, targetTime);
        if (readyTarget != null && readyTarget != targetDateTime)
        {
            ResetReadiness();
        }

        if (cancelledTarget != null && cancelledTarget < targetDateTime)
        {
            ClearCancellation();
        }

        if (completedTarget != null && completedTarget < targetDateTime)
        {
            completedTarget = null;
        }

        if (cancelledTarget == targetDateTime || completedTarget == targetDateTime)
        {
            return false;
        }

        if (IsReady || now < targetDateTime.AddMinutes(-1))
        {
            return false;
        }

        IsReady = true;
        readyTarget = targetDateTime;
        return true;
    }

    public void Cancel(DateTime now, string configuredTime)
    {
        string currentTime = configuredTime ?? string.Empty;
        cancelledTarget = TryParseTime(currentTime, out TimeSpan targetTime)
            ? ResolveTarget(now, targetTime)
            : null;
        scheduleTime = currentTime;
        ResetReadiness();
    }

    public void CompleteCurrent()
    {
        completedTarget = readyTarget;
        ResetReadiness();
    }

    public void ResetReadiness()
    {
        IsReady = false;
        readyTarget = null;
    }

    public void ResetAll()
    {
        ResetReadiness();
        ClearCancellation();
        completedTarget = null;
        scheduleTime = string.Empty;
    }

    private void ClearCancellation()
    {
        cancelledTarget = null;
    }

    private static DateTime ResolveTarget(DateTime now, TimeSpan targetTime)
    {
        DateTime targetDateTime = now.Date.Add(targetTime);
        return now > targetDateTime ? targetDateTime.AddDays(1) : targetDateTime;
    }
}
