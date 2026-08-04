using Emerde;
using Emerde.Extensions;

namespace Emerde.Tests;

public sealed class AutoStartupHelperTests : IDisposable
{
    public AutoStartupHelperTests()
    {
        RegistyAutoRunHelper.Disable(AppConfig.PackName);
    }

    [Fact]
    public void IsAutorun_UsesCurrentAutorunKey()
    {
        RegistyAutoRunHelper.Enable(AppConfig.PackName, AutoStartupHelper.GetLaunchCommand());

        bool result = AutoStartupHelper.IsAutorun();

        Assert.True(result);
        Assert.True(RegistyAutoRunHelper.IsEnabled(AppConfig.PackName, AutoStartupHelper.GetLaunchCommand()));
    }

    public void Dispose()
    {
        RegistyAutoRunHelper.Disable(AppConfig.PackName);
    }
}
