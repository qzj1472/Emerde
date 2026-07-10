using Emerde.Extensions;

namespace Emerde.Tests;

public sealed class SearchFileHelperTests
{
    [Fact]
    public void SearchExecutable_PrefersLocalDirectory()
    {
        string directory = CreateTempDirectory();

        try
        {
            string expectedPath = Path.Combine(directory, "ffmpeg.exe");
            File.WriteAllText(expectedPath, string.Empty);

            string? result = SearchFileHelper.SearchExecutable("ffmpeg.exe", [directory], string.Empty);

            Assert.Equal(Path.GetFullPath(expectedPath), result);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SearchExecutable_UsesPathValue()
    {
        string directory = CreateTempDirectory();

        try
        {
            string expectedPath = Path.Combine(directory, "ffmpeg.exe");
            File.WriteAllText(expectedPath, string.Empty);

            string? result = SearchFileHelper.SearchExecutable("ffmpeg.exe", [], directory);

            Assert.Equal(Path.GetFullPath(expectedPath), result);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EmerdeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
