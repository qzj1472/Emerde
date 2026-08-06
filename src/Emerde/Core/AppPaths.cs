using Fischless.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Emerde.Core;

internal static class AppPaths
{
    public static string DataDirectory => Path.GetDirectoryName(ConfigurationSpecialPath.GetPath("config.yaml", AppConfig.PackName)) ?? AppContext.BaseDirectory;

    public static string ConfigDirectory => DataDirectory;

    public static string ConfigFilesDirectory => Path.Combine(ConfigDirectory, "config");

    public static string ConfigFilePath => Path.Combine(ConfigFilesDirectory, "config.yaml");

    public static string ActiveConfigFilePath => string.IsNullOrWhiteSpace(ConfigurationManager.FilePath)
        ? ConfigFilePath
        : ConfigurationManager.FilePath;

    public static string ActiveConfigDirectory => Path.GetDirectoryName(ActiveConfigFilePath) ?? ConfigDirectory;

    public static string LogsDirectory => Path.Combine(ConfigDirectory, "logs");

    public static string CacheDirectory => Path.Combine(ConfigDirectory, "cache");

    public static string ThumbnailCacheDirectory => Path.Combine(CacheDirectory, "video_thumbnails");

    public static string MediaWorkerCommandDirectory => Path.Combine(CacheDirectory, "media_worker");

    public static string LivePreviewTemporaryDirectory => Path.Combine(CacheDirectory, "live_preview");

    public static string PendingRecordingsDirectory => Path.Combine(CacheDirectory, "pending_recordings");

    public static string RecordingCleanupStateFilePath => Path.Combine(CacheDirectory, "recording-cleanup-state.json");

    public static string DouyinWebViewDataDirectory => Path.Combine(CacheDirectory, "douyin_webview2");

    public static string PlatformLoginWebViewDataDirectory => Path.Combine(CacheDirectory, "platform_login_webview2");

    public static string GetPlatformLoginWebViewDataDirectory(string platformName)
    {
        string normalizedName = string.IsNullOrWhiteSpace(platformName) ? "unknown" : platformName.Trim().ToLowerInvariant();
        string identifier = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedName)))[..16].ToLowerInvariant();
        return Path.Combine(PlatformLoginWebViewDataDirectory, identifier);
    }

    public static string ExtensionsDirectory => Path.Combine(ConfigDirectory, "extensions");

    public static string ExtensionStateFilePath => Path.Combine(ExtensionsDirectory, "extensions-state.json");

    public static string[] GetConfigFiles()
    {
        string directory = ActiveConfigDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "config*.yaml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(directory, "config*.yml", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsConfigFile)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsConfigFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        return !fileName.Contains(".bak-", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains(".reset-bak-", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains(".import-", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains(".invalid-", StringComparison.OrdinalIgnoreCase);
    }
}
