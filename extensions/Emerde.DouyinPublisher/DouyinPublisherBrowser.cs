using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Emerde.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using FluentTitleBar = Wpf.Ui.Controls.TitleBar;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;
using WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference;

namespace Emerde.DouyinPublisher;

internal sealed class DouyinPublisherBrowser : IAsyncDisposable
{
    private const string UploadUrl = "https://creator.douyin.com/creator-micro/content/upload";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string dataDirectory;
    private readonly Func<string> getCookie;
    private readonly Action<string, string, string, object?> log;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private Window? window;
    private WebView2? webView;
    private Task? initializationTask;
    private string activeEventId = string.Empty;
    private string lastAppliedCookieHeader = string.Empty;
    private bool allowClose;

    public PublisherSessionState SessionState { get; private set; } = PublisherSessionState.Unknown;

    public string SessionMessage { get; private set; } = "尚未检查登录状态";

    public string ActivityText { get; private set; } = string.Empty;

    public int? UploadProgress { get; private set; }

    public event EventHandler? SessionStateChanged;

    public event EventHandler? ProgressChanged;

    public DouyinPublisherBrowser(
        string dataDirectory,
        Func<string> getCookie,
        Action<string, string, string, object?> log)
    {
        this.dataDirectory = Path.Combine(dataDirectory, "webview2");
        this.getCookie = getCookie;
        this.log = log;
    }

    public async Task<PublisherBrowserResult> PublishAsync(
        PublisherQueueItem item,
        PublisherOptions options,
        Func<CancellationToken, Task> uploadStarted,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await InvokeAsync(() => PublishOnUiThreadAsync(item, options, uploadStarted, cancellationToken));
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync(async () =>
        {
            await EnsureInitializedAsync(cancellationToken);
            ShowInteractive();
            if (webView?.CoreWebView2 != null && IsBlank(webView.Source))
            {
                await NavigateAsync(UploadUrl, TimeSpan.FromSeconds(30), cancellationToken);
            }
        });
    }

    public async Task CheckSessionAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            await InvokeAsync(async () =>
            {
                SetSessionState(PublisherSessionState.Checking, "正在检查抖音登录状态");
                try
                {
                    await EnsureInitializedAsync(cancellationToken);
                    await ApplyCookiesAsync();
                    PageState page = await ReadPageStateAsync();
                    if (!IsPublisherPage(page.Url))
                    {
                        await NavigateAsync(UploadUrl, TimeSpan.FromSeconds(30), cancellationToken);
                        await Task.Delay(500, cancellationToken);
                        await ReadPageStateAsync();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    SetSessionState(PublisherSessionState.Error, $"登录状态检查失败：{exception.Message}");
                    log("warn", "publisher_session_check_failed", exception.Message, new
                    {
                        type = exception.GetType().FullName,
                    });
                }
            });
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await operationGate.WaitAsync();
        try
        {
            await InvokeAsync(() =>
            {
                if (webView?.CoreWebView2 != null)
                {
                    webView.CoreWebView2.NewWindowRequested -= NewWindowRequested;
                }
                if (webView != null)
                {
                    webView.NavigationCompleted -= BrowserNavigationCompleted;
                }
                webView?.Dispose();
                webView = null;
                if (window != null)
                {
                    allowClose = true;
                    window.Close();
                    window = null;
                }
                return Task.CompletedTask;
            });
        }
        finally
        {
            operationGate.Release();
            operationGate.Dispose();
        }
    }

