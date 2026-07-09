using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class LookLiveSpider : ISpider
{
    private const string Modulus = "00e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b725152b3ab17a876aea8a5aa76d2e417629ec4ee341f56135fccf695280104e0312ecbda92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424d813cfe4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7";
    private const string Nonce = "0CoJUm6Qyw8W8jud";

    public static Lazy<LookLiveSpider> Instance { get; } = new(() => new LookLiveSpider());

    public string PlatformName => "Look";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "look.163.com")
        {
            return null;
        }

        string? roomId = GetQueryValue(uri.Query, "id");

        return string.IsNullOrWhiteSpace(roomId) ? null : $"https://look.163.com/live?id={Uri.EscapeDataString(roomId)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        LookLiveSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string? roomId = GetQueryValue(uri.Query, "id");

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        LookRequestData requestData = CreateRequestData(roomId);
        string? json = SpiderRequest.PostForm(
            "https://api.look.163.com/weapi/livestream/room/get/v3",
            new Dictionary<string, string>
            {
                ["params"] = requestData.Params,
                ["encSecKey"] = requestData.EncSecKey,
            },
            Headers(),
            PlatformCookieStore.GetCookie("Look", Configurations.CookieChina.Get()));
        ExtractRoomInfo(json, result);

        return result;
    }

    internal static void ExtractRoomInfo(string? json, LookLiveSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? data = root["data"] as JObject;

            if (data == null)
            {
                return;
            }

            result.Nickname = data["anchor"]?["nickName"]?.ToString();
            result.IsLiveStreaming = data["liveStatus"]?.Value<int>() == 1;

            if (result.IsLiveStreaming == true && data["roomInfo"]?["liveType"]?.Value<int>() != 1)
            {
                JObject? liveUrl = data["roomInfo"]?["liveUrl"] as JObject;
                result.FlvUrl = liveUrl?["httpPullUrl"]?.ToString();
                result.HlsUrl = liveUrl?["hlsPullUrl"]?.ToString();
            }
        }
        catch
        {
        }
    }

    internal static LookRequestData CreateRequestData(string roomId)
    {
        string text = JsonConvert.SerializeObject(new { liveRoomNo = roomId });
        string secretKey = CreateSecretKey();
        string first = AesEncrypt(text, Nonce);
        string second = AesEncrypt(first, secretKey);
        string encSecKey = RsaEncrypt(secretKey);

        return new LookRequestData(second, encSecKey);
    }

    private static string CreateSecretKey()
    {
        const string charset = "1234567890abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!@#$%^&*()_+-=[]{}|;:,.<>?";
        Span<char> chars = stackalloc char[16];

        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = charset[RandomNumberGenerator.GetInt32(charset.Length)];
        }

        return new string(chars);
    }

    private static string AesEncrypt(string text, string key)
    {
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Encoding.UTF8.GetBytes(key[..16]);
        aes.IV = Encoding.UTF8.GetBytes("0102030405060708");

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] input = Encoding.UTF8.GetBytes(text);
        byte[] encrypted = encryptor.TransformFinalBlock(input, 0, input.Length);

        return Convert.ToBase64String(encrypted);
    }

    private static string RsaEncrypt(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Array.Reverse(bytes);
        string hex = Convert.ToHexString(bytes).ToLowerInvariant();
        BigInteger value = BigInteger.Parse("0" + hex, NumberStyles.HexNumber);
        BigInteger exponent = BigInteger.Parse("010001", NumberStyles.HexNumber);
        BigInteger modulus = BigInteger.Parse("0" + Modulus, NumberStyles.HexNumber);
        BigInteger encrypted = BigInteger.ModPow(value, exponent, modulus);
        string hexOutput = encrypted.ToString("x").TrimStart('0');

        return (hexOutput.Length == 0 ? "0" : hexOutput).PadLeft(256, '0');
    }

    private static string? GetQueryValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match match = Regex.Match(value, $"(?:\\?|&){Regex.Escape(name)}=([^&]+)", RegexOptions.IgnoreCase);

        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Accept"] = "application/json, text/javascript",
            ["Accept-Language"] = "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2",
            ["Content-Type"] = "application/x-www-form-urlencoded",
            ["Referer"] = "https://look.163.com/",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:122.0) Gecko/20100101 Firefox/122.0",
        };
    }
}

internal sealed record LookRequestData(string Params, string EncSecKey);

public sealed class LookLiveSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
