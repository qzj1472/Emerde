using System.Net;
using System.Net.Http;

namespace Emerde.Core;

internal static class ProxyHttpClientPool
{
    private const int MaximumCachedClients = 8;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ClientEntry> Clients = new(StringComparer.OrdinalIgnoreCase);
    private static long lastUsedSequence;

    public static ClientLease RentCurrent()
    {
        string proxy = Configurations.IsUseProxy.Get()
            ? ProxyAddress.Normalize(Configurations.ProxyUrl.Get())
            : string.Empty;
        lock (Sync)
        {
            if (Clients.TryGetValue(proxy, out ClientEntry? existing))
            {
                existing.ReferenceCount++;
                existing.LastUsed = ++lastUsedSequence;
                return new ClientLease(existing, isCached: true);
            }

            if (Clients.Count >= MaximumCachedClients)
            {
                ClientEntry? idle = Clients.Values
                    .Where(entry => entry.ReferenceCount == 0)
                    .MinBy(entry => entry.LastUsed);
                if (idle != null)
                {
                    Clients.Remove(idle.Proxy);
                    idle.Client.Dispose();
                }
            }

            ClientEntry created = new(proxy, CreateClient(proxy), ++lastUsedSequence)
            {
                ReferenceCount = 1,
            };
            if (Clients.Count < MaximumCachedClients)
            {
                Clients.Add(proxy, created);
                return new ClientLease(created, isCached: true);
            }

            return new ClientLease(created, isCached: false);
        }
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

    private static void Release(ClientEntry entry, bool isCached)
    {
        lock (Sync)
        {
            entry.ReferenceCount--;
            if (!isCached || entry.ReferenceCount < 0)
            {
                entry.Client.Dispose();
            }
        }
    }

    internal sealed class ClientEntry(string proxy, HttpClient client, long lastUsed)
    {
        public string Proxy { get; } = proxy;
        public HttpClient Client { get; } = client;
        public long LastUsed { get; set; } = lastUsed;
        public int ReferenceCount { get; set; }
    }

    public sealed class ClientLease : IDisposable
    {
        private readonly ClientEntry entry;
        private readonly bool isCached;
        private bool disposed;

        internal ClientLease(ClientEntry entry, bool isCached)
        {
            this.entry = entry;
            this.isCached = isCached;
        }

        public HttpClient Client => entry.Client;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Release(entry, isCached);
        }
    }
}