    private async Task<PublisherBrowserResult> PublishOnUiThreadAsync(
        PublisherQueueItem item,
        PublisherOptions options,
        Func<CancellationToken, Task> uploadStarted,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            SetStatus($"正在准备：{Path.GetFileName(item.FilePath)}");
            await ApplyCookiesAsync();

            PageState current = await ReadPageStateAsync();
            if (string.Equals(activeEventId, item.EventId, StringComparison.OrdinalIgnoreCase))
            {
                PublisherBrowserResult? existingResult = ResolveTerminalPage(current);
                if (existingResult != null)
                {
                    return existingResult;
                }
            }

            if (!File.Exists(item.FilePath))
            {
                return PublisherBrowserResult.PermanentFailure("视频文件不存在");
            }

            if (!IsPublisherPage(current.Url) || !string.Equals(activeEventId, item.EventId, StringComparison.OrdinalIgnoreCase))
            {
                await NavigateAsync(UploadUrl, TimeSpan.FromSeconds(30), cancellationToken);
                await Task.Delay(800, cancellationToken);
                current = await ReadPageStateAsync();
            }

            if (current.LoginRequired)
            {
                SetStatus("需要登录抖音创作者中心");
                return PublisherBrowserResult.WaitingLogin("抖音登录已失效，请打开投稿浏览器完成登录");
            }
            if (current.VerificationRequired)
            {
                SetStatus("需要完成抖音安全验证");
                return PublisherBrowserResult.WaitingUser("抖音要求安全验证，请打开投稿浏览器处理");
            }

            bool resumingActiveUpload = string.Equals(activeEventId, item.EventId, StringComparison.OrdinalIgnoreCase);
            if (!resumingActiveUpload)
            {
                await SetFileInputAsync(item.FilePath, cancellationToken);
                activeEventId = item.EventId;
            }
            await uploadStarted(cancellationToken);
            SetStatus(resumingActiveUpload
                ? $"正在恢复：{Path.GetFileName(item.FilePath)}"
                : $"正在上传：{Path.GetFileName(item.FilePath)}");

            PublisherBrowserResult? uploadResult = current.UploadReady
                ? null
                : await WaitForUploadAsync(cancellationToken);
            if (uploadResult != null)
            {
                return uploadResult;
            }

            PublisherTaskOptions taskOptions = item.TaskOptions ?? options.CreateAutomaticTaskOptions(DateTimeOffset.Now);
            string titleTemplate = taskOptions.TitleTemplate;
            string descriptionTemplate = taskOptions.DescriptionTemplate;
            string topics = taskOptions.Topics;
            string title = PublisherTextFormatter.BuildTitle(titleTemplate, item);
            string description = PublisherTextFormatter.BuildDescription(descriptionTemplate, topics, item);
            PublishFormApplyResult formResult = await FillPublishFormAsync(title, description, taskOptions, cancellationToken);
            if (formResult.Missing.Count > 0)
            {
                string missing = string.Join("、", formResult.Missing);
                SetStatus($"等待确认投稿选项：{missing}");
                return PublisherBrowserResult.WaitingUser($"以下投稿选项需要在抖音页面确认：{missing}");
            }

            bool shouldConfirm = item.Source != "manual" && (!options.AutoPublish || options.ConfirmBeforePublish);
            if (shouldConfirm)
            {
                SetStatus("作品信息已填写，等待确认发布");
                return PublisherBrowserResult.WaitingUser("视频已经上传并填写完成，请打开投稿浏览器确认发布");
            }

            bool clicked = await ClickPublishAsync();
            if (!clicked)
            {
                return PublisherBrowserResult.RetryableFailure("没有找到可用的发布按钮");
            }
            SetStatus("正在确认发布结果");
            return await WaitForPublishResultAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return PublisherBrowserResult.PermanentFailure("未安装 Microsoft Edge WebView2 Runtime");
        }
        catch (PublisherLoginRequiredException)
        {
            return PublisherBrowserResult.WaitingLogin("抖音登录已失效，请打开投稿浏览器完成登录");
        }
        catch (Exception exception)
        {
            log("warn", "publish_browser_failed", exception.Message, new
            {
                type = exception.GetType().FullName,
                item.EventId,
                item.FilePath,
            });
            return PublisherBrowserResult.RetryableFailure(exception.Message);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (webView?.CoreWebView2 != null)
        {
            ShowHiddenIfNeeded();
            return;
        }

        initializationTask ??= InitializeBrowserAsync(cancellationToken);
        try
        {
            await initializationTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (initializationTask.IsCompleted)
            {
                initializationTask = null;
            }
            throw;
        }
    }

