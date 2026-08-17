using System.Xml.Linq;
using Emerde.Core;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Emerde.Tests;

[Collection("WpfUi")]
public sealed class AppFeedbackServiceTests
{
    [Theory]
    [InlineData("保存成功", 3)]
    [InlineData("视频列表已经完成刷新并显示最新内容", 5)]
    [InlineData("这是一条需要用户花费更多时间阅读并确认具体处理结果的应用内通知消息", 8)]
    public void CalculateDisplayDuration_UsesChineseReadingLength(string text, int expectedSeconds)
    {
        TimeSpan? duration = AppFeedbackService.CalculateDisplayDuration(AppFeedbackKind.Information, text);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), duration);
    }

    [Fact]
    public void CalculateDisplayDuration_KeepsPathsErrorsAndActiveTasksPersistent()
    {
        Assert.Null(AppFeedbackService.CalculateDisplayDuration(AppFeedbackKind.Information, "导出完成", @"C:\Users\User\Desktop\Emerde logs.zip"));
        Assert.Null(AppFeedbackService.CalculateDisplayDuration(AppFeedbackKind.Information, @"日志已导出：E:\logs.zip"));
        Assert.Null(AppFeedbackService.CalculateDisplayDuration(AppFeedbackKind.Error, "保存失败", "无法写入配置"));
        Assert.Null(AppFeedbackService.CalculateDisplayDuration(AppFeedbackKind.Task, "正在转码"));
        Assert.Equal(TimeSpan.FromSeconds(9), AppFeedbackService.CalculateDisplayDuration(AppFeedbackKind.Information, new string('长', 61)));
        Assert.Equal(TimeSpan.FromSeconds(3), AppFeedbackService.CalculateDisplayDuration(AppFeedbackKind.Task, "转码完成", isTaskCompleted: true));
    }

    [Fact]
    public void Archive_HidesNotificationAndKeepsItInHistory()
    {
        using AppFeedbackService service = new();

        Guid id = service.Information("测速结果", new string('速', 80));
        service.Archive(id);

        AppFeedbackHostSnapshot snapshot = service.GetSnapshot();
        Assert.Empty(snapshot.Visible);
        Assert.Contains(snapshot.History, item => item.Id == id);
    }

    [Fact]
    public void Dismiss_RemovesNotificationFromVisibleAndHistory()
    {
        using AppFeedbackService service = new();

        Guid id = service.Information("刷新完成");
        service.Dismiss(id);

        AppFeedbackHostSnapshot snapshot = service.GetSnapshot();
        Assert.Empty(snapshot.Visible);
        Assert.DoesNotContain(snapshot.History, item => item.Id == id);
    }

    [Fact]
    public void Show_DeduplicatesByKeyAndKeepsOnlyTwoVisible()
    {
        using AppFeedbackService service = new();

        Guid first = service.Information("第一次", key: "refresh");
        Guid updated = service.Success("刷新完成", key: "refresh");
        service.Information("第二条");
        service.Warning("第三条");

        AppFeedbackHostSnapshot snapshot = service.GetSnapshot();

        Assert.Equal(first, updated);
        Assert.Equal(3, snapshot.History.Count);
        Assert.Equal(2, snapshot.Visible.Count);
        AppFeedbackNotification deduplicated = Assert.Single(snapshot.History, item => item.Id == first);
        Assert.Equal(AppFeedbackKind.Success, deduplicated.Kind);
        Assert.Equal(2, deduplicated.RepetitionCount);
    }

    [Fact]
    public async Task Show_IsThreadSafeWhenTheSameKeyIsUpdatedConcurrently()
    {
        using AppFeedbackService service = new();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            service.Information($"刷新 {index}", key: "refresh"))));

        AppFeedbackNotification notification = Assert.Single(service.GetSnapshot().History);
        Assert.Equal(64, notification.RepetitionCount);
        Assert.Single(service.GetSnapshot().Visible);
    }

    [Fact]
    public async Task RegisteredHost_ReceivesBackgroundUpdatesOnItsDispatcher()
    {
        using AppFeedbackService service = new();
        object owner = new();
        TaskCompletionSource<(Dispatcher Dispatcher, IDisposable Registration, int ThreadId)> registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<int> deliveredThread = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            int threadId = Environment.CurrentManagedThreadId;
            IDisposable registration = service.RegisterHost(owner, dispatcher, snapshot =>
            {
                if (snapshot.Visible.Count > 0)
                {
                    deliveredThread.TrySetResult(Environment.CurrentManagedThreadId);
                }
            });
            registered.SetResult((dispatcher, registration, threadId));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        (Dispatcher dispatcher, IDisposable registration, int threadId) = await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Run(() => service.Success("后台操作完成", owner: owner));
            int callbackThread = await deliveredThread.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(threadId, callbackThread);
        }
        finally
        {
            registration.Dispose();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UnregisteredHost_DropsQueuedBackgroundSnapshot()
    {
        using AppFeedbackService service = new();
        object owner = new();
        using ManualResetEventSlim dispatcherBlocked = new();
        using ManualResetEventSlim releaseDispatcher = new();
        TaskCompletionSource<(Dispatcher Dispatcher, IDisposable Registration)> registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int nonEmptyDeliveryCount = 0;
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            IDisposable registration = service.RegisterHost(owner, dispatcher, snapshot =>
            {
                if (snapshot.Visible.Count > 0)
                {
                    Interlocked.Increment(ref nonEmptyDeliveryCount);
                }
            });
            dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                dispatcherBlocked.Set();
                releaseDispatcher.Wait(TimeSpan.FromSeconds(5));
            }));
            registered.SetResult((dispatcher, registration));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        (Dispatcher dispatcher, IDisposable registration) = await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(dispatcherBlocked.Wait(TimeSpan.FromSeconds(5)));
            await Task.Run(() => service.Success("后台操作完成", owner: owner));
            registration.Dispose();
            releaseDispatcher.Set();
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, Volatile.Read(ref nonEmptyDeliveryCount));
        }
        finally
        {
            releaseDispatcher.Set();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void TaskFeedback_UpdatesInPlaceAndCompletesWithTimedSuccess()
    {
        using AppFeedbackService service = new();

        Guid id = service.TaskFeedback("正在导出", key: "export", progress: 0.2d);
        service.UpdateTask("export", "正在导出", "已完成一半", 0.5d);
        bool completed = service.CompleteTask("export", "导出完成");

        AppFeedbackNotification notification = Assert.Single(service.GetSnapshot().History);
        Assert.True(completed);
        Assert.Equal(id, notification.Id);
        Assert.Equal(AppFeedbackKind.Success, notification.Kind);
        Assert.True(notification.IsTaskCompleted);
        Assert.False(notification.IsPersistent);
        Assert.Equal(1d, notification.Progress);
    }

    [Fact]
    public void NotificationHost_IsTransparentOutsideCardsAndUsesLocalUiXPresentation()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Controls", "UiXNotificationHost.xaml"));
        XElement rootGrid = document.Descendants().First(element => element.Name.LocalName == "Grid");
        XElement card = document.Descendants().Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "NotificationCard");
        XElement items = document.Descendants().Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "NotificationItems");

        Assert.Equal("{x:Null}", (string?)rootGrid.Attribute("Background"));
        Assert.Equal("520", (string?)items.Attribute("MaxWidth"));
        Assert.Equal("{DynamicResource UiXDialogElevatedBrush}", (string?)card.Attribute("Background"));
        Assert.Equal("1", (string?)card.Attribute("BorderThickness"));
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName.Contains("Shadow", StringComparison.OrdinalIgnoreCase));
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Controls", "UiXNotificationHost.xaml.cs"));
        Assert.Contains("HitTestCore", source, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.ClientAreaAnimation", source, StringComparison.Ordinal);
        Assert.Contains("StopNotificationCardAnimations", source, StringComparison.Ordinal);
        Assert.Contains("CreateGestureFallbackTimer", source, StringComparison.Ordinal);
        Assert.Contains("TryFinalizeNotificationGesture", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationHost_BlankSurfaceDoesNotInterceptPointerHitTests()
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                Emerde.Controls.UiXNotificationHost host = new()
                {
                    Width = 800,
                    Height = 600,
                };
                host.Measure(new Size(800, 600));
                host.Arrange(new Rect(0, 0, 800, 600));

                Assert.Null(VisualTreeHelper.HitTest(host, new Point(20, 500)));
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void NotificationHost_CardRemainsPointerInteractive()
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                Emerde.Controls.UiXNotificationHost host = new()
                {
                    Width = 800,
                    Height = 600,
                };
                host.VisibleNotifications.Add(new AppFeedbackNotification(
                    Guid.NewGuid(),
                    null,
                    AppFeedbackKind.Information,
                    "测试通知",
                    "通知内容",
                    null,
                    null,
                    false,
                    false,
                    false,
                    1,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));
                host.Measure(new Size(800, 600));
                host.Arrange(new Rect(0, 0, 800, 600));
                host.UpdateLayout();

                Assert.NotNull(VisualTreeHelper.HitTest(host, new Point(400, 40)));
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}

[CollectionDefinition("WpfUi", DisableParallelization = true)]
public sealed class WpfUiTestCollection;
