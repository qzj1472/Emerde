using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;

namespace Emerde.Core;

internal readonly record struct DouyinWebViewSnapshot(string? WebEnterJson, string? ReflowJson, string? Html);

internal static class DouyinWebViewResolver
{
    private static readonly SemaphoreSlim ResolveGate = new(1, 1);
    private static Window? hostWindow;
    private static WebView2? browser;
    private static string browserProxyKey = string.Empty;
    private static TaskCompletionSource? interactiveClosed;
    private static bool allowClose;

    public static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    public static DouyinWebViewSnapshot Resolve(string roomUrl, string cookie, bool allowInteractiveVerification)
    {
        Application? application = Application.Current;
        if (application == null || application.Dispatcher.HasShutdownStarted)
        {
            return default;
        }

        try
        {
            return ResolveAsync(application, roomUrl, cookie, allowInteractiveVerification).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            AppSessionLogger.Event("warn", "resolver", "douyin_webview_failed", e.Message, new
            {
                roomUrl,
                type = e.GetType().Name,
            });
            return default;
        }
    }

    public static void Shutdown()
    {
        allowClose = true;
        interactiveClosed?.TrySetResult();
        browser?.Dispose();
        browser = null;
        hostWindow?.Close();
        hostWindow = null;
    }

    private static async Task<DouyinWebViewSnapshot> ResolveAsync(
        Application application,
        string roomUrl,
        string cookie,
        bool allowInteractiveVerification)
    {
        await ResolveGate.WaitAsync();
        try
        {
            return await application.Dispatcher
                .InvokeAsync(() => ResolveOnUiThreadAsync(roomUrl, cookie, allowInteractiveVerification))
                .Task
                .Unwrap();
        }
        finally
        {
            ResolveGate.Release();
        }
    }

    private static async Task<DouyinWebViewSnapshot> ResolveOnUiThreadAsync(
        string roomUrl,
        string cookie,
        bool allowInteractiveVerification)
    {
        WebView2 webView = await EnsureBrowserAsync();
        await ApplyCookiesAsync(webView.CoreWebView2.CookieManager, cookie);
        DouyinWebViewSnapshot snapshot = await NavigateAndCaptureAsync(webView, roomUrl, TimeSpan.FromSeconds(12));
        if (allowInteractiveVerification && StreamResolver.ContainsDouyinChallenge(snapshot.Html))
        {
            ShowInteractiveWindow();
            snapshot = await WaitForInteractiveVerificationAsync(webView, roomUrl, snapshot);
        }
        webView.CoreWebView2.Stop();
        webView.NavigateToString("<html></html>");
        HideWindow();
        return snapshot;
    }

