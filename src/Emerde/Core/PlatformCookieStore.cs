using Fischless.Configuration;
using Newtonsoft.Json;

namespace Emerde.Core;

internal static class PlatformCookieStore
{
    public static string GetCookie(string platformName, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return fallback ?? string.Empty;
        }

        Dictionary<string, string> cookies = Load();
        return cookies.TryGetValue(platformName, out string? cookie) && !string.IsNullOrWhiteSpace(cookie)
            ? cookie
            : fallback ?? string.Empty;
    }

    public static void SetCookie(string platformName, string? cookie)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return;
        }

        Dictionary<string, string> cookies = Load();
        string value = cookie?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            cookies.Remove(platformName);
        }
        else
        {
            cookies[platformName] = value;
        }

        Configurations.PlatformCookies.Set(SecretProtector.Protect(JsonConvert.SerializeObject(cookies)));
        ConfigurationSaveScheduler.Request();
    }

    public static IReadOnlyDictionary<string, string> GetAll()
    {
        return Load();
    }

    private static Dictionary<string, string> Load()
    {
        string raw = SecretProtector.Unprotect(Configurations.PlatformCookies.Get());
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            Dictionary<string, string>? result = JsonConvert.DeserializeObject<Dictionary<string, string>>(raw);
            return result == null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(result, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
