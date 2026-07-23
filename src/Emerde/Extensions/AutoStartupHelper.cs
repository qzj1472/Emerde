using Microsoft.Win32;
using System.Diagnostics;

namespace Emerde.Extensions;

internal static class AutoStartupHelper
{
    public static bool IsAutorun()
    {
        string launchCommand = GetLaunchCommand();

        if (RegistyAutoRunHelper.Exists(AppConfig.LegacyPackName))
        {
            RegistyAutoRunHelper.Enable(AppConfig.PackName, launchCommand);
            RegistyAutoRunHelper.Disable(AppConfig.LegacyPackName);
        }

        return RegistyAutoRunHelper.IsEnabled(AppConfig.PackName, launchCommand);
    }

    public static void RemoveAutorunShortcut()
    {
        RegistyAutoRunHelper.Disable(AppConfig.PackName);
        RegistyAutoRunHelper.Disable(AppConfig.LegacyPackName);
    }

    public static void CreateAutorunShortcut()
    {
        RegistyAutoRunHelper.Disable(AppConfig.LegacyPackName);
        RegistyAutoRunHelper.Enable(AppConfig.PackName, GetLaunchCommand());
    }

    internal static string GetLaunchCommand()
    {
        return $"\"{Environment.ProcessPath}\" /autorun";
    }
}

internal static class RegistyAutoRunHelper
{
    private const string RunLocation = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static void Enable(string keyName, string launchCommand)
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunLocation);
        key?.SetValue(keyName, launchCommand);
    }

    public static bool IsEnabled(string keyName, string launchCommand)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunLocation);

        if (key == null)
        {
            return false;
        }

        string? value = (string?)key.GetValue(keyName);

        if (value == null)
        {
            return false;
        }

        return value == launchCommand;
    }

    public static bool Exists(string keyName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunLocation);

        if (key == null)
        {
            return false;
        }

        return key.GetValue(keyName) != null;
    }

    public static void Disable(string keyName, string launchCommand = null!)
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunLocation);

        _ = launchCommand;
        if (key == null)
        {
            return;
        }

        if (key.GetValue(keyName) != null)
        {
            key.DeleteValue(keyName);
        }
    }

    public static void SetEnabled(bool enable, string keyName, string launchCommand)
    {
        if (enable)
        {
            Enable(keyName, launchCommand);
        }
        else
        {
            Disable(keyName);
        }
    }
}
