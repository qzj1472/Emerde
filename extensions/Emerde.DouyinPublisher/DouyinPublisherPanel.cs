using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Emerde.ViewModels;
using Wpf.Ui.Violeta.Controls;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Emerde.DouyinPublisher;

internal sealed class DouyinPublisherPanel : Border, IDisposable
{
    private readonly MainViewModel mainViewModel;
    private readonly PublisherStateStore stateStore;
    private readonly DouyinPublisherWorker worker;
    private readonly WrapPanel roomPanel = new();
    private readonly TextBlock summaryText = new();
    private readonly TextBlock cookieText = new();
    private readonly TextBlock currentTaskText = new();
    private readonly TextBlock errorText = new();
    private readonly Button resumeButton = new();
    private readonly Ellipse sessionIndicator = new() { Width = 8, Height = 8 };
    private readonly ProgressBar uploadProgress = new() { Height = 4, Minimum = 0, Maximum = 100 };
    private readonly Func<bool> hasCookie;
    private bool disposed;

    public DouyinPublisherPanel(
        MainViewModel mainViewModel,
        PublisherStateStore stateStore,
        DouyinPublisherWorker worker,
        Func<bool> hasCookie)
    {
        this.mainViewModel = mainViewModel;
        this.stateStore = stateStore;
        this.worker = worker;
        this.hasCookie = hasCookie;
        Padding = new Thickness(14);
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Focusable = false;
        FocusVisualStyle = null;
        SetResourceReference(BackgroundProperty, "EmerdePanelBrush");
        SetResourceReference(CornerRadiusProperty, "Win11ControlCornerRadius");
        Child = BuildContent();
        stateStore.Changed += StateStoreChanged;
        worker.SessionStateChanged += WorkerStateChanged;
        worker.ProgressChanged += WorkerStateChanged;
        mainViewModel.RoomStatuses.CollectionChanged += RoomStatusesChanged;
        RebuildRooms();
        UpdateSummary();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        stateStore.Changed -= StateStoreChanged;
        worker.SessionStateChanged -= WorkerStateChanged;
        worker.ProgressChanged -= WorkerStateChanged;
        mainViewModel.RoomStatuses.CollectionChanged -= RoomStatusesChanged;
    }

