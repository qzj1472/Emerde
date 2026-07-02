namespace Emerde;

internal class AppConfig
{
    public static string PackName => "Emerde";
    public static string LegacyPackName => "TiktokLiveRec";
    public static string Version => $"v{typeof(App).Assembly.GetName().Version!.ToString(3)}";
    public static string Url => "https://github.com/qzj1472/Emerde";
}
