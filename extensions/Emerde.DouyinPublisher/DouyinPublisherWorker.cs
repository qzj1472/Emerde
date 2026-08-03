using Emerde.Plugins;

namespace Emerde.DouyinPublisher;

internal sealed class DouyinPublisherWorker : IAsyncDisposable
{
    private readonly IExtensionContext context;
    private readonly PublisherStateStore stateStore;
    private readonly DouyinPublisherBrowser browser;
    private readonly PublisherOptions options;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private Task? workerTask;
    private bool disposed;

    public PublisherSessionState SessionState => browser.SessionState;

    public string SessionMessage => browser.SessionMessage;

    public string ActivityText => browser.ActivityText;

    public int? UploadProgress => browser.UploadProgress;

    public event EventHandler? SessionStateChanged
    {
        add => browser.SessionStateChanged += value;
        remove => browser.SessionStateChanged -= value;
    }

    public event EventHandler? ProgressChanged
    {
        add => browser.ProgressChanged += value;
        remove => browser.ProgressChanged -= value;
    }

    public DouyinPublisherWorker(
        IExtensionContext context,
        PublisherStateStore stateStore,
        IExtensionPlatformCookieProvider? cookieProvider,
        PublisherOptions options)
    {
        this.context = context;
        this.stateStore = stateStore;
        this.options = options;
        browser = new DouyinPublisherBrowser(
            context.DataDirectory,
            () => cookieProvider?.GetCookie("Douyin") ?? string.Empty,
            context.Log);
        stateStore.Changed += StateStoreChanged;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        workerTask ??= Task.Run(() => RunAsync(lifetimeCancellation.Token));
    }

    public Task OpenBrowserAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return browser.OpenAsync(cancellationToken);
    }

    public Task CheckSessionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return browser.CheckSessionAsync(cancellationToken);
    }

    public async Task<int> ResumeBlockedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int resumed = await stateStore.ResumeBlockedAsync(cancellationToken);
        Wake();
        return resumed;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        stateStore.Changed -= StateStoreChanged;
        lifetimeCancellation.Cancel();
        Wake();
        if (workerTask != null)
        {
            try
            {
                await workerTask;
            }
            catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
            {
            }
        }
        await browser.DisposeAsync();
        signal.Dispose();
        lifetimeCancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await stateStore.RestoreInterruptedAsync(cancellationToken);
        bool sessionChecked = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                PublisherQueueItem? item = await stateStore.TryStartNextAsync(DateTimeOffset.UtcNow, cancellationToken);
                if (item == null)
                {
                    if (!sessionChecked)
                    {
                        await browser.CheckSessionAsync(cancellationToken);
                        sessionChecked = true;
                        continue;
                    }
                    await WaitForWorkAsync(cancellationToken);
                    continue;
                }
                await ProcessAsync(item, cancellationToken);
                sessionChecked = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                context.Log("error", "publish_worker_failed", exception.Message, new
                {
                    type = exception.GetType().FullName,
                });
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task ProcessAsync(PublisherQueueItem item, CancellationToken cancellationToken)
    {
        context.Log("info", "publish_started", "douyin publish task started", new
        {
            item.EventId,
            item.FilePath,
            item.Attempts,
            item.Source,
        });
        PublisherBrowserResult result = await browser.PublishAsync(
            item,
            options,
            token => stateStore.MarkUploadingAsync(item.EventId, token),
            cancellationToken);
        switch (result.Outcome)
        {
            case PublisherBrowserOutcome.Published:
                await stateStore.MarkPublishedAsync(item.EventId, result.PublishedUrl, cancellationToken);
                context.Log("info", "publish_succeeded", "douyin publish task succeeded", new
                {
                    item.EventId,
                    item.FilePath,
                    result.PublishedUrl,
                });
                break;
            case PublisherBrowserOutcome.WaitingLogin:
                await stateStore.MarkWaitingAsync(item.EventId, PublisherQueueStatus.WaitingLogin, result.Message, cancellationToken);
                context.Log("warn", "publish_waiting_login", result.Message, new { item.EventId, item.FilePath });
                break;
            case PublisherBrowserOutcome.WaitingUser:
                await stateStore.MarkWaitingAsync(item.EventId, PublisherQueueStatus.WaitingUser, result.Message, cancellationToken);
                context.Log("warn", "publish_waiting_user", result.Message, new { item.EventId, item.FilePath });
                break;
            case PublisherBrowserOutcome.PermanentFailure:
                await stateStore.MarkFailedAsync(item.EventId, result.Message, cancellationToken);
                context.Log("error", "publish_failed", result.Message, new { item.EventId, item.FilePath, permanent = true });
                break;
            default:
                await stateStore.MarkRetryAsync(item.EventId, result.Message, options.MaximumAttempts, DateTimeOffset.UtcNow, cancellationToken);
                context.Log("warn", "publish_retry_scheduled", result.Message, new
                {
                    item.EventId,
                    item.FilePath,
                    item.Attempts,
                    maximumAttempts = options.MaximumAttempts,
                });
                break;
        }
    }

    private async Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay = stateStore.GetNextWakeDelay(DateTimeOffset.UtcNow);
        delay = delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
        try
        {
            await signal.WaitAsync(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private void StateStoreChanged(object? sender, EventArgs e)
    {
        Wake();
    }

    private void Wake()
    {
        if (disposed || signal.CurrentCount != 0)
        {
            return;
        }
        try
        {
            signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
