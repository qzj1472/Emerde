using Emerde.Core;
using Emerde.Views;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Emerde.Tests;

public sealed class ScreenRecordListWindowTests
{
    [Theory]
    [InlineData(System.Windows.Input.Key.Up, true)]
    [InlineData(System.Windows.Input.Key.Down, true)]
    [InlineData(System.Windows.Input.Key.Left, false)]
    public void VideoListKeyboardNavigation_UsesVerticalArrowKeys(System.Windows.Input.Key key, bool expected)
    {
        Assert.Equal(expected, ScreenRecordListWindow.IsVideoListKeyboardNavigationKey(key));
    }

    [Fact]
    public void AdjacentVisibleVideo_MovesWithinFilteredOrderAndStopsAtBounds()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        videos.Add(first);
        videos.Add(second);

        Assert.Same(first, viewModel.GetAdjacentVisibleVideo(1));
        Assert.Same(second, viewModel.GetAdjacentVisibleVideo(-1));

        viewModel.SelectRegularItem(first);
        Assert.Same(second, viewModel.GetAdjacentVisibleVideo(1));
        Assert.Same(first, viewModel.GetAdjacentVisibleVideo(-1));

        viewModel.SelectRegularItem(second);
        Assert.Same(second, viewModel.GetAdjacentVisibleVideo(1));
    }

    [Fact]
    public void ReuseExistingVideoItems_PreservesUnchangedObjectsAndAddsNewFiles()
    {
        DateTime lastWrite = new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
        RecordedVideoItem existing = new() { FullPath = @"C:\videos\first.ts", IsEnriched = true, SourceLength = 100, SourceLastWriteTimeUtc = lastWrite };
        RecordedVideoItem reloaded = new() { FullPath = @"C:\videos\first.ts", SourceLength = 100, SourceLastWriteTimeUtc = lastWrite };
        RecordedVideoItem added = new() { FullPath = @"C:\videos\second.ts" };

        RecordedVideoItem[] result = ScreenRecordListViewModel.ReuseExistingVideoItems([existing], [reloaded, added]);

        Assert.Same(existing, result[0]);
        Assert.Same(added, result[1]);
        Assert.True(result[0].IsEnriched);
    }

    [Fact]
    public void ReuseExistingVideoItems_ReplacesFilesWhoseVersionChanged()
    {
        RecordedVideoItem existing = new() { FullPath = @"C:\videos\first.ts", SourceLength = 100 };
        RecordedVideoItem changed = new() { FullPath = @"C:\videos\first.ts", SourceLength = 200 };

        RecordedVideoItem[] result = ScreenRecordListViewModel.ReuseExistingVideoItems([existing], [changed]);

        Assert.Same(changed, result[0]);
    }

    [Fact]
    public void VideoListXaml_DoesNotUseSpacingStackPanelInConstrainedLayout()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.DoesNotContain("<ui:StackPanel", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListXaml_HandlesMouseWheelScrolling()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("PreviewMouseWheel=\"VideoListBoxPreviewMouseWheel\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListXaml_DisablesHorizontalScrollingAndFocusOutline()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FocusVisualStyle=\"{x:Null}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListXaml_HidesMultiSelectEntryWhenNoVideosAreVisible()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("Binding=\"{Binding HasVisibleVideos}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalResources_DoNotOverrideDefaultControlFocusOutlines()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Resources.xaml"));

        Assert.DoesNotContain("x:Key=\"{x:Static SystemParameters.FocusVisualStyleKey}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionHistoryLimit_IsTwoHundred()
    {
        Assert.Equal(200, ScreenRecordListViewModel.SelectionHistoryLimit);
    }

    [Fact]
    public void VideoSelection_SeparatesRegularAndMultiSelection()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        RecordedVideoItem third = new() { FullPath = @"C:\videos\third.ts" };
        videos.Add(first);
        videos.Add(second);
        videos.Add(third);

        viewModel.SelectRegularItem(first);

        Assert.Same(first, viewModel.RegularSelectedItem);
        Assert.False(first.IsSelected);
        Assert.False(viewModel.IsMultiSelectMode);

        viewModel.SelectMultipleItem(second, true, false);

        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.True(viewModel.IsMultiSelectMode);

        viewModel.SelectRegularItem(third);
        viewModel.ClearRegularSelection();

        Assert.Null(viewModel.RegularSelectedItem);
        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.True(viewModel.IsMultiSelectMode);
    }

    [Fact]
    public void SelectItems_EmptySelectionDoesNotEnterMultiSelect()
    {
        ScreenRecordListViewModel viewModel = new();

        viewModel.SelectItems([]);

        Assert.False(viewModel.IsMultiSelectMode);
    }

    [Fact]
    public void EmptyVideoList_CannotEnterMultiSelect()
    {
        ScreenRecordListViewModel viewModel = new();

        viewModel.BeginMultiSelectCommand.Execute(null);
        viewModel.SelectAllCommand.Execute(null);

        Assert.False(viewModel.HasVisibleVideos);
        Assert.False(viewModel.IsMultiSelectMode);
    }

    [Fact]
    public void EmptyFilteredVideoList_ExitsMultiSelectAndClearsSelection()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem item = new() { FullPath = @"C:\videos\first.ts", NickName = "Streamer" };
        videos.Add(item);
        viewModel.SelectAllCommand.Execute(null);

        viewModel.SelectedStreamer = "Other";

        Assert.False(viewModel.HasVisibleVideos);
        Assert.False(viewModel.IsMultiSelectMode);
        Assert.False(item.IsSelected);
        Assert.Null(viewModel.RegularSelectedItem);
    }

    [Fact]
    public void BeginMultiSelect_EntersModeWithoutSelectingRegularItem()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem item = new() { FullPath = @"C:\videos\first.ts" };
        videos.Add(item);
        viewModel.SelectRegularItem(item);

        viewModel.BeginMultiSelectCommand.Execute(null);

        Assert.True(viewModel.IsMultiSelectMode);
        Assert.Same(item, viewModel.RegularSelectedItem);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void SelectAll_EntersMultiSelectAndCanBeUndoneInOneStep()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        videos.Add(first);
        videos.Add(second);

        viewModel.SelectAllCommand.Execute(null);

        Assert.True(viewModel.IsMultiSelectMode);
        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);

        viewModel.UndoSelection();

        Assert.False(viewModel.IsMultiSelectMode);
        Assert.False(first.IsSelected);
        Assert.False(second.IsSelected);
    }

    [Theory]
    [InlineData(false, false, false, 2, true)]
    [InlineData(false, false, false, 1, false)]
    [InlineData(true, false, false, 2, false)]
    [InlineData(false, true, false, 2, false)]
    [InlineData(false, false, true, 2, false)]
    public void ShouldOpenVideoFromClick_OnlyAcceptsRegularDoubleClick(
        bool isMultiSelectMode,
        bool toggleSelection,
        bool selectRange,
        int clickCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            ScreenRecordListWindow.ShouldOpenVideoFromClick(isMultiSelectMode, toggleSelection, selectRange, clickCount));
    }

    [Fact]
    public void VideoMarquee_StartsOnlyFromEmptyListSpace()
    {
        Assert.True(ScreenRecordListWindow.CanStartVideoMarquee(null));
        Assert.False(ScreenRecordListWindow.CanStartVideoMarquee(new RecordedVideoItem()));
    }

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
        Assert.NotNull(viewModel.BeginMultiSelectCommand);
        Assert.NotNull(viewModel.SaveAsVideoCommand);
        Assert.NotNull(viewModel.SplitSelectedCommand);
        Assert.NotNull(viewModel.SplitContextCommand);
        Assert.NotNull(viewModel.MoveContextCommand);
        Assert.NotNull(viewModel.CopyContextCommand);
        Assert.NotNull(viewModel.DeleteContextCommand);
        Assert.NotNull(viewModel.OpenMergeSelectedCommand);
        Assert.NotNull(viewModel.ConfirmMergeSelectedCommand);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
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
    public void ParseMergeStreamSignature_DistinguishesCodecAndAudioLayout()
    {
        const string first = """
            {"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"pix_fmt":"yuv420p","time_base":"1/90000"},{"codec_type":"audio","codec_name":"aac","sample_rate":"48000","channels":2,"channel_layout":"stereo","time_base":"1/48000"}]}
            """;
        const string second = """
            {"streams":[{"codec_type":"video","codec_name":"hevc","width":1920,"height":1080,"pix_fmt":"yuv420p","time_base":"1/90000"},{"codec_type":"audio","codec_name":"aac","sample_rate":"48000","channels":6,"channel_layout":"5.1","time_base":"1/48000"}]}
            """;

        string firstSignature = ScreenRecordListViewModel.ParseMergeStreamSignature(first);
        string secondSignature = ScreenRecordListViewModel.ParseMergeStreamSignature(second);

        Assert.NotEmpty(firstSignature);
        Assert.NotEqual(firstSignature, secondSignature);
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
        string cacheDirectory = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-cache-{Guid.NewGuid():N}");
        string thumbnailPath = ScreenRecordListViewModel.GetThumbnailCachePath(videoPath, cacheDirectory);
        Directory.CreateDirectory(cacheDirectory);

        try
        {
            File.WriteAllText(videoPath, "video");
            File.WriteAllText(thumbnailPath, "fake");
            File.SetLastWriteTimeUtc(thumbnailPath, File.GetLastWriteTimeUtc(videoPath).AddSeconds(1));

            string result = ScreenRecordListViewModel.GetExistingThumbnailPath(videoPath, string.Empty, cacheDirectory);

            Assert.Equal(thumbnailPath, result);
        }
        finally
        {
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void GetExistingThumbnailPath_RejectsThumbnailOlderThanVideo()
    {
        string videoPath = Path.Combine(Path.GetTempPath(), $"record-{Guid.NewGuid():N}.mp4");
        string cacheDirectory = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-cache-{Guid.NewGuid():N}");
        string thumbnailPath = ScreenRecordListViewModel.GetThumbnailCachePath(videoPath, cacheDirectory);
        Directory.CreateDirectory(cacheDirectory);

        try
        {
            File.WriteAllText(thumbnailPath, "fake");
            File.WriteAllText(videoPath, "video");
            File.SetLastWriteTimeUtc(thumbnailPath, DateTime.UtcNow.AddMinutes(-1));
            File.SetLastWriteTimeUtc(videoPath, DateTime.UtcNow);

            string result = ScreenRecordListViewModel.GetExistingThumbnailPath(videoPath, string.Empty, cacheDirectory);

            Assert.Empty(result);
        }
        finally
        {
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void GetThumbnailCachePath_SharesCacheAcrossVideoFormats()
    {
        string cacheDirectory = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-cache-{Guid.NewGuid():N}");
        string stem = Path.Combine(Path.GetTempPath(), $"record-{Guid.NewGuid():N}");

        string transportStreamThumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(stem + ".ts", cacheDirectory);
        string mp4Thumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(stem + ".mp4", cacheDirectory);
        string otherThumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(stem + "-other.mp4", cacheDirectory);

        Assert.Equal(transportStreamThumbnail, mp4Thumbnail);
        Assert.NotEqual(mp4Thumbnail, otherThumbnail);
    }

    [Fact]
    public void CleanupOrphanedThumbnailCache_DeletesOnlyUnreferencedThumbnails()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-cleanup-{Guid.NewGuid():N}");
        string cacheDirectory = Path.Combine(root, "cache");
        Directory.CreateDirectory(cacheDirectory);
        string sourceVideo = Path.Combine(root, "record.ts");
        string convertedVideo = Path.Combine(root, "record.mp4");
        string sharedThumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(sourceVideo, cacheDirectory);
        string orphanedThumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(Path.Combine(root, "deleted.mp4"), cacheDirectory);

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(convertedVideo, "video");
            File.WriteAllText(sharedThumbnail, "thumbnail");
            File.WriteAllText(orphanedThumbnail, "thumbnail");

            int deleted = ScreenRecordListViewModel.CleanupOrphanedThumbnailCache([sourceVideo, convertedVideo], cacheDirectory);

            Assert.Equal(1, deleted);
            Assert.True(File.Exists(sharedThumbnail));
            Assert.False(File.Exists(orphanedThumbnail));
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
    public void DeleteThumbnailCacheIfUnused_WaitsForLastVideoFormat()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-delete-{Guid.NewGuid():N}");
        string cacheDirectory = Path.Combine(root, "cache");
        Directory.CreateDirectory(cacheDirectory);
        string sourceVideo = Path.Combine(root, "record.ts");
        string convertedVideo = Path.Combine(root, "record.mp4");
        string thumbnailPath = ScreenRecordListViewModel.GetThumbnailCachePath(sourceVideo, cacheDirectory);

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(convertedVideo, "video");
            File.WriteAllText(thumbnailPath, "thumbnail");

            File.Delete(sourceVideo);
            Assert.False(ScreenRecordListViewModel.DeleteThumbnailCacheIfUnused(sourceVideo, cacheDirectory));
            Assert.True(File.Exists(thumbnailPath));

            File.Delete(convertedVideo);
            Assert.True(ScreenRecordListViewModel.DeleteThumbnailCacheIfUnused(convertedVideo, cacheDirectory));
            Assert.False(File.Exists(thumbnailPath));
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
    public void ThumbnailImageConverter_DoesNotLockThumbnailFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-image-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string thumbnailPath = Path.Combine(root, "thumbnail.jpg");

        try
        {
            object? image = null;
            Exception? error = null;
            Thread thread = new(() =>
            {
                try
                {
                    System.Windows.Media.Imaging.BitmapSource source = System.Windows.Media.Imaging.BitmapSource.Create(
                        1,
                        1,
                        96,
                        96,
                        System.Windows.Media.PixelFormats.Bgra32,
                        null,
                        new byte[] { 0, 0, 255, 255 },
                        4);
                    System.Windows.Media.Imaging.PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
                    using (FileStream stream = File.Create(thumbnailPath))
                    {
                        encoder.Save(stream);
                    }

                    image = ThumbnailImageConverter.LoadImage(thumbnailPath);
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(error);
            Assert.NotSame(System.Windows.DependencyProperty.UnsetValue, image);
            File.Delete(thumbnailPath);
            Assert.False(File.Exists(thumbnailPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
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
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(targetVideo));
            Assert.Equal("Host", metadata.NickName);
            Assert.Equal(Path.GetFileName(targetVideo), metadata.FileName);
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
