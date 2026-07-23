using Emerde.Core;

namespace Emerde.Tests;

public sealed class AppSessionLoggerTests
{
    [Fact]
    public void GetDailyLogPaths_UsesOnePairOfFilesPerLocalDate()
    {
        string directory = Path.Combine("D:\\", "logs");

        (string filePath, string errorFilePath) = AppSessionLogger.GetDailyLogPaths(
            directory,
            new DateTime(2026, 7, 22, 23, 59, 59, DateTimeKind.Local));

        Assert.Equal(Path.Combine(directory, "2026-07-22.log"), filePath);
        Assert.Equal(Path.Combine(directory, "2026-07-22.error.log"), errorFilePath);
    }

    [Fact]
    public void GetDailyLogPaths_ChangesAtNextLocalDate()
    {
        string directory = Path.Combine("D:\\", "logs");

        (string firstFilePath, _) = AppSessionLogger.GetDailyLogPaths(
            directory,
            new DateTime(2026, 7, 22, 23, 59, 59, DateTimeKind.Local));
        (string secondFilePath, _) = AppSessionLogger.GetDailyLogPaths(
            directory,
            new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Local));

        Assert.NotEqual(firstFilePath, secondFilePath);
        Assert.Equal(Path.Combine(directory, "2026-07-23.log"), secondFilePath);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(30, 30)]
    [InlineData(5000, 3650)]
    public void NormalizeRetentionDays_ClampsConfiguredValue(int value, int expected)
    {
        Assert.Equal(expected, AppSessionLogger.NormalizeRetentionDays(value));
    }
}
