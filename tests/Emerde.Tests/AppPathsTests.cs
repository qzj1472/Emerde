using Emerde.Core;

namespace Emerde.Tests;

public sealed class AppPathsTests
{
    [Theory]
    [InlineData("config.yaml", true)]
    [InlineData("config.dev.yaml", true)]
    [InlineData("config.bak-20260710120000.yaml", false)]
    [InlineData("config.reset-bak-20260710120000.yaml", false)]
    [InlineData("config.bak-20260710120000-2.yml", false)]
    [InlineData("config.reset-bak-20260710120000-2.yml", false)]
    public void IsConfigFile_ExcludesBackupFiles(string fileName, bool expected)
    {
        string path = Path.Combine("C:\\config", fileName);

        Assert.Equal(expected, AppPaths.IsConfigFile(path));
    }
}
