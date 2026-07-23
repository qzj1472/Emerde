using Emerde.Extensions;

namespace Emerde.Tests;

public sealed class RuntimeHelperTests
{
    [Fact]
    public void RestartArguments_AppendCurrentParent()
    {
        string arguments = RuntimeHelper.BuildRestartArguments("--mode test", 1234);

        Assert.Equal("--mode test --emerde-restart-parent=1234", arguments);
        Assert.Equal(1234, RuntimeHelper.GetRestartParentProcessId(arguments.Split(' ')));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--emerde-restart-parent=invalid")]
    [InlineData("--emerde-restart-parent=0")]
    public void RestartParentProcessId_RejectsInvalidArguments(string? argument)
    {
        Assert.Null(RuntimeHelper.GetRestartParentProcessId(argument == null ? null : [argument]));
    }

    [Fact]
    public void Restart_DoesNotRunExitCallbackWhenProcessCannotStart()
    {
        bool exitCallbackRan = false;
        string missingExecutable = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe");

        bool restarted = RuntimeHelper.Restart(
            fileName: missingExecutable,
            forced: false,
            beforeExit: () => exitCallbackRan = true);

        Assert.False(restarted);
        Assert.False(exitCallbackRan);
    }

    [Fact]
    public void CompleteRestart_RunsExitWhenCleanupFails()
    {
        bool exitRan = false;

        Assert.Throws<InvalidOperationException>(() => RuntimeHelper.CompleteRestart(
            () => throw new InvalidOperationException(),
            () => exitRan = true));

        Assert.True(exitRan);
    }
}
