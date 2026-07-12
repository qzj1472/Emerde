using System.Collections.Specialized;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;

namespace Emerde.Core;

public sealed record StreamQualityOption(string Value, string DisplayName);

internal static class StreamQualityCatalog
{
    public const string Original = "Original";
    public const string BlueRay = "BlueRay";
    public const string UltraHigh = "UltraHigh";
    public const string High = "High";
    public const string Standard = "Standard";
    public const string Smooth = "Smooth";

    private static readonly IReadOnlyList<StreamQualityOption> AllOptions =
    [
        new(Original, "原画"),
        new(BlueRay, "蓝光"),
        new(UltraHigh, "超清"),
        new(High, "高清"),
        new(Standard, "标清"),
        new(Smooth, "流畅"),
    ];

    private static readonly IReadOnlyList<StreamQualityOption> BilibiliOptions =
    [
        AllOptions[0],
        AllOptions[1],
        AllOptions[2],
        AllOptions[3],
        AllOptions[5],
    ];

    private static readonly IReadOnlyList<StreamQualityOption> KuaishouOptions =
    [
        AllOptions[0],
        AllOptions[3],
        AllOptions[4],
        AllOptions[5],
    ];

    private static readonly IReadOnlyList<StreamQualityOption> OriginalOnlyOptions = [AllOptions[0]];

    public static IReadOnlyList<StreamQualityOption> GlobalOptions => AllOptions;

    public static IReadOnlyList<StreamQualityOption> GetOptions(string? platformName)
    {
        return platformName?.Trim() switch
        {
            "Douyin" => AllOptions,
            "Bilibili" => BilibiliOptions,
            "Kuaishou" => KuaishouOptions,
            _ => OriginalOnlyOptions,
        };
    }

    public static string GetSupportedPreference(string? platformName, string? preference)
    {
        string normalized = NormalizePreference(preference);
        return GetOptions(platformName).Any(option => option.Value == normalized) ? normalized : Original;
    }

    public static string NormalizePreference(string? value, string fallback = Original)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string normalized = value.Trim();
        if (AllOptions.Any(option => option.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return AllOptions.First(option => option.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)).Value;
        }

        return GetDisplayName(null, normalized, null) switch
        {
            "原画" => Original,
            "蓝光" => BlueRay,
            "超清" => UltraHigh,
            "高清" => High,
            "标清" => Standard,
            "流畅" => Smooth,
            _ => fallback,
        };
    }

    public static string GetDisplayName(string? platformName, string? quality, string? resolution)
    {
        if (!string.IsNullOrWhiteSpace(quality))
        {
            string token = quality.Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
            string? mapped = platformName?.Trim() switch
            {
                "Bilibili" => token switch
                {
                    "10000" => "原画",
                    "400" => "蓝光",
                    "250" => "超清",
                    "150" => "高清",
                    "80" => "流畅",
                    _ => null,
                },
                _ => null,
            };

            mapped ??= token switch
            {
                "ORIGINAL" or "ORIGIN" or "SOURCE" or "RAW" or "原画" => "原画",
                "BLUERAY" or "BLUE_RAY" or "FULL_HD1" or "BD" or "蓝光" => "蓝光",
                "ULTRAHIGH" or "ULTRA_HIGH" or "FULL_HD" or "UHD" or "FHD" or "SUPER" or "超清" => "超清",
                "HIGH" or "HIGH_QUALITY" or "HD1" or "HD" or "高清" => "高清",
                "STANDARD" or "STANDARD_DEFINITION" or "SD1" or "标清" => "标清",
                "SMOOTH" or "LOW" or "LOW_QUALITY" or "SD2" or "SD" or "流畅" => "流畅",
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            return quality.Trim();
        }

        int? height = StreamMetadataParser.ParseResolutionHeight(resolution);
        return height switch
        {
            >= 1080 => "超清",
            >= 720 => "高清",
            >= 480 => "标清",
            > 0 => "流畅",
            _ => "原画",
        };
    }

    public static IReadOnlyList<string> GetStreamKeyOrder(string? preference)
    {
        return NormalizePreference(preference) switch
        {
            BlueRay => ["FULL_HD1", "ORIGIN", "FULL_HD", "HD1", "HD", "SD1", "SD2", "SD"],
            UltraHigh => ["FULL_HD", "FULL_HD1", "ORIGIN", "HD1", "HD", "SD1", "SD2", "SD"],
            High => ["HD1", "HD", "FULL_HD", "FULL_HD1", "ORIGIN", "SD1", "SD2", "SD"],
            Standard => ["SD1", "SD", "HD", "HD1", "SD2", "FULL_HD", "FULL_HD1", "ORIGIN"],
            Smooth => ["SD2", "SD", "SD1", "HD", "HD1", "FULL_HD", "FULL_HD1", "ORIGIN"],
            _ => ["ORIGIN", "FULL_HD1", "FULL_HD", "HD1", "HD", "SD1", "SD2", "SD"],
        };
    }

    public static int GetBilibiliQualityNumber(string? preference)
    {
        return NormalizePreference(preference) switch
        {
            BlueRay => 400,
            UltraHigh => 250,
            High => 150,
            Standard or Smooth => 80,
            _ => 10000,
        };
    }

    public static int GetVariantIndex(string? preference, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        return NormalizePreference(preference) switch
        {
            High => count <= 3 ? 0 : 1,
            Standard => Math.Max(0, count - 2),
            Smooth => count - 1,
            _ => 0,
        };
    }

    public static string? FormatBitrate(double? value)
    {
        if (value is not > 0)
        {
            return null;
        }

        double bitrate = value.Value;
        if (bitrate >= 100_000d)
        {
            return $"{bitrate / 1_000_000d:0.##} Mbps";
        }

        if (bitrate >= 1_000d)
        {
            return $"{bitrate / 1_000d:0.##} Mbps";
        }

        return $"{bitrate:0.##} Kbps";
    }
}

