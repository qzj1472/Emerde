using System.Reflection;

namespace Emerde;

internal class AppConfig
{
    public static string PackName => "Emerde";
    public static string LegacyPackName => "TiktokLiveRec";
    public static string Version => $"v{typeof(App).Assembly.GetName().Version!.ToString(3)}";
    public static string BuildId => GetAssemblyMetadata("BuildIdentifier", typeof(App).Module.ModuleVersionId.ToString("N"));
    public static string BuildConfiguration => GetAssemblyMetadata("BuildConfiguration", "Unknown");
    public static string Url => "https://github.com/qzj1472/Emerde";

    private static string GetAssemblyMetadata(string key, string fallback)
    {
        return typeof(App).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))?
            .Value ?? fallback;
    }
}
