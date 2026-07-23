using System.IO;
using Emerde.Core;

namespace Emerde.Tests;

public sealed class ConfigFileManagerTests
{
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
}
