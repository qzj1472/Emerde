namespace Emerde.Extensions;

internal static class SearchFileHelper
{
    public static string? SearchExecutable(string fileName)
    {
        return SearchExecutable(fileName, [AppContext.BaseDirectory, Environment.CurrentDirectory], Environment.GetEnvironmentVariable("PATH"));
    }

    internal static string? SearchExecutable(string fileName, IEnumerable<string> localDirectories, string? pathValue)
    {
        string executableName = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        foreach (string directory in localDirectories.Where(directory => !string.IsNullOrWhiteSpace(directory)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        foreach (string directory in SplitPathValue(pathValue))
        {
            string candidate = Path.Combine(directory, executableName);

            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitPathValue(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string normalizedDirectory = directory.Trim('"');

            if (!string.IsNullOrWhiteSpace(normalizedDirectory))
            {
                yield return normalizedDirectory;
            }
        }
    }
}
