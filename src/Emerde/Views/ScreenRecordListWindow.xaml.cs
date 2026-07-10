using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.Threading;
using MediaInfoLib;
using Microsoft.VisualBasic.FileIO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Windows.System;
using WindowsAPICodePack.Dialogs;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

public partial class ScreenRecordListWindow : System.Windows.Controls.UserControl
{
    public ScreenRecordListViewModel ViewModel { get; } = new();

    public ScreenRecordListWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        VideoListModalOverlay.IsVisibleChanged += (_, _) => DialogBlurScope.ApplyBackdropBrush(VideoListModalOverlay);
        Loaded += async (_, _) =>
        {
            DialogBlurScope.ApplyBackdropBrush(VideoListModalOverlay);
            await ViewModel.RefreshAsync();
        };
    }

    private void VideoListModalOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) && ViewModel.IsIdle)
        {
            ViewModel.CloseModalCommand.Execute(null);
            e.Handled = true;
        }
    }
}

public partial class ScreenRecordListViewModel : ObservableObject
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".flv", ".ts", ".mov", ".webm"];
    private const string AllStreamerOption = "全部主播";
    private const string UnknownStreamerText = "未知";
    private const string UnknownResolutionText = "未知";
    private const string UnknownBitrateText = "未知";
    private static readonly string[] TimeRangeOptionsInternal = ["全部时间", "24 小时内", "一周内", "一个月内", "三个月内", "一年内"];

    private readonly ObservableCollection<RecordedVideoItem> videos = [];
    private readonly SemaphoreSlim thumbnailLoadSemaphore = new(Math.Clamp(Environment.ProcessorCount, 2, 4));
    private CancellationTokenSource? thumbnailLoadCancellationTokenSource;

    public ICollectionView Videos { get; }
    public ObservableCollection<string> StreamerOptions { get; } = [AllStreamerOption];
    public IReadOnlyList<string> TimeRangeOptions => TimeRangeOptionsInternal;

    public ScreenRecordListViewModel()
    {
        Videos = CollectionViewSource.GetDefaultView(videos);
        Videos.Filter = FilterVideo;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionText))]
    private bool isSortDescending = true;

    public string SortDirectionText => IsSortDescending ? "倒序" : "正序";

    [ObservableProperty]
    private string selectedStreamer = AllStreamerOption;

    partial void OnSelectedStreamerChanged(string value)
    {
        ApplyFilters();
    }

    [ObservableProperty]
    private int selectedTimeRangeIndex;

    partial void OnSelectedTimeRangeIndexChanged(int value)
    {
        int next = Math.Clamp(value, 0, TimeRangeOptionsInternal.Length - 1);
        if (next != value)
        {
            SelectedTimeRangeIndex = next;
            return;
        }

        ApplyFilters();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedVideoSummary))]
    [NotifyPropertyChangedFor(nameof(HasSelectedVideos))]
    private bool isMultiSelectMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    private bool isSplitPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isOperating;

    [ObservableProperty]
    private string operationProgressText = string.Empty;

    [ObservableProperty]
    private string splitDurationValue = "30";

    [ObservableProperty]
    private int splitDurationUnitIndex;

    [ObservableProperty]
    private string splitTargetFileName = string.Empty;

    private RecordedVideoItem? splitTargetItem;

    public int SelectedVideoCount => videos.Count(video => video.IsSelected);

    public bool HasSelectedVideos => SelectedVideoCount > 0;

    public string SelectedVideoSummary => $"已选 {SelectedVideoCount} 个";

    public bool IsModalOpen => IsSplitPanelOpen;

    public bool IsIdle => !IsOperating;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        string root = SaveFolderHelper.GetSaveFolder(Configurations.SaveFolder.Get());
        await LoadVideosFromFolderAsync(root, replace: true);
    }

    [RelayCommand]
    private void ToggleSort()
    {
        IsSortDescending = !IsSortDescending;
        ApplySort();
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
        };

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            await LoadVideosFromFolderAsync(dialog.FileName, replace: false);
        }
    }

    [RelayCommand]
    private async Task OpenSaveFolderAsync()
    {
        string root = SaveFolderHelper.GetSaveFolder(Configurations.SaveFolder.Get());
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        await Launcher.LaunchFolderPathAsync(root);
    }

    [RelayCommand]
    private void ToggleMultiSelect()
    {
        IsMultiSelectMode = true;
    }

    [RelayCommand]
    private void CancelMultiSelect()
    {
        foreach (RecordedVideoItem item in videos)
        {
            item.IsSelected = false;
        }

        IsMultiSelectMode = false;
        RefreshSelectionSummary();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (RecordedVideoItem item in videos)
        {
            item.IsSelected = true;
        }

        RefreshSelectionSummary();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (RecordedVideoItem item in videos)
        {
            item.IsSelected = !item.IsSelected;
        }

        RefreshSelectionSummary();
    }

    [RelayCommand]
    private void OpenVideo(RecordedVideoItem? item)
    {
        if (item == null)
        {
            return;
        }

        if (!File.Exists(item.FullPath))
        {
            Toast.Warning("PlayerErrorOfNoFile".Tr());
            return;
        }

        try
        {
            _ = Process.Start(BuildDefaultOpenStartInfo(item.FullPath));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Toast.Warning("打开视频失败");
        }
    }

    internal static ProcessStartInfo BuildDefaultOpenStartInfo(string filePath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = filePath,
            UseShellExecute = true,
        };

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            startInfo.WorkingDirectory = directory;
        }

        return startInfo;
    }

    [RelayCommand]
    private async Task OpenDirectoryAsync(RecordedVideoItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.DirectoryPath))
        {
            return;
        }

        await Launcher.LaunchFolderPathAsync(item.DirectoryPath);
    }

    [RelayCommand]
    private async Task TranscodeVideoAsync(RecordedVideoItem? item)
    {
        if (item == null || !item.CanTranscode)
        {
            return;
        }

        bool converted = await new Converter().ExecuteAsync(item.FullPath, ".mp4");
        if (converted)
        {
            Toast.Success("转码完成");
        }
        else
        {
            Toast.Warning("转码失败");
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private void SplitVideo(RecordedVideoItem? item)
    {
        if (item == null)
        {
            return;
        }

        splitTargetItem = item;
        SplitTargetFileName = item.FileName;
        OperationProgressText = string.Empty;
        IsSplitPanelOpen = true;
    }

    [RelayCommand]
    private void CloseModal()
    {
        if (IsOperating)
        {
            return;
        }

        IsSplitPanelOpen = false;
        splitTargetItem = null;
        OperationProgressText = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmSplitAsync()
    {
        if (splitTargetItem == null || IsOperating)
        {
            return;
        }

        if (!TryGetSplitDurationSeconds(out int seconds))
        {
            Toast.Warning("分割时间无效");
            return;
        }

        IsOperating = true;
        OperationProgressText = "正在分割...";
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            bool result = await ExecuteFfmpegAsync(BuildSplitArguments(splitTargetItem.FullPath, seconds));
            OperationProgressText = result ? "分割完成" : "分割失败";
            if (result)
            {
                Toast.Success("分割完成");
                IsSplitPanelOpen = false;
                await RefreshAsync();
            }
            else
            {
                Toast.Warning("分割失败");
            }
        }
        finally
        {
            IsOperating = false;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    [RelayCommand]
    private async Task MergeSelectedAsync()
    {
        RecordedVideoItem[] selected = GetSelectedVideos()
            .OrderBy(video => video.CreatedAt)
            .ToArray();

        if (selected.Length < 2)
        {
            Toast.Warning("至少选择 2 个视频");
            return;
        }

        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }

        IsOperating = true;
        OperationProgressText = "正在合并...";
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            string extension = selected.Select(video => Path.GetExtension(video.FullPath))
                .FirstOrDefault(extension => !string.IsNullOrWhiteSpace(extension)) ?? ".mp4";
            string outputPath = Path.Combine(dialog.FileName, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
            string listPath = Path.Combine(Path.GetTempPath(), $"emerde_concat_{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(listPath, selected.Select(video => $"file '{video.FullPath.Replace("'", "'\\''", StringComparison.Ordinal)}'"), Encoding.UTF8);

            try
            {
                bool result = await ExecuteFfmpegAsync($"-y -f concat -safe 0 -i \"{listPath}\" -c copy \"{outputPath}\"");
                if (result)
                {
                    Toast.Success("合并完成");
                    await RefreshAsync();
                }
                else
                {
                    Toast.Warning("合并失败");
                }
            }
            finally
            {
                File.Delete(listPath);
            }
        }
        finally
        {
            IsOperating = false;
            OperationProgressText = string.Empty;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        RecordedVideoItem[] selected = GetSelectedVideos();
        if (selected.Length == 0)
        {
            return;
        }

        System.Windows.MessageBoxResult result;
        using (DialogBlurScope blurScope = new())
        {
            result = await MessageBox.QuestionAsync($"确定删除 {selected.Length} 个视频文件？");
        }
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        foreach (RecordedVideoItem item in selected)
        {
            try
            {
                if (File.Exists(item.FullPath))
                {
                    FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        await CopyOrMoveSelectedAsync(move: true);
    }

    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        await CopyOrMoveSelectedAsync(move: false);
    }

    private async Task CopyOrMoveSelectedAsync(bool move)
    {
        RecordedVideoItem[] selected = GetSelectedVideos();
        if (selected.Length == 0)
        {
            return;
        }

        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }

        foreach (RecordedVideoItem item in selected)
        {
            try
            {
                string targetPath = Path.Combine(dialog.FileName, item.FileName);
                if (move)
                {
                    File.Move(item.FullPath, targetPath, overwrite: true);
                }
                else
                {
                    File.Copy(item.FullPath, targetPath, overwrite: true);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        await RefreshAsync();
    }

    private async Task LoadVideosFromFolderAsync(string folder, bool replace)
    {
        thumbnailLoadCancellationTokenSource?.Cancel();
        thumbnailLoadCancellationTokenSource?.Dispose();
        thumbnailLoadCancellationTokenSource = new CancellationTokenSource();
        CancellationToken thumbnailToken = thumbnailLoadCancellationTokenSource.Token;

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        RecordedVideoItem[] items = await Task.Run(() =>
        {
            return Directory.EnumerateFiles(folder, "*.*", System.IO.SearchOption.AllDirectories)
                .Where(path => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => CreateRecordedVideoItem(path, folder))
                .ToArray();
        });

        if (replace)
        {
            foreach (RecordedVideoItem item in videos)
            {
                item.PropertyChanged -= VideoItemPropertyChanged;
            }

            videos.Clear();
        }

        HashSet<string> existing = videos.Select(video => video.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (RecordedVideoItem item in items.Where(item => existing.Add(item.FullPath)))
        {
            item.PropertyChanged += VideoItemPropertyChanged;
            videos.Add(item);
        }

        UpdateStreamerOptions();
        ApplySort();
        ApplyFilters();
        RefreshSelectionSummary();
        QueueThumbnailLoading(videos.ToArray(), thumbnailToken);
    }

    private static RecordedVideoItem CreateRecordedVideoItem(string path, string rootFolder)
    {
        FileInfo fileInfo = new(path);
        VideoRecordingMetadata metadata = LoadMetadata(fileInfo);
        VideoProbeInfo probeInfo = VideoProbeInfo.Empty;
        bool hasProbeInfo = false;

        if (VideoRecordingMetadataStore.NeedsEmbeddedMetadataProbe(metadata))
        {
            probeInfo = ProbeVideoFileInfo(fileInfo.FullName, fileInfo.Length);
            hasProbeInfo = true;
            metadata = VideoRecordingMetadataStore.Merge(metadata, probeInfo.Metadata);
        }

        string resolution = NormalizeResolution(metadata.Resolution);
        string bitrate = NormalizeBitrate(metadata.Bitrate);
        bool hasVideoResolution = false;
        bool hasVideoBitrate = false;

        try
        {
            using MediaInfo mediaInfo = new();
            mediaInfo.Open(path);
            string width = mediaInfo.Get(StreamKind.Video, 0, "Width");
            string height = mediaInfo.Get(StreamKind.Video, 0, "Height");
            string bitRateRaw = mediaInfo.Get(StreamKind.Video, 0, "BitRate");

            if (TryBuildResolution(width, height, out string mediaResolution))
            {
                resolution = mediaResolution;
                hasVideoResolution = true;
            }

            if (double.TryParse(bitRateRaw, out double bitRate) && bitRate > 0)
            {
                bitrate = $"{bitRate / 1_000_000d:0.##} Mbps";
                hasVideoBitrate = true;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        if (!hasVideoResolution || !hasVideoBitrate)
        {
            if (!hasProbeInfo)
            {
                probeInfo = ProbeVideoFileInfo(fileInfo.FullName, fileInfo.Length);
            }

            if (!hasVideoResolution && !string.IsNullOrWhiteSpace(probeInfo.Resolution))
            {
                resolution = probeInfo.Resolution;
            }
            if (!hasVideoBitrate && !string.IsNullOrWhiteSpace(probeInfo.Bitrate))
            {
                bitrate = probeInfo.Bitrate;
            }
        }

        string nickName = NormalizeStreamerName(string.IsNullOrWhiteSpace(metadata.NickName) ? InferNickName(path, rootFolder) : metadata.NickName);
        DateTime createdAt = metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt : fileInfo.LastWriteTime;
        string thumbnailPath = GetExistingThumbnailPath(fileInfo.FullName, metadata.CoverPath);
        string title = string.IsNullOrWhiteSpace(metadata.Title)
            ? $"{createdAt:yyyy-MM-dd HH:mm:ss}  |  {FormatFileSize(fileInfo.Length)}  |  {fileInfo.DirectoryName}"
            : $"{metadata.Title}  |  {createdAt:yyyy-MM-dd HH:mm:ss}  |  {FormatFileSize(fileInfo.Length)}";

        return new RecordedVideoItem
        {
            FileName = fileInfo.Name,
            FullPath = fileInfo.FullName,
            DirectoryPath = fileInfo.DirectoryName ?? string.Empty,
            NickName = nickName,
            Resolution = resolution,
            Bitrate = bitrate,
            CoverPath = metadata.CoverPath,
            ThumbnailPath = thumbnailPath,
            Title = title,
            CreatedAt = createdAt,
            CanTranscode = fileInfo.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || fileInfo.Extension.Equals(".flv", StringComparison.OrdinalIgnoreCase),
        };
    }

    internal static VideoRecordingMetadata LoadMetadata(FileInfo file)
    {
        return VideoRecordingMetadataStore.Load(file);
    }

    internal static string NormalizeResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsMediaInfoErrorText(value))
        {
            return UnknownResolutionText;
        }

        string text = value.Trim();
        string[] parts = text.Split(['x', 'X', '*', '×'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            && width > 0
            && height > 0)
        {
            return $"{width}x{height}";
        }

        return UnknownResolutionText;
    }

    private static bool TryBuildResolution(string widthText, string heightText, out string resolution)
    {
        resolution = string.Empty;
        if (IsMediaInfoErrorText(widthText) || IsMediaInfoErrorText(heightText))
        {
            return false;
        }

        if (int.TryParse(widthText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(heightText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            && width > 0
            && height > 0)
        {
            resolution = $"{width}x{height}";
            return true;
        }

        return false;
    }

    private static string NormalizeBitrate(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || IsMediaInfoErrorText(value)
            ? UnknownBitrateText
            : value.Trim();
    }

    private static string NormalizeStreamerName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || IsMediaInfoErrorText(value)
            ? UnknownStreamerText
            : value.Trim();
    }

    private static bool IsMediaInfoErrorText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains("Unable to load MediaInfo", StringComparison.OrdinalIgnoreCase);
    }

    private static VideoProbeInfo ProbeVideoFileInfo(string filePath, long fileSize)
    {
        string? ffprobePath = SearchFileHelper.SearchExecutable("ffprobe.exe");
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(filePath))
        {
            return VideoProbeInfo.Empty;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = ffprobePath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };

            foreach (string argument in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,bit_rate:format=bit_rate,duration:format_tags", "-of", "json", filePath })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            _ = ChildProcessTracerPeriodicTimer.Default.TryTraceProcess(process);

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return VideoProbeInfo.Empty;
            }

            _ = errorTask.GetAwaiter().GetResult();
            return ParseVideoProbeJson(outputTask.GetAwaiter().GetResult(), fileSize);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or Win32Exception or JsonException)
        {
            return VideoProbeInfo.Empty;
        }
    }

    internal static VideoProbeInfo ParseVideoProbeJson(string json, long fileSize = 0)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return VideoProbeInfo.Empty;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string resolution = string.Empty;
        double bitrate = 0;
        VideoRecordingMetadata? metadata = null;

        if (root.TryGetProperty("streams", out JsonElement streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                if (TryGetInt(stream, "width", out int width)
                    && TryGetInt(stream, "height", out int height)
                    && width > 0
                    && height > 0)
                {
                    resolution = $"{width}x{height}";
                }

                if (TryGetDouble(stream, "bit_rate", out double streamBitrate) && streamBitrate > 0)
                {
                    bitrate = streamBitrate;
                }

                break;
            }
        }

        if (root.TryGetProperty("format", out JsonElement format))
        {
            if (format.TryGetProperty("tags", out JsonElement tags))
            {
                VideoRecordingMetadata embeddedMetadata = VideoRecordingMetadataStore.FromTags(tags, string.Empty);
                if (VideoRecordingMetadataStore.HasAnyMetadata(embeddedMetadata))
                {
                    metadata = embeddedMetadata;
                }
            }

            if (bitrate <= 0 && TryGetDouble(format, "bit_rate", out double formatBitrate) && formatBitrate > 0)
            {
                bitrate = formatBitrate;
            }

            if (bitrate <= 0
                && fileSize > 0
                && TryGetDouble(format, "duration", out double duration)
                && duration > 0)
            {
                bitrate = fileSize * 8d / duration;
            }
        }

        return new VideoProbeInfo(resolution, bitrate > 0 ? FormatBitrate(bitrate) : string.Empty, metadata);
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static string FormatBitrate(double bitsPerSecond)
    {
        return $"{(bitsPerSecond / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture)} Mbps";
    }

    private void QueueThumbnailLoading(IEnumerable<RecordedVideoItem> items, CancellationToken token)
    {
        foreach (RecordedVideoItem item in items.Where(item => !item.HasThumbnail))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await thumbnailLoadSemaphore.WaitAsync(token);
                    try
                    {
                        await EnrichThumbnailAsync(item, token);
                    }
                    finally
                    {
                        thumbnailLoadSemaphore.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);
        }
    }

    private static async Task EnrichThumbnailAsync(RecordedVideoItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (item.HasThumbnail)
        {
            return;
        }

        string thumbnailPath = File.Exists(item.CoverPath)
            ? item.CoverPath
            : await ExtractThumbnailAsync(item.FullPath, token);

        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!token.IsCancellationRequested)
            {
                item.ThumbnailPath = thumbnailPath;
            }
        });
    }

    private static async Task<string> ExtractThumbnailAsync(string filePath, CancellationToken token)
    {
        string? ffmpegPath = SearchFileHelper.SearchExecutable("ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(filePath))
        {
            return string.Empty;
        }

        string cacheDir = GetThumbnailCacheDirectory();
        Directory.CreateDirectory(cacheDir);
        string imagePath = GetThumbnailCachePath(filePath);

        if (File.Exists(imagePath) && new FileInfo(imagePath).Length > 0)
        {
            return imagePath;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };

            foreach (string argument in new[] { "-y", "-v", "error", "-ss", "00:00:01", "-i", filePath, "-frames:v", "1", "-vf", "scale=320:-1", imagePath })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            _ = ChildProcessTracerPeriodicTimer.Default.TryTraceProcess(process);

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(token);
            Task exitTask = process.WaitForExitAsync(CancellationToken.None);
            Task timeoutTask = Task.Delay(12000, token);

            if (await Task.WhenAny(exitTask, timeoutTask) != exitTask)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                token.ThrowIfCancellationRequested();
                return string.Empty;
            }

            await Task.WhenAll(outputTask, errorTask);
            return File.Exists(imagePath) && new FileInfo(imagePath).Length > 0 ? imagePath : string.Empty;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or OperationCanceledException or Win32Exception)
        {
            return string.Empty;
        }
    }

    private static string ToStableHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private static string GetThumbnailCacheDirectory()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "video_thumbnails");
    }

    internal static string GetThumbnailCachePath(string filePath)
    {
        return Path.Combine(GetThumbnailCacheDirectory(), $"{ToStableHash(filePath)}.jpg");
    }

    internal static string GetExistingThumbnailPath(string filePath, string coverPath)
    {
        if (File.Exists(coverPath))
        {
            return coverPath;
        }

        string cachePath = GetThumbnailCachePath(filePath);
        return File.Exists(cachePath) && new FileInfo(cachePath).Length > 0 ? cachePath : string.Empty;
    }

    internal static string InferNickName(string filePath, string rootFolder)
    {
        FileInfo fileInfo = new(filePath);
        string fallback = fileInfo.Directory?.Name ?? "-";

        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            return fallback;
        }

        string fullRoot = Path.GetFullPath(rootFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(filePath);
        string relativePath = Path.GetRelativePath(fullRoot, fullPath);

        if (relativePath == "." || relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return fallback;
        }

        string? relativeDirectory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(relativeDirectory))
        {
            return fallback;
        }

        string[] parts = relativeDirectory
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return fallback;
        }

        if (parts.Length >= 2 && IsYearMonth(parts[1]))
        {
            return parts[0];
        }

        return parts[^1];
    }

    private static bool IsYearMonth(string value)
    {
        return value.Length == 7
            && value[4] == '-'
            && int.TryParse(value[..4], out int year)
            && int.TryParse(value[5..], out int month)
            && year is >= 1900 and <= 9999
            && month is >= 1 and <= 12;
    }

    private void ApplySort()
    {
        Videos.SortDescriptions.Clear();
        Videos.SortDescriptions.Add(new SortDescription(
            nameof(RecordedVideoItem.CreatedAt),
            IsSortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        Videos.Refresh();
    }

    private void ApplyFilters()
    {
        Videos.Refresh();
    }

    private bool FilterVideo(object item)
    {
        if (item is not RecordedVideoItem video)
        {
            return false;
        }

        bool streamerMatched = string.IsNullOrWhiteSpace(SelectedStreamer)
            || string.Equals(SelectedStreamer, AllStreamerOption, StringComparison.Ordinal)
            || string.Equals(video.NickName, SelectedStreamer, StringComparison.OrdinalIgnoreCase);
        if (!streamerMatched)
        {
            return false;
        }

        DateTime threshold = SelectedTimeRangeIndex switch
        {
            1 => DateTime.Now.AddDays(-1),
            2 => DateTime.Now.AddDays(-7),
            3 => DateTime.Now.AddDays(-30),
            4 => DateTime.Now.AddDays(-90),
            5 => DateTime.Now.AddDays(-365),
            _ => DateTime.MinValue,
        };

        return threshold == DateTime.MinValue || video.CreatedAt >= threshold;
    }

    private void UpdateStreamerOptions()
    {
        string currentSelection = SelectedStreamer;
        string[] streamers = videos
            .Select(video => video.NickName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        StreamerOptions.Clear();
        StreamerOptions.Add(AllStreamerOption);
        foreach (string streamer in streamers)
        {
            StreamerOptions.Add(streamer);
        }

        if (!StreamerOptions.Contains(currentSelection))
        {
            SelectedStreamer = AllStreamerOption;
            return;
        }

        SelectedStreamer = currentSelection;
    }

    private void VideoItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordedVideoItem.IsSelected))
        {
            RefreshSelectionSummary();
        }
    }

    private void RefreshSelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedVideoCount));
        OnPropertyChanged(nameof(SelectedVideoSummary));
        OnPropertyChanged(nameof(HasSelectedVideos));
    }

    private RecordedVideoItem[] GetSelectedVideos()
    {
        return videos.Where(video => video.IsSelected).ToArray();
    }

    private bool TryGetSplitDurationSeconds(out int seconds)
    {
        seconds = 0;
        if (!double.TryParse(SplitDurationValue, out double value) || value <= 0)
        {
            return false;
        }

        seconds = SplitDurationUnitIndex switch
        {
            1 => (int)Math.Round(value),
            2 => (int)Math.Round(value * 3600d),
            0 or _ => (int)Math.Round(value * 60d),
        };

        seconds = Math.Max(1, seconds);
        return true;
    }

    private static string BuildSplitArguments(string sourcePath, int seconds)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string name = Path.GetFileNameWithoutExtension(sourcePath);
        string extension = Path.GetExtension(sourcePath);
        string targetPattern = Path.Combine(directory, $"{name}_part_%03d{extension}");

        return $"-y -i \"{sourcePath}\" -c copy -map 0 -f segment -segment_time {seconds} -reset_timestamps 1 \"{targetPattern}\"";
    }

    private static async Task<bool> ExecuteFfmpegAsync(string arguments)
    {
        string? ffmpegPath = SearchFileHelper.SearchExecutable("ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return false;
        }

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int index = 0;

        while (value >= 1024d && index < units.Length - 1)
        {
            value /= 1024d;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }
}

internal readonly record struct VideoProbeInfo(string Resolution, string Bitrate, VideoRecordingMetadata? Metadata)
{
    public static VideoProbeInfo Empty { get; } = new(string.Empty, string.Empty, null);
}

public partial class RecordedVideoItem : ObservableObject
{
    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private string directoryPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreamerChipText))]
    private string nickName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolutionChipText))]
    private string resolution = "-";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateChipText))]
    private string bitrate = "-";

    [ObservableProperty]
    private string coverPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private string thumbnailPath = string.Empty;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private DateTime createdAt;

    [ObservableProperty]
    private bool canTranscode;

    [ObservableProperty]
    private bool isSelected;

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);

    public string StreamerChipText => string.IsNullOrWhiteSpace(NickName) ? "主播 未知" : $"主播 {NickName}";

    public string ResolutionChipText => string.IsNullOrWhiteSpace(Resolution) ? "分辨率 未知" : $"分辨率 {Resolution}";

    public string BitrateChipText => string.IsNullOrWhiteSpace(Bitrate) ? "码率 未知" : $"码率 {Bitrate}";
}
