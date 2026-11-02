using Emerde.Core;

namespace Emerde.Tests;

public sealed class VideoRepairServiceTests
{
    [Theory]
    [InlineData("record.ts", true)]
    [InlineData("record.FLV", true)]
    [InlineData("record.mp4", false)]
    [InlineData("record.mkv", false)]
    public void SupportedSource_AcceptsRawRecordingContainers(string fileName, bool expected)
    {
        Assert.Equal(expected, VideoRepairService.IsSupportedSource(fileName));
    }

    [Fact]
    public void RequestedTargetPath_UsesNormalConvertedFileName()
    {
        string source = Path.Combine("D:\\records", "直播_卡顿分段002.ts");

        Assert.Equal(
            Path.Combine("D:\\records", "直播_卡顿分段002.mkv"),
            VideoRepairService.BuildRequestedTargetPath(source));
        Assert.Equal(
            Path.Combine("D:\\records", "直播_卡顿分段002.mp4"),
            VideoRepairService.BuildRequestedTargetPath(source, "MP4"));
    }

    [Theory]
    [InlineData("mkv", ".mkv")]
    [InlineData(".MP4", ".mp4")]
    [InlineData("avi", "")]
    public void TargetExtension_OnlyAcceptsRepairContainers(string input, string expected)
    {
        Assert.Equal(expected, VideoRepairService.NormalizeTargetExtension(input));
    }

    [Fact]
    public void ReencodeFallback_RejectsInvalidPathsWithoutStartingNativeWork()
    {
        FfmpegMediaRunResult result = FfmpegMediaEngine.ReencodeFile(
            string.Empty,
            string.Empty,
            new VideoRecordingMetadata(),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.HadMediaProgress);
        Assert.Equal("reencode_path_invalid", result.ErrorOutput);
    }

    [Fact]
    public void OrphanedRepairReport_RequiresARecognizedMissingMediaPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-repair-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string mediaPath = Path.Combine(directory, "record.mkv");
        string reportPath = mediaPath + VideoRepairService.RepairReportSuffix;
        File.WriteAllText(reportPath, "{}");

        try
        {
            Assert.True(VideoRepairService.IsOrphanedRepairReport(reportPath));
            Assert.False(VideoRepairService.IsOrphanedRepairReport(Path.Combine(directory, "record.repair.json")));

            File.WriteAllText(mediaPath, "media");

            Assert.False(VideoRepairService.IsOrphanedRepairReport(reportPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RepairValidation_DoesNotRejectOutputOnlyBecauseSourceTracksWereMisaligned()
    {
        FfmpegMediaProbeResult source = CreateProbe(90, 100, 100);
        FfmpegMediaProbeResult output = CreateProbe(99, 100, 100);

        Assert.Empty(VideoRepairService.GetValidationError(source, output));
        Assert.True(Converter.IsTrackTimelineWithinTolerance(output.AudioEndSeconds, output.VideoEndSeconds));
    }

    [Fact]
    public void RepairValidation_AllowsPartialOutputTimelineToBeReportedSeparately()
    {
        FfmpegMediaProbeResult source = CreateProbe(100, 100, 100);
        FfmpegMediaProbeResult output = CreateProbe(96, 100, 100);

        Assert.Empty(VideoRepairService.GetValidationError(source, output));
        Assert.False(Converter.IsTrackTimelineWithinTolerance(output.AudioEndSeconds, output.VideoEndSeconds));
    }

    [Fact]
    public void DiscardRejectedRepairOutput_RemovesMediaAndReport()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-repair-rejected-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string mediaPath = Path.Combine(directory, "record.mkv");
        File.WriteAllText(mediaPath, "media");
        File.WriteAllText(mediaPath + VideoRepairService.RepairReportSuffix, "{}");

        try
        {
            VideoRepairService.DiscardRejectedRepairOutput(mediaPath);

            Assert.False(File.Exists(mediaPath));
            Assert.False(File.Exists(mediaPath + VideoRepairService.RepairReportSuffix));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static FfmpegMediaProbeResult CreateProbe(double audioEnd, double videoEnd, double duration)
    {
        return new FfmpegMediaProbeResult(
            true,
            true,
            1,
            1,
            audioEnd,
            videoEnd,
            1920,
            1080,
            30,
            duration,
            0,
            "h264",
            "aac",
            string.Empty,
            new VideoRecordingMetadata(),
            false);
    }
}
