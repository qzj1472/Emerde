using Fischless.Configuration;
using System.IO;

namespace Emerde.Core;

internal static class ConfigFileManager
{
    private const int MaxResetBackupCount = 5;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Configurations.Language),
        nameof(Configurations.Theme),
        nameof(Configurations.DisplayScale),
        nameof(Configurations.UpdateChannel),
        nameof(Configurations.IsSessionLogEnabled),
        nameof(Configurations.IsOffRemindCloseToTray),
        nameof(Configurations.Rooms),
        nameof(Configurations.IsUseStatusTray),
        nameof(Configurations.RoutineInterval),
        nameof(Configurations.RoutineIntervalUnit),
        nameof(Configurations.IsMonitorRunning),
        nameof(Configurations.IsToMonitor),
        nameof(Configurations.RoutineScheduleMode),
        nameof(Configurations.RoutineScheduleDays),
        nameof(Configurations.RoutineScheduleStartHour),
        nameof(Configurations.RoutineScheduleStartMinute),
        nameof(Configurations.RoutineScheduleEndHour),
        nameof(Configurations.RoutineScheduleEndMinute),
        nameof(Configurations.IsToNotify),
        nameof(Configurations.IsToNotifyWithSystem),
        nameof(Configurations.IsToNotifyWithMusic),
        nameof(Configurations.ToNotifyWithMusicPath),
        nameof(Configurations.IsToNotifyWithEmail),
        nameof(Configurations.ToNotifyWithEmailSmtp),
        nameof(Configurations.ToNotifyWithEmailUserName),
        nameof(Configurations.ToNotifyWithEmailPassword),
        nameof(Configurations.IsToNotifyGotoRoomUrl),
        nameof(Configurations.IsToNotifyGotoRoomUrlAndMute),
        nameof(Configurations.IsToRecord),
        nameof(Configurations.RecordFormat),
        nameof(Configurations.IsRemoveTs),
        nameof(Configurations.IsToSegment),
        nameof(Configurations.SegmentTime),
        nameof(Configurations.SegmentTimeUnit),
        nameof(Configurations.SaveFolder),
        nameof(Configurations.SaveFolderPathLevel),
        nameof(Configurations.SaveFolderDistinguishedByAuthors),
        nameof(Configurations.SaveFileNameRule),
        nameof(Configurations.SaveFileNameCustomRule),
        nameof(Configurations.IsDataRetentionEnabled),
        nameof(Configurations.DataRetentionValue),
        nameof(Configurations.DataRetentionUnit),
        nameof(Configurations.Player),
        nameof(Configurations.IsPlayerRect),
        nameof(Configurations.IsUseKeepAwake),
        nameof(Configurations.IsUseAutoShutdown),
        nameof(Configurations.AutoShutdownTime),
        nameof(Configurations.IsUseProxy),
        nameof(Configurations.ProxyUrl),
        nameof(Configurations.PlatformCookies),
        nameof(Configurations.CookieChina),
        nameof(Configurations.CookieOversea),
        nameof(Configurations.UserAgent),
    };

    public static string Import(string sourcePath)
    {
        Validate(sourcePath);

        string targetPath = ConfigurationManager.FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory);

        string backupPath = GetBackupPath(targetPath);
        if (File.Exists(targetPath))
        {
            File.Copy(targetPath, backupPath, overwrite: false);
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
        ConfigurationManager.Setup(targetPath);

        return backupPath;
    }

    public static string Export(string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory);
        ConfigurationManager.Save();
        File.Copy(ConfigurationManager.FilePath, targetPath, overwrite: true);
        return targetPath;
    }

    public static string[] Reset()
    {
        List<string> backupPaths = [];

        foreach (string configPath in AppPaths.GetConfigFiles())
        {
            string backupPath = GetResetBackupPath(configPath);
            File.Copy(configPath, backupPath, overwrite: false);
            File.Delete(configPath);
            PruneResetBackups(configPath);
            backupPaths.Add(backupPath);
        }

        return [.. backupPaths];
    }

    private static void Validate(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("没有找到配置文件。", sourcePath);
        }

        string extension = Path.GetExtension(sourcePath);
        if (!extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("仅支持 YAML 配置文件。");
        }

        string text = File.ReadAllText(sourcePath);
        if (!KnownKeys.Any(key => text.Contains($"{key}:", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("该 YAML 文件不像 Emerde 支持的配置文件。");
        }
    }

    private static string GetBackupPath(string targetPath)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        string name = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);
        Directory.CreateDirectory(directory);

        for (int index = 1; ; index++)
        {
            string suffix = index == 1 ? string.Empty : $"-{index}";
            string backupPath = Path.Combine(directory, $"{name}.bak-{timestamp}{suffix}{extension}");
            if (!File.Exists(backupPath))
            {
                return backupPath;
            }
        }
    }

    private static string GetResetBackupPath(string targetPath)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        string name = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);
        Directory.CreateDirectory(directory);

        for (int index = 1; ; index++)
        {
            string suffix = index == 1 ? string.Empty : $"-{index}";
            string backupPath = Path.Combine(directory, $"{name}.reset-bak-{timestamp}{suffix}{extension}");
            if (!File.Exists(backupPath))
            {
                return backupPath;
            }
        }
    }

    private static void PruneResetBackups(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        string name = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);

        foreach (FileInfo backup in new DirectoryInfo(directory)
            .GetFiles($"{name}.reset-bak-*{extension}")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(MaxResetBackupCount))
        {
            backup.Delete();
        }
    }
}
