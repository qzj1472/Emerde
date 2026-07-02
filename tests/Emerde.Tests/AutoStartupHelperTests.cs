using Emerde;
using Emerde.Extensions;

namespace Emerde.Tests;

public sealed class AutoStartupHelperTests : IDisposable
{
    public AutoStartupHelperTests()
    {
        RegistyAutoRunHelper.Disable(AppConfig.PackName);
        RegistyAutoRunHelper.Disable(AppConfig.LegacyPackName);
    }

    [Fact]
    public void IsAutorun_MigratesLegacyAutorunKey()
    {
        RegistyAutoRunHelper.Enable(AppConfig.LegacyPackName, "\"legacy.exe\" /autorun");

        bool result = AutoStartupHelper.IsAutorun();

        Assert.True(result);
        Assert.False(RegistyAutoRunHelper.Exists(AppConfig.LegacyPackName));
        Assert.True(RegistyAutoRunHelper.IsEnabled(AppConfig.PackName, AutoStartupHelper.GetLaunchCommand()));
    }

    public void Dispose()
    {
        RegistyAutoRunHelper.Disable(AppConfig.PackName);
        RegistyAutoRunHelper.Disable(AppConfig.LegacyPackName);
    }
}
