using System.Text.RegularExpressions;

namespace Emerde.Extensions;

internal static class SearchFileHelper
{
    public static IEnumerable<string> SearchFiles(string directory, string regexPattern, bool searchSubdirectories = true)
    {
        try
        {
            string[] files = Directory.GetFiles(directory, "*",
                searchSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            Regex regex = new(regexPattern, RegexOptions.IgnoreCase);
            return files.Where(file => regex.IsMatch(Path.GetFileName(file)));
        }
        catch (UnauthorizedAccessException e)
        {
            Console.WriteLine($"Unauthorized: {directory}, Detail: {e.Message}");
            return [];
        }
    }

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
            string? localPath = SearchFiles(directory, $"^{Regex.Escape(executableName)}$").FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(localPath))
            {
                return Path.GetFullPath(localPath);
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
