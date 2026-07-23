using Emerde.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Emerde.Tests;

public sealed class LogSanitizerTests
{
    [Fact]
    public void SanitizeText_RedactsUrlQueryAndCookieHeader()
    {
        string value = "open https://example.com/live.m3u8?token=secret&expires=1 Cookie: session=private";

        string sanitized = LogSanitizer.SanitizeText(value);

        Assert.Contains("https://example.com/live.m3u8?[redacted]", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("private", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeData_RedactsSensitivePropertiesAndNestedUrls()
    {
        JsonSerializerOptions options = new();
        JsonNode? sanitized = LogSanitizer.SanitizeData(new
        {
            inputUrl = "https://media.example/live.m3u8?token=secret",
            headers = "Cookie: session=private",
            room = new
            {
                url = "https://room.example/123?share_token=hidden",
            },
        }, options);
        string json = sanitized?.ToJsonString() ?? string.Empty;

        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", json, StringComparison.Ordinal);
        Assert.Equal("[redacted]", sanitized?["inputUrl"]?.GetValue<string>());
        Assert.Equal("[redacted]", sanitized?["headers"]?.GetValue<string>());
        Assert.Equal("https://room.example/123?[redacted]", sanitized?["room"]?["url"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("rtmp://media.example/live/key?wsSecret=private")]
    [InlineData("rtmps://media.example/live/key?txSecret=private")]
    [InlineData("https://media.example/live.flv?auth_key=private")]
    public void SanitizeText_RedactsSignedStreamingUrls(string value)
    {
        string sanitized = LogSanitizer.SanitizeText(value);

        Assert.DoesNotContain("private", sanitized, StringComparison.Ordinal);
        Assert.EndsWith("?[redacted]", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeData_SanitizesStringValuesInsideArrays()
    {
        JsonSerializerOptions options = new();

        JsonNode? sanitized = LogSanitizer.SanitizeData(new
        {
            values = new[]
            {
                "https://media.example/live.m3u8?token=secret",
                "plain",
            },
        }, options);

        Assert.Equal("https://media.example/live.m3u8?[redacted]", sanitized?["values"]?[0]?.GetValue<string>());
        Assert.Equal("plain", sanitized?["values"]?[1]?.GetValue<string>());
    }

    [Theory]
    [InlineData("https://user:password@example.com/live", "password")]
    [InlineData("https://token@example.com/live?key=value", "token")]
    [InlineData("https://", "")]
    public void SanitizeText_RedactsUrlUserInfoWithoutFailingOnEmptyAuthority(string value, string secret)
    {
        string sanitized = LogSanitizer.SanitizeText(value);

        if (!string.IsNullOrEmpty(secret))
        {
            Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
            Assert.Contains("[redacted]@", sanitized, StringComparison.Ordinal);
        }
    }
}
