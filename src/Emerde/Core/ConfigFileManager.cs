using Fischless.Configuration;
using System.Globalization;
using System.IO;
using System.Reflection;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Emerde.Core;

internal static class ConfigFileManager
{
    private const int MaxResetBackupCount = 5;

    private const int MaxImportBackupCount = 5;

    private static readonly IReadOnlyDictionary<string, Type> ConfigurationValueTypes = typeof(Configurations)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(property => property.PropertyType.IsGenericType && property.PropertyType.GenericTypeArguments.Length == 1)
        .ToDictionary(property => property.Name, property => property.PropertyType.GenericTypeArguments[0], StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Type> RoomValueTypes = typeof(Room)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

    public static string Import(string sourcePath)
    {
        Validate(sourcePath);
        return ConfigurationSaveScheduler.ExecuteExclusive(() =>
            ReplaceConfigurationFile(sourcePath, ConfigurationManager.FilePath, ConfigurationManager.Setup));
    }

    public static string Export(string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory);
        ConfigurationSaveScheduler.ExecuteExclusive(() =>
        {
            ConfigurationSaveScheduler.SaveNow();
            File.Copy(ConfigurationManager.FilePath, targetPath, overwrite: true);
            return true;
        });
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

    internal static void Validate(string sourcePath, bool requireYamlExtension = true)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("没有找到配置文件。", sourcePath);
        }

        string extension = Path.GetExtension(sourcePath);
        if (requireYamlExtension &&
            !extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("仅支持 YAML 配置文件。");
        }

        YamlStream yaml = new();
        try
        {
            using StreamReader reader = File.OpenText(sourcePath);
            yaml.Load(reader);
        }
        catch (YamlException e)
        {
            throw new InvalidDataException("YAML 配置文件语法无效。", e);
        }

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidDataException("该 YAML 文件不像 Emerde 支持的配置文件。");
        }

        foreach ((YamlNode keyNode, YamlNode valueNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
            {
                throw new InvalidDataException("配置项名称必须是有效文本。");
            }

            if (ConfigurationValueTypes.TryGetValue(key.Value, out Type? valueType) &&
                !IsValidValueNode(valueNode, valueType))
            {
                throw new InvalidDataException($"配置项 {key.Value} 的数据类型无效。");
            }
        }

        KeyValuePair<YamlNode, YamlNode>[] roomsEntries = root.Children
            .Where(entry => entry.Key is YamlScalarNode key && key.Value == nameof(Configurations.Rooms))
            .ToArray();
        if (roomsEntries.Length != 1 || roomsEntries[0].Value is not YamlSequenceNode rooms)
        {
            throw new InvalidDataException("配置文件必须包含 Rooms 列表。");
        }

        foreach (YamlNode node in rooms.Children)
        {
            if (node is not YamlMappingNode room ||
                !TryGetNonEmptyScalar(room, nameof(Room.RoomUrl), out string roomUrl) ||
                !IsValidRoomUrl(roomUrl))
            {
                throw new InvalidDataException("Rooms 中的每个房间都必须包含有效的 RoomUrl。");
            }

            foreach ((YamlNode keyNode, YamlNode valueNode) in room.Children)
            {
                if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
                {
                    throw new InvalidDataException("房间配置项名称必须是有效文本。");
                }

                if (RoomValueTypes.TryGetValue(key.Value, out Type? valueType) &&
                    !IsValidValueNode(valueNode, valueType))
                {
                    throw new InvalidDataException($"房间配置项 {key.Value} 的数据类型无效。");
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

    internal static string ReplaceConfigurationFile(string sourcePath, string targetPath, Action<string> setup)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        Directory.CreateDirectory(directory);
        bool targetExisted = File.Exists(targetPath);
        string backupPath = GetBackupPath(targetPath);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        string? restorePath = null;

        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            Validate(temporaryPath, requireYamlExtension: false);
            if (targetExisted)
            {
                File.Replace(temporaryPath, targetPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }
            setup(targetPath);
            if (targetExisted)
            {
                try
                {
                    PruneImportBackups(targetPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    AppSessionLogger.WriteException(e);
                }
            }
            return backupPath;
        }
        catch
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
            throw;
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
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

    private static void PruneImportBackups(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? AppPaths.ConfigDirectory;
        string name = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);

        foreach (FileInfo backup in new DirectoryInfo(directory)
            .GetFiles($"{name}.bak-*{extension}")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(MaxImportBackupCount))
        {
            backup.Delete();
        }
    }
}
