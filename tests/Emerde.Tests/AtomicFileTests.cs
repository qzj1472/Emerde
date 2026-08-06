using System.Text.Json;
using Emerde.Core;

namespace Emerde.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void Copy_ReplacesTargetWithoutLeavingTemporaryFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-atomic-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string sourcePath = Path.Combine(root, "source.txt");
        string targetPath = Path.Combine(root, "target.txt");
        try
        {
            File.WriteAllText(sourcePath, "new");
            File.WriteAllText(targetPath, "old");

            AtomicFile.Copy(sourcePath, targetPath);

            Assert.Equal("new", File.ReadAllText(targetPath));
            Assert.Equal(2, Directory.GetFiles(root, "*.txt").Length);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteJsonAsync_ReplacesTargetAndKeepsPreviousVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-atomic-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string targetPath = Path.Combine(root, "state.json");
        string backupPath = targetPath + ".bak";
        try
        {
            File.WriteAllText(targetPath, "{\"Value\":1}");

            await AtomicFile.WriteJsonAsync(
                targetPath,
                new AtomicState(2),
                new JsonSerializerOptions(),
                CancellationToken.None,
                backupPath);

            Assert.Equal(2, JsonSerializer.Deserialize<AtomicState>(File.ReadAllText(targetPath))!.Value);
            Assert.Equal(1, JsonSerializer.Deserialize<AtomicState>(File.ReadAllText(backupPath))!.Value);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record AtomicState(int Value);
}
