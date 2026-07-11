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

    [Theory]
    [InlineData(true, false, null, true)]
    [InlineData(false, true, null, true)]
    [InlineData(false, false, ".mp4", true)]
    [InlineData(false, false, ".mkv", true)]
    [InlineData(false, false, null, false)]
    public void ShouldUseTransportStream_MatchesAudioProcessingRequirements(bool isHls, bool isToSegment, string? targetFormat, bool expected)
    {
        Assert.Equal(expected, Recorder.ShouldUseTransportStream(isHls, isToSegment, targetFormat));
    }

    [Fact]
    public void BuildAudioMappingArguments_AddsOriginalAndOptimizedTracks()
    {
        IReadOnlyList<string> arguments = Recorder.BuildAudioMappingArguments(useOptimizedAudio: true);

        Assert.Contains("-filter_complex", arguments);
        Assert.Contains("0:a:0?", arguments);
        Assert.Contains("[aopt]", arguments);
        Assert.Contains("title=原音频", arguments);
        Assert.Contains("title=优化音频", arguments);
    }

    [Theory]
    [InlineData("Stream specifier ':a:0' matches no streams")]
    [InlineData("Cannot find a matching stream for unlabeled input pad")]
    [InlineData("Streamcopy requested for output stream fed from a complex filtergraph")]
    public void IsMissingAudioError_RecognizesFfmpegFailures(string errorOutput)
    {
        Assert.True(Recorder.IsMissingAudioError(errorOutput));
    }
}
