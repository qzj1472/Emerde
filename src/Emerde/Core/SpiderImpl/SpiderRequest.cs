using RestSharp;
using System.Net;

namespace Emerde.Core;

internal static class SpiderRequest
{
    public static string? Get(string url, IReadOnlyDictionary<string, string>? headers = null, string? cookie = null)
    {
        return Execute(url, Method.Get, headers, cookie, null, null);
    }

    public static string? PostJson(string url, string body, IReadOnlyDictionary<string, string>? headers = null, string? cookie = null)
    {
        return Execute(url, Method.Post, headers, cookie, body, DataFormat.Json);
    }

    public static string? PostForm(string url, IReadOnlyDictionary<string, string> form, IReadOnlyDictionary<string, string>? headers = null, string? cookie = null)
    {
        RestClientOptions options = BuildOptions(url);
        using RestClient client = new(options);
        RestRequest request = BuildRequest(Method.Post, headers, cookie);

        foreach ((string key, string value) in form)
        {
            request.AddParameter(key, value);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    private static string? Execute(string url, Method method, IReadOnlyDictionary<string, string>? headers, string? cookie, string? body, DataFormat? dataFormat)
    {
        RestClientOptions options = BuildOptions(url);
        using RestClient client = new(options);
        RestRequest request = BuildRequest(method, headers, cookie);

        if (!string.IsNullOrWhiteSpace(body) && dataFormat != null)
        {
            request.AddStringBody(body, dataFormat.Value);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    private static RestClientOptions BuildOptions(string url)
    {
        RestClientOptions options = new()
        {
            BaseUrl = new Uri(url),
        };

        if (Configurations.IsUseProxy.Get())
        {
            string proxyUrl = Configurations.ProxyUrl.Get();

            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                options.Proxy = ProxyAddress.Create(proxyUrl);
            }
        }

        return options;
    }

    private static RestRequest BuildRequest(Method method, IReadOnlyDictionary<string, string>? headers, string? cookie)
    {
        RestRequest request = new()
        {
            Method = method,
            Timeout = TimeSpan.FromSeconds(5),
        };

        if (headers != null)
        {
            foreach ((string key, string value) in headers)
            {
                request.AddHeader(key, value);
            }
        }

        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        return request;
    }
}
