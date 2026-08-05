using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Emerde.Cleanup;

internal static class Program
{
    private const uint MoveFileDelayUntilReboot = 0x4;

    public static int Main(string[] args)
    {
        try
        {
            string root = NormalizeRoot(GetArgument(args, "--root"));
            ValidateInstallRoot(root);
            int parentProcessId = int.TryParse(GetArgument(args, "--parent"), out int parsedProcessId)
                ? parsedProcessId
                : 0;
            WaitForParent(parentProcessId);

            bool cleaned = false;
            for (int attempt = 0; attempt < 40 && !cleaned; attempt++)
            {
                cleaned = DeleteManagedDirectoriesCore(root);
                if (!cleaned)
                {
                    Thread.Sleep(500);
                }
            }

            ScheduleSelfDeletion();
            return cleaned ? 0 : 1;
        }
        catch (Exception)
        {
            ScheduleSelfDeletion();
            return 1;
        }
    }

    internal static bool DeleteManagedDirectories(string root)
    {
        string normalizedRoot = NormalizeRoot(root);
        ValidateInstallRoot(normalizedRoot);
        return DeleteManagedDirectoriesCore(normalizedRoot);
    }

    private static bool DeleteManagedDirectoriesCore(string normalizedRoot)
    {
        TryDeleteDirectory(Path.Combine(normalizedRoot, "maintenance"));
        TryDeleteDirectory(Path.Combine(normalizedRoot, "runtime"));

        if (Directory.Exists(normalizedRoot) && !Directory.EnumerateFileSystemEntries(normalizedRoot).Any())
        {
            try
            {
                Directory.Delete(normalizedRoot);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return !Directory.Exists(Path.Combine(normalizedRoot, "maintenance"))
            && !Directory.Exists(Path.Combine(normalizedRoot, "runtime"));
    }

    private static string NormalizeRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidDataException();
        }

        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim().Trim('"')));
        string? driveRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(driveRoot)
            || string.Equals(fullPath, Path.TrimEndingDirectorySeparator(driveRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException();
        }

        return fullPath;
    }

    private static void ValidateInstallRoot(string root)
    {
        string uninstallerPath = Path.Combine(root, "maintenance", "Emerde.Uninstaller.exe");
        if (!File.Exists(uninstallerPath))
        {
            throw new InvalidDataException();
        }
    }

    private static void WaitForParent(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            process.WaitForExit(30000);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? GetArgument(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void ScheduleSelfDeletion()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            MoveFileEx(processPath, null, MoveFileDelayUntilReboot);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
