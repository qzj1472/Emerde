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
    public void RequestedTargetPath_KeepsOriginalAndUsesSelectedContainer()
    {
        string source = Path.Combine("D:\\records", "直播_卡顿分段002.ts");

        Assert.Equal(
            Path.Combine("D:\\records", "直播_卡顿分段002_修复.mkv"),
            VideoRepairService.BuildRequestedTargetPath(source));
        Assert.Equal(
            Path.Combine("D:\\records", "直播_卡顿分段002_修复.mp4"),
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
}
