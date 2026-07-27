using Fischless.Configuration;
using Newtonsoft.Json;

namespace Emerde.Core;

internal static class PlatformCookieStore
{
    private static readonly object SyncRoot = new();
    private static string cachedEncryptedValue = string.Empty;
    private static Dictionary<string, string> cachedCookies = new(StringComparer.OrdinalIgnoreCase);

    public static string GetCookie(string platformName, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return fallback ?? string.Empty;
        }

        lock (SyncRoot)
        {
            Dictionary<string, string> cookies = Load();
            return cookies.TryGetValue(platformName, out string? cookie) && !string.IsNullOrWhiteSpace(cookie)
                ? cookie
                : fallback ?? string.Empty;
        }
    }

    public static void SetCookie(string platformName, string? cookie)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return;
        }

        lock (SyncRoot)
        {
            Dictionary<string, string> cookies = Load();
            string value = cookie?.Trim() ?? string.Empty;
            bool changed;
            if (string.IsNullOrWhiteSpace(value))
            {
                changed = cookies.Remove(platformName);
            }
            else
            {
                changed = !cookies.TryGetValue(platformName, out string? existing)
                    || !string.Equals(existing, value, StringComparison.Ordinal);
                cookies[platformName] = value;
            }

            if (!changed)
            {
                return;
            }

            string encrypted = SecretProtector.Protect(JsonConvert.SerializeObject(cookies));
            Configurations.PlatformCookies.Set(encrypted);
            cachedEncryptedValue = encrypted;
            cachedCookies = new(cookies, StringComparer.OrdinalIgnoreCase);
            ConfigurationSaveScheduler.Request();
        }
    }

    public static IReadOnlyDictionary<string, string> GetAll()
    {
        lock (SyncRoot)
        {
            return new Dictionary<string, string>(Load(), StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> Load()
    {
        string encrypted = Configurations.PlatformCookies.Get() ?? string.Empty;
        if (string.Equals(encrypted, cachedEncryptedValue, StringComparison.Ordinal))
        {
            return new(cachedCookies, StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string> cookies = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            string raw = SecretProtector.Unprotect(encrypted);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                Dictionary<string, string>? result = JsonConvert.DeserializeObject<Dictionary<string, string>>(raw);
                if (result != null)
                {
                    cookies = new(result, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        cachedEncryptedValue = encrypted;
        cachedCookies = cookies;
        return new(cookies, StringComparer.OrdinalIgnoreCase);
    }
}
