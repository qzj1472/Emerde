using Emerde.Extensions;

namespace Emerde.Tests;

public sealed class RuntimeHelperTests
{
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
}
