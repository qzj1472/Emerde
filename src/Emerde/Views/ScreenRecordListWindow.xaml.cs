using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.Properties;
using Emerde.Threading;
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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.System;
using WindowsAPICodePack.Dialogs;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

public partial class ScreenRecordListWindow : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer visibleVideoLoadTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public ScreenRecordListViewModel ViewModel { get; } = new();

    public ScreenRecordListWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        VideoListModalOverlay.IsVisibleChanged += (_, _) => DialogBlurScope.ApplyBackdropBrush(VideoListModalOverlay);
        visibleVideoLoadTimer.Tick += VisibleVideoLoadTimerTick;
        ViewModel.VisibleItemsChanged += (_, _) => ScheduleVisibleVideoLoading();
        Loaded += ScreenRecordListWindowLoaded;
        Unloaded += ScreenRecordListWindowUnloaded;
        SizeChanged += (_, _) => ScheduleVisibleVideoLoading();
        PreviewKeyDown += ScreenRecordListWindowPreviewKeyDown;
    }

    private async void ScreenRecordListWindowLoaded(object sender, RoutedEventArgs e)
    {
        DialogBlurScope.ApplyBackdropBrush(VideoListModalOverlay);
        await ViewModel.RefreshAsync();
        ScheduleVisibleVideoLoading();
    }

    private void ScreenRecordListWindowUnloaded(object sender, RoutedEventArgs e)
    {
        visibleVideoLoadTimer.Stop();
        ViewModel.CancelBackgroundLoading();
    }

    private void ScheduleVisibleVideoLoading()
    {
        visibleVideoLoadTimer.Stop();
        visibleVideoLoadTimer.Start();
    }

    private void VisibleVideoLoadTimerTick(object? sender, EventArgs e)
    {
        visibleVideoLoadTimer.Stop();
        ViewModel.QueueVisibleVideoEnrichment(GetVisibleVideoItems());
    }

    private IEnumerable<RecordedVideoItem> GetVisibleVideoItems()
    {
        if (VideoListBox == null)
        {
            yield break;
        }

        for (int index = 0; index < VideoListBox.Items.Count; index++)
        {
            if (VideoListBox.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container)
            {
                continue;
            }

            Rect bounds = container.TransformToAncestor(VideoListBox)
                .TransformBounds(new Rect(new System.Windows.Point(0, 0), container.RenderSize));
            if (bounds.Bottom >= 0 && bounds.Top <= VideoListBox.ActualHeight && VideoListBox.Items[index] is RecordedVideoItem item)
            {
                yield return item;
            }
        }
    }

    private void VideoListScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        ScheduleVisibleVideoLoading();
    }

    private void VideoCardLoaded(object sender, RoutedEventArgs e)
    {
        ScheduleVisibleVideoLoading();
    }

    private void SelectionCheckBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { DataContext: RecordedVideoItem item })
        {
            return;
        }

        e.Handled = true;
        ViewModel.ToggleSelection(item, (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
    }

    private void VideoCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecordedVideoItem item }
            || IsInteractiveElement(e.OriginalSource as DependencyObject, sender as DependencyObject))
        {
            return;
        }

        if (ViewModel.IsMultiSelectMode)
        {
            if (e.ClickCount == 1)
            {
                ViewModel.ToggleSelection(item, (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
            }
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            ViewModel.OpenVideoCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void ScreenRecordListWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.Z)
        {
            ViewModel.UndoSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            ViewModel.RedoSelection();
            e.Handled = true;
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source, DependencyObject? boundary)
    {
        while (source != null && !ReferenceEquals(source, boundary))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Selector)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
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
    private static readonly string AllStreamerOption = GetResourceText("VideoAllStreamers", "All streamers");
    private static readonly string UnknownStreamerText = GetResourceText("CommonUnknown", "Unknown");
    private static readonly string UnknownResolutionText = GetResourceText("CommonUnknown", "Unknown");
    private static readonly string UnknownBitrateText = GetResourceText("CommonUnknown", "Unknown");
    private static readonly string[] TimeRangeOptionsInternal =
    [
        GetResourceText("TimeRangeAll", "All time"),
        GetResourceText("TimeRangeLast24Hours", "Last 24 hours"),
        GetResourceText("TimeRangeLastWeek", "Last week"),
        GetResourceText("TimeRangeLastMonth", "Last month"),
        GetResourceText("TimeRangeLastThreeMonths", "Last 3 months"),
        GetResourceText("TimeRangeLastYear", "Last year"),
    ];

    private readonly ObservableCollection<RecordedVideoItem> videos = [];
    private const int SelectionHistoryLimit = 50;
    private readonly SemaphoreSlim videoEnrichmentSemaphore = new(Math.Clamp(Environment.ProcessorCount, 2, 4));
    private readonly object videoEnrichmentLock = new();
    private readonly HashSet<string> queuedVideoEnrichmentPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<SelectionSnapshot> selectionUndoStack = [];
    private readonly Stack<SelectionSnapshot> selectionRedoStack = [];
    private CancellationTokenSource? videoLoadCancellationTokenSource;
    private CancellationTokenSource? videoEnrichmentCancellationTokenSource;
    private RecordedVideoItem? lastSelectedItem;
    private bool isRestoringSelection;

    public ICollectionView Videos { get; }
    public ObservableCollection<string> StreamerOptions { get; } = [AllStreamerOption];
    public IReadOnlyList<string> TimeRangeOptions => TimeRangeOptionsInternal;
    public event EventHandler? VisibleItemsChanged;

    public ScreenRecordListViewModel()
    {
        Videos = CollectionViewSource.GetDefaultView(videos);
        Videos.Filter = FilterVideo;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionText))]
    private bool isSortDescending = true;

    public string SortDirectionText => IsSortDescending
        ? GetResourceText("SortDescending", "Descending")
        : GetResourceText("SortAscending", "Ascending");

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
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    private bool isMergePanelOpen;

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

    [ObservableProperty]
    private string mergeWarningText = string.Empty;

    [ObservableProperty]
    private double mergeProgressValue;

    [ObservableProperty]
    private bool isMergeProgressIndeterminate;

    private RecordedVideoItem? splitTargetItem;

    public int SelectedVideoCount => GetVisibleVideos().Count(video => video.IsSelected);

    public bool HasSelectedVideos => SelectedVideoCount > 0;

    public bool CanMergeSelectedVideos => SelectedVideoCount >= 2;

    public string SelectedVideoSummary => FormatResourceText("VideoSelectedCount", "{0} selected", SelectedVideoCount);

    public bool IsModalOpen => IsSplitPanelOpen || IsMergePanelOpen;

    public bool IsIdle => !IsOperating;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        string root = SaveFolderHelper.GetSaveFolder(Configurations.SaveFolder.Get());
        await LoadVideosFromFolderAsync(root);
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
        if (IsOperating)
        {
            return;
        }

        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
            Title = "选择要导入的视频文件夹",
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok || !Directory.Exists(dialog.FileName))
        {
            return;
        }

        string sourceFolder = dialog.FileName;
        string root = SaveFolderHelper.GetSaveFolder(Configurations.SaveFolder.Get());
        if (IsSameOrAncestorDirectory(sourceFolder, root) || IsSameOrAncestorDirectory(root, sourceFolder))
        {
            Toast.Warning("不能从保存目录、保存目录的上级或子目录导入视频");
            return;
        }

        IsOperating = true;
        OperationProgressText = "正在导入视频...";
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            (int succeeded, int failed) = await Task.Run(() => ImportVideos(sourceFolder, root));
            if (succeeded > 0)
            {
                Toast.Success($"已导入 {succeeded} 个视频");
            }
            if (failed > 0)
            {
                Toast.Warning($"有 {failed} 个视频导入失败");
            }
            await RefreshAsync();
        }
        finally
        {
            IsOperating = false;
            OperationProgressText = string.Empty;
            OnPropertyChanged(nameof(IsIdle));
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
        ApplySelectionChange(() =>
        {
            foreach (RecordedVideoItem item in videos)
            {
                item.IsSelected = false;
            }
        });

        IsMultiSelectMode = false;
        lastSelectedItem = null;
    }

    [RelayCommand]
    private void SelectAll()
    {
        ApplySelectionChange(() =>
        {
            foreach (RecordedVideoItem item in GetVisibleVideos())
            {
                item.IsSelected = true;
            }
        });
    }

    [RelayCommand]
    private void InvertSelection()
    {
        ApplySelectionChange(() =>
        {
            foreach (RecordedVideoItem item in GetVisibleVideos())
            {
                item.IsSelected = !item.IsSelected;
            }
        });
    }

    internal void ToggleSelection(RecordedVideoItem item, bool selectRange)
    {
        ApplySelectionChange(() =>
        {
            if (selectRange && lastSelectedItem != null)
            {
                RecordedVideoItem[] visible = GetVisibleVideos();
                int start = Array.IndexOf(visible, lastSelectedItem);
                int end = Array.IndexOf(visible, item);
                if (start >= 0 && end >= 0)
                {
                    if (start > end)
                    {
                        (start, end) = (end, start);
                    }

                    for (int index = start; index <= end; index++)
                    {
                        visible[index].IsSelected = true;
                    }
                    return;
                }
            }

            item.IsSelected = !item.IsSelected;
            if (item.IsSelected)
            {
                lastSelectedItem = item;
            }
        });
    }

    internal void UndoSelection()
    {
        if (selectionUndoStack.Count == 0)
        {
            return;
        }

        SelectionSnapshot snapshot = selectionUndoStack.Pop();
        selectionRedoStack.Push(snapshot);
        RestoreSelection(snapshot.Before);
    }

    internal void RedoSelection()
    {
        if (selectionRedoStack.Count == 0)
        {
            return;
        }

        SelectionSnapshot snapshot = selectionRedoStack.Pop();
        selectionUndoStack.Push(snapshot);
        RestoreSelection(snapshot.After);
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
            Toast.Warning(GetResourceText("OpenVideoFailed", "Failed to open video"));
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

    internal static bool TryBuildRenameTarget(string sourcePath, string? requestedName, out string targetPath)
    {
        targetPath = string.Empty;
        string name = (requestedName ?? string.Empty).Trim();
        string extension = Path.GetExtension(sourcePath);
        if (!string.IsNullOrWhiteSpace(extension) && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^extension.Length].TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(name)
            || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        targetPath = Path.Combine(directory, name + extension);
        return true;
    }

    [RelayCommand]
    private async Task OpenDirectoryAsync(RecordedVideoItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.DirectoryPath) || !Directory.Exists(item.DirectoryPath))
        {
            Toast.Warning(GetResourceText("OpenFolderFailed", "Folder does not exist"));
            return;
        }

        await Launcher.LaunchFolderPathAsync(item.DirectoryPath);
    }

    [RelayCommand]
    private async Task RenameVideoAsync(RecordedVideoItem? item)
    {
        if (item == null || IsOperating || !File.Exists(item.FullPath))
        {
            return;
        }

        System.Windows.Controls.TextBox input = new()
        {
            MinWidth = 420,
            Text = Path.GetFileNameWithoutExtension(item.FileName),
        };
        input.SelectAll();
        ContentDialog dialog = new()
        {
            Title = "重命名视频",
            Content = input,
            CloseButtonText = "取消",
            PrimaryButtonText = "重命名",
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.TryFindResource("DefaultVioletaContentDialogStyle") as Style,
        };

        Window owner = Application.Current.MainWindow;
        using DialogBlurScope blurScope = DialogBlurScope.ForDialog(owner, dialog);
        ContentDialogResult result = await WindowSizing.ShowContentDialogAsync(dialog, owner);
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (!TryBuildRenameTarget(item.FullPath, input.Text, out string targetPath))
        {
            Toast.Warning("文件名不能为空，也不能包含路径或非法字符");
            return;
        }

        if (string.Equals(item.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(targetPath))
        {
            Toast.Warning("同名视频已经存在");
            return;
        }

        try
        {
            string sourcePath = item.FullPath;
            RenameVideoFile(sourcePath, targetPath);
            Toast.Success("重命名完成");
            await RefreshAsync();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Toast.Warning("重命名失败");
        }
    }

    [RelayCommand]
    private async Task TranscodeVideoAsync(RecordedVideoItem? item)
    {
        if (item == null || !item.CanTranscode || IsOperating)
        {
            return;
        }

        IsOperating = true;
        OperationProgressText = GetResourceText("TranscodingVideo", "Transcoding...");
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            bool converted = await new Converter().ExecuteAsync(item.FullPath, ".mp4");
            if (converted)
            {
                Toast.Success(GetResourceText("TranscodeComplete", "Transcoding complete"));
            }
            else
            {
                Toast.Warning(GetResourceText("TranscodeFailed", "Transcoding failed"));
            }
            await RefreshAsync();
        }
        finally
        {
            IsOperating = false;
            OperationProgressText = string.Empty;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    [RelayCommand]
    private void SplitVideo(RecordedVideoItem? item)
    {
        if (item == null || !File.Exists(item.FullPath))
        {
            Toast.Warning("视频文件不存在");
            return;
        }

        splitTargetItem = item;
        SplitTargetFileName = item.FileName;
        OperationProgressText = string.Empty;
        IsSplitPanelOpen = true;
    }

    [RelayCommand]
    private void SplitSelected()
    {
        RecordedVideoItem[] selected = GetSelectedVideos();
        if (selected.Length == 0)
        {
            Toast.Warning("请先选择要分割的视频");
            return;
        }

        splitTargetItem = null;
        SplitTargetFileName = $"已选择 {selected.Length} 个视频";
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
        IsMergePanelOpen = false;
        splitTargetItem = null;
        OperationProgressText = string.Empty;
        MergeProgressValue = 0;
        IsMergeProgressIndeterminate = false;
    }

    [RelayCommand]
    private async Task ConfirmSplitAsync()
    {
        if (IsOperating)
        {
            return;
        }

        RecordedVideoItem[] targets = splitTargetItem != null ? [splitTargetItem] : GetSelectedVideos();
        if (targets.Length == 0)
        {
            Toast.Warning("请先选择要分割的视频");
            return;
        }

        if (!TryGetSplitDurationSeconds(out int seconds))
        {
            Toast.Warning(GetResourceText("SplitDurationInvalid", "Invalid split interval"));
            return;
        }

        IsOperating = true;
        OperationProgressText = GetResourceText("SplittingVideo", "Splitting...");
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            int completed = 0;
            foreach (RecordedVideoItem target in targets)
            {
                OperationProgressText = $"正在分割 {completed + 1}/{targets.Length}";
                if (await SplitVideoFileAsync(target, seconds))
                {
                    completed++;
                }
            }

            if (completed > 0)
            {
                Toast.Success($"已分割 {completed} 个视频");
                IsSplitPanelOpen = false;
                await RefreshAsync();
            }
            else
            {
                Toast.Warning(GetResourceText("SplitFailed", "Split failed"));
            }
        }
        finally
        {
            IsOperating = false;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    [RelayCommand]
    private void OpenMergeSelected()
    {
        RecordedVideoItem[] selected = GetSelectedVideos();
        if (selected.Length < 2)
        {
            Toast.Warning(GetResourceText("SelectAtLeastTwoVideos", "Select at least two videos"));
            return;
        }

        MergeWarningText = BuildMergeWarningText(selected);
        MergeProgressValue = 0;
        IsMergeProgressIndeterminate = false;
        IsMergePanelOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmMergeSelectedAsync()
    {
        if (IsOperating)
        {
            return;
        }

        RecordedVideoItem[] selected = OrderVideosForMerge(GetSelectedVideos()).ToArray();

        if (selected.Length < 2)
        {
            Toast.Warning(GetResourceText("SelectAtLeastTwoVideos", "Select at least two videos"));
            return;
        }

        if (selected.Select(video => Path.GetExtension(video.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            Toast.Warning(GetResourceText("MergeFormatsMustMatch", "Only videos with the same format can be merged"));
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
        OperationProgressText = GetResourceText("MergingVideos", "Merging...");
        IsMergeProgressIndeterminate = true;
        MergeProgressValue = 0;
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            bool result = await MergeVideosAsync(selected, dialog.FileName, progress =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsMergeProgressIndeterminate = false;
                    MergeProgressValue = progress;
                });
            });
            if (result)
            {
                MergeProgressValue = 100;
                IsMergePanelOpen = false;
                Toast.Success(GetResourceText("MergeComplete", "Merge complete"));
                await RefreshAsync();
            }
            else
            {
                Toast.Warning(GetResourceText("MergeFailed", "Merge failed"));
            }
        }
        finally
        {
            IsOperating = false;
            IsMergeProgressIndeterminate = false;
            OperationProgressText = string.Empty;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        await DeleteVideosAsync(GetSelectedVideos());
    }

    [RelayCommand]
    private async Task DeleteVideoAsync(RecordedVideoItem? item)
    {
        if (item != null)
        {
            await DeleteVideosAsync([item]);
        }
    }

    private async Task DeleteVideosAsync(IReadOnlyCollection<RecordedVideoItem> items)
    {
        if (IsOperating || items.Count == 0)
        {
            return;
        }

        System.Windows.MessageBoxResult result;
        using (DialogBlurScope blurScope = new())
        {
            result = await MessageBox.QuestionAsync(FormatResourceText("ConfirmDeleteVideos", "Delete {0} video files?", items.Count));
        }
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        int deleted = 0;
        int failed = 0;
        foreach (RecordedVideoItem item in items)
        {
            try
            {
                if (File.Exists(item.FullPath))
                {
                    FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(item.FullPath, sendToRecycleBin: true);
                    DeleteThumbnailCache(item.FullPath);
                    deleted++;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                failed++;
            }
        }

        if (deleted > 0)
        {
            Toast.Success($"已删除 {deleted} 个视频");
        }
        if (failed > 0)
        {
            Toast.Warning($"有 {failed} 个视频删除失败");
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        await CopyOrMoveVideosAsync(GetSelectedVideos(), move: true);
    }

    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        await CopyOrMoveVideosAsync(GetSelectedVideos(), move: false);
    }

    [RelayCommand]
    private async Task SaveAsVideoAsync(RecordedVideoItem? item)
    {
        if (item != null)
        {
            await CopyOrMoveVideosAsync([item], move: false);
        }
    }

    private async Task CopyOrMoveVideosAsync(IReadOnlyCollection<RecordedVideoItem> items, bool move)
    {
        if (IsOperating || items.Count == 0)
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

        IsOperating = true;
        OperationProgressText = move
            ? GetResourceText("MovingVideos", "Moving...")
            : GetResourceText("CopyingVideos", "Copying...");
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            int succeeded = 0;
            int failed = 0;
            foreach (RecordedVideoItem item in items)
            {
                try
                {
                    string targetPath = GetUniquePath(Path.Combine(dialog.FileName, item.FileName));
                    if (string.Equals(item.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        failed++;
                        continue;
                    }

                    await Task.Run(() =>
                    {
                        TransferVideoFile(item.FullPath, targetPath, move);
                        if (move)
                        {
                            DeleteThumbnailCache(item.FullPath);
                        }
                    });
                    succeeded++;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    failed++;
                }
            }

            if (succeeded > 0)
            {
                Toast.Success(move ? $"已移动 {succeeded} 个视频" : $"已另存 {succeeded} 个视频");
            }
            if (failed > 0)
            {
                Toast.Warning($"有 {failed} 个视频未能处理，请检查目标目录或同名文件");
            }
            await RefreshAsync();
        }
        finally
        {
            IsOperating = false;
            OperationProgressText = string.Empty;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    private async Task LoadVideosFromFolderAsync(string folder)
    {
        videoLoadCancellationTokenSource?.Cancel();
        videoLoadCancellationTokenSource?.Dispose();
        videoLoadCancellationTokenSource = new CancellationTokenSource();
        CancellationToken loadToken = videoLoadCancellationTokenSource.Token;

        videoEnrichmentCancellationTokenSource?.Cancel();
        videoEnrichmentCancellationTokenSource?.Dispose();
        videoEnrichmentCancellationTokenSource = new CancellationTokenSource();
        lock (videoEnrichmentLock)
        {
            queuedVideoEnrichmentPaths.Clear();
        }

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        RecordedVideoItem[] items;
        try
        {
            items = await Task.Run(() =>
            {
                return Directory.EnumerateFiles(folder, "*.*", System.IO.SearchOption.AllDirectories)
                    .Where(path => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .Select(path =>
                    {
                        loadToken.ThrowIfCancellationRequested();
                        return CreateRecordedVideoItem(path, folder);
                    })
                    .ToArray();
            }, loadToken);
        }
        catch (OperationCanceledException) when (loadToken.IsCancellationRequested)
        {
            return;
        }

        if (loadToken.IsCancellationRequested)
        {
            return;
        }

        HashSet<string> selectedPaths = videos
            .Where(video => video.IsSelected)
            .Select(video => video.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (RecordedVideoItem item in videos)
        {
            item.PropertyChanged -= VideoItemPropertyChanged;
        }

        videos.Clear();
        foreach (RecordedVideoItem item in items)
        {
            item.IsSelected = selectedPaths.Contains(item.FullPath);
            item.PropertyChanged += VideoItemPropertyChanged;
            videos.Add(item);
        }

        UpdateStreamerOptions();
        ApplySort();
        ApplyFilters();
        RefreshSelectionSummary();
        lastSelectedItem = GetVisibleVideos().LastOrDefault(item => item.IsSelected);
        VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static RecordedVideoItem CreateRecordedVideoItem(string path, string rootFolder)
    {
        FileInfo fileInfo = new(path);
        VideoRecordingMetadata metadata = LoadMetadata(fileInfo);
        string resolution = NormalizeResolution(metadata.Resolution);
        string bitrate = NormalizeBitrate(metadata.Bitrate);

        string nickName = NormalizeStreamerName(string.IsNullOrWhiteSpace(metadata.NickName) ? InferNickName(path, rootFolder) : metadata.NickName);
        DateTime createdAt = metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt : fileInfo.LastWriteTime;
        string thumbnailPath = GetExistingThumbnailPath(fileInfo.FullName, metadata.CoverPath);

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
            Title = BuildDisplayTitle(metadata.Title, createdAt, fileInfo),
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

        if (text.Length > 1
            && (text[^1] is 'p' or 'P' or 'i' or 'I')
            && int.TryParse(text[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int verticalResolution)
            && verticalResolution > 0)
        {
            return $"{verticalResolution}{char.ToLowerInvariant(text[^1])}";
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
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                process.WaitForExit();
                Task.WaitAll([outputTask, errorTask]);
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

    internal void QueueVisibleVideoEnrichment(IEnumerable<RecordedVideoItem> items)
    {
        CancellationTokenSource? source = videoEnrichmentCancellationTokenSource;
        if (source == null)
        {
            return;
        }

        foreach (RecordedVideoItem item in items)
        {
            QueueVideoEnrichment(item, source.Token);
        }
    }

    internal void CancelBackgroundLoading()
    {
        videoLoadCancellationTokenSource?.Cancel();
        videoEnrichmentCancellationTokenSource?.Cancel();
    }

    private void QueueVideoEnrichment(RecordedVideoItem item, CancellationToken token)
    {
        if (item.IsEnriched || token.IsCancellationRequested)
        {
            return;
        }

        lock (videoEnrichmentLock)
        {
            if (!queuedVideoEnrichmentPaths.Add(item.FullPath))
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await videoEnrichmentSemaphore.WaitAsync(token);
                try
                {
                    await EnrichVideoAsync(item, token);
                }
                finally
                {
                    videoEnrichmentSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
            finally
            {
                lock (videoEnrichmentLock)
                {
                    queuedVideoEnrichmentPaths.Remove(item.FullPath);
                }
            }
        }, CancellationToken.None);
    }

    private static async Task EnrichVideoAsync(RecordedVideoItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(item.FullPath))
        {
            return;
        }

        FileInfo file = new(item.FullPath);
        VideoRecordingMetadata metadata = LoadMetadata(file);
        VideoProbeInfo probeInfo = ProbeVideoFileInfo(item.FullPath, file.Length);
        metadata = VideoRecordingMetadataStore.Merge(metadata, probeInfo.Metadata);
        string coverPath = string.IsNullOrWhiteSpace(metadata.CoverPath) ? item.CoverPath : metadata.CoverPath;
        string thumbnailPath = File.Exists(coverPath) ? coverPath : await ExtractThumbnailAsync(item.FullPath, token);
        DateTime createdAt = metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt : file.LastWriteTime;
        string resolution = string.IsNullOrWhiteSpace(probeInfo.Resolution) ? NormalizeResolution(metadata.Resolution) : probeInfo.Resolution;
        string bitrate = string.IsNullOrWhiteSpace(probeInfo.Bitrate) ? NormalizeBitrate(metadata.Bitrate) : probeInfo.Bitrate;

        token.ThrowIfCancellationRequested();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!token.IsCancellationRequested)
            {
                item.NickName = NormalizeStreamerName(string.IsNullOrWhiteSpace(metadata.NickName) ? item.NickName : metadata.NickName);
                item.Resolution = resolution;
                item.Bitrate = bitrate;
                item.CoverPath = coverPath;
                if (!string.IsNullOrWhiteSpace(thumbnailPath))
                {
                    item.ThumbnailPath = thumbnailPath;
                }
                item.CreatedAt = createdAt;
                item.Title = BuildDisplayTitle(metadata.Title, createdAt, file);
                item.IsEnriched = true;
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

        if (IsThumbnailCacheCurrent(filePath, imagePath))
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

                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(outputTask, errorTask);
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
        return Path.Combine(AppPaths.ConfigDirectory, "video_thumbnails");
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
        return IsThumbnailCacheCurrent(filePath, cachePath) ? cachePath : string.Empty;
    }

    internal static string GetResourceText(string key, string fallback)
    {
        return Resources.ResourceManager.GetString(key, Resources.Culture ?? CultureInfo.CurrentUICulture) ?? fallback;
    }

    internal static string FormatResourceText(string key, string fallback, params object[] values)
    {
        return string.Format(CultureInfo.CurrentCulture, GetResourceText(key, fallback), values);
    }

    private static bool IsThumbnailCacheCurrent(string filePath, string cachePath)
    {
        if (!File.Exists(filePath) || !File.Exists(cachePath))
        {
            return false;
        }

        FileInfo video = new(filePath);
        FileInfo thumbnail = new(cachePath);
        return thumbnail.Length > 0 && thumbnail.LastWriteTimeUtc >= video.LastWriteTimeUtc;
    }

    internal static void CopyAssociatedMetadata(string sourceFilePath, string targetFilePath)
    {
        FileInfo source = new(sourceFilePath);
        string[] sourceCandidates = VideoRecordingMetadataStore.GetMetadataCandidates(source).ToArray();
        int sourceIndex = Array.FindIndex(sourceCandidates, File.Exists);
        if (sourceIndex < 0)
        {
            return;
        }

        string[] targetCandidates = VideoRecordingMetadataStore.GetMetadataCandidates(new FileInfo(targetFilePath)).ToArray();
        if (targetCandidates.Length == 0)
        {
            return;
        }

        string targetMetadataPath = targetCandidates[Math.Min(sourceIndex, targetCandidates.Length - 1)];
        Directory.CreateDirectory(Path.GetDirectoryName(targetMetadataPath) ?? Environment.CurrentDirectory);
        if (!File.Exists(targetMetadataPath))
        {
            File.Copy(sourceCandidates[sourceIndex], targetMetadataPath);
            return;
        }

        if (File.ReadAllBytes(sourceCandidates[sourceIndex]).SequenceEqual(File.ReadAllBytes(targetMetadataPath)))
        {
            return;
        }

        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(source);
        string targetDirectory = Path.GetDirectoryName(targetFilePath) ?? Environment.CurrentDirectory;
        _ = VideoRecordingMetadataStore.WriteSidecar(
            targetDirectory,
            Path.GetFileNameWithoutExtension(targetFilePath),
            VideoRecordingMetadataStore.WithFileName(metadata, Path.GetFileName(targetFilePath)));
    }

    internal static void TransferVideoFile(string sourceFilePath, string targetFilePath, bool move)
    {
        if (!move)
        {
            File.Copy(sourceFilePath, targetFilePath);
            try
            {
                CopyAssociatedMetadata(sourceFilePath, targetFilePath);
                return;
            }
            catch
            {
                DeleteFileIfExists(targetFilePath);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFilePath);
                throw;
            }
        }

        File.Move(sourceFilePath, targetFilePath);
        try
        {
            CopyAssociatedMetadata(sourceFilePath, targetFilePath);
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourceFilePath);
        }
        catch
        {
            try
            {
                if (File.Exists(targetFilePath) && !File.Exists(sourceFilePath))
                {
                    File.Move(targetFilePath, sourceFilePath);
                }
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFilePath);
            }
            catch (Exception rollbackException)
            {
                AppSessionLogger.WriteException(rollbackException);
            }
            throw;
        }
    }

    internal static void RenameVideoFile(string sourceFilePath, string targetFilePath)
    {
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(sourceFilePath));
        File.Move(sourceFilePath, targetFilePath);
        try
        {
            if (VideoRecordingMetadataStore.HasAnyMetadata(metadata))
            {
                string directory = Path.GetDirectoryName(targetFilePath) ?? Environment.CurrentDirectory;
                string? metadataPath = VideoRecordingMetadataStore.WriteSidecar(
                    directory,
                    Path.GetFileNameWithoutExtension(targetFilePath),
                    VideoRecordingMetadataStore.WithFileName(metadata, Path.GetFileName(targetFilePath)));
                if (metadataPath == null)
                {
                    throw new IOException("Failed to write recording metadata after renaming the video.");
                }
            }

            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourceFilePath);
            DeleteThumbnailCache(sourceFilePath);
        }
        catch
        {
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFilePath);
            if (File.Exists(targetFilePath) && !File.Exists(sourceFilePath))
            {
                File.Move(targetFilePath, sourceFilePath);
            }
            throw;
        }
    }

    private static void DeleteThumbnailCache(string filePath)
    {
        string cachePath = GetThumbnailCachePath(filePath);
        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }
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

    private static (int Succeeded, int Failed) ImportVideos(string sourceFolder, string root)
    {
        Directory.CreateDirectory(root);
        int succeeded = 0;
        int failed = 0;
        foreach (string path in Directory.EnumerateFiles(sourceFolder, "*.*", System.IO.SearchOption.AllDirectories).Where(IsVideoFile))
        {
            try
            {
                FileInfo source = new(path);
                VideoRecordingMetadata metadata = LoadMetadata(source);
                string targetFolder = BuildClassifiedFolder(root, metadata, source, sourceFolder);
                Directory.CreateDirectory(targetFolder);
                string targetPath = GetUniquePath(Path.Combine(targetFolder, source.Name));
                TransferVideoFile(source.FullName, targetPath, move: false);
                succeeded++;
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
                failed++;
            }
        }

        return (succeeded, failed);
    }

    internal static string BuildClassifiedFolder(string root, VideoRecordingMetadata metadata, FileInfo file, string sourceRoot, int? pathLevel = null)
    {
        string nickName = string.IsNullOrWhiteSpace(metadata.NickName) ? InferNickName(file.FullName, sourceRoot) : metadata.NickName;
        string author = SanitizeFolderName(nickName);
        DateTime recordedAt = metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt : file.LastWriteTime;
        string month = recordedAt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        string day = recordedAt.ToString("dd", CultureInfo.InvariantCulture);
        return Math.Clamp(pathLevel ?? Configurations.SaveFolderPathLevel.Get(), 0, 3) switch
        {
            1 => Path.Combine(root, author),
            2 => Path.Combine(root, author, month),
            3 => Path.Combine(root, author, month, day),
            _ => root,
        };
    }

    private static string SanitizeFolderName(string? value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? UnknownStreamerText : value.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        name = name.TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? UnknownStreamerText : name;
    }

    internal static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int index = 1; index < 10000; index++)
        {
            string candidate = Path.Combine(directory, $"{name}_{index:000}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
    }

    private static bool IsSameOrAncestorDirectory(string parent, string child)
    {
        string normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedChild.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)
            || normalizedChild.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedChild.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideoFile(string path)
    {
        return VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildDisplayTitle(string? title, DateTime createdAt, FileInfo file)
    {
        string details = $"{createdAt:yyyy-MM-dd HH:mm:ss}  |  {FormatFileSize(file.Length)}";
        return string.IsNullOrWhiteSpace(title) ? $"{details}  |  {file.DirectoryName}" : $"{title}  |  {details}";
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
        VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyFilters()
    {
        Videos.Refresh();
        RefreshSelectionSummary();
        VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
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
        if (e.PropertyName == nameof(RecordedVideoItem.IsSelected) && !isRestoringSelection)
        {
            RefreshSelectionSummary();
        }
        else if (e.PropertyName == nameof(RecordedVideoItem.NickName))
        {
            UpdateStreamerOptions();
        }
    }

    private void RefreshSelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedVideoCount));
        OnPropertyChanged(nameof(SelectedVideoSummary));
        OnPropertyChanged(nameof(HasSelectedVideos));
        OnPropertyChanged(nameof(CanMergeSelectedVideos));
    }

    private RecordedVideoItem[] GetSelectedVideos()
    {
        return GetVisibleVideos().Where(video => video.IsSelected && File.Exists(video.FullPath)).ToArray();
    }

    private RecordedVideoItem[] GetVisibleVideos()
    {
        return Videos.Cast<RecordedVideoItem>().ToArray();
    }

    private void ApplySelectionChange(Action change)
    {
        HashSet<string> before = CaptureSelectedPaths();
        change();
        HashSet<string> after = CaptureSelectedPaths();
        if (!before.SetEquals(after))
        {
            selectionUndoStack.Push(new SelectionSnapshot(before, after));
            while (selectionUndoStack.Count > SelectionHistoryLimit)
            {
                SelectionSnapshot[] snapshots = selectionUndoStack.Reverse().Skip(1).ToArray();
                selectionUndoStack.Clear();
                foreach (SelectionSnapshot snapshot in snapshots)
                {
                    selectionUndoStack.Push(snapshot);
                }
            }
            selectionRedoStack.Clear();
        }

        RefreshSelectionSummary();
    }

    private HashSet<string> CaptureSelectedPaths()
    {
        return videos.Where(video => video.IsSelected)
            .Select(video => video.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RestoreSelection(ISet<string> selectedPaths)
    {
        isRestoringSelection = true;
        try
        {
            foreach (RecordedVideoItem item in videos)
            {
                item.IsSelected = selectedPaths.Contains(item.FullPath);
            }
        }
        finally
        {
            isRestoringSelection = false;
        }

        lastSelectedItem = GetVisibleVideos().LastOrDefault(item => item.IsSelected);
        RefreshSelectionSummary();
    }

    private bool TryGetSplitDurationSeconds(out int seconds)
    {
        return TryConvertSplitDurationSeconds(SplitDurationValue, SplitDurationUnitIndex, out seconds);
    }

    internal static bool TryConvertSplitDurationSeconds(string? valueText, int unitIndex, out int seconds)
    {
        seconds = 0;
        if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            || !double.IsFinite(value)
            || value <= 0)
        {
            return false;
        }

        double multiplier = unitIndex switch
        {
            1 => 1d,
            2 => 3600d,
            0 or _ => 60d,
        };
        double totalSeconds = Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(totalSeconds) || totalSeconds < 1d || totalSeconds > int.MaxValue)
        {
            return false;
        }

        seconds = (int)totalSeconds;
        return true;
    }

    internal static string BuildMergeWarningText(IReadOnlyList<RecordedVideoItem> selected)
    {
        List<string> reasons = [];
        if (selected.Count < 2)
        {
            reasons.Add("至少需要选择两个视频");
        }
        if (selected.Select(video => video.NickName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            reasons.Add("包含不同主播的视频");
        }
        if (selected.Select(video => Path.GetExtension(video.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            reasons.Add("视频格式不一致");
        }
        if (selected.Select(video => video.Resolution).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            reasons.Add("视频分辨率不一致");
        }
        if (!IsContinuousSegmentSelection(selected))
        {
            reasons.Add("不是同一组连续分段，将按照录制时间合并");
        }

        return reasons.Count == 0 ? "检查通过，将按分段顺序合并。" : string.Join("；", reasons.Distinct());
    }

    internal static IEnumerable<RecordedVideoItem> OrderVideosForMerge(IEnumerable<RecordedVideoItem> source)
    {
        RecordedVideoItem[] items = source.ToArray();
        if (items.Length > 0 && items.All(item => TryGetSegmentIdentity(item.FullPath, out _, out _)))
        {
            string[] bases = items.Select(item =>
            {
                _ = TryGetSegmentIdentity(item.FullPath, out string baseStem, out _);
                return baseStem;
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (bases.Length == 1)
            {
                return items.OrderBy(item =>
                {
                    _ = TryGetSegmentIdentity(item.FullPath, out _, out int index);
                    return index;
                });
            }
        }

        return items.OrderBy(item => item.CreatedAt).ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsContinuousSegmentSelection(IReadOnlyList<RecordedVideoItem> selected)
    {
        if (selected.Count < 2)
        {
            return false;
        }

        List<(string BaseStem, int Index)> segments = [];
        foreach (RecordedVideoItem item in selected)
        {
            if (!TryGetSegmentIdentity(item.FullPath, out string baseStem, out int index))
            {
                return false;
            }
            segments.Add((baseStem, index));
        }

        if (segments.Select(segment => segment.BaseStem).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            return false;
        }

        int[] indexes = segments.Select(segment => segment.Index).Order().ToArray();
        return indexes.Skip(1).Select((index, offset) => index == indexes[offset] + 1).All(value => value);
    }

    private static bool TryGetSegmentIdentity(string path, out string baseStem, out int index)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        int separator = stem.LastIndexOf('_');
        if (separator > 0 && separator < stem.Length - 1
            && int.TryParse(stem[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            baseStem = stem[..separator];
            return true;
        }

        baseStem = string.Empty;
        index = 0;
        return false;
    }

    private static async Task<bool> SplitVideoFileAsync(RecordedVideoItem item, int seconds)
    {
        if (!File.Exists(item.FullPath))
        {
            return false;
        }

        FileInfo source = new(item.FullPath);
        string directory = source.DirectoryName ?? Environment.CurrentDirectory;
        string outputBase = GetUniqueSegmentBase(directory, $"{Path.GetFileNameWithoutExtension(source.Name)}_part");
        string outputPattern = Path.Combine(directory, $"{outputBase}_%03d{source.Extension}");
        string[] arguments = ["-i", source.FullName, "-c", "copy", "-map", "0", "-f", "segment", "-segment_time", seconds.ToString(CultureInfo.InvariantCulture), "-reset_timestamps", "1", outputPattern];
        (bool succeeded, _) = await ExecuteFfmpegAsync(arguments);
        string[] outputs = Directory.EnumerateFiles(directory, $"{outputBase}_*{source.Extension}", System.IO.SearchOption.TopDirectoryOnly).ToArray();
        if (!succeeded || outputs.Length == 0 || outputs.Any(path => new FileInfo(path).Length == 0))
        {
            foreach (string output in outputs)
            {
                DeleteFileIfExists(output);
            }
            return false;
        }

        foreach (string output in outputs)
        {
            CopyAssociatedMetadata(source.FullName, output);
        }
        return true;
    }

    private static string GetUniqueSegmentBase(string directory, string stem)
    {
        for (int index = 0; index < 10000; index++)
        {
            string candidate = index == 0 ? stem : $"{stem}_{index:000}";
            if (!Directory.EnumerateFiles(directory, $"{candidate}_*.*", System.IO.SearchOption.TopDirectoryOnly).Any())
            {
                return candidate;
            }
        }

        return $"{stem}_{Guid.NewGuid():N}";
    }

    private static async Task<bool> MergeVideosAsync(IReadOnlyList<RecordedVideoItem> selected, string targetFolder, Action<double> progress)
    {
        RecordedVideoItem[] ordered = OrderVideosForMerge(selected).ToArray();
        FileInfo first = new(ordered[0].FullPath);
        string baseStem = TryGetSegmentIdentity(first.FullName, out string segmentBaseStem, out _) ? segmentBaseStem : Path.GetFileNameWithoutExtension(first.Name);
        string target = GetUniquePath(Path.Combine(targetFolder, $"{baseStem}_merged{first.Extension}"));
        string listPath = Path.Combine(Path.GetTempPath(), $"emerde_concat_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllLinesAsync(listPath, ordered.Select(item => $"file '{EscapeConcatPath(item.FullPath)}'"), new UTF8Encoding(false));
            double totalSeconds = await Task.Run(() => ordered.Sum(item => GetVideoDurationSeconds(item.FullPath)));
            string[] arguments = ["-f", "concat", "-safe", "0", "-i", listPath, "-c", "copy", "-progress", "pipe:1", "-nostats", target];
            (bool succeeded, _) = await ExecuteFfmpegAsync(arguments, line =>
            {
                if (totalSeconds > 0 && line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(line["out_time_ms=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double microseconds))
                {
                    progress(Math.Min(99, microseconds / 1_000_000d / totalSeconds * 100d));
                }
                return Task.CompletedTask;
            });
            if (!succeeded || !File.Exists(target) || new FileInfo(target).Length == 0)
            {
                DeleteFileIfExists(target);
                return false;
            }

            CopyAssociatedMetadata(first.FullName, target);
            return true;
        }
        finally
        {
            DeleteFileIfExists(listPath);
        }
    }

    private static double GetVideoDurationSeconds(string filePath)
    {
        string? ffprobePath = SearchFileHelper.SearchExecutable("ffprobe.exe");
        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            return 0;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = ffprobePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", filePath })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            _ = ChildProcessTracerPeriodicTimer.Default.TryTraceProcess(process);
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(4000))
            {
                process.Kill(entireProcessTree: true);
                return 0;
            }
            return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double duration) ? duration : 0;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            return 0;
        }
    }

    private static async Task<(bool Succeeded, string Error)> ExecuteFfmpegAsync(IEnumerable<string> arguments, Func<string, Task>? outputLine = null)
    {
        string? ffmpegPath = SearchFileHelper.SearchExecutable("ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return (false, "ffmpeg executable was not found");
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            _ = ChildProcessTracerPeriodicTimer.Default.TryTraceProcess(process);
            Task outputTask = ReadFfmpegOutputAsync(process.StandardOutput, outputLine);
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await outputTask;
            string error = await errorTask;
            if (process.ExitCode != 0)
            {
                AppSessionLogger.Event("error", "video_list", "ffmpeg_operation_failed", error, new { process.ExitCode });
            }
            return (process.ExitCode == 0, error);
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            AppSessionLogger.Event("error", "video_list", "ffmpeg_operation_exception", e.Message);
            return (false, e.Message);
        }
    }

    private static async Task ReadFfmpegOutputAsync(StreamReader reader, Func<string, Task>? outputLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (outputLine != null)
            {
                await outputLine(line);
            }
        }
    }

    private static string EscapeConcatPath(string path)
    {
        return path.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }
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

internal sealed record SelectionSnapshot(HashSet<string> Before, HashSet<string> After);

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

    [ObservableProperty]
    private bool isEnriched;

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);

    public string StreamerChipText => ScreenRecordListViewModel.FormatResourceText(
        "StreamerChip",
        "Streamer {0}",
        string.IsNullOrWhiteSpace(NickName) ? ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown") : NickName);

    public string ResolutionChipText => ScreenRecordListViewModel.FormatResourceText(
        "ResolutionChip",
        "Resolution {0}",
        string.IsNullOrWhiteSpace(Resolution) ? ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown") : Resolution);

    public string BitrateChipText => ScreenRecordListViewModel.FormatResourceText(
        "BitrateChip",
        "Bitrate {0}",
        string.IsNullOrWhiteSpace(Bitrate) ? ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown") : Bitrate);
}
