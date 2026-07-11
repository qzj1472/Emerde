using Emerde.Core;

namespace Emerde.Tests;

public sealed class RecorderTests
{
    private static readonly DateTime Timestamp = new(2026, 7, 3, 12, 34, 56);

    [Theory]
    [InlineData(true, true, "Host_2026-07-03_12-34-56_%03d.ts")]
    [InlineData(false, true, "Host_2026-07-03_12-34-56_%03d.ts")]
    [InlineData(true, false, "Host_2026-07-03_12-34-56.ts")]
    [InlineData(false, false, "Host_2026-07-03_12-34-56.flv")]
    public void BuildOutputFileName_SelectsExpectedSuffix(bool isHls, bool isToSegment, string expectedFileName)
    {
        string result = Recorder.BuildOutputFileName("D:\\records", "Host", Timestamp, isToSegment, isHls);

        Assert.Equal(Path.Combine("D:\\records", expectedFileName), result);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void CanRetryRecording_StopsAfterFourAttempts(int completedAttempts, bool expected)
    {
        Assert.Equal(expected, Recorder.CanRetryRecording(completedAttempts));
    }
}
