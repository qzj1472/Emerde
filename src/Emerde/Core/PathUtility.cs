namespace Emerde.Core;

internal static class PathUtility
{
    public static bool IsSameOrDescendant(string path, string root)
    {
        return TryGetRelativePathWithinRoot(path, root, out _);
    }

    public static bool TryGetRelativePathWithinRoot(string path, string root, out string relativePath)
    {
        relativePath = string.Empty;
        try
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
            if (Path.IsPathRooted(relative)
                || relative.Equals("..", StringComparison.Ordinal)
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }

            relativePath = relative;
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
