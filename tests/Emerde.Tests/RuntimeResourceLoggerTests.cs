using Emerde.Core;

namespace Emerde.Tests;

public sealed class RuntimeResourceLoggerTests
{
    [Fact]
    public void Stop_AllowsSamplerToRestart()
    {
        RuntimeResourceLogger.Start();
        RuntimeResourceLogger.Stop();
        RuntimeResourceLogger.Start();
        RuntimeResourceLogger.Stop();
    }
}
