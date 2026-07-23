using System.Net.Http;
using System.Text;

namespace Emerde.Core;

internal static class SpiderRequest
{
    public static string? Get(string url, IReadOnlyDictionary<string, string>? headers = null, string? cookie = null)
    {
        return Execute(url, HttpMethod.Get, headers, cookie, null);
    }

    public static string? PostJson(string url, string body, IReadOnlyDictionary<string, string>? headers = null, string? cookie = null)
    {
        return Execute(url, HttpMethod.Post, headers, cookie, new StringContent(body, Encoding.UTF8, "application/json"));
    }

    public static string? PostForm(string url, IReadOnlyDictionary<string, string> form, IReadOnlyDictionary<string, string>? headers = null, string? cookie = null)
    {
        return Execute(url, HttpMethod.Post, headers, cookie, new FormUrlEncodedContent(form));
    }

    private static string? Execute(string url, HttpMethod method, IReadOnlyDictionary<string, string>? headers, string? cookie, HttpContent? content)
    {
        try
        {
            using HttpRequestMessage request = new(method, url)
            {
                Content = content,
            };
            AddHeaders(request, headers, cookie);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            using HttpResponseMessage response = ProxyHttpClientPool.GetCurrent()
                .Send(request, HttpCompletionOption.ResponseContentRead, timeout.Token);

            return response.IsSuccessStatusCode
                ? response.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult()
                : null;
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

        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
    }
}