internal static partial class StreamMetadataParser
{
    private static readonly string[] ResolutionKeys = ["resolution", "res", "video_size", "size"];
    private static readonly string[] WidthKeys = ["width", "vwidth", "video_width", "vw"];
    private static readonly string[] HeightKeys = ["height", "vheight", "video_height", "vh"];
    private static readonly string[] BitrateKeys = ["bitrate", "video_bitrate", "vbitrate", "vb", "br"];
    private static readonly string[] QualityKeys = ["quality", "definition", "level", "qn"];

    public static string? GetQuality(params string?[] urls)
    {
        foreach (string? url in urls)
        {
            NameValueCollection? query = ParseQuery(url);
            if (query == null)
            {
                continue;
            }

            foreach (string key in QualityKeys)
            {
                string? value = query[key];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    public static string? GetResolution(params string?[] urls)
    {
        foreach (string? url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            NameValueCollection? query = ParseQuery(url);
            if (query != null)
            {
                foreach (string key in ResolutionKeys)
                {
                    string? parsed = NormalizeResolution(query[key]);
                    if (!string.IsNullOrWhiteSpace(parsed))
                    {
                        return parsed;
                    }
                }

                int? width = GetPositiveInt(query, WidthKeys);
                int? height = GetPositiveInt(query, HeightKeys);
                if (width > 0 && height > 0)
                {
                    return $"{width}x{height}";
                }

                if (height > 0)
                {
                    return $"{height}p";
                }
            }

            string? fromUrl = NormalizeResolution(Uri.UnescapeDataString(url));
            if (!string.IsNullOrWhiteSpace(fromUrl))
            {
                return fromUrl;
            }
        }

        return null;
    }

    public static string? GetBitrate(params string?[] urls)
    {
        foreach (string? url in urls)
        {
            NameValueCollection? query = ParseQuery(url);
            if (query == null)
            {
                continue;
            }

            foreach (string key in BitrateKeys)
            {
                string? formatted = NormalizeBitrate(query[key]);
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    return formatted;
                }
            }
        }

        return null;
    }

    public static int? ParseResolutionHeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match dimensions = ResolutionRegex().Match(value);
        if (dimensions.Success && int.TryParse(dimensions.Groups[2].Value, out int height))
        {
            return height;
        }

        Match vertical = VerticalResolutionRegex().Match(value);
        return vertical.Success && int.TryParse(vertical.Groups[1].Value, out height) ? height : null;
    }

    public static (int Height, double Bitrate) GetQualityMetrics(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return default;
        }

        int height = ParseResolutionHeight(GetResolution(url)) ?? 0;
        NameValueCollection? query = ParseQuery(url);
        double bitrate = query == null ? 0 : GetBitrateBitsPerSecond(query);
        return (height, bitrate);
    }

    private static NameValueCollection? ParseQuery(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? HttpUtility.ParseQueryString(uri.Query) : null;
    }

    private static int? GetPositiveInt(NameValueCollection query, IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            if (int.TryParse(query[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
            {
                return value;
            }
        }

        return null;
    }

    private static double GetBitrateBitsPerSecond(NameValueCollection query)
    {
        foreach (string key in BitrateKeys.Concat(["bandwidth", "average_bandwidth", "origin_bitrate"]))
        {
            string? value = query[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            Match match = NumberRegex().Match(value.Replace(',', '.'));
            if (!match.Success || !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number <= 0)
            {
                continue;
            }

            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("mb", StringComparison.Ordinal))
            {
                return number * 1_000_000d;
            }

            if (normalized.Contains("kb", StringComparison.Ordinal))
            {
                return number * 1_000d;
            }

            return number >= 100_000d ? number : number * 1_000d;
        }

        return 0;
    }

    private static string? NormalizeResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match dimensions = ResolutionRegex().Match(value);
        if (dimensions.Success)
        {
            return $"{dimensions.Groups[1].Value}x{dimensions.Groups[2].Value}";
        }

        Match vertical = VerticalResolutionRegex().Match(value);
        return vertical.Success ? $"{vertical.Groups[1].Value}p" : null;
    }

    private static string? NormalizeBitrate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match match = NumberRegex().Match(value.Replace(',', '.'));
        if (!match.Success || !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number <= 0)
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("mb", StringComparison.Ordinal) || normalized.Contains("mbps", StringComparison.Ordinal))
        {
            return $"{number:0.##} Mbps";
        }

        if (normalized.Contains("kb", StringComparison.Ordinal) || normalized.Contains("kbps", StringComparison.Ordinal))
        {
            return number >= 1_000d ? $"{number / 1_000d:0.##} Mbps" : $"{number:0.##} Kbps";
        }

        return StreamQualityCatalog.FormatBitrate(number);
    }

    [GeneratedRegex(@"(?<!\d)(\d{3,5})\s*[xX*]\s*(\d{3,5})(?!\d)")]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"(?<!\d)(\d{3,4})p(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex VerticalResolutionRegex();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex NumberRegex();
}
