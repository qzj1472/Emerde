using System.IO;
using System.Text.RegularExpressions;
using Emerde.Core;

namespace Emerde.Tests;

public sealed class ConfigFileManagerTests
{
    [Fact]
    public void ReplaceConfigurationFile_DoesNotRunSetupBeforeTargetIsReplaced()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeConfigFileManagerTests", Guid.NewGuid().ToString("N"));
        string sourcePath = Path.Combine(root, "invalid.yaml");
        string targetPath = Path.Combine(root, "config.yaml");
        Directory.CreateDirectory(root);
        File.WriteAllText(sourcePath, "Rooms: [");
        int setupCalls = 0;

        try
        {
            Assert.ThrowsAny<Exception>(() => ConfigFileManager.ReplaceConfigurationFile(
                sourcePath,
                targetPath,
                _ => setupCalls++));

            Assert.Equal(0, setupCalls);
            Assert.False(File.Exists(targetPath));
            Assert.Empty(Directory.GetFiles(root, ".config.yaml.*.restore"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("# Rooms:\n#   - RoomUrl: https://live.douyin.com/123456", false)]
    [InlineData("Wrapper:\n  Rooms: []", false)]
    [InlineData("Rooms: [", false)]
    [InlineData("- Rooms\n- Theme", false)]
    [InlineData("Theme: Dark", false)]
    [InlineData("rooms: []", false)]
    [InlineData("Rooms: invalid", false)]
    [InlineData("Rooms:\n  - NickName: missing-url", false)]
    [InlineData("Rooms:\n  - RoomUrl: ''", false)]
    [InlineData("Rooms:\n  - roomurl: https://live.douyin.com/123456", false)]
    [InlineData("Rooms:\n  - RoomUrl: invalid-room-url", false)]
    [InlineData("Rooms: []\nRoutineInterval: invalid", false)]
    [InlineData("Rooms: []\nIsToRecord:\n  Value: true", false)]
    [InlineData("Rooms:\n  - RoomUrl: https://live.douyin.com/123456\n    IsToRecord: invalid", false)]
    [InlineData("Rooms:\n  - RoomUrl: https://live.douyin.com/123456\n    SegmentTime: invalid", false)]
    [InlineData("Rooms: []", true)]
    [InlineData("Theme: Dark\nRooms: []", true)]
    [InlineData("UpdateChannel: auto\nRooms: []", true)]
    [InlineData("Rooms:\n  - RoomUrl: https://live.douyin.com/123456\n    SegmentTime:", true)]
    [InlineData("Rooms:\n  - RoomUrl: https://live.douyin.com/123456", true)]
    public void Validate_RequiresValidRoomsStructure(string yaml, bool expectedValid)
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-config-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(path, yaml);

            Exception? exception = Record.Exception(() => ConfigFileManager.Validate(path));

            Assert.Equal(expectedValid, exception == null);
            if (!expectedValid)
            {
                Assert.IsType<InvalidDataException>(exception);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReplaceConfigurationFile_RestoresPreviousFileWhenSetupFails()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "import.yaml");
        string targetPath = Path.Combine(directory, "config.yaml");
        const string importedConfiguration = "Theme: Dark\nRooms: []";
        const string previousConfiguration = "Theme: Light\nRooms: []";
        File.WriteAllText(sourcePath, importedConfiguration);
        File.WriteAllText(targetPath, previousConfiguration);
        int setupCount = 0;

        try
        {
            Assert.Throws<InvalidDataException>(() => ConfigFileManager.ReplaceConfigurationFile(
                sourcePath,
                targetPath,
                _ =>
                {
                    setupCount++;
                    if (File.ReadAllText(targetPath) == importedConfiguration)
                    {
                        throw new InvalidDataException();
                    }
                }));

            Assert.Equal(previousConfiguration, File.ReadAllText(targetPath));
            Assert.Equal(2, setupCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Seconds", "RoutineIntervalUnit: 1")]
    [InlineData("invalid", "RoutineIntervalUnit: 1")]
    [InlineData("3", "RoutineIntervalUnit: 3")]
    [InlineData("8", "RoutineIntervalUnit: 3")]
    public void ReplaceConfigurationFile_NormalizesRoutineIntervalUnit(string unit, string expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "import.yaml");
        string targetPath = Path.Combine(directory, "config.yaml");
        File.WriteAllText(sourcePath, $"RoutineIntervalUnit: {unit}\nRooms: []");

        try
        {
            _ = ConfigFileManager.ReplaceConfigurationFile(sourcePath, targetPath, _ => { });

            Assert.Contains(expected, File.ReadAllText(targetPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReplaceConfigurationFile_UsesUnifiedBackupTimestamp()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "import.yaml");
        string targetPath = Path.Combine(directory, "config.yaml");
        File.WriteAllText(sourcePath, "Theme: Dark\nRooms: []");
        File.WriteAllText(targetPath, "Theme: Light\nRooms: []");

        try
        {
            string backupPath = ConfigFileManager.ReplaceConfigurationFile(sourcePath, targetPath, _ => { });

            Assert.Matches(new Regex(@"config\.bak-\d{8}_\d{6}\.yaml$", RegexOptions.CultureInvariant), Path.GetFileName(backupPath));
            Assert.True(ConfigFileManager.IsBackupFile(backupPath));
            Assert.True(File.Exists(backupPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReplaceConfigurationFile_ReusesEquivalentBackup()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "import.yaml");
        string targetPath = Path.Combine(directory, "config.yaml");
        string existingBackupPath = Path.Combine(directory, "config.bak-20260725_010000.yaml");
        const string importedConfiguration = "Theme: Dark\nRooms: []";
        const string previousConfiguration = "Theme: Light\nRooms: []";
        File.WriteAllText(sourcePath, importedConfiguration);
        File.WriteAllText(targetPath, previousConfiguration);
        File.WriteAllText(existingBackupPath, previousConfiguration);

        try
        {
            string backupPath = ConfigFileManager.ReplaceConfigurationFile(sourcePath, targetPath, _ => { });

            Assert.Equal(existingBackupPath, backupPath);
            Assert.Single(Directory.EnumerateFiles(directory, "config.bak-*.yaml"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("config.bak-20260710_120000.yaml", true)]
    [InlineData("config.bak-reset-20260710_120000.yaml", true)]
    [InlineData("config.reset-bak-20260710120000.yaml", true)]
    [InlineData("config.import-20260710_120000.yaml", true)]
    [InlineData("config.invalid-20260710_120000.yaml", false)]
    [InlineData("config.yaml", false)]
    public void IsBackupFile_AcceptsCurrentAndLegacyBackupNames(string fileName, bool expected)
    {
        string path = Path.Combine("C:\\config", fileName);

        Assert.Equal(expected, ConfigFileManager.IsBackupFile(path));
    }

    [Theory]
    [InlineData("IsStartupAboutNoticeShown: true", false)]
    [InlineData("IsStartupAboutNoticeShown: true\nRooms: []", false)]
    [InlineData("LastShownUpgradeNoticeVersion: 1.6.7.0", false)]
    [InlineData("LastShownUpgradeNoticeId: upgrade-1", false)]
    [InlineData("LastShownUpgradeNoticeDebugBuildId: debug:1.6.7.1:build-1", false)]
    [InlineData("LastShownUpgradeNoticeVersion: 1.6.7.0\nRooms: []", false)]
    [InlineData("Rooms: []", false)]
    [InlineData("Theme: ''\nRooms: []", false)]
    [InlineData("RoutineIntervalUnit: Seconds\nRooms: []", false)]
    [InlineData("Theme: Dark\nRooms: []", true)]
    [InlineData("RoutineInterval: 8000\nRooms: []", true)]
    [InlineData("Rooms:\n  - RoomUrl: https://live.douyin.com/123456", true)]
    public void IsMeaningfulConfigurationFile_IgnoresStartupNoticeOnlyFiles(string yaml, bool expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-config-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(path, yaml);

            Assert.Equal(expected, ConfigFileManager.IsMeaningfulConfigurationFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsMeaningfulConfigurationFile_IgnoresPersistedDefaultConfiguration()
    {
        const string yaml = """
PreferredStreamQuality: Original
SaveFolder: ''
SegmentTime: 1800
RoutineScheduleStartMinute: 0
IsMonitorRunning: true
Theme: ''
IsDataRetentionEnabled: false
SessionLogRetentionDays: 30
IsUseStatusTray: true
IsAutoShutdownComputer: false
RoutineIntervalUnit: 1
ToNotifyWithEmailSmtp:
Rooms: []
ToNotifyWithEmailPort: 25
ToNotifyWithEmailUserName:
IsStartupAboutNoticeShown: true
IsToNotifyGotoRoomUrl: false
IsToNotify: true
IsToSegment: false
SaveFolderPathLevel: 3
RoutineScheduleEndHour: 23
CookieOversea: ''
ToNotifyWithMusicPath:
AutoShutdownTime: 00:00
IsRemoveTs: false
SegmentTimeUnit: 1
DataRetentionValue: 1
RoutineScheduleMode: 0
DisplayScale: 100
IsAutoShutdownAfterTranscode: false
RoutineScheduleStartHour: 0
RecordFormat: TS/FLV
Language: ''
ToNotifyWithEmailPassword: ''
SaveFileNameCustomRule: ''
IsUseAutoShutdown: false
IsToNotifyWithEmail: false
IsSessionLogEnabled: true
RoutineScheduleDays: Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday
IsToRecord: true
IsUseProxy: false
IsUseKeepAwake: false
IsToNotifyGotoRoomUrlAndMute: false
CookieChina: ''
RoutineScheduleEndMinute: 59
PlatformCookies: ''
IsToMonitor: true
UserAgent: ''
ProxyUrl: ''
IsToNotifyWithSystem: true
IsToNotifyWithMusic: true
RoutineInterval: 5000
DataRetentionUnit: 1
""";
        string path = Path.Combine(Path.GetTempPath(), $"emerde-config-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(path, yaml);

            Assert.False(ConfigFileManager.IsMeaningfulConfigurationFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
