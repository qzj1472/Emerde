using System.Net.Http;
using System.Text;

namespace Emerde.Core;

internal static class SpiderRequest
{
    public static string? Get(
        string url,
        IReadOnlyDictionary<string, string>? headers = null,
        string? cookie = null,
        CancellationToken cancellationToken = default)
    {
        return Execute(url, HttpMethod.Get, headers, cookie, null, cancellationToken);
    }

    public static string? PostJson(
        string url,
        string body,
        IReadOnlyDictionary<string, string>? headers = null,
        string? cookie = null,
        CancellationToken cancellationToken = default)
    {
        return Execute(
            url,
            HttpMethod.Post,
            headers,
            cookie,
            new StringContent(body, Encoding.UTF8, "application/json"),
            cancellationToken);
    }

    public static string? PostForm(
        string url,
        IReadOnlyDictionary<string, string> form,
        IReadOnlyDictionary<string, string>? headers = null,
        string? cookie = null,
        CancellationToken cancellationToken = default)
    {
        return Execute(url, HttpMethod.Post, headers, cookie, new FormUrlEncodedContent(form), cancellationToken);
    }

    private static string? Execute(
        string url,
        HttpMethod method,
        IReadOnlyDictionary<string, string>? headers,
        string? cookie,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(method, url)
            {
                Content = content,
            };
            AddHeaders(request, headers, cookie);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using HttpResponseMessage response = ProxyHttpClientPool.GetCurrent()
                .Send(request, HttpCompletionOption.ResponseContentRead, timeout.Token);

            return response.IsSuccessStatusCode
                ? response.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult()
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
        finally
        {
            content?.Dispose();
        }
    }

    private static void AddHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers, string? cookie)
    {
        if (headers != null)
        {
            foreach ((string key, string value) in headers)
            {
                if (!request.Headers.TryAddWithoutValidation(key, value) && request.Content != null)
                {
                    request.Content.Headers.TryAddWithoutValidation(key, value);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(cookie) && ShouldAttachCookie(request.RequestUri))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
    }

    internal static bool ShouldAttachCookie(Uri? uri)
    {
        return uri != null && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
