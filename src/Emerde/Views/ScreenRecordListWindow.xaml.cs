using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Core;
using Emerde.Extensions;
using MediaInfoLib;
using Microsoft.VisualBasic.FileIO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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

    private readonly ObservableCollection<RecordedVideoItem> videos = [];

    public ICollectionView Videos { get; }

    public ScreenRecordListViewModel()
    {
        Videos = CollectionViewSource.GetDefaultView(videos);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionText))]
    private bool isSortDescending = true;

    public string SortDirectionText => IsSortDescending ? "倒序" : "正序";

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
    private async Task OpenVideoAsync(RecordedVideoItem? item)
    {
        if (item == null)
        {
            return;
        }

        await Player.PlayAsync(item.FullPath);
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

        ApplySort();
        RefreshSelectionSummary();
    }

    private static RecordedVideoItem CreateRecordedVideoItem(string path, string rootFolder)
    {
        FileInfo fileInfo = new(path);
        string nickName = InferNickName(path, rootFolder);
        string resolution = "-";
        string bitrate = "-";

        try
        {
            using MediaInfo mediaInfo = new();
            mediaInfo.Open(path);
            string width = mediaInfo.Get(StreamKind.Video, 0, "Width");
            string height = mediaInfo.Get(StreamKind.Video, 0, "Height");
            string bitRateRaw = mediaInfo.Get(StreamKind.Video, 0, "BitRate");

            if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
            {
                resolution = $"{width}x{height}";
            }

            if (double.TryParse(bitRateRaw, out double bitRate) && bitRate > 0)
            {
                bitrate = $"{bitRate / 1_000_000d:0.##} Mbps";
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return new RecordedVideoItem
        {
            FileName = fileInfo.Name,
            FullPath = fileInfo.FullName,
            DirectoryPath = fileInfo.DirectoryName ?? string.Empty,
            NickName = nickName,
            Resolution = resolution,
            Bitrate = bitrate,
            Title = $"{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}  路  {FormatFileSize(fileInfo.Length)}  路  {fileInfo.DirectoryName}",
            CreatedAt = fileInfo.LastWriteTime,
            CanTranscode = fileInfo.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || fileInfo.Extension.Equals(".flv", StringComparison.OrdinalIgnoreCase),
        };
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
        string? ffmpegPath = SearchFileHelper.SearchFiles(".", "ffmpeg[\\.exe]").FirstOrDefault();
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

public partial class RecordedVideoItem : ObservableObject
{
    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private string directoryPath = string.Empty;

    [ObservableProperty]
    private string nickName = string.Empty;

    [ObservableProperty]
    private string resolution = "-";

    [ObservableProperty]
    private string bitrate = "-";

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private DateTime createdAt;

    [ObservableProperty]
    private bool canTranscode;

    [ObservableProperty]
    private bool isSelected;
}
