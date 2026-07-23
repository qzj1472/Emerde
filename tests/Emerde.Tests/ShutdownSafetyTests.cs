namespace Emerde.Tests;

public sealed class ShutdownSafetyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void HasShutdownSensitiveWork_IncludesRecorderCleanupAndConversion(bool hasActiveRecorders, bool hasActiveConversions, bool expected)
    {
        Assert.Equal(expected, TrayIconManager.HasShutdownSensitiveWork(hasActiveRecorders, hasActiveConversions));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    public void HasShutdownSensitiveWork_IncludesAllMediaOperations(
        bool hasActiveRecorders,
        bool hasActiveConversions,
        bool hasActiveMediaOperations,
        bool expected)
    {
        Assert.Equal(expected, TrayIconManager.HasShutdownSensitiveWork(
            hasActiveRecorders,
            hasActiveConversions,
            hasActiveMediaOperations));
    }

    [Fact]
    public void CompleteApplicationShutdown_AlwaysTerminatesAfterCleanup()
    {
        List<string> calls = [];

        TrayIconManager.CompleteApplicationShutdown(
            () => calls.Add("shutdown"),
            () => calls.Add("exit"));

        Assert.Equal(["shutdown", "exit"], calls);
    }

    [Fact]
    public void CompleteApplicationShutdown_TerminatesWhenCleanupFails()
    {
        bool exitCalled = false;

        Assert.Throws<InvalidOperationException>(() => TrayIconManager.CompleteApplicationShutdown(
            () => throw new InvalidOperationException(),
            () => exitCalled = true));

        Assert.True(exitCalled);
    }
}
