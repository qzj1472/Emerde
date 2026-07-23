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
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value ?? string.Empty;
        }

        try
        {
            byte[] protectedBytes = Convert.FromBase64String(value[Prefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            AppSessionLogger.WriteException(e);
            return string.Empty;
        }
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
}
