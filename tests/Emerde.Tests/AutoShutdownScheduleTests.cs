using Emerde.Core;

namespace Emerde.Tests;

public sealed class AutoShutdownScheduleTests
{
    [Fact]
    public void ResolveCloseTarget_DefaultsToApplication()
    {
        Assert.Equal(ScheduledCloseTarget.Application, AutoShutdownSchedule.ResolveCloseTarget(false));
        Assert.Equal(ScheduledCloseTarget.Computer, AutoShutdownSchedule.ResolveCloseTarget(true));
    }

    [Fact]
    public void ShouldStartPrompt_StartsOneMinuteBeforeTargetOnlyOnce()
    {
        AutoShutdownSchedule schedule = new();
        DateTime beforePrompt = new(2026, 7, 12, 21, 58, 59);
        DateTime promptTime = new(2026, 7, 12, 21, 59, 0);

        Assert.False(schedule.ShouldStartPrompt(beforePrompt, true, "22:00"));
        Assert.True(schedule.ShouldStartPrompt(promptTime, true, "22:00"));
        Assert.False(schedule.ShouldStartPrompt(promptTime.AddSeconds(1), true, "22:00"));
        Assert.True(schedule.IsReady);
    }

    [Fact]
    public void Cancel_SuppressesOnlyCurrentDayAndSchedule()
    {
        AutoShutdownSchedule schedule = new();
        DateTime promptTime = new(2026, 7, 12, 21, 59, 0);

        schedule.Cancel(promptTime, "22:00");

        Assert.False(schedule.ShouldStartPrompt(promptTime, true, "22:00"));
        Assert.True(schedule.ShouldStartPrompt(promptTime.AddDays(1), true, "22:00"));
    }

    [Fact]
    public void CancelAfterTarget_SuppressesReadyTargetWithoutCancellingNextDay()
    {
        AutoShutdownSchedule schedule = new();
        DateTime promptTime = new(2026, 7, 12, 21, 59, 30);

        Assert.True(schedule.ShouldStartPrompt(promptTime, true, "22:00"));
        schedule.Cancel(promptTime.AddSeconds(31), "22:00");

        Assert.True(schedule.ShouldStartPrompt(promptTime.AddDays(1), true, "22:00"));
    }

    [Fact]
    public void ScheduleChange_ClearsPreviousCancellation()
    {
        AutoShutdownSchedule schedule = new();
        DateTime now = new(2026, 7, 12, 21, 59, 0);

        schedule.Cancel(now, "22:00");

        Assert.True(schedule.ShouldStartPrompt(now, true, "21:59"));
    }

    [Fact]
    public void DisabledSchedule_ResetsReadiness()
    {
        AutoShutdownSchedule schedule = new();
        DateTime now = new(2026, 7, 12, 21, 59, 0);

        Assert.True(schedule.ShouldStartPrompt(now, true, "22:00"));
        Assert.False(schedule.ShouldStartPrompt(now, false, "22:00"));
        Assert.False(schedule.IsReady);
    }

    [Fact]
    public void TimeMoreThanFiveMinutesPast_TargetsNextDay()
    {
        AutoShutdownSchedule schedule = new();
        DateTime now = new(2026, 7, 12, 22, 6, 0);

        Assert.False(schedule.ShouldStartPrompt(now, true, "22:00"));
    }

    [Fact]
    public void TimeImmediatelyPastTarget_DoesNotStartLatePrompt()
    {
        AutoShutdownSchedule schedule = new();

        Assert.False(schedule.ShouldStartPrompt(new DateTime(2026, 7, 12, 22, 0, 1), true, "22:00"));
    }

    [Fact]
    public void RemainingTime_UsesActualTargetInsteadOfFixedMinute()
    {
        AutoShutdownSchedule schedule = new();
        DateTime now = new(2026, 7, 12, 21, 59, 30);

        Assert.True(schedule.ShouldStartPrompt(now, true, "22:00"));
        Assert.Equal(TimeSpan.FromSeconds(30), schedule.GetRemainingTime(now));
    }

    [Fact]
    public void CompleteCurrent_PreventsSameTargetFromStartingAgain()
    {
        AutoShutdownSchedule schedule = new();
        DateTime now = new(2026, 7, 12, 21, 59, 30);

        Assert.True(schedule.ShouldStartPrompt(now, true, "22:00"));
        schedule.CompleteCurrent();

        Assert.False(schedule.ShouldStartPrompt(now.AddSeconds(1), true, "22:00"));
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("23:60")]
    [InlineData("invalid")]
    public void OutOfRangeOrMalformedSchedule_DoesNotStart(string value)
    {
        AutoShutdownSchedule schedule = new();

        Assert.False(schedule.ShouldStartPrompt(new DateTime(2026, 7, 12, 23, 59, 0), true, value));
    }

    [Fact]
    public void CancelBeforeMidnight_SuppressesTheMidnightOccurrence()
    {
        AutoShutdownSchedule schedule = new();
        DateTime promptTime = new(2026, 7, 12, 23, 59, 0);

        Assert.True(schedule.ShouldStartPrompt(promptTime, true, "00:00"));
        schedule.Cancel(promptTime, "00:00");

        Assert.False(schedule.ShouldStartPrompt(promptTime.AddMinutes(1), true, "00:00"));
    }

    [Theory]
    [InlineData(null, 0, 23, 0)]
    [InlineData("invalid", 1, 59, 0)]
    [InlineData("25:99", 0, 23, 23)]
    [InlineData("25:99", 1, 59, 59)]
    public void GetTimePart_HandlesMalformedAndOutOfRangeValues(string? value, int index, int maximum, int expected)
    {
        Assert.Equal(expected, AutoShutdownSchedule.GetTimePart(value, index, maximum));
    }
}
