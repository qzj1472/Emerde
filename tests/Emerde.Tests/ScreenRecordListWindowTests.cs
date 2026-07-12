using Emerde.Core;
using Emerde.Views;
using System.Text.Json;

namespace Emerde.Tests;

public sealed class ScreenRecordListWindowTests
{
    [Fact]
    public void ContextMenuCommands_AreAvailable()
    {
        ScreenRecordListViewModel viewModel = new();

        Assert.NotNull(viewModel.DeleteVideoCommand);
        Assert.NotNull(viewModel.SplitVideoCommand);
        Assert.NotNull(viewModel.TranscodeVideoCommand);
        Assert.NotNull(viewModel.OpenDirectoryCommand);
        Assert.NotNull(viewModel.RenameVideoCommand);
        Assert.NotNull(viewModel.OpenVideoCommand);
        Assert.NotNull(viewModel.SaveAsVideoCommand);
        Assert.NotNull(viewModel.SplitSelectedCommand);
        Assert.NotNull(viewModel.OpenMergeSelectedCommand);
        Assert.NotNull(viewModel.ConfirmMergeSelectedCommand);
    }

    [Fact]
    public void EnumerateVideoFiles_ReturnsEmptyWhenFolderDoesNotExist()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"emerde-missing-{Guid.NewGuid():N}");

        Assert.Empty(ScreenRecordListViewModel.EnumerateVideoFiles(folder));
    }

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

    [Theory]
    [InlineData("new-name", "new-name.mkv")]
    [InlineData("new-name.mkv", "new-name.mkv")]
    public void TryBuildRenameTarget_PreservesOriginalExtension(string requestedName, string expectedFileName)
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), "old-name.mkv");

        bool result = ScreenRecordListViewModel.TryBuildRenameTarget(sourcePath, requestedName, out string targetPath);

        Assert.True(result);
        Assert.Equal(Path.Combine(Path.GetTempPath(), expectedFileName), targetPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("folder/name")]
    [InlineData("bad:name")]
    public void TryBuildRenameTarget_RejectsInvalidNames(string requestedName)
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), "old-name.mkv");

        Assert.False(ScreenRecordListViewModel.TryBuildRenameTarget(sourcePath, requestedName, out _));
    }

    [Fact]
    public void GetUniquePath_DoesNotOverwriteExistingVideo()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-unique-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string original = Path.Combine(root, "record.mkv");

        try
        {
            File.WriteAllText(original, "video");

            string result = ScreenRecordListViewModel.GetUniquePath(original);

            Assert.Equal(Path.Combine(root, "record_001.mkv"), result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "Host")]
    [InlineData(2, @"Host\2026-07")]
    [InlineData(3, @"Host\2026-07\12")]
    public void BuildClassifiedFolder_MatchesRecorderPathLevels(int pathLevel, string relativePath)
    {
        string root = Path.Combine(Path.GetTempPath(), "records");
        FileInfo file = new(Path.Combine(Path.GetTempPath(), "source", "record.ts"));
        VideoRecordingMetadata metadata = new()
        {
            NickName = "Host",
            RecordedAt = new DateTime(2026, 7, 12, 10, 0, 0),
        };

        string result = ScreenRecordListViewModel.BuildClassifiedFolder(root, metadata, file, file.DirectoryName!, pathLevel);

        Assert.Equal(string.IsNullOrEmpty(relativePath) ? root : Path.Combine(root, relativePath), result);
    }

    [Fact]
    public void TransferVideoFile_MovePreservesMetadata()
    {
        string sourceRoot = Path.Combine(Path.GetTempPath(), $"emerde-move-source-{Guid.NewGuid():N}");
        string targetRoot = Path.Combine(Path.GetTempPath(), $"emerde-move-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        string sourceVideo = Path.Combine(sourceRoot, "record.ts");
        string sourceMetadata = Path.Combine(sourceRoot, "record.mplr.json");
        string targetVideo = Path.Combine(targetRoot, "record.ts");

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(sourceMetadata, JsonSerializer.Serialize(new VideoRecordingMetadata { NickName = "Host" }));

            ScreenRecordListViewModel.TransferVideoFile(sourceVideo, targetVideo, move: true);

            Assert.False(File.Exists(sourceVideo));
            Assert.False(File.Exists(sourceMetadata));
            Assert.True(File.Exists(targetVideo));
            Assert.Equal("Host", ScreenRecordListViewModel.LoadMetadata(new FileInfo(targetVideo)).NickName);
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

    [Fact]
    public void OrderVideosForMerge_UsesFourDigitSegmentIndex()
    {
        RecordedVideoItem[] videos =
        [
            new() { FileName = "record_1001.ts", FullPath = @"C:\videos\record_1001.ts", CreatedAt = new DateTime(2026, 7, 12) },
            new() { FileName = "record_1000.ts", FullPath = @"C:\videos\record_1000.ts", CreatedAt = new DateTime(2026, 7, 12) },
            new() { FileName = "record_1002.ts", FullPath = @"C:\videos\record_1002.ts", CreatedAt = new DateTime(2026, 7, 12) },
        ];

        string[] result = ScreenRecordListViewModel.OrderVideosForMerge(videos).Select(video => video.FileName).ToArray();

        Assert.Equal(["record_1000.ts", "record_1001.ts", "record_1002.ts"], result);
    }

    [Fact]
    public void BuildMergeWarningText_ReportsNonContinuousSegments()
    {
        RecordedVideoItem[] videos =
        [
            new() { NickName = "Host", Resolution = "1920x1080", FullPath = @"C:\videos\record_001.ts" },
            new() { NickName = "Host", Resolution = "1920x1080", FullPath = @"C:\videos\record_003.ts" },
        ];

        string result = ScreenRecordListViewModel.BuildMergeWarningText(videos);

        Assert.Contains("不是同一组连续分段", result);
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

    [Fact]
    public void RenameVideoFile_MovesVideoAndUpdatesMetadataFileName()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string sourceVideo = Path.Combine(root, "old-name.mkv");
        string sourceMetadata = Path.Combine(root, "old-name.mplr.json");
        string targetVideo = Path.Combine(root, "new-name.mkv");
        string targetMetadata = Path.Combine(root, "new-name.mplr.json");

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(sourceMetadata, JsonSerializer.Serialize(new VideoRecordingMetadata
            {
                FileName = "old-name.mkv",
                NickName = "Host",
            }));

            ScreenRecordListViewModel.RenameVideoFile(sourceVideo, targetVideo);

            Assert.False(File.Exists(sourceVideo));
            Assert.False(File.Exists(sourceMetadata));
            Assert.True(File.Exists(targetVideo));
            Assert.True(File.Exists(targetMetadata));
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(targetVideo));
            Assert.Equal("new-name.mkv", metadata.FileName);
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
    public void RenameVideoFile_RollsBackWhenTargetMetadataCannotBeWritten()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-rename-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string sourceVideo = Path.Combine(root, "old-name.mkv");
        string sourceMetadata = Path.Combine(root, "old-name.mplr.json");
        string targetVideo = Path.Combine(root, "new-name.mkv");
        string targetMetadata = Path.Combine(root, "new-name.mplr.json");

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(sourceMetadata, JsonSerializer.Serialize(new VideoRecordingMetadata
            {
                FileName = "old-name.mkv",
                NickName = "Host",
            }));
            Directory.CreateDirectory(targetMetadata);

            Assert.Throws<IOException>(() => ScreenRecordListViewModel.RenameVideoFile(sourceVideo, targetVideo));

            Assert.True(File.Exists(sourceVideo));
            Assert.True(File.Exists(sourceMetadata));
            Assert.False(File.Exists(targetVideo));
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(sourceVideo));
            Assert.Equal("old-name.mkv", metadata.FileName);
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
}
