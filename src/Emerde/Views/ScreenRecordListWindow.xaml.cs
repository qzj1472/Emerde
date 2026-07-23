using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.Properties;
using Emerde.Threading;
using Microsoft.VisualBasic.FileIO;
using System.Collections.Concurrent;
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
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Emerde.Views;

public partial class ScreenRecordListWindow : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer visibleVideoLoadTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private Task? visibleVideoRefreshTask;
    private bool videoMarqueeCandidate;
    private bool isVideoMarqueeSelecting;
    private Point videoMarqueeStart;
    private RecordedVideoItem? videoMarqueePressedItem;
    private int videoMarqueeClickCount;

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
        IsVisibleChanged += ScreenRecordListWindowIsVisibleChanged;
        SizeChanged += (_, _) => ScheduleVisibleVideoLoading();
        PreviewKeyDown += ScreenRecordListWindowPreviewKeyDown;
    }

    private async void ScreenRecordListWindowLoaded(object sender, RoutedEventArgs e)
    {
        DialogBlurScope.ApplyBackdropBrush(VideoListModalOverlay);
        ViewModel.StartMonitoring();
        if (IsVisible)
        {
            FocusVideoList();
            await RefreshVisibleVideoListAsync();
        }
    }

    private void ScreenRecordListWindowUnloaded(object sender, RoutedEventArgs e)
    {
        visibleVideoLoadTimer.Stop();
        ViewModel.StopMonitoring();
        ViewModel.CancelBackgroundLoading();
    }

    private async void ScreenRecordListWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded && e.NewValue is true)
        {
            FocusVideoList();
            await RefreshVisibleVideoListAsync();
        }
    }

    private async Task RefreshVisibleVideoListAsync()
    {
        Task refreshTask = visibleVideoRefreshTask ??= RefreshVisibleVideoListCoreAsync();
        try
        {
            await refreshTask;
        }
        finally
        {
            if (ReferenceEquals(visibleVideoRefreshTask, refreshTask))
            {
                visibleVideoRefreshTask = null;
            }
        }
    }

    private async Task RefreshVisibleVideoListCoreAsync()
    {
        await ViewModel.RefreshForDisplayAsync();
        ScheduleVisibleVideoLoading();
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

    private void VideoListBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindVisualChild<ScrollViewer>(VideoListBox) is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (SystemParameters.WheelScrollLines < 0)
        {
            if (e.Delta > 0)
            {
                scrollViewer.PageUp();
            }
            else
            {
                scrollViewer.PageDown();
            }
        }
        else
        {
            int lines = Math.Max(1, SystemParameters.WheelScrollLines);
            for (int index = 0; index < lines; index++)
            {
                if (e.Delta > 0)
                {
                    scrollViewer.LineUp();
                }
                else
                {
                    scrollViewer.LineDown();
                }
            }
        }

        e.Handled = true;
    }

    private void VideoSelectionHostPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ListBoxItem? item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (e.ChangedButton != MouseButton.Left
            || ViewModel.IsModalOpen
            || FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(e.OriginalSource as DependencyObject) != null
            || item != null && IsInteractiveElement(e.OriginalSource as DependencyObject, item))
        {
            return;
        }

        videoMarqueeCandidate = true;
        videoMarqueeStart = e.GetPosition(VideoListBox);
        videoMarqueePressedItem = item?.DataContext as RecordedVideoItem;
        videoMarqueeClickCount = e.ClickCount;
        if (item != null)
        {
            item.Focus();
            e.Handled = true;
        }
    }

    private void VideoSelectionHostPreviewMouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(VideoListBox);
        if (isVideoMarqueeSelecting)
        {
            UpdateVideoMarquee(position);
            e.Handled = true;
            return;
        }

        if (!videoMarqueeCandidate)
        {
            return;
        }

        if (!CanStartVideoMarquee(videoMarqueePressedItem))
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            videoMarqueeCandidate = false;
            videoMarqueePressedItem = null;
            videoMarqueeClickCount = 0;
            return;
        }

        bool moved = Math.Abs(position.X - videoMarqueeStart.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(position.Y - videoMarqueeStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!moved)
        {
            return;
        }

        videoMarqueeCandidate = false;
        isVideoMarqueeSelecting = true;
        VideoSelectionRectangle.Visibility = Visibility.Visible;
        VideoListBox.CaptureMouse();
        UpdateVideoMarquee(position);
        e.Handled = true;
    }

    private void VideoSelectionHostPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isVideoMarqueeSelecting)
        {
            if (videoMarqueeCandidate)
            {
                if (videoMarqueePressedItem != null)
                {
                    bool toggleSelection = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    bool selectRange = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                    bool wasMultiSelectMode = ViewModel.IsMultiSelectMode;
                    if (wasMultiSelectMode)
                    {
                        ViewModel.SelectMultipleItem(videoMarqueePressedItem, !selectRange, selectRange);
                    }
                    else if (toggleSelection || selectRange)
                    {
                        ViewModel.SelectMultipleItem(videoMarqueePressedItem, toggleSelection, selectRange);
                    }
                    else
                    {
                        ViewModel.SelectRegularItem(videoMarqueePressedItem);
                    }
                    if (ShouldOpenVideoFromClick(wasMultiSelectMode, toggleSelection, selectRange, videoMarqueeClickCount))
                    {
                        ViewModel.OpenVideoCommand.Execute(videoMarqueePressedItem);
                    }
                }
                else if (!ViewModel.IsMultiSelectMode)
                {
                    ViewModel.ClearRegularSelection();
                }
                e.Handled = true;
            }
            videoMarqueeCandidate = false;
            videoMarqueePressedItem = null;
            videoMarqueeClickCount = 0;
            return;
        }

        FinishVideoMarquee(true);
        e.Handled = true;
    }

    private void VideoSelectionHostLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (isVideoMarqueeSelecting)
        {
            FinishVideoMarquee(false);
            return;
        }

        videoMarqueeCandidate = false;
        videoMarqueePressedItem = null;
        videoMarqueeClickCount = 0;
    }

    private void UpdateVideoMarquee(Point position)
    {
        Rect selection = CreateSelectionRect(videoMarqueeStart, position);
        Canvas.SetLeft(VideoSelectionRectangle, selection.Left);
        Canvas.SetTop(VideoSelectionRectangle, selection.Top);
        VideoSelectionRectangle.Width = selection.Width;
        VideoSelectionRectangle.Height = selection.Height;
    }

    private void FinishVideoMarquee(bool commit)
    {
        Rect selection = CreateSelectionRect(videoMarqueeStart, Mouse.GetPosition(VideoListBox));
        isVideoMarqueeSelecting = false;
        videoMarqueeCandidate = false;
        videoMarqueePressedItem = null;
        videoMarqueeClickCount = 0;
        VideoSelectionRectangle.Visibility = Visibility.Collapsed;
        if (VideoListBox.IsMouseCaptured)
        {
            VideoListBox.ReleaseMouseCapture();
        }

        if (!commit || selection.Width < 1d || selection.Height < 1d)
        {
            return;
        }

        List<RecordedVideoItem> selectedItems = [];
        for (int index = 0; index < VideoListBox.Items.Count; index++)
        {
            if (VideoListBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item
                && selection.IntersectsWith(item.TransformToVisual(VideoListBox).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight)))
                && item.DataContext is RecordedVideoItem video)
            {
                selectedItems.Add(video);
            }
        }
        ViewModel.SelectItems(selectedItems);
    }

    private void VideoSelectionHostPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is not ListBoxItem item
            || item.DataContext is not RecordedVideoItem video)
        {
            return;
        }

        if (ViewModel.IsMultiSelectMode)
        {
            if (!video.IsSelected)
            {
                ViewModel.SelectMultipleItem(video, false, false);
            }
        }
        else
        {
            ViewModel.SelectRegularItem(video);
        }
        item.Focus();
        if (FindVisualChildByName(item, "VideoCardShell") is FrameworkElement { ContextMenu: not null } card)
        {
            card.ContextMenu.PlacementTarget = card;
            card.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    internal static bool ShouldOpenVideoFromClick(bool isMultiSelectMode, bool toggleSelection, bool selectRange, int clickCount)
    {
        return !isMultiSelectMode && !toggleSelection && !selectRange && clickCount >= 2;
    }

    internal static bool CanStartVideoMarquee(RecordedVideoItem? pressedItem)
    {
        return pressedItem == null;
    }

    private static Rect CreateSelectionRect(Point start, Point end)
    {
        return new Rect(
            new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));
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
        ViewModel.SelectMultipleItem(item, true, (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
    }

    private void ScreenRecordListWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsMultiSelectMode)
        {
            ViewModel.CancelMultiSelectCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ViewModel.IsModalOpen
            || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox)
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (IsVideoListKeyboardNavigationKey(e.Key) && modifiers == ModifierKeys.None && VideoListBox.IsKeyboardFocusWithin)
        {
            RecordedVideoItem? item = ViewModel.GetAdjacentVisibleVideo(e.Key == Key.Down ? 1 : -1);
            if (item != null)
            {
                ViewModel.SelectRegularItem(item);
                BringVideoListItemIntoView(item);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            ViewModel.DeleteContextCommand.Execute(ViewModel.RegularSelectedItem);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ViewModel.CopyContextCommand.Execute(ViewModel.RegularSelectedItem);
            }
            else
            {
                ViewModel.MoveContextCommand.Execute(ViewModel.RegularSelectedItem);
            }
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.A)
        {
            ViewModel.SelectAllCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Z)
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

    internal static bool IsVideoListKeyboardNavigationKey(Key key)
    {
        return key is Key.Up or Key.Down;
    }

    private void BringVideoListItemIntoView(RecordedVideoItem item)
    {
        if (VideoListBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            Rect bounds = container.TransformToAncestor(VideoListBox)
                .TransformBounds(new Rect(new Point(0, 0), container.RenderSize));
            if (bounds.Bottom >= 0 && bounds.Top <= VideoListBox.ActualHeight)
            {
                return;
            }
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (ReferenceEquals(ViewModel.RegularSelectedItem, item))
            {
                VideoListBox.ScrollIntoView(item);
            }
        });
    }

    private void FocusVideoList()
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsVisible && !ViewModel.IsModalOpen)
            {
                Keyboard.Focus(VideoListBox);
            }
        });
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

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T match)
            {
                return match;
            }
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static FrameworkElement? FindVisualChildByName(DependencyObject parent, string name)
    {
        int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            FrameworkElement? match = FindVisualChildByName(child, name);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
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

internal sealed class ThumbnailImageConverter : IValueConverter
{
    public static ThumbnailImageConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return DependencyProperty.UnsetValue;
        }

        try
        {
            return LoadImage(path);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    internal static System.Windows.Media.ImageSource LoadImage(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        System.Windows.Media.Imaging.BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 320;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public partial class ScreenRecordListViewModel : ObservableObject
{
    private static readonly EnumerationOptions RecursiveEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static string UnknownStreamerText => GetResourceText("CommonUnknown", "Unknown");
    private static string UnknownResolutionText => GetResourceText("CommonUnknown", "Unknown");
    private static string UnknownBitrateText => GetResourceText("CommonUnknown", "Unknown");

    private readonly ObservableCollection<RecordedVideoItem> videos = [];
    internal const int SelectionHistoryLimit = 200;
    private readonly SemaphoreSlim videoEnrichmentSemaphore = new(Math.Clamp(Environment.ProcessorCount, 2, 4));
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> thumbnailExtractionTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object videoEnrichmentLock = new();
    private readonly HashSet<string> queuedVideoEnrichmentPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<SelectionSnapshot> selectionUndoStack = [];
    private readonly Stack<SelectionSnapshot> selectionRedoStack = [];
    private CancellationTokenSource? videoLoadCancellationTokenSource;
    private CancellationTokenSource? videoEnrichmentCancellationTokenSource;
    private RecordedVideoItem? lastSelectedItem;
    private bool isRestoringSelection;
    private readonly Dictionary<string, FileSystemWatcher> directoryWatchers = new(StringComparer.OrdinalIgnoreCase);
    private string[] watchedRoots = [];
    private int directorySnapshotDirty = 1;
    private int operationRefreshPending;
    private int directoryRefreshPending;
    private bool isMonitoring;

    public ICollectionView Videos { get; }
    private string allStreamerOption = string.Empty;

    public ObservableCollection<string> StreamerOptions { get; } = [];
    public ObservableCollection<string> TimeRangeOptions { get; } = [];
    public event EventHandler? VisibleItemsChanged;

    public ScreenRecordListViewModel()
    {
        Videos = CollectionViewSource.GetDefaultView(videos);
        Videos.Filter = FilterVideo;
        ReloadLocalizedOptions();
        Locale.CultureChanged += OnCultureChanged;
    }

    internal void StartMonitoring()
    {
        if (isMonitoring)
        {
            return;
        }

        isMonitoring = true;
        MediaOperationRegistry.OperationsChanged += MediaOperationRegistryOperationsChanged;
        ConfigureDirectoryWatchers(MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true));
    }

    internal void StopMonitoring()
    {
        if (!isMonitoring)
        {
            return;
        }

        isMonitoring = false;
        MediaOperationRegistry.OperationsChanged -= MediaOperationRegistryOperationsChanged;
        foreach (FileSystemWatcher watcher in directoryWatchers.Values)
        {
            watcher.Dispose();
        }
        directoryWatchers.Clear();
        watchedRoots = [];
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
    }

    private void ConfigureDirectoryWatchers(IEnumerable<string> roots)
    {
        string[] normalizedRoots = roots
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (watchedRoots.SequenceEqual(normalizedRoots, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (FileSystemWatcher watcher in directoryWatchers.Values)
        {
            watcher.Dispose();
        }
        directoryWatchers.Clear();
        watchedRoots = normalizedRoots;
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
        if (!isMonitoring)
        {
            return;
        }

        foreach (string root in normalizedRoots)
        {
            try
            {
                FileSystemWatcher watcher = new(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                };
                watcher.Changed += DirectoryWatcherChanged;
                watcher.Created += DirectoryWatcherChanged;
                watcher.Deleted += DirectoryWatcherChanged;
                watcher.Renamed += DirectoryWatcherChanged;
                watcher.Error += DirectoryWatcherError;
                watcher.EnableRaisingEvents = true;
                directoryWatchers[root] = watcher;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                AppSessionLogger.WriteException(e);
            }
        }
    }

    private void DirectoryWatcherChanged(object sender, FileSystemEventArgs e)
    {
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
        if (e.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Deleted or WatcherChangeTypes.Renamed
            && MediaFileCatalog.IsMediaPath(e.FullPath)
            && !MediaFileCatalog.IsApplicationTemporaryPath(e.FullPath))
        {
            QueueDirectoryRefresh();
        }
    }

    private void DirectoryWatcherError(object sender, ErrorEventArgs e)
    {
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
        if (e.GetException() is Exception error)
        {
            AppSessionLogger.WriteException(error);
        }
    }

    private void MediaOperationRegistryOperationsChanged(object? sender, MediaOperationsChangedEventArgs e)
    {
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(() =>
        {
            foreach (RecordedVideoItem item in videos)
            {
                item.IsInProgress = MediaOperationRegistry.IsPathProtected(item.FullPath);
            }
            VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
            if (!e.IsActive)
            {
                _ = RefreshAfterOperationAsync();
            }
        });
    }

    private void QueueDirectoryRefresh()
    {
        if (Interlocked.Exchange(ref directoryRefreshPending, 1) != 0)
        {
            return;
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref directoryRefreshPending, 0);
            return;
        }

        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(180);
                if (isMonitoring)
                {
                    await RefreshForDisplayAsync();
                }
            }
            finally
            {
                Interlocked.Exchange(ref directoryRefreshPending, 0);
            }
        });
    }

    private async Task RefreshAfterOperationAsync()
    {
        if (Interlocked.Exchange(ref operationRefreshPending, 1) != 0)
        {
            return;
        }

        try
        {
            await Task.Delay(150);
            if (isMonitoring)
            {
                await RefreshForDisplayAsync();
            }
        }
        finally
        {
            Interlocked.Exchange(ref operationRefreshPending, 0);
            if (isMonitoring && Volatile.Read(ref directorySnapshotDirty) != 0)
            {
                _ = RefreshAfterOperationAsync();
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionText))]
    [NotifyPropertyChangedFor(nameof(SortDirectionToolTip))]
    private bool isSortDescending = true;

    public string SortDirectionText => IsSortDescending
        ? GetResourceText("SortDescending", "Descending")
        : GetResourceText("SortAscending", "Ascending");

    public string SortDirectionToolTip => IsSortDescending
        ? "当前按录制时间倒序排列，最新录制的视频在最前面"
        : "当前按录制时间顺序排列，最早录制的视频在最前面";

    [ObservableProperty]
    private string selectedStreamer = string.Empty;

    partial void OnSelectedStreamerChanged(string value)
    {
        ApplyFilters();
    }

    [ObservableProperty]
    private int selectedTimeRangeIndex;

    partial void OnSelectedTimeRangeIndexChanged(int value)
    {
        int next = Math.Clamp(value, 0, Math.Max(0, TimeRangeOptions.Count - 1));
        if (next != value)
        {
            SelectedTimeRangeIndex = next;
            return;
        }

        ApplyFilters();
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            bool selectedAll = string.IsNullOrWhiteSpace(SelectedStreamer)
                || string.Equals(SelectedStreamer, allStreamerOption, StringComparison.Ordinal);
            ReloadLocalizedOptions();
            if (selectedAll)
            {
                SelectedStreamer = allStreamerOption;
            }
            foreach (RecordedVideoItem item in videos)
            {
                item.RefreshLocalizedText();
            }
            OnPropertyChanged(nameof(SortDirectionText));
            OnPropertyChanged(nameof(SortDirectionToolTip));
            RefreshSelectionSummary();
            ApplyFilters();
        });
    }

    private void ReloadLocalizedOptions()
    {
        allStreamerOption = GetResourceText("VideoAllStreamers", "All streamers");
        TimeRangeOptions.Clear();
        foreach ((string key, string fallback) in new[]
        {
            ("TimeRangeAll", "All time"),
            ("TimeRangeLast24Hours", "Last 24 hours"),
            ("TimeRangeLastWeek", "Last week"),
            ("TimeRangeLastMonth", "Last month"),
            ("TimeRangeLastThreeMonths", "Last 3 months"),
            ("TimeRangeLastYear", "Last year"),
        })
        {
            TimeRangeOptions.Add(GetResourceText(key, fallback));
        }

        UpdateStreamerOptions();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedVideoSummary))]
    [NotifyPropertyChangedFor(nameof(HasSelectedVideos))]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private RecordedVideoItem? regularSelectedItem;

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

    public bool HasVisibleVideos => !Videos.IsEmpty;

    public bool HasSelectedVideos => SelectedVideoCount > 0;

    public bool CanMergeSelectedVideos => SelectedVideoCount >= 2;

    public string SelectedVideoSummary => FormatResourceText("VideoSelectedCount", "{0} selected", SelectedVideoCount);

    public bool IsModalOpen => IsSplitPanelOpen || IsMergePanelOpen;

    public bool IsIdle => !IsOperating;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadVideosFromFoldersAsync(MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true), false);
    }

    internal async Task RefreshForDisplayAsync()
    {
        await LoadVideosFromFoldersAsync(MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true), true);
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
        if (!TryChooseImportDestination(out string root))
        {
            return;
        }
        string[] configuredRoots = MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true);
        if (configuredRoots.Any(configuredRoot =>
                IsSameOrAncestorDirectory(sourceFolder, configuredRoot)
                || IsSameOrAncestorDirectory(configuredRoot, sourceFolder)))
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
        string[] roots = MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true);
        if (!TryChooseConfiguredFolder(roots, "选择要打开的保存目录", out string root))
        {
            return;
        }

        await Launcher.LaunchFolderPathAsync(root);
    }

    private static bool TryChooseImportDestination(out string root)
    {
        string[] roots = MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true);
        return TryChooseConfiguredFolder(roots, "选择导入目标保存目录", out root);
    }

    private static bool TryChooseConfiguredFolder(string[] roots, string title, out string root)
    {
        if (roots.Length == 0)
        {
            root = string.Empty;
            Toast.Warning("没有可用的保存目录");
            return false;
        }
        if (roots.Length == 1)
        {
            root = roots[0];
            return true;
        }

        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
            Title = title,
            InitialDirectory = roots[0],
        };
        if (dialog.ShowDialog() == CommonFileDialogResult.Ok
            && roots.Any(configuredRoot => IsSameOrAncestorDirectory(configuredRoot, dialog.FileName)))
        {
            root = dialog.FileName;
            return true;
        }

        root = string.Empty;
        Toast.Warning("所选目录必须位于已配置的保存目录中");
        return false;
    }

    [RelayCommand]
    private void BeginMultiSelect()
    {
        ApplySelectionChange(() => IsMultiSelectMode = true);
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
            IsMultiSelectMode = false;
            lastSelectedItem = null;
        });
    }

    [RelayCommand]
    private void SelectAll()
    {
        ApplySelectionChange(() =>
        {
            IsMultiSelectMode = true;
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

    internal void SelectRegularItem(RecordedVideoItem item)
    {
        RegularSelectedItem = item;
    }

    internal RecordedVideoItem? GetAdjacentVisibleVideo(int offset)
    {
        RecordedVideoItem[] visible = GetVisibleVideos();
        if (visible.Length == 0)
        {
            return null;
        }

        int currentIndex = RegularSelectedItem == null ? -1 : Array.IndexOf(visible, RegularSelectedItem);
        if (currentIndex < 0)
        {
            return offset < 0 ? visible[^1] : visible[0];
        }

        int nextIndex = Math.Clamp(currentIndex + Math.Sign(offset), 0, visible.Length - 1);
        return visible[nextIndex];
    }

    internal void ClearRegularSelection()
    {
        RegularSelectedItem = null;
    }

    internal void SelectMultipleItem(RecordedVideoItem item, bool toggleSelection, bool selectRange)
    {
        bool wasMultiSelectMode = IsMultiSelectMode;
        ApplySelectionChange(() =>
        {
            IsMultiSelectMode = true;
            if (!wasMultiSelectMode && RegularSelectedItem != null)
            {
                RegularSelectedItem.IsSelected = true;
                lastSelectedItem = RegularSelectedItem;
            }

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
                    lastSelectedItem = item;
                    return;
                }
            }

            if (!toggleSelection)
            {
                item.IsSelected = true;
            }
            else
            {
                item.IsSelected = !item.IsSelected;
            }
            lastSelectedItem = item.IsSelected ? item : null;
        });
    }

    internal void SelectItems(IEnumerable<RecordedVideoItem> items)
    {
        RecordedVideoItem[] targets = items.Distinct().ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        ApplySelectionChange(() =>
        {
            IsMultiSelectMode = true;
            foreach (RecordedVideoItem item in targets)
            {
                item.IsSelected = true;
            }
            lastSelectedItem = targets[^1];
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
        RestoreSelection(snapshot.Before, snapshot.BeforeMultiSelectMode);
    }

    internal void RedoSelection()
    {
        if (selectionRedoStack.Count == 0)
        {
            return;
        }

        SelectionSnapshot snapshot = selectionRedoStack.Pop();
        selectionUndoStack.Push(snapshot);
        RestoreSelection(snapshot.After, snapshot.AfterMultiSelectMode);
    }

    [RelayCommand]
    private void OpenVideo(RecordedVideoItem? item)
    {
        if (item == null || !CanModifyVideo(item))
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
        if (item == null || IsOperating || !CanModifyVideo(item) || !File.Exists(item.FullPath))
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
        if (item == null || !item.CanTranscode || !CanModifyVideo(item) || IsOperating)
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
        if (item == null || !CanModifyVideo(item) || !File.Exists(item.FullPath))
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
        if (selected.Length == 0 || selected.Any(item => !CanModifyVideo(item)))
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
    private void SplitContext(RecordedVideoItem? item)
    {
        RecordedVideoItem[] targets = GetContextVideos(item);
        if (targets.Length == 1)
        {
            SplitVideo(targets[0]);
            return;
        }

        SplitSelected();
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
        if (targets.Length == 0 || targets.Any(item => !CanModifyVideo(item)))
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
        if (selected.Length < 2 || selected.Any(item => !CanModifyVideo(item)))
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

        if (selected.Length < 2 || selected.Any(item => !CanModifyVideo(item)))
        {
            Toast.Warning(GetResourceText("SelectAtLeastTwoVideos", "Select at least two videos"));
            return;
        }

        if (selected.Select(video => Path.GetExtension(video.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            Toast.Warning(GetResourceText("MergeFormatsMustMatch", "Only videos with the same format can be merged"));
            return;
        }

        bool streamsCompatible = await Task.Run(() => HaveCompatibleMergeStreams(selected));
        if (!streamsCompatible)
        {
            Toast.Warning("所选视频的编码、分辨率或音轨参数不一致  无法无损合并");
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
    private async Task DeleteContextAsync(RecordedVideoItem? item)
    {
        await DeleteVideosAsync(GetContextVideos(item));
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
        if (IsOperating || items.Count == 0 || items.Any(item => !CanModifyVideo(item)))
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
                    DeleteThumbnailCacheIfUnused(item.FullPath);
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
    private async Task MoveContextAsync(RecordedVideoItem? item)
    {
        await CopyOrMoveVideosAsync(GetContextVideos(item), move: true);
    }

    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        await CopyOrMoveVideosAsync(GetSelectedVideos(), move: false);
    }

    [RelayCommand]
    private async Task CopyContextAsync(RecordedVideoItem? item)
    {
        await CopyOrMoveVideosAsync(GetContextVideos(item), move: false);
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
        if (IsOperating || items.Count == 0 || items.Any(item => !CanModifyVideo(item)))
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
                            DeleteThumbnailCacheIfUnused(item.FullPath);
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

    private async Task LoadVideosFromFoldersAsync(IEnumerable<string> folders, bool reuseExistingItems)
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

        string[] roots = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool rootsChanged = !watchedRoots.SequenceEqual(roots, StringComparer.OrdinalIgnoreCase);
        ConfigureDirectoryWatchers(roots);
        if (reuseExistingItems && !rootsChanged && Volatile.Read(ref directorySnapshotDirty) == 0)
        {
            foreach (RecordedVideoItem item in videos)
            {
                item.IsInProgress = MediaOperationRegistry.IsPathProtected(item.FullPath);
            }
            VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        Interlocked.Exchange(ref directorySnapshotDirty, 0);

        RecordedVideoItem[] items;
        try
        {
            items = await Task.Run(() =>
            {
                Dictionary<string, RecordedVideoItem> existingByPath = reuseExistingItems
                    ? videos.Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
                        .GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, RecordedVideoItem>(StringComparer.OrdinalIgnoreCase);
                VideoFileSnapshot[] snapshots = roots
                    .Where(Directory.Exists)
                    .SelectMany(root => EnumerateVideoFiles(root).Select(path => (Path: path, Root: root)))
                    .Where(item => MediaFileCatalog.IsMediaPath(item.Path) && !MediaFileCatalog.IsApplicationTemporaryPath(item.Path))
                    .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(group => CreateVideoFileSnapshot(group.First().Path, group.First().Root))
                    .OrderBy(snapshot => snapshot.Path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                CleanupOrphanedThumbnailCache(snapshots.Select(snapshot => snapshot.Path));
                return snapshots
                    .Select(snapshot =>
                    {
                        loadToken.ThrowIfCancellationRequested();
                        bool isInProgress = MediaOperationRegistry.IsPathProtected(snapshot.Path);
                        if (existingByPath.TryGetValue(snapshot.Path, out RecordedVideoItem? existing)
                            && existing.SourceLength == snapshot.Length
                            && existing.SourceLastWriteTimeUtc == snapshot.LastWriteTimeUtc
                            && existing.MetadataLastWriteTimeUtc == snapshot.MetadataLastWriteTimeUtc)
                        {
                            return existing;
                        }

                        return CreateRecordedVideoItem(snapshot, isInProgress);
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

        foreach (RecordedVideoItem item in items)
        {
            item.IsInProgress = MediaOperationRegistry.IsPathProtected(item.FullPath);
        }

        HashSet<string> selectedPaths = videos
            .Where(video => video.IsSelected)
            .Select(video => video.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string regularSelectedPath = RegularSelectedItem?.FullPath ?? string.Empty;
        if (reuseExistingItems)
        {
            items = ReuseExistingVideoItems(videos, items);
            if (videos.SequenceEqual(items))
            {
                lastSelectedItem = GetVisibleVideos().LastOrDefault(item => item.IsSelected);
                RegularSelectedItem = videos.FirstOrDefault(item => string.Equals(item.FullPath, regularSelectedPath, StringComparison.OrdinalIgnoreCase));
                VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

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
        RegularSelectedItem = videos.FirstOrDefault(item => string.Equals(item.FullPath, regularSelectedPath, StringComparison.OrdinalIgnoreCase));
        VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static RecordedVideoItem[] ReuseExistingVideoItems(
        IEnumerable<RecordedVideoItem> existingItems,
        IEnumerable<RecordedVideoItem> loadedItems)
    {
        Dictionary<string, RecordedVideoItem> existingByPath = existingItems
            .Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
            .GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return loadedItems
            .Select(item => existingByPath.TryGetValue(item.FullPath, out RecordedVideoItem? existing)
                && existing.SourceLength == item.SourceLength
                && existing.SourceLastWriteTimeUtc == item.SourceLastWriteTimeUtc
                && existing.MetadataLastWriteTimeUtc == item.MetadataLastWriteTimeUtc
                    ? existing
                    : item)
            .ToArray();
    }

    private static VideoFileSnapshot CreateVideoFileSnapshot(string path, string rootFolder)
    {
        FileInfo fileInfo = new(path);
        DateTime metadataLastWriteTimeUtc = VideoRecordingMetadataStore.GetMetadataCandidates(fileInfo)
            .Where(File.Exists)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return new VideoFileSnapshot(fileInfo.FullName, rootFolder, fileInfo.Length, fileInfo.LastWriteTimeUtc, metadataLastWriteTimeUtc);
    }

    private static RecordedVideoItem CreateRecordedVideoItem(VideoFileSnapshot snapshot, bool isInProgress)
    {
        FileInfo fileInfo = new(snapshot.Path);
        VideoRecordingMetadata metadata = LoadMetadata(fileInfo);
        string resolution = NormalizeResolution(metadata.Resolution);
        string bitrate = NormalizeBitrate(metadata.Bitrate);

        string nickName = NormalizeStreamerName(string.IsNullOrWhiteSpace(metadata.NickName) ? InferNickName(snapshot.Path, snapshot.RootFolder) : metadata.NickName);
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
            SupportsTranscode = fileInfo.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || fileInfo.Extension.Equals(".flv", StringComparison.OrdinalIgnoreCase),
            IsInProgress = isInProgress,
            SourceLength = snapshot.Length,
            SourceLastWriteTimeUtc = snapshot.LastWriteTimeUtc,
            MetadataLastWriteTimeUtc = snapshot.MetadataLastWriteTimeUtc,
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
        if (item.IsEnriched || item.IsInProgress || MediaOperationRegistry.IsPathProtected(item.FullPath) || token.IsCancellationRequested)
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

    private async Task EnrichVideoAsync(RecordedVideoItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(item.FullPath) || MediaOperationRegistry.IsPathProtected(item.FullPath))
        {
            return;
        }

        FileInfo file = new(item.FullPath);
        VideoRecordingMetadata metadata = LoadMetadata(file);
        VideoProbeInfo probeInfo = ProbeVideoFileInfo(item.FullPath, file.Length);
        metadata = VideoRecordingMetadataStore.Merge(metadata, probeInfo.Metadata);
        if (!VideoRecordingMetadataStore.HasValidSidecar(file)
            && VideoRecordingMetadataStore.HasAnyMetadata(probeInfo.Metadata))
        {
            _ = VideoRecordingMetadataStore.WriteSidecar(
                file.DirectoryName ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(file.Name),
                VideoRecordingMetadataStore.WithFileName(metadata, file.Name));
        }
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
                bool createdAtChanged = item.CreatedAt != createdAt;
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
                if (createdAtChanged)
                {
                    Videos.Refresh();
                    VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        });
    }

    private async Task<string> ExtractThumbnailAsync(string filePath, CancellationToken token)
    {
        string? ffmpegPath = SearchFileHelper.SearchExecutable("ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(filePath) || MediaOperationRegistry.IsPathProtected(filePath))
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

        Lazy<Task<string>> extraction = thumbnailExtractionTasks.GetOrAdd(
            imagePath,
            _ => new Lazy<Task<string>>(
                () => ExtractThumbnailSharedAsync(ffmpegPath, filePath, imagePath),
                LazyThreadSafetyMode.ExecutionAndPublication));
        Task<string> extractionTask = extraction.Value;
        _ = extractionTask.ContinueWith(
            _ => thumbnailExtractionTasks.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(imagePath, extraction)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return await extractionTask.WaitAsync(token);
    }

    private static async Task<string> ExtractThumbnailSharedAsync(string ffmpegPath, string filePath, string imagePath)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(45));
        return await ExtractThumbnailCoreAsync(ffmpegPath, filePath, imagePath, timeout.Token);
    }

    private static async Task<string> ExtractThumbnailCoreAsync(string ffmpegPath, string filePath, string imagePath, CancellationToken token)
    {
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
        return GetThumbnailCachePath(filePath, GetThumbnailCacheDirectory());
    }

    internal static string GetThumbnailCachePath(string filePath, string cacheDirectory)
    {
        return Path.Combine(cacheDirectory, $"{ToStableHash(GetThumbnailIdentity(filePath))}.jpg");
    }

    internal static string GetExistingThumbnailPath(string filePath, string coverPath, string? cacheDirectory = null)
    {
        if (File.Exists(coverPath))
        {
            return coverPath;
        }

        string cachePath = cacheDirectory == null
            ? GetThumbnailCachePath(filePath)
            : GetThumbnailCachePath(filePath, cacheDirectory);
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

    private static string GetThumbnailIdentity(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, stem).ToUpperInvariant();
    }

    internal static int CleanupOrphanedThumbnailCache(IEnumerable<string> videoFilePaths, string? cacheDirectory = null)
    {
        string directory = cacheDirectory ?? GetThumbnailCacheDirectory();
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        HashSet<string> expectedFileNames = videoFilePaths
            .Select(path => Path.GetFileName(GetThumbnailCachePath(path, directory)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int deleted = 0;

        try
        {
            foreach (string thumbnailPath in Directory.EnumerateFiles(directory, "*.jpg", System.IO.SearchOption.TopDirectoryOnly))
            {
                if (expectedFileNames.Contains(Path.GetFileName(thumbnailPath)))
                {
                    continue;
                }

                try
                {
                    File.Delete(thumbnailPath);
                    deleted++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine(e);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
        }

        return deleted;
    }

    internal static void CopyAssociatedMetadata(string sourceFilePath, string targetFilePath)
    {
        FileInfo source = new(sourceFilePath);
        string[] sourceCandidates = VideoRecordingMetadataStore.GetMetadataCandidates(source).ToArray();
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(source);
        if (!VideoRecordingMetadataStore.HasAnyMetadata(metadata))
        {
            return;
        }

        int candidateIndex = Array.FindIndex(sourceCandidates, File.Exists);
        string[] targetCandidates = VideoRecordingMetadataStore.GetMetadataCandidates(new FileInfo(targetFilePath)).ToArray();
        string targetMetadataPath = targetCandidates[Math.Clamp(candidateIndex, 0, targetCandidates.Length - 1)];
        string targetDirectory = Path.GetDirectoryName(targetFilePath) ?? Environment.CurrentDirectory;
        if (VideoRecordingMetadataStore.WriteSidecar(
            targetDirectory,
            VideoRecordingMetadataStore.GetSidecarStem(targetMetadataPath),
            VideoRecordingMetadataStore.WithFileName(metadata, Path.GetFileName(targetFilePath))) == null)
        {
            throw new IOException("Failed to write recording metadata.");
        }
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

        if (!IsSameVolume(sourceFilePath, targetFilePath))
        {
            TransferAcrossVolumes(sourceFilePath, targetFilePath);
            return;
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
            DeleteThumbnailCacheIfUnused(sourceFilePath);
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

    internal static bool DeleteThumbnailCacheIfUnused(string filePath, string? cacheDirectory = null)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(filePath);
        if (Directory.Exists(directory)
            && Directory.EnumerateFiles(directory, stem + ".*", System.IO.SearchOption.TopDirectoryOnly).Any(MediaFileCatalog.IsMediaPath))
        {
            return false;
        }

        string cachePath = cacheDirectory == null
            ? GetThumbnailCachePath(filePath)
            : GetThumbnailCachePath(filePath, cacheDirectory);
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            File.Delete(cachePath);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
            return false;
        }
    }

    private static bool IsSameVolume(string sourceFilePath, string targetFilePath)
    {
        string sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourceFilePath)) ?? string.Empty;
        string targetRoot = Path.GetPathRoot(Path.GetFullPath(targetFilePath)) ?? string.Empty;
        return string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TransferAcrossVolumes(string sourceFilePath, string targetFilePath)
    {
        string temporaryTargetPath = MediaFileCatalog.CreateTemporaryPath(targetFilePath, "move");
        try
        {
            File.Copy(sourceFilePath, temporaryTargetPath, overwrite: false);
            if (new FileInfo(sourceFilePath).Length != new FileInfo(temporaryTargetPath).Length)
            {
                throw new IOException("The copied video length does not match the source.");
            }

            File.Move(temporaryTargetPath, targetFilePath, overwrite: false);
            CopyAssociatedMetadata(sourceFilePath, targetFilePath);
            File.Delete(sourceFilePath);
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourceFilePath);
        }
        catch
        {
            DeleteFileIfExists(temporaryTargetPath);
            DeleteFileIfExists(targetFilePath);
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFilePath);
            throw;
        }
    }

    private static bool CanModifyVideo(RecordedVideoItem item)
    {
        return item.CanModify && !MediaOperationRegistry.IsPathProtected(item.FullPath);
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
        foreach (string path in EnumerateVideoFiles(sourceFolder).Where(IsVideoFile))
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

    internal static IEnumerable<string> EnumerateVideoFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.*", RecursiveEnumerationOptions).ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
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
        return MediaFileCatalog.IsMediaPath(path) && !MediaFileCatalog.IsApplicationTemporaryPath(path);
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
        NormalizeSelectionForVisibleVideos();
        RefreshSelectionSummary();
        VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NormalizeSelectionForVisibleVideos()
    {
        OnPropertyChanged(nameof(HasVisibleVideos));
        if (HasVisibleVideos)
        {
            return;
        }

        isRestoringSelection = true;
        try
        {
            foreach (RecordedVideoItem item in videos)
            {
                item.IsSelected = false;
            }
        }
        finally
        {
            isRestoringSelection = false;
        }

        IsMultiSelectMode = false;
        RegularSelectedItem = null;
        lastSelectedItem = null;
    }

    private bool FilterVideo(object item)
    {
        if (item is not RecordedVideoItem video)
        {
            return false;
        }

        bool streamerMatched = string.IsNullOrWhiteSpace(SelectedStreamer)
            || string.Equals(SelectedStreamer, allStreamerOption, StringComparison.Ordinal)
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
        StreamerOptions.Add(allStreamerOption);
        foreach (string streamer in streamers)
        {
            StreamerOptions.Add(streamer);
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
            ApplyFilters();
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

    private RecordedVideoItem[] GetContextVideos(RecordedVideoItem? item)
    {
        RecordedVideoItem[] selected = GetSelectedVideos();
        if (IsMultiSelectMode && selected.Length > 0)
        {
            return selected;
        }

        return item != null && File.Exists(item.FullPath) ? [item] : [];
    }

    private RecordedVideoItem[] GetVisibleVideos()
    {
        return Videos.Cast<RecordedVideoItem>().ToArray();
    }

    private void ApplySelectionChange(Action change)
    {
        HashSet<string> before = CaptureSelectedPaths();
        bool beforeMultiSelectMode = IsMultiSelectMode;
        change();
        if (!HasVisibleVideos)
        {
            IsMultiSelectMode = false;
        }
        HashSet<string> after = CaptureSelectedPaths();
        bool afterMultiSelectMode = IsMultiSelectMode;
        if (!before.SetEquals(after) || beforeMultiSelectMode != afterMultiSelectMode)
        {
            selectionUndoStack.Push(new SelectionSnapshot(before, after, beforeMultiSelectMode, afterMultiSelectMode));
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

    private void RestoreSelection(ISet<string> selectedPaths, bool multiSelectMode)
    {
        bool canRestoreMultiSelect = multiSelectMode && HasVisibleVideos;
        isRestoringSelection = true;
        try
        {
            foreach (RecordedVideoItem item in videos)
            {
                item.IsSelected = canRestoreMultiSelect && selectedPaths.Contains(item.FullPath);
            }
        }
        finally
        {
            isRestoringSelection = false;
        }

        lastSelectedItem = GetVisibleVideos().LastOrDefault(item => item.IsSelected);
        IsMultiSelectMode = canRestoreMultiSelect;
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

    internal static bool HaveCompatibleMergeStreams(IReadOnlyList<RecordedVideoItem> selected)
    {
        string[] signatures = selected
            .Select(video => ProbeMergeStreamSignature(video.FullPath))
            .ToArray();
        return signatures.Length >= 2
            && signatures.All(signature => !string.IsNullOrWhiteSpace(signature))
            && signatures.Distinct(StringComparer.Ordinal).Count() == 1;
    }

    private static string ProbeMergeStreamSignature(string filePath)
    {
        string? ffprobePath = SearchFileHelper.SearchExecutable("ffprobe.exe");
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(filePath))
        {
            return string.Empty;
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

            foreach (string argument in new[] { "-v", "error", "-show_entries", "stream=codec_type,codec_name,profile,width,height,pix_fmt,time_base,sample_rate,channels,channel_layout", "-of", "json", filePath })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            _ = ChildProcessTracerPeriodicTimer.Default.TryTraceProcess(process);
            return TryReadProcessOutput(process, 15000, out string output) && process.ExitCode == 0
                ? ParseMergeStreamSignature(output)
                : string.Empty;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or Win32Exception or JsonException)
        {
            return string.Empty;
        }
    }

    internal static string ParseMergeStreamSignature(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out JsonElement streams)
            || streams.ValueKind != JsonValueKind.Array
            || streams.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        string[] properties = ["codec_type", "codec_name", "profile", "width", "height", "pix_fmt", "time_base", "sample_rate", "channels", "channel_layout"];
        return string.Join(";", streams.EnumerateArray().Select(stream => string.Join("|", properties.Select(property =>
            stream.TryGetProperty(property, out JsonElement value) ? value.ToString() : string.Empty))));
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
        string temporaryStem = $".emerde-split-{Guid.NewGuid():N}";
        string temporaryPattern = Path.Combine(directory, $"{temporaryStem}_%03d{source.Extension}");
        string finalPattern = Path.Combine(directory, $"{outputBase}_%03d{source.Extension}");
        string[] arguments = ["-i", source.FullName, "-c", "copy", "-map", "0", "-f", "segment", "-segment_time", seconds.ToString(CultureInfo.InvariantCulture), "-reset_timestamps", "1", temporaryPattern];
        using CancellationTokenSource operationCancellation = new(TimeSpan.FromHours(12));
        using IDisposable operation = MediaOperationRegistry.Register(
            MediaOperationKind.Split,
            () => [source.FullName, temporaryPattern, finalPattern],
            operationCancellation.Cancel);
        (bool succeeded, _) = await ExecuteFfmpegAsync(arguments, operationCancellation.Token);
        string[] temporaryOutputs = Directory
            .EnumerateFiles(directory, $"{temporaryStem}_*{source.Extension}", System.IO.SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!succeeded || temporaryOutputs.Length == 0 || temporaryOutputs.Any(path => new FileInfo(path).Length == 0))
        {
            foreach (string output in temporaryOutputs)
            {
                DeleteFileIfExists(output);
            }
            return false;
        }

        VideoRecordingMetadata sourceMetadata = VideoRecordingMetadataStore.Load(source);
        bool hasMetadata = VideoRecordingMetadataStore.HasAnyMetadata(sourceMetadata);
        List<(string Temporary, string Final, StagedVideoMetadata? Metadata)> preparedOutputs = [];
        List<string> finalOutputs = [];
        try
        {
            for (int index = 0; index < temporaryOutputs.Length; index++)
            {
                string finalOutput = Path.Combine(directory, $"{outputBase}_{index:000}{source.Extension}");
                StagedVideoMetadata? stagedMetadata = hasMetadata
                    ? VideoRecordingMetadataStore.StageSidecarForMedia(finalOutput, sourceMetadata, "split-metadata")
                    : null;
                if (hasMetadata && stagedMetadata == null)
                {
                    throw new IOException("Failed to stage split recording metadata.");
                }
                preparedOutputs.Add((temporaryOutputs[index], finalOutput, stagedMetadata));
            }

            foreach ((string temporary, string final, StagedVideoMetadata? metadata) in preparedOutputs)
            {
                metadata?.Commit();
                try
                {
                    File.Move(temporary, final, overwrite: false);
                }
                catch
                {
                    metadata?.DeleteCommitted();
                    throw;
                }
                finalOutputs.Add(final);
            }
            return true;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            foreach (string output in temporaryOutputs.Concat(finalOutputs))
            {
                DeleteFileIfExists(output);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(output);
            }
            return false;
        }
        finally
        {
            foreach (var prepared in preparedOutputs)
            {
                prepared.Metadata?.Dispose();
            }
        }
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
        string temporaryTarget = MediaFileCatalog.CreateTemporaryPath(target, "merge");
        string listPath = Path.Combine(Path.GetTempPath(), $"emerde_concat_{Guid.NewGuid():N}.txt");
        using CancellationTokenSource operationCancellation = new(TimeSpan.FromHours(12));
        using IDisposable operation = MediaOperationRegistry.Register(
            MediaOperationKind.Merge,
            () => [.. ordered.Select(item => item.FullPath), temporaryTarget, target],
            operationCancellation.Cancel);
        try
        {
            await File.WriteAllLinesAsync(listPath, ordered.Select(item => $"file '{EscapeConcatPath(item.FullPath)}'"), new UTF8Encoding(false));
            double totalSeconds = await Task.Run(() => ordered.Sum(item => GetVideoDurationSeconds(item.FullPath)));
            string[] arguments = ["-f", "concat", "-safe", "0", "-i", listPath, "-c", "copy", "-progress", "pipe:1", "-nostats", temporaryTarget];
            (bool succeeded, _) = await ExecuteFfmpegAsync(arguments, operationCancellation.Token, line =>
            {
                if (totalSeconds > 0 && line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(line["out_time_ms=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double microseconds))
                {
                    progress(Math.Min(99, microseconds / 1_000_000d / totalSeconds * 100d));
                }
                return Task.CompletedTask;
            });
            if (!succeeded || !File.Exists(temporaryTarget) || new FileInfo(temporaryTarget).Length == 0)
            {
                DeleteFileIfExists(temporaryTarget);
                return false;
            }

            try
            {
                VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(first);
                bool hasMetadata = VideoRecordingMetadataStore.HasAnyMetadata(metadata);
                using StagedVideoMetadata? stagedMetadata = hasMetadata
                    ? VideoRecordingMetadataStore.StageSidecarForMedia(target, metadata, "merge-metadata")
                    : null;
                if (hasMetadata && stagedMetadata == null)
                {
                    throw new IOException("Failed to stage merged recording metadata.");
                }
                stagedMetadata?.Commit();
                try
                {
                    File.Move(temporaryTarget, target, overwrite: false);
                }
                catch
                {
                    stagedMetadata?.DeleteCommitted();
                    throw;
                }
                return true;
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
                DeleteFileIfExists(target);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(target);
                return false;
            }
        }
        finally
        {
            DeleteFileIfExists(temporaryTarget);
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
            return TryReadProcessOutput(process, 15000, out string output)
                && process.ExitCode == 0
                && double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double duration)
                    ? duration
                    : 0;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            return 0;
        }
    }

    private static bool TryReadProcessOutput(Process process, int timeoutMilliseconds, out string output)
    {
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task completionTask = Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());
        Task completedTask = Task.WhenAny(completionTask, Task.Delay(timeoutMilliseconds)).GetAwaiter().GetResult();
        if (!ReferenceEquals(completedTask, completionTask))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
            }
            _ = completionTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            output = string.Empty;
            return false;
        }

        completionTask.GetAwaiter().GetResult();
        output = outputTask.Result;
        return true;
    }

    private static async Task<(bool Succeeded, string Error)> ExecuteFfmpegAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        Func<string, Task>? outputLine = null)
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
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await Task.WhenAll(outputTask, errorTask);
                return (false, "ffmpeg operation was cancelled or timed out");
            }
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

internal readonly record struct VideoFileSnapshot(
    string Path,
    string RootFolder,
    long Length,
    DateTime LastWriteTimeUtc,
    DateTime MetadataLastWriteTimeUtc);

internal sealed record SelectionSnapshot(
    HashSet<string> Before,
    HashSet<string> After,
    bool BeforeMultiSelectMode,
    bool AfterMultiSelectMode);

public partial class RecordedVideoItem : ObservableObject
{
    internal bool SupportsTranscode { get; init; }

    internal long SourceLength { get; init; }

    internal DateTime SourceLastWriteTimeUtc { get; init; }

    internal DateTime MetadataLastWriteTimeUtc { get; init; }

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
    [NotifyPropertyChangedFor(nameof(CanModify))]
    [NotifyPropertyChangedFor(nameof(CanTranscode))]
    private bool isInProgress;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isEnriched;

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);

    public bool CanModify => !IsInProgress;

    public bool CanTranscode => SupportsTranscode && !IsInProgress;

    internal void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(StreamerChipText));
        OnPropertyChanged(nameof(ResolutionChipText));
        OnPropertyChanged(nameof(BitrateChipText));
    }

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
