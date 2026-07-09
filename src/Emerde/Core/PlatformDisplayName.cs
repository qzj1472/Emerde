using System.Globalization;

namespace Emerde.Core;

internal static class PlatformDisplayName
{
    private static readonly IReadOnlyDictionary<string, string> SimplifiedChineseNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Douyin"] = "抖音",
            ["Bilibili"] = "哔哩哔哩",
            ["Kuaishou"] = "快手",
            ["Huya"] = "虎牙",
            ["Douyu"] = "斗鱼",
            ["Baidu"] = "百度",
            ["MaoerFM"] = "猫耳FM",
            ["Lianjie"] = "链街",
            ["6Rooms"] = "六间房",
            ["VVXqiu"] = "VV星球",
            ["Blued"] = "Blued",
            ["Liuxing"] = "流星",
            ["Changliao"] = "畅聊",
            ["Yinbo"] = "音播",
            ["Zhihu"] = "知乎",
            ["PPLive"] = "PPLive",
            ["CatShow"] = "CatShow",
            ["Laixiu"] = "来秀",
            ["JD"] = "京东",
            ["Weibo"] = "微博",
            ["Huajiao"] = "花椒",
            ["Look"] = "Look",
            ["Taobao"] = "淘宝",
            ["Xiaohongshu"] = "小红书",
            ["Kugou"] = "酷狗",
            ["Yingke"] = "映客",
            ["AcFun"] = "AcFun",
            ["YY"] = "YY",
            ["NeteaseCC"] = "网易CC",
            ["QianduRebo"] = "千度热播",
            ["Direct"] = "直链",
        };

    private static readonly IReadOnlyDictionary<string, string> TraditionalChineseNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Douyin"] = "抖音",
            ["Bilibili"] = "嗶哩嗶哩",
            ["Kuaishou"] = "快手",
            ["Huya"] = "虎牙",
            ["Douyu"] = "鬥魚",
            ["Baidu"] = "百度",
            ["MaoerFM"] = "貓耳FM",
            ["Lianjie"] = "鏈街",
            ["6Rooms"] = "六間房",
            ["VVXqiu"] = "VV星球",
            ["Blued"] = "Blued",
            ["Liuxing"] = "流星",
            ["Changliao"] = "暢聊",
            ["Yinbo"] = "音播",
            ["Zhihu"] = "知乎",
            ["PPLive"] = "PPLive",
            ["CatShow"] = "CatShow",
            ["Laixiu"] = "來秀",
            ["JD"] = "京東",
            ["Weibo"] = "微博",
            ["Huajiao"] = "花椒",
            ["Look"] = "Look",
            ["Taobao"] = "淘寶",
            ["Xiaohongshu"] = "小紅書",
            ["Kugou"] = "酷狗",
            ["Yingke"] = "映客",
            ["AcFun"] = "AcFun",
            ["YY"] = "YY",
            ["NeteaseCC"] = "網易CC",
            ["QianduRebo"] = "千度熱播",
            ["Direct"] = "直鏈",
        };

    public static string Get(string? platformName)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return string.Empty;
        }

        IReadOnlyDictionary<string, string> localizedNames = GetLocalizedNames(Locale.Culture) ?? SimplifiedChineseNames;
        return localizedNames.TryGetValue(platformName, out string? displayName)
            ? displayName
            : platformName;
    }

    private static IReadOnlyDictionary<string, string>? GetLocalizedNames(CultureInfo culture)
    {
        if (culture.Name.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase))
        {
            return TraditionalChineseNames;
        }

        if (culture.Name.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
            culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            return SimplifiedChineseNames;
        }

        return null;
    }
}
