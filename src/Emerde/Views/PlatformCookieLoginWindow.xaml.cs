using System.ComponentModel;
using System.IO;
using System.Windows;
using Emerde.Core;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Controls;

namespace Emerde.Views;

public partial class PlatformCookieLoginWindow : FluentWindow
{
    private readonly PlatformCookieAcquisitionProfile profile;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly CancellationToken lifetimeToken;
    private bool isClosed;

    internal PlatformCookieLoginWindow(
        PlatformCookieAcquisitionProfile profile,
        string displayName,
        Window? owner)
    {
        this.profile = profile;
        lifetimeToken = lifetimeCancellation.Token;
        InitializeComponent();
        WindowAppearance.EnableBorderless(this);
        Owner = owner;
        string actionText = "AcquireCookie".Tr();
        Title = $"{displayName} - {actionText}";
        WindowTitleBar.Title = Title;
        Loaded += WindowLoaded;
        Closing += WindowClosing;
        Closed += WindowClosed;
        Browser.NavigationStarting += BrowserNavigationStarting;
        Browser.NavigationCompleted += BrowserNavigationCompleted;
    }

    public string? AcquiredCookieHeader { get; private set; }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WindowLoaded;
        try
        {
            if (!DouyinWebViewResolver.IsRuntimeAvailable())
            {
                StatusText.Text = "CookieLoginRuntimeMissing".Tr();
                return;
            }

            string webViewDataDirectory = AppPaths.GetPlatformLoginWebViewDataDirectory(profile.PlatformName);
            Directory.CreateDirectory(webViewDataDirectory);
            CoreWebView2EnvironmentOptions options = new(GetBrowserArguments());
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null,
                webViewDataDirectory,
                options).WaitAsync(lifetimeToken);
            await Browser.EnsureCoreWebView2Async(environment).WaitAsync(lifetimeToken);
            lifetimeToken.ThrowIfCancellationRequested();
            ApplyProxyCredentials(Browser.CoreWebView2);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            Browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            Browser.CoreWebView2.NewWindowRequested += BrowserNewWindowRequested;
            CompleteButton.IsEnabled = true;
            StatusText.Text = "CookieLoginInstruction".Tr();
            Browser.CoreWebView2.Navigate(profile.LoginUri.AbsoluteUri);
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = "CookieLoginOpenFailed".Tr(exception.Message);
            AppSessionLogger.Event("error", "settings", "platform_cookie_browser_failed", exception.Message, new
            {
                profile.PlatformName,
                type = exception.GetType().Name,
            });
        }
    }

    private async void CompleteButtonClick(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2 == null)
        {
            return;
        }

        CompleteButton.IsEnabled = false;
        StatusText.Text = "CookieLoginReading".Tr();
        try
        {
            List<PlatformBrowserCookie> cookies = [];
            foreach (Uri origin in profile.CookieOrigins)
            {
                IReadOnlyList<CoreWebView2Cookie> originCookies = await Browser.CoreWebView2.CookieManager
                    .GetCookiesAsync(origin.AbsoluteUri)
                    .WaitAsync(lifetimeToken);
                cookies.AddRange(originCookies.Select(cookie => new PlatformBrowserCookie(
                    cookie.Name,
                    cookie.Value,
                    cookie.Domain,
                    cookie.Path)));
            }

            lifetimeToken.ThrowIfCancellationRequested();

            string cookieHeader = PlatformCookieAcquisition.BuildCookieHeader(profile, cookies);
            if (string.IsNullOrWhiteSpace(cookieHeader)
                || !PlatformCookieAcquisition.HasAuthenticatedSession(profile, cookies))
            {
                StatusText.Text = "CookieLoginEmpty".Tr();
                AppSessionLogger.Event("warn", "settings", "platform_cookie_not_found", "no authenticated platform cookie was found", new
                {
                    profile.PlatformName,
                });
                return;
            }

            AcquiredCookieHeader = cookieHeader;
            AppSessionLogger.Event("info", "settings", "platform_cookie_acquired", "platform cookie was acquired", new
            {
                profile.PlatformName,
                cookieCount = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries).Length,
            });
            DialogResult = true;
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!isClosed)
            {
                StatusText.Text = "CookieLoginReadFailed".Tr(exception.Message);
            }
            AppSessionLogger.Event("error", "settings", "platform_cookie_read_failed", exception.Message, new
            {
                profile.PlatformName,
                type = exception.GetType().Name,
            });
        }
        finally
        {
            if (!isClosed)
            {
                CompleteButton.IsEnabled = true;
            }
        }
    }

    private void BackButtonClick(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoBack == true)
        {
            Browser.CoreWebView2.GoBack();
        }
    }

    private void RefreshButtonClick(object sender, RoutedEventArgs e)
    {
        Browser.CoreWebView2?.Reload();
    }

    private void CancelButtonClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BrowserNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        AddressText.Text = e.Uri;
        StatusText.Text = "CookieLoginLoading".Tr();
    }

    private void BrowserNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AddressText.Text = Browser.Source?.AbsoluteUri ?? profile.LoginUri.AbsoluteUri;
        BackButton.IsEnabled = Browser.CoreWebView2?.CanGoBack == true;
        StatusText.Text = e.IsSuccess
            ? "CookieLoginInstruction".Tr()
            : "CookieLoginNavigationFailed".Tr();
    }

    private void BrowserNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https")
        {
            Browser.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        isClosed = true;
        lifetimeCancellation.Cancel();
        Browser.NavigationStarting -= BrowserNavigationStarting;
        Browser.NavigationCompleted -= BrowserNavigationCompleted;
        if (Browser.CoreWebView2 != null)
        {
            Browser.CoreWebView2.NewWindowRequested -= BrowserNewWindowRequested;
        }
        Browser.Dispose();
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        Closed -= WindowClosed;
        lifetimeCancellation.Dispose();
    }

    private static string GetBrowserArguments()
    {
        if (!Configurations.IsUseProxy.Get()
            || !Uri.TryCreate(ProxyAddress.Normalize(Configurations.ProxyUrl.Get()), UriKind.Absolute, out Uri? proxyUri))
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

    private static void ApplyProxyCredentials(CoreWebView2 coreWebView)
    {
        if (!Configurations.IsUseProxy.Get()
            || !Uri.TryCreate(ProxyAddress.Normalize(Configurations.ProxyUrl.Get()), UriKind.Absolute, out Uri? proxyUri)
            || string.IsNullOrWhiteSpace(proxyUri.UserInfo))
        {
            return;
        }

        string[] credentials = proxyUri.UserInfo.Split(':', 2);
        coreWebView.BasicAuthenticationRequested += (_, args) =>
        {
            args.Response.UserName = Uri.UnescapeDataString(credentials[0]);
            args.Response.Password = credentials.Length > 1
                ? Uri.UnescapeDataString(credentials[1])
                : string.Empty;
        };
    }
}
