using Fischless.Configuration;
using System.Diagnostics;

namespace Emerde.Extensions;

internal static class ConfigurationMigrationHelper
{
    public static void MigrateLegacyConfiguration()
    {
        string legacyConfigPath = ConfigurationSpecialPath.GetPath("config.yaml", AppConfig.LegacyPackName);
        string currentConfigPath = ConfigurationSpecialPath.GetPath("config.yaml", AppConfig.PackName);

        if (File.Exists(currentConfigPath) || !File.Exists(legacyConfigPath))
        {
            return;
        }

        try
        {
            string? currentDirectory = Path.GetDirectoryName(currentConfigPath);

            if (!string.IsNullOrWhiteSpace(currentDirectory))
            {
                Directory.CreateDirectory(currentDirectory);
            }

            File.Copy(legacyConfigPath, currentConfigPath, overwrite: false);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }
}