    private async Task InitializeBrowserAsync(CancellationToken cancellationToken)
    {
        if (webView?.CoreWebView2 != null)
        {
            ShowHiddenIfNeeded();
            return;
        }

        Directory.CreateDirectory(dataDirectory);
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, dataDirectory);
        WebView2 browser = new()
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 248, 248, 248),
        };
        Grid root = new()
        {
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        root.SetResourceReference(Panel.BackgroundProperty, "EmerdeShellBackgroundBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        root.RowDefinitions.Add(new RowDefinition());
        const string windowTitle = "\u6296\u97f3\u6295\u7a3f - Emerde";
        FluentTitleBar titleBar = new()
        {
            Title = windowTitle,
            Height = 36,
            ShowMaximize = true,
        };
        root.Children.Add(titleBar);
        Grid.SetRow(browser, 1);
        root.Children.Add(browser);
        FluentWindow browserWindow = new()
        {
            Title = windowTitle,
            Content = root,
            Width = 1120,
            Height = 780,
            MinWidth = 860,
            MinHeight = 600,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ResizeMode = ResizeMode.CanResize,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ExtendsContentIntoTitleBar = true,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            WindowBackdropType = WindowBackdropType.Mica,
            WindowCornerPreference = WindowCornerPreference.Round,
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
        };
        WindowAppearance.EnableBorderless(browserWindow);
        browserWindow.Closing += WindowClosing;
        window = browserWindow;
        webView = browser;
        ShowHiddenIfNeeded();
        try
        {
            await browser.EnsureCoreWebView2Async(environment).WaitAsync(cancellationToken);
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
            browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
            browser.CoreWebView2.NewWindowRequested += NewWindowRequested;
            browser.NavigationCompleted += BrowserNavigationCompleted;
            browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
        }
        catch
        {
            browser.Dispose();
            webView = null;
            allowClose = true;
            browserWindow.Close();
            allowClose = false;
            window = null;
            throw;
        }
    }

    private async Task ApplyCookiesAsync()
    {
        if (webView?.CoreWebView2 == null)
        {
            return;
        }
        string cookieHeader = getCookie();
        if (string.IsNullOrWhiteSpace(cookieHeader)
            || string.Equals(cookieHeader, lastAppliedCookieHeader, StringComparison.Ordinal))
        {
            return;
        }
        foreach (string segment in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            string name = segment[..separator].Trim();
            string value = segment[(separator + 1)..].Trim();
            if (name.Length == 0)
            {
                continue;
            }
            CoreWebView2Cookie cookie = webView.CoreWebView2.CookieManager.CreateCookie(name, value, ".douyin.com", "/");
            webView.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
        }
        lastAppliedCookieHeader = cookieHeader;
        await Task.CompletedTask;
    }

    private async Task NavigateAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (webView?.CoreWebView2 == null)
        {
            throw new InvalidOperationException("投稿浏览器尚未初始化");
        }
        TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            completed.TrySetResult(args);
        }
        webView.NavigationCompleted += NavigationCompleted;
        try
        {
            webView.CoreWebView2.Navigate(url);
            CoreWebView2NavigationCompletedEventArgs result = await completed.Task.WaitAsync(timeout, cancellationToken);
            if (!result.IsSuccess && result.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            {
                throw new InvalidOperationException($"抖音页面加载失败：{result.WebErrorStatus}");
            }
        }
        finally
        {
            webView.NavigationCompleted -= NavigationCompleted;
        }
    }

    private async Task SetFileInputAsync(string filePath, CancellationToken cancellationToken)
    {
        if (webView?.CoreWebView2 == null)
        {
            throw new InvalidOperationException("投稿浏览器尚未初始化");
        }
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string documentJson = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "DOM.getDocument",
                "{\"depth\":-1,\"pierce\":true}");
            using JsonDocument document = JsonDocument.Parse(documentJson);
            int rootNodeId = document.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
            string queryJson = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "DOM.querySelector",
                JsonSerializer.Serialize(new { nodeId = rootNodeId, selector = "input[type=file]" }));
            using JsonDocument query = JsonDocument.Parse(queryJson);
            int nodeId = query.RootElement.GetProperty("nodeId").GetInt32();
            if (nodeId > 0)
            {
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "DOM.setFileInputFiles",
                    JsonSerializer.Serialize(new { nodeId, files = new[] { Path.GetFullPath(filePath) } }));
                return;
            }
            PageState page = await ReadPageStateAsync();
            if (page.LoginRequired)
            {
                throw new PublisherLoginRequiredException();
            }
            await Task.Delay(500, cancellationToken);
        }
        throw new InvalidOperationException("抖音投稿页面没有找到视频上传入口");
    }

    private async Task<PublisherBrowserResult?> WaitForUploadAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddHours(12);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageState page = await ReadPageStateAsync();
            if (page.LoginRequired)
            {
                return PublisherBrowserResult.WaitingLogin("抖音登录已失效，请打开投稿浏览器完成登录");
            }
            if (page.VerificationRequired)
            {
                return PublisherBrowserResult.WaitingUser("抖音要求安全验证，请打开投稿浏览器处理");
            }
            if (page.UploadFailed)
            {
                return PublisherBrowserResult.RetryableFailure("抖音页面报告视频上传失败");
            }
            if (page.UploadReady)
            {
                return null;
            }
            if (page.UploadProgress.HasValue)
            {
                SetProgress($"视频上传中 · {page.UploadProgress.Value}%", page.UploadProgress);
            }
            await Task.Delay(1000, cancellationToken);
        }
        return PublisherBrowserResult.RetryableFailure("等待抖音上传完成超时");
    }

    private async Task<PublishFormApplyResult> FillPublishFormAsync(
        string title,
        string description,
        PublisherTaskOptions? options,
        CancellationToken cancellationToken)
    {
        if (webView?.CoreWebView2 == null)
        {
            throw new InvalidOperationException("投稿浏览器尚未初始化");
        }
        string script = $$"""
            (() => {
                const setValue = (element, value) => {
                    if (!element) return false;
                    const prototype = element instanceof HTMLInputElement ? HTMLInputElement.prototype : HTMLTextAreaElement.prototype;
                    const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set;
                    setter ? setter.call(element, value) : element.value = value;
                    element.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                    return true;
                };
                const titleInput = [...document.querySelectorAll('input')].find(x => (x.placeholder || '').includes('填写作品标题'));
                setValue(titleInput, {{JsonSerializer.Serialize(title)}});
                const editor = document.querySelector('div.zone-container[contenteditable="true"], div[contenteditable="true"]');
                if (editor && {{JsonSerializer.Serialize(description)}}) {
                    editor.focus();
                    editor.textContent = {{JsonSerializer.Serialize(description)}};
                    editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: {{JsonSerializer.Serialize(description)}} }));
                    editor.dispatchEvent(new Event('change', { bubbles: true }));
                }
                return { titleFilled: Boolean(titleInput), descriptionFilled: !{{JsonSerializer.Serialize(!string.IsNullOrWhiteSpace(description))}} || Boolean(editor) };
            })()
            """;
        string json = await webView.CoreWebView2.ExecuteScriptAsync(script);
        PublishFormCoreResult coreResult = JsonSerializer.Deserialize<PublishFormCoreResult>(json, JsonOptions) ?? new();
        if (!coreResult.TitleFilled)
        {
            throw new InvalidOperationException("抖音投稿页面没有找到作品标题输入框");
        }
        List<string> missing = [];
        if (!coreResult.DescriptionFilled)
        {
            missing.Add("作品简介");
        }
        if (options == null)
        {
            return new PublishFormApplyResult(missing);
        }
        if (!string.IsNullOrWhiteSpace(options.CoverPath)
            && !await ApplyCoverAsync(options.CoverPath, cancellationToken))
        {
            missing.Add("视频封面");
        }
        PublishFormApplyResult optionResult = await ApplyPublishOptionsAsync(options, cancellationToken);
        missing.AddRange(optionResult.Missing);
        return new PublishFormApplyResult(missing.Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<bool> ApplyCoverAsync(string coverPath, CancellationToken cancellationToken)
    {
        if (webView?.CoreWebView2 == null || !File.Exists(coverPath))
        {
            return false;
        }
        const string openScript = """
            (() => {
                const visible = element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                };
                const target = [...document.querySelectorAll('button, [role="button"], span, div')]
                    .filter(element => visible(element) && /上传封面|选择封面/.test((element.textContent || '').trim()))
                    .sort((left, right) => (left.textContent || '').length - (right.textContent || '').length)[0];
                (target?.closest('button, [role="button"]') || target)?.click();
                return Boolean(target);
            })()
            """;
        await webView.CoreWebView2.ExecuteScriptAsync(openScript);
        await Task.Delay(300, cancellationToken);
        bool selected = await TrySetFileInputAsync(
            coverPath,
            "input[type=file][accept*='image'],input[type=file][accept*='.jpg'],input[type=file][accept*='.png']",
            TimeSpan.FromSeconds(5),
            cancellationToken);
        if (!selected)
        {
            return false;
        }
        await Task.Delay(500, cancellationToken);
        const string confirmScript = """
            (() => {
                const visible = element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                };
                const button = [...document.querySelectorAll('[role="dialog"] button, [class*="modal"] button, [class*="dialog"] button')]
                    .filter(visible)
                    .find(element => /^(完成|确定|确认)$/.test((element.textContent || '').trim()) && !element.disabled);
                button?.click();
                return true;
            })()
            """;
        await webView.CoreWebView2.ExecuteScriptAsync(confirmScript);
        return true;
    }

    private async Task<bool> TrySetFileInputAsync(
        string filePath,
        string selector,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (webView?.CoreWebView2 == null)
        {
            return false;
        }
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string documentJson = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "DOM.getDocument",
                "{\"depth\":-1,\"pierce\":true}");
            using JsonDocument document = JsonDocument.Parse(documentJson);
            int rootNodeId = document.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
            string queryJson = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "DOM.querySelector",
                JsonSerializer.Serialize(new { nodeId = rootNodeId, selector }));
            using JsonDocument query = JsonDocument.Parse(queryJson);
            int nodeId = query.RootElement.GetProperty("nodeId").GetInt32();
            if (nodeId > 0)
            {
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "DOM.setFileInputFiles",
                    JsonSerializer.Serialize(new { nodeId, files = new[] { Path.GetFullPath(filePath) } }));
                return true;
            }
            await Task.Delay(250, cancellationToken);
        }
        return false;
    }

    private async Task<PublishFormApplyResult> ApplyPublishOptionsAsync(
        PublisherTaskOptions options,
        CancellationToken cancellationToken)
    {
        if (webView?.CoreWebView2 == null)
        {
            return new PublishFormApplyResult(["投稿设置"]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        string visibility = PublisherVisibility.ToDisplayText(options.Visibility);
        string savePermission = options.AllowSave ? "允许" : "不允许";
        string publishTime = options.ScheduledAt.HasValue ? "定时发布" : "立即发布";
        string scheduledDate = options.ScheduledAt?.LocalDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        string scheduledTime = options.ScheduledAt?.LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        string script = $$"""
            (async () => {
                const missing = [];
                const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
                const normalize = value => (value || '').replace(/\s+/g, ' ').trim();
                const visible = element => {
                    if (!element) return false;
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                };
                const clickable = element => element?.closest('button, label, [role="button"], [role="radio"], [role="option"], [role="checkbox"], [role="combobox"]') || element;
                const textElements = () => [...document.querySelectorAll('button, label, [role="button"], [role="radio"], [role="option"], [role="checkbox"], span, div')].filter(visible);
                const findText = text => textElements()
                    .filter(element => normalize(element.textContent) === text)
                    .sort((left, right) => left.children.length - right.children.length)[0];
                const findSection = label => {
                    const labelElement = findText(label);
                    if (!labelElement) return null;
                    let best = labelElement;
                    for (let node = labelElement; node && node !== document.body; node = node.parentElement) {
                        const text = normalize(node.innerText);
                        if (text.length > 600) break;
                        best = node;
                    }
                    return best;
                };
                const clickText = async (label, value) => {
                    if (!value) return true;
                    const candidates = textElements().filter(element => normalize(element.textContent) === value);
                    if (!candidates.length) return false;
                    const scored = candidates.map(element => {
                        let node = element;
                        let score = Number.MAX_SAFE_INTEGER;
                        for (let depth = 0; node && depth < 8; depth++, node = node.parentElement) {
                            const text = normalize(node.innerText);
                            if (text.includes(label)) score = Math.min(score, text.length + depth * 1000);
                        }
                        return { element, score };
                    }).sort((left, right) => left.score - right.score);
                    const target = clickable(scored[0].element);
                    target?.click();
                    await sleep(180);
                    return Boolean(target);
                };
                const findControl = label => {
                    const controls = [...document.querySelectorAll('input:not([type=file]), textarea, [contenteditable="true"], [role="combobox"]')].filter(visible);
                    const scored = [];
                    for (const control of controls) {
                        let node = control;
                        for (let depth = 0; node && depth < 8; depth++, node = node.parentElement) {
                            if (node === document.body || node === document.documentElement) break;
                            const text = normalize(node.innerText);
                            if (text.length <= 600 && text.includes(label)) {
                                scored.push({ control, score: text.length + depth * 1000 });
                                break;
                            }
                        }
                    }
                    return scored.sort((left, right) => left.score - right.score)[0]?.control || null;
                };
                const setValue = (element, value) => {
                    if (!element) return false;
                    element.focus();
                    if (element.isContentEditable) {
                        element.textContent = value;
                    } else {
                        const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
                        const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set;
                        setter ? setter.call(element, value) : element.value = value;
                    }
                    element.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                    element.dispatchEvent(new Event('blur', { bubbles: true }));
                    return true;
                };
                const selectNamedValue = async (label, value) => {
                    if (!value) return true;
                    const section = findSection(label);
                    if (section && normalize(section.innerText).includes(value)) return true;
                    const control = findControl(label);
                    if (control) {
                        clickable(control)?.click();
                        await sleep(180);
                        setValue(control, value);
                        await sleep(300);
                    } else {
                        const labelElement = findText(label);
                        clickable(labelElement)?.click();
                        await sleep(300);
                        const overlayControl = [...document.querySelectorAll('[role="dialog"] input, [role="dialog"] textarea, [class*="modal"] input, [class*="popover"] input')]
                            .filter(visible)[0];
                        if (overlayControl) {
                            setValue(overlayControl, value);
                            await sleep(300);
                        }
                    }
                    const option = findText(value);
                    if (option) {
                        clickable(option)?.click();
                        await sleep(180);
                        return true;
                    }
                    return Boolean(control && normalize(control.value || control.textContent).includes(value));
                };
                const apply = async (label, value, action) => {
                    if (!value) return;
                    if (!await action(label, value)) missing.push(label);
                };

                await apply('官方活动', {{JsonSerializer.Serialize(options.OfficialActivity)}}, clickText);
                await apply('添加合集', {{JsonSerializer.Serialize(options.CollectionName)}}, selectNamedValue);
                await apply('自主声明', {{JsonSerializer.Serialize(options.Declaration)}}, selectNamedValue);
                await apply('视频章节', {{JsonSerializer.Serialize(options.VideoChapters)}}, async (label, value) => setValue(findControl(label), value));
                await apply('添加标签', {{JsonSerializer.Serialize(options.Tags)}}, selectNamedValue);
                await apply('添加地点', {{JsonSerializer.Serialize(options.Location)}}, selectNamedValue);
                await apply('关联热点', {{JsonSerializer.Serialize(options.Hotspot)}}, selectNamedValue);
                if (!await clickText('谁可以看', {{JsonSerializer.Serialize(visibility)}})) missing.push('谁可以看');
                if (!await clickText('保存权限', {{JsonSerializer.Serialize(savePermission)}})) missing.push('保存权限');
                if (!await clickText('发布时间', {{JsonSerializer.Serialize(publishTime)}})) missing.push('发布时间');
                if ({{JsonSerializer.Serialize(options.ScheduledAt.HasValue)}}) {
                    await sleep(250);
                    const dateControl = [...document.querySelectorAll('input')].find(element => visible(element) && /日期|年月日/.test(element.placeholder || ''));
                    const timeControl = [...document.querySelectorAll('input')].find(element => visible(element) && /时间|时分/.test(element.placeholder || ''));
                    const combinedControl = [...document.querySelectorAll('input')].find(element => visible(element) && /发布时间/.test(element.placeholder || ''));
                    const dateApplied = dateControl ? setValue(dateControl, {{JsonSerializer.Serialize(scheduledDate)}}) : false;
                    const timeApplied = timeControl ? setValue(timeControl, {{JsonSerializer.Serialize(scheduledTime)}}) : false;
                    const combinedApplied = combinedControl ? setValue(combinedControl, {{JsonSerializer.Serialize(scheduledDate + " " + scheduledTime)}}) : false;
                    if (!(combinedApplied || dateApplied && timeApplied)) {
                        missing.push('定时发布时间');
                    }
                }
                return { missing: [...new Set(missing)] };
            })()
            """;
        string json = await webView.CoreWebView2.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<PublishFormApplyResult>(json, JsonOptions) ?? new PublishFormApplyResult(["投稿设置"]);
    }

    private async Task<bool> ClickPublishAsync()
    {
        if (webView?.CoreWebView2 == null)
        {
            return false;
        }
        const string script = """
            (() => {
                const visible = element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                };
                const button = [...document.querySelectorAll('button')]
                    .find(x => x.textContent.trim() === '发布' && !x.disabled && visible(x));
                if (!button) return false;
                button.click();
                return true;
            })()
            """;
        string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
        return bool.TryParse(result, out bool clicked) && clicked;
    }

    private async Task<PublisherBrowserResult> WaitForPublishResultAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageState page = await ReadPageStateAsync();
            PublisherBrowserResult? terminal = ResolveTerminalPage(page);
            if (terminal != null)
            {
                return terminal;
            }
            if (page.LoginRequired)
            {
                return PublisherBrowserResult.WaitingLogin("抖音登录已失效，请打开投稿浏览器完成登录");
            }
            if (page.VerificationRequired || page.UserActionRequired)
            {
                return PublisherBrowserResult.WaitingUser("抖音要求确认或安全验证，请打开投稿浏览器处理");
            }
            if (page.PublishFailed)
            {
                return PublisherBrowserResult.RetryableFailure("抖音页面报告作品发布失败");
            }
            await Task.Delay(1000, cancellationToken);
        }
        return PublisherBrowserResult.WaitingUser("发布结果尚未确认，请打开投稿浏览器查看");
    }

    private async Task<PageState> ReadPageStateAsync()
    {
        if (webView?.CoreWebView2 == null)
        {
            return new PageState();
        }
        const string script = """
            (() => {
                const text = (document.body?.innerText || '').slice(0, 30000);
                const inputs = [...document.querySelectorAll('input')];
                const placeholders = inputs.map(x => x.placeholder || '').join('|');
                const url = location.href;
                const visible = element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                };
                const dialogText = [...document.querySelectorAll('[role="dialog"], [class*="modal"], [class*="dialog"]')]
                    .filter(visible)
                    .map(x => x.innerText || '')
                    .join('|');
                const loginRequired = /扫码登录|验证码登录|密码登录|手机号登录/.test(text)
                    && !url.includes('/content/manage');
                const verificationRequired = /安全验证|请完成验证|拖动滑块|短信验证码|验证码已发送/.test(text)
                    || /验证码|短信|手机号/.test(placeholders) && !loginRequired;
                const uploadReady = text.includes('重新上传')
                    && inputs.some(x => (x.placeholder || '').includes('填写作品标题'));
                const uploadFailed = /上传失败|视频上传失败/.test(text);
                const percentages = [...text.matchAll(/(?:上传|处理中)[^%]{0,24}(\d{1,3})%/g)]
                    .map(match => Number(match[1]))
                    .filter(value => Number.isFinite(value) && value >= 0 && value <= 100);
                const uploadProgress = percentages.length > 0 ? Math.max(...percentages) : null;
                const publishFailed = /发布失败|投稿失败/.test(text);
                const published = url.includes('/creator-micro/content/manage')
                    || /作品发布成功|发布成功/.test(text);
                const userActionRequired = /内容声明|确认声明|实名认证|确认发布/.test(dialogText);
                const publishedLink = [...document.querySelectorAll('a[href]')]
                    .map(x => x.href)
                    .find(x => /douyin\.com\/video\//.test(x)) || '';
                return { url, loginRequired, verificationRequired, uploadReady, uploadFailed, uploadProgress, publishFailed, published, userActionRequired, publishedLink };
            })()
            """;
        string json = await webView.CoreWebView2.ExecuteScriptAsync(script);
        PageState page = JsonSerializer.Deserialize<PageState>(json, JsonOptions) ?? new PageState();
        UpdateSessionState(page);
        return page;
    }

    private PublisherBrowserResult? ResolveTerminalPage(PageState page)
    {
        if (!page.Published)
        {
            return null;
        }
        string publishedUrl = string.IsNullOrWhiteSpace(page.PublishedLink) ? page.Url : page.PublishedLink;
        activeEventId = string.Empty;
        SetStatus("投稿成功");
        return PublisherBrowserResult.Published(publishedUrl);
    }

    private static bool IsPublisherPage(string url)
    {
        return url.Contains("creator.douyin.com/creator-micro", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlank(Uri? uri)
    {
        return uri == null || uri.AbsoluteUri is "about:blank" or "";
    }

    private void SetStatus(string text)
    {
        SetProgress(text, null);
    }

    private void SetProgress(string text, int? progress)
    {
        if (string.Equals(ActivityText, text, StringComparison.Ordinal) && UploadProgress == progress)
        {
            return;
        }
        ActivityText = text;
        UploadProgress = progress;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSessionState(PageState page)
    {
        if (!IsPublisherPage(page.Url))
        {
            return;
        }
        if (page.LoginRequired)
        {
            SetSessionState(PublisherSessionState.LoginRequired, "未登录抖音创作者中心");
            return;
        }
        if (page.VerificationRequired)
        {
            SetSessionState(PublisherSessionState.VerificationRequired, "登录需要完成安全验证");
            return;
        }
        SetSessionState(PublisherSessionState.LoggedIn, "已登录抖音创作者中心");
    }

    private void SetSessionState(PublisherSessionState state, string message)
    {
        if (SessionState == state && string.Equals(SessionMessage, message, StringComparison.Ordinal))
        {
            return;
        }
        SessionState = state;
        SessionMessage = message;
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowHiddenIfNeeded()
    {
        if (window == null || window.IsVisible)
        {
            return;
        }
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Left = -10000;
        window.Top = -10000;
        window.Show();
    }

    private void ShowInteractive()
    {
        if (window == null)
        {
            return;
        }
        window.ShowInTaskbar = true;
        window.ShowActivated = true;
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Left = SystemParameters.WorkArea.Left + Math.Max(0, (SystemParameters.WorkArea.Width - window.Width) / 2);
        window.Top = SystemParameters.WorkArea.Top + Math.Max(0, (SystemParameters.WorkArea.Height - window.Height) / 2);
        if (!window.IsVisible)
        {
            window.Show();
        }
        window.Activate();
        webView?.Focus();
    }

    private void Hide()
    {
        if (window == null)
        {
            return;
        }
        window.Hide();
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowState = WindowState.Normal;
        window.Left = -10000;
        window.Top = -10000;
    }

    private void WindowClosing(object? sender, CancelEventArgs args)
    {
        if (allowClose)
        {
            return;
        }
        args.Cancel = true;
        _ = window?.Dispatcher.BeginInvoke((Action)Hide, DispatcherPriority.Background);
    }

    private void NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (webView?.CoreWebView2 != null
            && Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https")
        {
            webView.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
    }

    private async void BrowserNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            return;
        }
        try
        {
            await ReadPageStateAsync();
        }
        catch (Exception exception)
        {
            log("warn", "publisher_session_refresh_failed", exception.Message, new
            {
                type = exception.GetType().FullName,
            });
        }
    }

    private static Task InvokeAsync(Func<Task> action)
    {
        Application application = Application.Current
            ?? throw new InvalidOperationException("Emerde 应用程序尚未就绪");
        if (application.Dispatcher.CheckAccess())
        {
            return action();
        }
        return application.Dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private static Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        Application application = Application.Current
            ?? throw new InvalidOperationException("Emerde 应用程序尚未就绪");
        if (application.Dispatcher.CheckAccess())
        {
            return action();
        }
        return application.Dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private sealed class PageState
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("loginRequired")]
        public bool LoginRequired { get; set; }

        [JsonPropertyName("verificationRequired")]
        public bool VerificationRequired { get; set; }

        [JsonPropertyName("uploadReady")]
        public bool UploadReady { get; set; }

        [JsonPropertyName("uploadFailed")]
        public bool UploadFailed { get; set; }

        [JsonPropertyName("uploadProgress")]
        public int? UploadProgress { get; set; }

        [JsonPropertyName("publishFailed")]
        public bool PublishFailed { get; set; }

        [JsonPropertyName("published")]
        public bool Published { get; set; }

        [JsonPropertyName("userActionRequired")]
        public bool UserActionRequired { get; set; }

        [JsonPropertyName("publishedLink")]
        public string PublishedLink { get; set; } = string.Empty;
    }

    private sealed class PublisherLoginRequiredException : InvalidOperationException;

    private sealed class PublishFormCoreResult
    {
        [JsonPropertyName("titleFilled")]
        public bool TitleFilled { get; set; }

        [JsonPropertyName("descriptionFilled")]
        public bool DescriptionFilled { get; set; }
    }
}

internal sealed record PublishFormApplyResult(
    [property: JsonPropertyName("missing")] IReadOnlyList<string> Missing);

internal enum PublisherBrowserOutcome
{
    Published,
    WaitingLogin,
    WaitingUser,
    RetryableFailure,
    PermanentFailure,
}

internal enum PublisherSessionState
{
    Unknown,
    Checking,
    LoggedIn,
    LoginRequired,
    VerificationRequired,
    Error,
}

internal sealed record PublisherBrowserResult(
    PublisherBrowserOutcome Outcome,
    string Message,
    string PublishedUrl = "")
{
    public static PublisherBrowserResult Published(string url) => new(PublisherBrowserOutcome.Published, string.Empty, url);

    public static PublisherBrowserResult WaitingLogin(string message) => new(PublisherBrowserOutcome.WaitingLogin, message);

    public static PublisherBrowserResult WaitingUser(string message) => new(PublisherBrowserOutcome.WaitingUser, message);

    public static PublisherBrowserResult RetryableFailure(string message) => new(PublisherBrowserOutcome.RetryableFailure, message);

    public static PublisherBrowserResult PermanentFailure(string message) => new(PublisherBrowserOutcome.PermanentFailure, message);
}