    private static async Task<WebView2> EnsureBrowserAsync()
    {
        string proxyKey = GetProxyKey();
        if (browser != null && string.Equals(browserProxyKey, proxyKey, StringComparison.OrdinalIgnoreCase))
        {
            ShowHiddenWindow();
            return browser;
        }

        DisposeBrowser();
        Directory.CreateDirectory(AppPaths.DouyinWebViewDataDirectory);
        CoreWebView2EnvironmentOptions options = new(GetBrowserArguments(proxyKey));
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
            null,
            AppPaths.DouyinWebViewDataDirectory,
            options);
        WebView2 createdBrowser = new();
        Window createdWindow = new()
        {
            Content = createdBrowser,
            Width = 900,
            Height = 700,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ResizeMode = ResizeMode.CanResize,
            Title = "抖音验证 - Emerde",
        };
        createdWindow.Closing += OnHostWindowClosing;
        hostWindow = createdWindow;
        browser = createdBrowser;
        browserProxyKey = proxyKey;
        ShowHiddenWindow();
        await createdBrowser.EnsureCoreWebView2Async(environment);
        ApplyProxyCredentials(createdBrowser.CoreWebView2, proxyKey);
        createdBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        createdBrowser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        createdBrowser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        return createdBrowser;
    }

    private static async Task<DouyinWebViewSnapshot> NavigateAndCaptureAsync(
        WebView2 webView,
        string roomUrl,
        TimeSpan timeout)
    {
        TaskCompletionSource navigationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource roomDataReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> webEnterResponses = [];
        List<string> reflowResponses = [];
        object responseSync = new();

        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            navigationCompleted.TrySetResult();
        }

        async void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
        {
            bool isWebEnter = args.Request.Uri.Contains("/webcast/room/web/enter/", StringComparison.OrdinalIgnoreCase);
            bool isReflow = args.Request.Uri.Contains("/webcast/room/reflow/info", StringComparison.OrdinalIgnoreCase)
                || args.Request.Uri.Contains("/webcast/reflow/", StringComparison.OrdinalIgnoreCase);
            if (!isWebEnter && !isReflow)
            {
                return;
            }
            try
            {
                using Stream content = await args.Response.GetContentAsync();
                using StreamReader reader = new(content);
                string response = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(response))
                {
                    lock (responseSync)
                    {
                        (isWebEnter ? webEnterResponses : reflowResponses).Add(response);
                    }
                    roomDataReceived.TrySetResult();
                }
            }
            catch
            {
            }
        }

        webView.NavigationCompleted += OnNavigationCompleted;
        webView.CoreWebView2.WebResourceResponseReceived += OnResponseReceived;
        try
        {
            webView.CoreWebView2.Navigate(roomUrl);
            Task timeoutTask = Task.Delay(timeout);
            await Task.WhenAny(navigationCompleted.Task, timeoutTask);
            await Task.WhenAny(roomDataReceived.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            await Task.Delay(300);
            string? html = await GetPageHtmlAsync(webView);
            lock (responseSync)
            {
                string? webEnterJson = webEnterResponses.OrderByDescending(response => response.Length).FirstOrDefault();
                string? reflowJson = reflowResponses.OrderByDescending(response => response.Length).FirstOrDefault();
                return new DouyinWebViewSnapshot(webEnterJson, reflowJson, html);
            }
        }
        finally
        {
            webView.NavigationCompleted -= OnNavigationCompleted;
            webView.CoreWebView2.WebResourceResponseReceived -= OnResponseReceived;
        }
    }

    private static async Task<DouyinWebViewSnapshot> WaitForInteractiveVerificationAsync(
        WebView2 webView,
        string roomUrl,
        DouyinWebViewSnapshot initialSnapshot)
    {
        interactiveClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        DouyinWebViewSnapshot snapshot = initialSnapshot;
        while (DateTime.UtcNow < deadline && !interactiveClosed.Task.IsCompleted)
        {
            await Task.WhenAny(Task.Delay(1000), interactiveClosed.Task);
            string? html = await GetPageHtmlAsync(webView);
            if (!StreamResolver.ContainsDouyinChallenge(html))
            {
                snapshot = await NavigateAndCaptureAsync(webView, roomUrl, TimeSpan.FromSeconds(8));
                break;
            }
        }
        interactiveClosed = null;
        return snapshot;
    }

    private static async Task<string?> GetPageHtmlAsync(WebView2 webView)
    {
        try
        {
            string json = await webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            return JsonConvert.DeserializeObject<string>(json);
        }
        catch
        {
            return null;
        }
    }

    private static Task ApplyCookiesAsync(CoreWebView2CookieManager cookieManager, string cookie)
    {
        foreach (string segment in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            string name = segment[..separator].Trim();
            string value = segment[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            CoreWebView2Cookie webViewCookie = cookieManager.CreateCookie(name, value, ".douyin.com", "/");
            cookieManager.AddOrUpdateCookie(webViewCookie);
        }
        return Task.CompletedTask;
    }

    private static string GetProxyKey()
    {
        return Configurations.IsUseProxy.Get()
            ? ProxyAddress.Normalize(Configurations.ProxyUrl.Get())
            : string.Empty;
    }

    private static string GetBrowserArguments(string proxyKey)
    {
        if (!Uri.TryCreate(proxyKey, UriKind.Absolute, out Uri? proxyUri))
        {
            return string.Empty;
        }
        UriBuilder builder = new(proxyUri)
        {
            UserName = string.Empty,
            Password = string.Empty,
        };
        return $"--proxy-server={builder.Uri.GetLeftPart(UriPartial.Authority)}";
    }

    private static void ApplyProxyCredentials(CoreWebView2 coreWebView, string proxyKey)
    {
        if (!Uri.TryCreate(proxyKey, UriKind.Absolute, out Uri? proxyUri)
            || string.IsNullOrWhiteSpace(proxyUri.UserInfo))
        {
            return;
        }
        string[] credentials = proxyUri.UserInfo.Split(':', 2);
        coreWebView.BasicAuthenticationRequested += (_, args) =>
        {
            args.Response.UserName = Uri.UnescapeDataString(credentials[0]);
            args.Response.Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty;
        };
    }

    private static void ShowHiddenWindow()
    {
        if (hostWindow == null)
        {
            return;
        }
        hostWindow.ShowInTaskbar = false;
        hostWindow.ShowActivated = false;
        hostWindow.Left = -10000;
        hostWindow.Top = -10000;
        if (!hostWindow.IsVisible)
        {
            hostWindow.Show();
        }
    }

    private static void ShowInteractiveWindow()
    {
        if (hostWindow == null)
        {
            return;
        }
        hostWindow.ShowInTaskbar = true;
        hostWindow.ShowActivated = true;
        hostWindow.Left = SystemParameters.WorkArea.Left + Math.Max(0, (SystemParameters.WorkArea.Width - hostWindow.Width) / 2);
        hostWindow.Top = SystemParameters.WorkArea.Top + Math.Max(0, (SystemParameters.WorkArea.Height - hostWindow.Height) / 2);
        if (!hostWindow.IsVisible)
        {
            hostWindow.Show();
        }
        hostWindow.Activate();
    }

    private static void HideWindow()
    {
        if (hostWindow == null)
        {
            return;
        }
        hostWindow.Hide();
        hostWindow.ShowInTaskbar = false;
        hostWindow.ShowActivated = false;
        hostWindow.Left = -10000;
        hostWindow.Top = -10000;
    }

    private static void OnHostWindowClosing(object? sender, CancelEventArgs args)
    {
        if (allowClose)
        {
            return;
        }
        args.Cancel = true;
        interactiveClosed?.TrySetResult();
        HideWindow();
    }

    private static void DisposeBrowser()
    {
        browser?.Dispose();
        browser = null;
        if (hostWindow != null)
        {
            allowClose = true;
            hostWindow.Close();
            allowClose = false;
            hostWindow = null;
        }
    }
}
