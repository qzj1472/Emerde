using Fischless.Configuration;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Emerde.Core;

internal static class ConfigFileManager
{
    private const int MaxBackupCount = 10;

    private static readonly IReadOnlyDictionary<string, Type> ConfigurationValueTypes = typeof(Configurations)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => property.PropertyType.IsGenericType && property.PropertyType.GenericTypeArguments.Length == 1)
        .ToDictionary(property => property.Name, property => property.PropertyType.GenericTypeArguments[0], StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, object?> ConfigurationDefaultValues = typeof(Configurations)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => property.PropertyType.IsGenericType && property.PropertyType.GenericTypeArguments.Length == 1)
        .Select(property => new { property.Name, Definition = property.GetValue(null) })
        .Where(item => item.Definition != null)
        .ToDictionary(
            item => item.Name,
            item => item.Definition!.GetType().GetProperty("DefaultValue")?.GetValue(item.Definition),
            StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Type> RoomValueTypes = typeof(Room)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

    public static string Import(string sourcePath)
    {
        Validate(sourcePath);
        return ConfigurationSaveScheduler.ExecuteExclusive<string>(() =>
            ReplaceConfigurationFile(sourcePath, AppPaths.ActiveConfigFilePath, ConfigurationManager.Setup));
    }

    public static string Export(string targetPath)
    {
        ConfigurationSaveScheduler.ExecuteExclusive(() =>
        {
            ConfigurationSaveScheduler.SaveNow();
            AtomicFile.Copy(ConfigurationManager.FilePath, targetPath);
            return true;
        });
        return targetPath;
    }

    public static ConfigurationBackupPoint StoreImportedConfiguration(string sourcePath)
    {
        Validate(sourcePath);
        return ConfigurationSaveScheduler.ExecuteExclusive<ConfigurationBackupPoint>(() =>
        {
            string targetPath = GetConfigArtifactPath(AppPaths.ActiveConfigFilePath, "import");
            string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigFilesDirectory;
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(sourcePath, temporaryPath, overwrite: false);
                NormalizeConfigurationFile(temporaryPath);
                Validate(temporaryPath, requireYamlExtension: false);
                if (!IsMeaningfulConfigurationFile(temporaryPath))
                {
                    throw new InvalidDataException("ImportedConfigEmpty".Tr());
                }

                string? existingPath = FindEquivalentConfigArtifact(temporaryPath, AppPaths.ActiveConfigFilePath, includeImports: true);
                if (!string.IsNullOrWhiteSpace(existingPath))
                {
                    return CreateBackupPoint(existingPath);
                }

                File.Move(temporaryPath, targetPath);
                return CreateBackupPoint(targetPath);
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
            }
        });
    }

    public static string[] Reset()
    {
        return ConfigurationSaveScheduler.ExecuteExclusive<string[]>(() =>
        {
            List<string> backupPaths = [];
            foreach (string configPath in AppPaths.GetConfigFiles())
            {
                if (IsMeaningfulConfigurationFile(configPath))
                {
                    string backupPath = CreateUniqueBackupCopy(configPath, "bak");
                    backupPaths.Add(backupPath);
                }
                File.Delete(configPath);
                PruneBackups(configPath);
            }

            ConfigurationSaveScheduler.SuppressUntilRestart();
            return [.. backupPaths];
        });
    }

    public static ConfigurationBackupPoint[] GetBackupPoints()
    {
        if (!Directory.Exists(AppPaths.ActiveConfigDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(AppPaths.ActiveConfigDirectory, "config*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(AppPaths.ActiveConfigDirectory, "config*.yaml", SearchOption.TopDirectoryOnly))
            .Where(IsBackupFile)
            .Where(IsMeaningfulConfigurationFile)
            .Select(CreateBackupPoint)
            .OrderByDescending(static point => point.LastWriteTime)
            .ThenBy(static point => point.FileName, StringComparer.OrdinalIgnoreCase)
            .GroupBy(static point => GetFileFingerprint(point.FilePath), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    public static string RestoreBackup(string backupPath)
    {
        if (!IsKnownBackupPath(backupPath))
        {
            throw new InvalidDataException("SelectEmerdeConfigBackup".Tr());
        }

        Validate(backupPath);
        return ConfigurationSaveScheduler.ExecuteExclusive<string>(() =>
            ReplaceConfigurationFile(backupPath, AppPaths.ActiveConfigFilePath, ConfigurationManager.Setup));
    }

    internal static void Validate(string sourcePath, bool requireYamlExtension = true)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("ConfigFileNotFound".Tr(), sourcePath);
        }

        string extension = Path.GetExtension(sourcePath);
        if (requireYamlExtension &&
            !extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("YamlOnly".Tr());
        }

        YamlStream yaml = new();
        try
        {
            using StreamReader reader = File.OpenText(sourcePath);
            yaml.Load(reader);
        }
        catch (YamlException e)
        {
            throw new InvalidDataException("YamlSyntaxInvalid".Tr(), e);
        }

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidDataException("UnsupportedEmerdeConfig".Tr());
        }

        foreach ((YamlNode keyNode, YamlNode valueNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
            {
                throw new InvalidDataException("ConfigKeyNameInvalid".Tr());
            }

            if (ConfigurationValueTypes.TryGetValue(key.Value, out Type? valueType) &&
                !IsValidConfigurationValueNode(key.Value, valueNode, valueType))
            {
                throw new InvalidDataException("ConfigKeyTypeInvalid".Tr(key.Value));
            }
        }

        KeyValuePair<YamlNode, YamlNode>[] roomsEntries = root.Children
            .Where(entry => entry.Key is YamlScalarNode key && key.Value == nameof(Configurations.Rooms))
            .ToArray();
        if (roomsEntries.Length != 1 || roomsEntries[0].Value is not YamlSequenceNode rooms)
        {
            throw new InvalidDataException("ConfigRoomsRequired".Tr());
        }

        foreach (YamlNode node in rooms.Children)
        {
            if (node is not YamlMappingNode room ||
                !TryGetNonEmptyScalar(room, nameof(Room.RoomUrl), out string roomUrl) ||
                !IsValidRoomUrl(roomUrl))
            {
                throw new InvalidDataException("ConfigRoomUrlInvalid".Tr());
            }

            foreach ((YamlNode keyNode, YamlNode valueNode) in room.Children)
            {
                if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
                {
                    throw new InvalidDataException("RoomConfigKeyNameInvalid".Tr());
                }

                if (RoomValueTypes.TryGetValue(key.Value, out Type? valueType) &&
                    !IsValidValueNode(valueNode, valueType))
                {
                    throw new InvalidDataException("RoomConfigKeyTypeInvalid".Tr(key.Value));
                }
            }
        }
    }

    private static bool IsValidValueNode(YamlNode node, Type declaredType)
    {
        Type? nullableType = Nullable.GetUnderlyingType(declaredType);
        Type valueType = nullableType ?? declaredType;

        if (node is YamlScalarNode nullableScalar &&
            string.IsNullOrWhiteSpace(nullableScalar.Value) &&
            (nullableType != null || !declaredType.IsValueType))
        {
            return true;
        }

        if (valueType == typeof(string))
        {
            return node is YamlScalarNode;
        }

        if (valueType == typeof(bool))
        {
            return node is YamlScalarNode scalar && bool.TryParse(scalar.Value, out _);
        }

        if (valueType == typeof(int))
        {
            return node is YamlScalarNode scalar &&
                int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        if (valueType == typeof(long))
        {
            return node is YamlScalarNode scalar &&
                long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        if (valueType.IsArray)
        {
            return node is YamlSequenceNode;
        }

        return node is YamlMappingNode;
    }

    private static bool IsValidConfigurationValueNode(string key, YamlNode node, Type declaredType)
    {
        if (string.Equals(key, nameof(Configurations.RoutineIntervalUnit), StringComparison.Ordinal))
        {
            return node is YamlScalarNode scalar && TryNormalizeRoutineIntervalUnit(scalar.Value, out _);
        }

        return IsValidValueNode(node, declaredType);
    }

    internal static string ReplaceConfigurationFile(string sourcePath, string targetPath, Action<string> setup)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        Directory.CreateDirectory(directory);
        bool targetExisted = File.Exists(targetPath);
        bool keepBackup = targetExisted && IsMeaningfulConfigurationFile(targetPath);
        string backupPath = keepBackup
            ? GetBackupPath(targetPath)
            : Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.restore");
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        string? restorePath = null;
        bool targetReplaced = false;

        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            NormalizeConfigurationFile(temporaryPath);
            Validate(temporaryPath, requireYamlExtension: false);
            if (targetExisted)
            {
                File.Replace(temporaryPath, targetPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }
            targetReplaced = true;
            setup(targetPath);
            string resultBackupPath = backupPath;
            if (keepBackup)
            {
                resultBackupPath = KeepUniqueBackup(backupPath, targetPath);
                try
                {
                    PruneBackups(targetPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    AppSessionLogger.WriteException(e);
                }
            }
            return keepBackup ? resultBackupPath : string.Empty;
        }
        catch
        {
            if (targetReplaced)
            {
                try
                {
                    if (targetExisted)
                    {
                        restorePath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.restore");
                        File.Copy(backupPath, restorePath, overwrite: false);
                        File.Replace(restorePath, targetPath, null, true);
                        restorePath = null;
                    }
                    else
                    {
                        File.Delete(targetPath);
                    }
                    setup(targetPath);
                }
                catch (Exception restoreException)
                {
                    AppSessionLogger.WriteException(restoreException);
                }
            }
            throw;
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
            if (!keepBackup)
            {
                DeleteTemporaryFile(backupPath);
            }
            if (!string.IsNullOrWhiteSpace(restorePath))
            {
                DeleteTemporaryFile(restorePath);
            }
        }
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    private static void NormalizeConfigurationFile(string path)
    {
        YamlStream yaml = new();
        using (StreamReader reader = File.OpenText(path))
        {
            yaml.Load(reader);
        }

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            return;
        }

        bool changed = false;
        foreach ((YamlNode keyNode, YamlNode valueNode) in root.Children.ToArray())
        {
            if (keyNode is not YamlScalarNode key || valueNode is not YamlScalarNode value)
            {
                continue;
            }

            if (!string.Equals(key.Value, nameof(Configurations.RoutineIntervalUnit), StringComparison.Ordinal)
                || !TryNormalizeRoutineIntervalUnit(value.Value, out int routineIntervalUnit))
            {
                continue;
            }

            string normalized = routineIntervalUnit.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(value.Value, normalized, StringComparison.Ordinal))
            {
                continue;
            }

            value.Value = normalized;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        using StreamWriter writer = File.CreateText(path);
        yaml.Save(writer, assignAnchors: false);
    }

    private static bool TryNormalizeRoutineIntervalUnit(string? value, out int unit)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
        {
            unit = Math.Clamp(numeric, 1, 3);
            return true;
        }

        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        unit = normalized switch
        {
            "second" or "seconds" or "sec" or "s" or "秒" => 1,
            "minute" or "minutes" or "min" or "m" or "分钟" or "分鐘" or "分" => 2,
            "hour" or "hours" or "h" or "小时" or "小時" or "時間" => 3,
            _ => 1,
        };
        return true;
    }

    private static bool TryGetNonEmptyScalar(YamlMappingNode mapping, string keyName, out string value)
    {
        YamlScalarNode? scalar = mapping.Children
            .Where(entry => entry.Key is YamlScalarNode key && string.Equals(key.Value, keyName, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .OfType<YamlScalarNode>()
            .FirstOrDefault();
        value = scalar?.Value ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsValidRoomUrl(string roomUrl)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(Spider.ParseUrl(roomUrl));
        }
        catch
        {
            return false;
        }
    }

    private static string GetBackupPath(string targetPath)
    {
        return GetConfigArtifactPath(targetPath, "bak");
    }

    private static string CreateUniqueBackupCopy(string sourcePath, string kind)
    {
        string? existingPath = FindEquivalentConfigArtifact(sourcePath, sourcePath, includeImports: false);
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            return existingPath;
        }

        string backupPath = GetConfigArtifactPath(sourcePath, kind);
        File.Copy(sourcePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static string KeepUniqueBackup(string backupPath, string targetPath)
    {
        string? existingPath = FindEquivalentConfigArtifact(backupPath, targetPath, includeImports: false, excludedPath: backupPath);
        if (string.IsNullOrWhiteSpace(existingPath))
        {
            return backupPath;
        }

        try
        {
            File.Delete(backupPath);
            return existingPath;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            return backupPath;
        }
    }

    private static string? FindEquivalentConfigArtifact(string sourcePath, string targetPath, bool includeImports, string? excludedPath = null)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        string name = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);
        string sourceFingerprint = GetFileFingerprint(sourcePath);
        string? excludedFullPath = string.IsNullOrWhiteSpace(excludedPath) ? null : Path.GetFullPath(excludedPath);

        return GetConfigArtifactFiles(directory, name, extension, includeImports)
            .Where(file => excludedFullPath == null || !string.Equals(file.FullName, excludedFullPath, StringComparison.OrdinalIgnoreCase))
            .Where(file => IsMeaningfulConfigurationFile(file.FullName))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault(file => string.Equals(GetFileFingerprint(file.FullName), sourceFingerprint, StringComparison.OrdinalIgnoreCase))
            ?.FullName;
    }

    private static string GetFileFingerprint(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Path.GetFullPath(path);
        }
    }

    private static string GetConfigArtifactPath(string targetPath, string kind)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        string name = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);
        Directory.CreateDirectory(directory);

        for (int index = 1; ; index++)
        {
            string suffix = index == 1 ? string.Empty : $"-{index}";
            string backupPath = Path.Combine(directory, $"{name}.{kind}-{timestamp}{suffix}{extension}");
            if (!File.Exists(backupPath))
            {
                return backupPath;
            }
        }
    }

    private static void PruneBackups(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        string name = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);

        foreach (FileInfo backup in GetBackupFiles(directory, name, extension)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(MaxBackupCount))
        {
            backup.Delete();
        }
    }

    private static IEnumerable<FileInfo> GetBackupFiles(string directory, string name, string extension)
    {
        return GetConfigArtifactFiles(directory, name, extension, includeImports: false);
    }

    private static IEnumerable<FileInfo> GetConfigArtifactFiles(string directory, string name, string extension, bool includeImports)
    {
        DirectoryInfo directoryInfo = new(directory);
        IEnumerable<FileInfo> files = directoryInfo
            .GetFiles($"{name}.bak-*{extension}")
            .Concat(directoryInfo.GetFiles($"{name}.reset-bak-*{extension}"));

        return includeImports
            ? files.Concat(directoryInfo.GetFiles($"{name}.import-*{extension}"))
            : files;
    }

    internal static bool IsMeaningfulConfigurationFile(string path)
    {
        try
        {
            YamlStream yaml = new();
            using StreamReader reader = File.OpenText(path);
            yaml.Load(reader);

            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                return true;
            }

            foreach ((YamlNode keyNode, YamlNode valueNode) in root.Children)
            {
                if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
                {
                    return true;
                }

                if (IsMeaninglessConfigurationEntry(key.Value, valueNode))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlException)
        {
            return true;
        }
    }

    private static bool IsMeaninglessConfigurationEntry(string key, YamlNode valueNode)
    {
        if (string.Equals(key, nameof(Configurations.IsStartupAboutNoticeShown), StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(key, nameof(Configurations.Rooms), StringComparison.Ordinal))
        {
            return valueNode is YamlSequenceNode rooms && rooms.Children.Count == 0;
        }

        if (!ConfigurationValueTypes.TryGetValue(key, out Type? valueType) ||
            !ConfigurationDefaultValues.TryGetValue(key, out object? defaultValue))
        {
            return false;
        }

        return IsDefaultConfigurationValueNode(key, valueNode, valueType, defaultValue);
    }

    private static bool IsDefaultConfigurationValueNode(string key, YamlNode node, Type declaredType, object? defaultValue)
    {
        Type? nullableType = Nullable.GetUnderlyingType(declaredType);
        Type valueType = nullableType ?? declaredType;

        if (node is YamlScalarNode nullableScalar &&
            string.IsNullOrWhiteSpace(nullableScalar.Value) &&
            (nullableType != null || !declaredType.IsValueType))
        {
            return defaultValue == null || defaultValue is string text && text.Length == 0;
        }

        if (string.Equals(key, nameof(Configurations.RoutineIntervalUnit), StringComparison.Ordinal))
        {
            return node is YamlScalarNode scalar &&
                TryNormalizeRoutineIntervalUnit(scalar.Value, out int normalized) &&
                defaultValue is int defaultUnit &&
                normalized == defaultUnit;
        }

        if (valueType == typeof(string))
        {
            return node is YamlScalarNode scalar &&
                string.Equals(scalar.Value ?? string.Empty, defaultValue as string ?? string.Empty, StringComparison.Ordinal);
        }

        if (valueType == typeof(bool))
        {
            return node is YamlScalarNode scalar &&
                bool.TryParse(scalar.Value, out bool value) &&
                defaultValue is bool defaultBool &&
                value == defaultBool;
        }

        if (valueType == typeof(int))
        {
            return node is YamlScalarNode scalar &&
                int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
                defaultValue is int defaultInt &&
                value == defaultInt;
        }

        if (valueType == typeof(long))
        {
            return node is YamlScalarNode scalar &&
                long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) &&
                defaultValue is long defaultLong &&
                value == defaultLong;
        }

        if (valueType.IsArray)
        {
            return node is YamlSequenceNode sequence &&
                sequence.Children.Count == 0 &&
                defaultValue is Array defaultArray &&
                defaultArray.Length == 0;
        }

        return false;
    }

    internal static bool IsBackupFile(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Contains(".bak-", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".reset-bak-", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".import-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownBackupPath(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath) || !IsBackupFile(backupPath))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(backupPath);
        string configDirectory = Path.GetFullPath(AppPaths.ActiveConfigDirectory);
        return string.Equals(Path.GetDirectoryName(fullPath), configDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static ConfigurationBackupPoint CreateBackupPoint(string path)
    {
        FileInfo file = new(path);
        return new ConfigurationBackupPoint(file.Name, file.FullName, file.LastWriteTime);
    }
}

internal sealed record ConfigurationBackupPoint(string FileName, string FilePath, DateTime LastWriteTime);
