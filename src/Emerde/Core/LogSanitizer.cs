using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Emerde.Core;

internal static partial class LogSanitizer
{
    private static readonly string[] SensitivePropertyNames = ["authorization", "cookie", "headers", "inputurl", "password", "secret", "signature", "token"];

    public static string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        string sanitized = UrlRegex().Replace(value, static match => RedactUrl(match.Value));
        sanitized = HeaderRegex().Replace(sanitized, static match => $"{match.Groups[1].Value}[redacted]");
        return SensitiveAssignmentRegex().Replace(sanitized, static match => $"{match.Groups[1].Value}=[redacted]");
    }

    public static JsonNode? SanitizeData(object? data, JsonSerializerOptions options)
    {
        if (data is null)
        {
            return null;
        }

        JsonNode? node = JsonSerializer.SerializeToNode(data, options);
        SanitizeNode(node);
        return node;
    }

    private static void SanitizeNode(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (KeyValuePair<string, JsonNode?> property in jsonObject.ToArray())
            {
                if (IsSensitiveProperty(property.Key))
                {
                    jsonObject[property.Key] = "[redacted]";
                }
                else
                {
                    SanitizeNode(property.Value);
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (JsonNode? item in jsonArray.ToArray())
            {
                SanitizeNode(item);
            }

            return;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue(out string? value))
        {
            jsonValue.ReplaceWith(SanitizeText(value));
        }
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        string normalized = propertyName.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return SensitivePropertyNames.Any(normalized.Contains);
    }

    private static string RedactUrl(string value)
    {
        int suffixStart = value.Length;
        while (suffixStart > 0 && value[suffixStart - 1] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}')
        {
            suffixStart--;
        }

        string url = value[..suffixStart];
        string suffix = value[suffixStart..];
        int schemeSeparator = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
        {
            int authorityStart = schemeSeparator + 3;
            int authorityEnd = url.IndexOfAny(['/', '?', '#'], authorityStart);
            if (authorityEnd < 0)
            {
                authorityEnd = url.Length;
            }
            string authority = url[authorityStart..authorityEnd];
            int relativeUserInfoEnd = authority.LastIndexOf('@');
            if (relativeUserInfoEnd >= 0)
            {
                int userInfoEnd = authorityStart + relativeUserInfoEnd;
                url = url[..authorityStart] + "[redacted]@" + url[(userInfoEnd + 1)..];
            }
        }
        int queryIndex = url.IndexOf('?');
        int fragmentIndex = url.IndexOf('#');
        int secretIndex = queryIndex < 0
            ? fragmentIndex
            : fragmentIndex < 0 ? queryIndex : Math.Min(queryIndex, fragmentIndex);

        return secretIndex < 0
            ? url + suffix
            : $"{url[..(secretIndex + 1)]}[redacted]{suffix}";
    }

    [GeneratedRegex(@"(?:https?|rtmps?)://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\b(cookie|authorization)\s*([:=])\s*[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"\b(token|signature|sign|sig|auth|auth_key|authorization|cookie|wssecret|txsecret)=([^&\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignmentRegex();
}