    private UIElement BuildContent()
    {
        StackPanel root = new();
        root.Children.Add(new TextBlock
        {
            Text = "投稿中心",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        });
        TextBlock description = new()
        {
            Text = "查看登录状态和投稿进度，遇到验证时在投稿浏览器中继续。",
            Margin = new Thickness(0, 4, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(description);
        Border statusPanel = new()
        {
            Padding = new Thickness(12),
            BorderThickness = new Thickness(0),
        };
        statusPanel.SetResourceReference(BackgroundProperty, "EmerdeSurfaceBrush");
        statusPanel.SetResourceReference(CornerRadiusProperty, "Win11ControlCornerRadius");
        StackPanel statusContent = new();
        statusPanel.Child = statusContent;

        StackPanel sessionRow = new()
        {
            Orientation = Orientation.Horizontal,
        };
        sessionIndicator.Margin = new Thickness(0, 0, 8, 0);
        sessionIndicator.VerticalAlignment = VerticalAlignment.Center;
        sessionRow.Children.Add(sessionIndicator);
        cookieText.FontWeight = FontWeights.SemiBold;
        cookieText.VerticalAlignment = VerticalAlignment.Center;
        sessionRow.Children.Add(cookieText);
        statusContent.Children.Add(sessionRow);

        summaryText.FontWeight = FontWeights.SemiBold;
        summaryText.Margin = new Thickness(0, 9, 0, 0);
        summaryText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        statusContent.Children.Add(summaryText);
        currentTaskText.Margin = new Thickness(0, 5, 0, 0);
        currentTaskText.TextWrapping = TextWrapping.Wrap;
        currentTaskText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        statusContent.Children.Add(currentTaskText);
        uploadProgress.Margin = new Thickness(0, 9, 0, 0);
        statusContent.Children.Add(uploadProgress);
        errorText.Margin = new Thickness(0, 5, 0, 0);
        errorText.TextWrapping = TextWrapping.Wrap;
        errorText.Foreground = new SolidColorBrush(Color.FromRgb(216, 59, 1));
        statusContent.Children.Add(errorText);
        root.Children.Add(statusPanel);

        WrapPanel actions = new()
        {
            Margin = new Thickness(0, 10, 0, 0),
        };
        Button openButton = new()
        {
            Content = "打开投稿浏览器",
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0),
            FocusVisualStyle = null,
        };
        openButton.Click += OpenBrowserClicked;
        actions.Children.Add(openButton);
        Button checkButton = new()
        {
            Content = "检查登录",
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0),
            FocusVisualStyle = null,
        };
        checkButton.Click += CheckSessionClicked;
        actions.Children.Add(checkButton);
        resumeButton.Content = "继续并重试";
        resumeButton.Height = 34;
        resumeButton.Padding = new Thickness(12, 0, 12, 0);
        resumeButton.FocusVisualStyle = null;
        resumeButton.Click += ResumeClicked;
        actions.Children.Add(resumeButton);
        root.Children.Add(actions);

        root.Children.Add(new TextBlock
        {
            Text = "自动投稿直播间",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 4),
        });
        TextBlock roomDescription = new()
        {
            Text = "选中的直播间在录制与转码完成后自动投稿。",
            Margin = new Thickness(0, 0, 0, 9),
            TextWrapping = TextWrapping.Wrap,
        };
        roomDescription.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(roomDescription);
        root.Children.Add(roomPanel);
        return root;
    }

    private void RebuildRooms()
    {
        PublisherState state = stateStore.Snapshot();
        roomPanel.Children.Clear();
        RoomStatusReactive[] rooms = mainViewModel.RoomStatuses
            .OrderBy(room => room.AddedOrder)
            .ToArray();
        foreach (RoomStatusReactive room in rooms)
        {
            ToggleButton button = CreateRoomButton(room, state.SelectedRoomUrls.Contains(room.RoomUrl));
            roomPanel.Children.Add(button);
        }
        if (rooms.Length == 0)
        {
            TextBlock empty = new()
            {
                Text = "首页还没有直播间",
                Margin = new Thickness(0, 2, 0, 8),
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            roomPanel.Children.Add(empty);
        }
    }

    private ToggleButton CreateRoomButton(RoomStatusReactive room, bool selected)
    {
        ToggleButton button = new()
        {
            Content = string.IsNullOrWhiteSpace(room.NickName) ? room.RoomCodeText : room.NickName,
            Tag = room.RoomUrl,
            IsChecked = selected,
            MinHeight = 34,
            MinWidth = 72,
            MaxWidth = 220,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 8),
            BorderThickness = new Thickness(0),
            FocusVisualStyle = null,
        };
        button.Checked += RoomSelectionChanged;
        button.Unchecked += RoomSelectionChanged;
        return button;
    }

    private async void RoomSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (disposed || sender is not ToggleButton { Tag: string roomUrl } button)
        {
            return;
        }
        button.IsEnabled = false;
        try
        {
            await stateStore.SetRoomSelectedAsync(roomUrl, button.IsChecked == true);
        }
        catch (Exception exception)
        {
            errorText.Text = exception.Message;
            errorText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!disposed)
            {
                button.IsEnabled = true;
            }
        }
    }

    private void StateStoreChanged(object? sender, EventArgs e)
    {
        QueueUiUpdate(UpdateSummary);
    }

    private void WorkerStateChanged(object? sender, EventArgs e)
    {
        QueueUiUpdate(UpdateSummary);
    }

    private void RoomStatusesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueUiUpdate(RebuildRooms);
    }

    private void QueueUiUpdate(Action action)
    {
        if (disposed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!disposed)
            {
                action();
            }
        }, DispatcherPriority.Background);
    }

    private void UpdateSummary()
    {
        PublisherState state = stateStore.Snapshot();
        int pending = state.Queue.Count(item => item.Status is PublisherQueueStatus.Pending or PublisherQueueStatus.Preparing or PublisherQueueStatus.Uploading or PublisherQueueStatus.Retry);
        int waiting = state.Queue.Count(item => PublisherQueueStatus.IsWaiting(item.Status));
        int failed = state.Queue.Count(item => item.Status == PublisherQueueStatus.Failed);
        int published = state.Queue.Count(item => item.Status == PublisherQueueStatus.Published);
        summaryText.Text = $"待投稿 {pending} · 等待处理 {waiting} · 失败 {failed} · 已发布 {published}";
        PublisherQueueItem? current = state.Queue.FirstOrDefault(item => item.Status is PublisherQueueStatus.Preparing or PublisherQueueStatus.Uploading)
            ?? state.Queue.FirstOrDefault(item => PublisherQueueStatus.IsWaiting(item.Status))
            ?? state.Queue.FirstOrDefault(item => item.Status == PublisherQueueStatus.Retry);
        currentTaskText.Text = current == null
            ? "当前没有正在执行的投稿任务"
            : $"当前：{Path.GetFileName(current.FilePath)} · {GetCurrentStatusText(current)}";
        PublisherQueueItem? error = state.Queue.LastOrDefault(item => !string.IsNullOrWhiteSpace(item.LastError)
            && item.Status is PublisherQueueStatus.WaitingLogin or PublisherQueueStatus.WaitingUser or PublisherQueueStatus.Failed or PublisherQueueStatus.Retry);
        errorText.Text = error?.LastError ?? string.Empty;
        errorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        resumeButton.IsEnabled = waiting + failed > 0;
        cookieText.Text = worker.SessionMessage;
        sessionIndicator.Fill = new SolidColorBrush(GetSessionColor(worker.SessionState));
        uploadProgress.Value = worker.UploadProgress ?? 0;
        uploadProgress.Visibility = current?.Status == PublisherQueueStatus.Uploading && worker.UploadProgress.HasValue
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OpenBrowserClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;
            try
            {
                await worker.OpenBrowserAsync();
            }
            catch (Exception exception)
            {
                Toast.Warning($"打开投稿浏览器失败：{exception.Message}");
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }

    private async void ResumeClicked(object sender, RoutedEventArgs e)
    {
        resumeButton.IsEnabled = false;
        try
        {
            int resumed = await worker.ResumeBlockedAsync();
            if (resumed > 0)
            {
                Toast.Success($"已恢复 {resumed} 个投稿任务");
            }
        }
        catch (Exception exception)
        {
            Toast.Warning($"恢复投稿失败：{exception.Message}");
        }
        finally
        {
            UpdateSummary();
        }
    }

    private async void CheckSessionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }
        button.IsEnabled = false;
        try
        {
            await worker.CheckSessionAsync();
        }
        catch (Exception exception)
        {
            Toast.Warning($"检查登录状态失败：{exception.Message}");
        }
        finally
        {
            button.IsEnabled = true;
            UpdateSummary();
        }
    }

    private string GetCurrentStatusText(PublisherQueueItem item)
    {
        if ((item.Status is PublisherQueueStatus.Preparing or PublisherQueueStatus.Uploading)
            && !string.IsNullOrWhiteSpace(worker.ActivityText))
        {
            return worker.ActivityText;
        }
        return GetStatusText(item.Status);
    }

    private Color GetSessionColor(PublisherSessionState state)
    {
        return state switch
        {
            PublisherSessionState.LoggedIn => Color.FromRgb(16, 137, 62),
            PublisherSessionState.Checking => Color.FromRgb(77, 167, 176),
            PublisherSessionState.LoginRequired or PublisherSessionState.VerificationRequired => Color.FromRgb(216, 89, 1),
            PublisherSessionState.Error => Color.FromRgb(196, 43, 28),
            _ when hasCookie() => Color.FromRgb(77, 167, 176),
            _ => Color.FromRgb(138, 138, 138),
        };
    }

    private static string GetStatusText(string status)
    {
        return status switch
        {
            PublisherQueueStatus.Preparing => "正在准备",
            PublisherQueueStatus.Uploading => "正在上传",
            PublisherQueueStatus.WaitingLogin => "等待登录",
            PublisherQueueStatus.WaitingUser => "等待用户处理",
            PublisherQueueStatus.Retry => "等待自动重试",
            PublisherQueueStatus.Failed => "投稿失败",
            PublisherQueueStatus.Published => "投稿成功",
            _ => "等待投稿",
        };
    }
}
