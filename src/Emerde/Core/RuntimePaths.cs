namespace Emerde.Core;

internal static class RuntimePaths
{
    public static string FfmpegDirectory => ResolveDirectory(
        Path.Combine(AppContext.BaseDirectory, "..", "native", "ffmpeg"),
        Path.Combine(AppContext.BaseDirectory, "ffmpeg"));

    public static string LibVlcDirectory => ResolveDirectory(
        Path.Combine(AppContext.BaseDirectory, "..", "native", "libvlc", "win-x64"),
        Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"));

    private static string ResolveDirectory(params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return Path.GetFullPath(candidates[0]);
    }
}
