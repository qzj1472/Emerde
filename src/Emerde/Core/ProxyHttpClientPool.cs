using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace Emerde.Core;

internal static class ProxyHttpClientPool
{
    private static readonly ConcurrentDictionary<string, HttpClient> Clients = new(StringComparer.OrdinalIgnoreCase);

    public static HttpClient GetCurrent()
    {
        string proxy = Configurations.IsUseProxy.Get()
            ? ProxyAddress.Normalize(Configurations.ProxyUrl.Get())
            : string.Empty;
        return Clients.GetOrAdd(proxy, CreateClient);
    }

    private static HttpClient CreateClient(string proxy)
    {
        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 32,
        };
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = ProxyAddress.Create(proxy);
            handler.UseProxy = handler.Proxy != null;
        }

        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
