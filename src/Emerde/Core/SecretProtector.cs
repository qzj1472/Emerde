using Fischless.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Emerde.Core;

internal static class SecretProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value ?? string.Empty;
        }

        byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? value)
    {
        return TryUnprotect(value, out string result) ? result : string.Empty;
    }

    public static bool TryUnprotect(string? value, out string result)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            result = value ?? string.Empty;
            return true;
        }

        try
        {
            byte[] protectedBytes = Convert.FromBase64String(value[Prefix.Length..]);
            result = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
            return true;
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            AppSessionLogger.WriteException(e);
            result = string.Empty;
            return false;
        }
    }

    public static string[] GetUnavailableStoredSecretNames()
    {
        List<string> names = [];
        AddIfUnavailable(Configurations.ToNotifyWithEmailPassword, names);
        AddIfUnavailable(Configurations.PlatformCookies, names);
        AddIfUnavailable(Configurations.CookieChina, names);
        AddIfUnavailable(Configurations.CookieOversea, names);
        return [.. names];
    }

    public static string GetChinaCookie()
    {
        return Unprotect(Configurations.CookieChina.Get());
    }

    public static string GetOverseaCookie()
    {
        return Unprotect(Configurations.CookieOversea.Get());
    }

    public static void MigrateStoredSecrets()
    {
        bool changed = Migrate(Configurations.ToNotifyWithEmailPassword)
            | Migrate(Configurations.PlatformCookies)
            | Migrate(Configurations.CookieChina)
            | Migrate(Configurations.CookieOversea);
        if (changed)
        {
            ConfigurationSaveScheduler.SaveNow();
        }
    }

    private static bool Migrate(ConfigurationDefinition<string> definition)
    {
        string value = definition.Get();
        string protectedValue = Protect(value);
        if (string.Equals(value, protectedValue, StringComparison.Ordinal))
        {
            return false;
        }

        definition.Set(protectedValue);
        return true;
    }

    private static void AddIfUnavailable(ConfigurationDefinition<string> definition, ICollection<string> names)
    {
        string value = definition.Get();
        if (!string.IsNullOrWhiteSpace(value) && !TryUnprotect(value, out _))
        {
            names.Add(definition.Name);
        }
    }
}
