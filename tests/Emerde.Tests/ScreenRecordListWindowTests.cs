using Emerde.Core;
using Emerde.Views;
using System.Text.Json;

namespace Emerde.Tests;

public sealed class ScreenRecordListWindowTests
{
    [Theory]
    [InlineData(@"主播A\2026-07\03\record.mp4", "主播A")]
    [InlineData(@"主播A\2026-07\record.mp4", "主播A")]
    [InlineData(@"Imported\Nested\record.mp4", "Nested")]
    public void InferNickName_UsesRecordedAuthorFolder(string relativePath, string expected)
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-videos-{Guid.NewGuid():N}");
        string filePath = Path.Combine(relativePath.Split('\\').Prepend(root).ToArray());

        string nickName = ScreenRecordListViewModel.InferNickName(filePath, root);

        Assert.Equal(expected, nickName);
    }

    [Fact]
    public void InferNickName_UsesParentFolderForRootVideos()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-videos-{Guid.NewGuid():N}");
        string filePath = Path.Combine(root, "record.mp4");

        string nickName = ScreenRecordListViewModel.InferNickName(filePath, root);

        Assert.Equal(Path.GetFileName(root), nickName);
    }

    [Fact]
    public void BuildDefaultOpenStartInfo_UsesSystemFileAssociation()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "record.mp4");

        System.Diagnostics.ProcessStartInfo startInfo = ScreenRecordListViewModel.BuildDefaultOpenStartInfo(filePath);

        Assert.Equal(filePath, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), startInfo.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void LoadMetadata_UsesSharedSegmentMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string videoPath = Path.Combine(root, "Host_2026-07-03_12-34-56_001.ts");
        string metadataPath = Path.Combine(root, "Host_2026-07-03_12-34-56.mplr.json");

        try
        {
            File.WriteAllText(videoPath, string.Empty);
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new VideoRecordingMetadata
            {
                NickName = "Host",
                Title = "Live Title",
                Resolution = "1920x1080",
                Bitrate = "8 Mbps",
                RecordedAt = new DateTime(2026, 7, 3, 12, 34, 56),
            }));

            VideoRecordingMetadata metadata = ScreenRecordListViewModel.LoadMetadata(new FileInfo(videoPath));

            Assert.Equal("Host", metadata.NickName);
            Assert.Equal("Live Title", metadata.Title);
            Assert.Equal("1920x1080", metadata.Resolution);
            Assert.Equal("8 Mbps", metadata.Bitrate);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadMetadata_UsesSharedMetadataForFourDigitSegment()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string videoPath = Path.Combine(root, "Host_2026-07-03_12-34-56_1000.ts");
        string metadataPath = Path.Combine(root, "Host_2026-07-03_12-34-56.mplr.json");

        try
        {
            File.WriteAllText(videoPath, string.Empty);
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new VideoRecordingMetadata
            {
                NickName = "Host",
            }));

            VideoRecordingMetadata metadata = ScreenRecordListViewModel.LoadMetadata(new FileInfo(videoPath));

            Assert.Equal("Host", metadata.NickName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void NormalizeResolution_HidesMediaInfoLoadError()
    {
        string resolution = ScreenRecordListViewModel.NormalizeResolution("Unable to load MediaInfo library");

        Assert.Equal(ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown"), resolution);
    }

    [Theory]
    [InlineData("1080p", "1080p")]
    [InlineData("2160P", "2160p")]
    [InlineData("1080I", "1080i")]
    public void NormalizeResolution_PreservesVerticalResolutionLabels(string value, string expected)
    {
        Assert.Equal(expected, ScreenRecordListViewModel.NormalizeResolution(value));
    }

    [Fact]
    public void ParseVideoProbeJson_UsesVideoStreamResolutionAndBitrate()
    {
        string json = """
        {
          "streams": [
            {
              "width": 1920,
              "height": 1080,
              "bit_rate": "8000000"
            }
          ],
          "format": {
            "bit_rate": "4000000"
          }
        }
        """;

        VideoProbeInfo info = ScreenRecordListViewModel.ParseVideoProbeJson(json);

        Assert.Equal("1920x1080", info.Resolution);
        Assert.Equal("8 Mbps", info.Bitrate);
    }

    [Fact]
    public void ParseVideoProbeJson_FallsBackToFormatBitrate()
    {
        string json = """
        {
          "streams": [
            {
              "width": 1280,
              "height": 720
            }
          ],
          "format": {
            "bit_rate": "2500000"
          }
        }
        """;

        VideoProbeInfo info = ScreenRecordListViewModel.ParseVideoProbeJson(json);

        Assert.Equal("1280x720", info.Resolution);
        Assert.Equal("2.5 Mbps", info.Bitrate);
    }

    [Fact]
    public void ParseVideoProbeJson_ReadsEmbeddedMetadataTags()
    {
        string json = """
        {
          "streams": [
            {
              "width": 1920,
              "height": 1080
            }
          ],
          "format": {
            "bit_rate": "8000000",
            "tags": {
              "emerde_nick_name": "Host",
              "emerde_title": "Live Title",
              "emerde_room_url": "https://example.com/live",
              "emerde_platform": "Twitch",
              "emerde_recorded_at": "2026-07-03T12:34:56.0000000"
            }
          }
        }
        """;

        VideoProbeInfo info = ScreenRecordListViewModel.ParseVideoProbeJson(json);

        Assert.NotNull(info.Metadata);
        Assert.Equal("Host", info.Metadata!.NickName);
        Assert.Equal("Live Title", info.Metadata.Title);
        Assert.Equal("https://example.com/live", info.Metadata.RoomUrl);
        Assert.Equal("Twitch", info.Metadata.Platform);
        Assert.Equal(new DateTime(2026, 7, 3, 12, 34, 56), info.Metadata.RecordedAt);
    }

    [Fact]
    public void TryDeleteSidecarIfNoSourceVideosRemain_KeepsSharedMetadataUntilLastSegmentRemoved()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-sidecar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string metadataPath = Path.Combine(root, "Host_2026-07-03_12-34-56.mplr.json");
        string firstSegment = Path.Combine(root, "Host_2026-07-03_12-34-56_000.ts");
        string secondSegment = Path.Combine(root, "Host_2026-07-03_12-34-56_001.ts");

        try
        {
            File.WriteAllText(metadataPath, "{}");
            File.WriteAllText(firstSegment, string.Empty);
            File.WriteAllText(secondSegment, string.Empty);

            File.Delete(firstSegment);
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(firstSegment);

            Assert.True(File.Exists(metadataPath));

            File.Delete(secondSegment);
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(secondSegment);

            Assert.False(File.Exists(metadataPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GetExistingThumbnailPath_UsesCachedThumbnail()
    {
        string videoPath = Path.Combine(Path.GetTempPath(), $"record-{Guid.NewGuid():N}.mp4");
        string thumbnailPath = ScreenRecordListViewModel.GetThumbnailCachePath(videoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);

        try
        {
            File.WriteAllText(videoPath, "video");
            File.WriteAllText(thumbnailPath, "fake");
            File.SetLastWriteTimeUtc(thumbnailPath, File.GetLastWriteTimeUtc(videoPath).AddSeconds(1));

            string result = ScreenRecordListViewModel.GetExistingThumbnailPath(videoPath, string.Empty);

            Assert.Equal(thumbnailPath, result);
        }
        finally
        {
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
            if (File.Exists(thumbnailPath))
            {
                File.Delete(thumbnailPath);
            }
        }
    }

    [Fact]
    public void GetExistingThumbnailPath_RejectsThumbnailOlderThanVideo()
    {
        string videoPath = Path.Combine(Path.GetTempPath(), $"record-{Guid.NewGuid():N}.mp4");
        string thumbnailPath = ScreenRecordListViewModel.GetThumbnailCachePath(videoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);

        try
        {
            File.WriteAllText(thumbnailPath, "fake");
            File.WriteAllText(videoPath, "video");
            File.SetLastWriteTimeUtc(thumbnailPath, DateTime.UtcNow.AddMinutes(-1));
            File.SetLastWriteTimeUtc(videoPath, DateTime.UtcNow);

            string result = ScreenRecordListViewModel.GetExistingThumbnailPath(videoPath, string.Empty);

            Assert.Empty(result);
        }
        finally
        {
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
            if (File.Exists(thumbnailPath))
            {
                File.Delete(thumbnailPath);
            }
        }
    }

    [Theory]
    [InlineData("30", 0, 1800)]
    [InlineData("30", 1, 30)]
    [InlineData("2", 2, 7200)]
    public void TryConvertSplitDurationSeconds_ConvertsValidValues(string value, int unitIndex, int expected)
    {
        Assert.True(ScreenRecordListViewModel.TryConvertSplitDurationSeconds(value, unitIndex, out int seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData("NaN", 0)]
    [InlineData("Infinity", 0)]
    [InlineData("-1", 0)]
    [InlineData("999999999999", 2)]
    public void TryConvertSplitDurationSeconds_RejectsInvalidValues(string value, int unitIndex)
    {
        Assert.False(ScreenRecordListViewModel.TryConvertSplitDurationSeconds(value, unitIndex, out _));
    }

    [Fact]
    public void CopyAssociatedMetadata_PreservesSharedSegmentMetadata()
    {
        string sourceRoot = Path.Combine(Path.GetTempPath(), $"emerde-source-{Guid.NewGuid():N}");
        string targetRoot = Path.Combine(Path.GetTempPath(), $"emerde-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        string sourceVideo = Path.Combine(sourceRoot, "Host_2026-07-03_12-34-56_1000.ts");
        string sourceMetadata = Path.Combine(sourceRoot, "Host_2026-07-03_12-34-56.mplr.json");
        string targetVideo = Path.Combine(targetRoot, Path.GetFileName(sourceVideo));
        string targetMetadata = Path.Combine(targetRoot, Path.GetFileName(sourceMetadata));

        try
        {
            File.WriteAllText(sourceVideo, string.Empty);
            File.WriteAllText(sourceMetadata, "{\"NickName\":\"Host\"}");

            ScreenRecordListViewModel.CopyAssociatedMetadata(sourceVideo, targetVideo);

            Assert.True(File.Exists(targetMetadata));
            Assert.Equal(File.ReadAllText(sourceMetadata), File.ReadAllText(targetMetadata));
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }
            if (Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, recursive: true);
            }
        }
    }
}
