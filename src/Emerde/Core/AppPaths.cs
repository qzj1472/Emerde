using Fischless.Configuration;
using System.IO;

namespace Emerde.Core;

internal static class AppPaths
{
    public static string ConfigFilePath => ConfigurationSpecialPath.GetPath("config.yaml", AppConfig.PackName);

    public static string ConfigDirectory => Path.GetDirectoryName(ConfigFilePath) ?? AppContext.BaseDirectory;

    public static string LogsDirectory => Path.Combine(ConfigDirectory, "logs");

    public static string[] GetConfigFiles()
    {
        if (!Directory.Exists(ConfigDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(ConfigDirectory, "config*.yaml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(ConfigDirectory, "config*.yml", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsConfigFile)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsConfigFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        return !fileName.Contains(".bak-", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains(".reset-bak-", StringComparison.OrdinalIgnoreCase);
    }
}
