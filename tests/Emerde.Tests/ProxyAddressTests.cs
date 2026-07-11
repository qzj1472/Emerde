using Emerde.Core;

namespace Emerde.Tests;

public sealed class ProxyAddressTests
{
    [Theory]
    [InlineData("127.0.0.1:7890", "http://127.0.0.1:7890")]
    [InlineData("http://127.0.0.1:7890", "http://127.0.0.1:7890")]
    [InlineData("https://proxy.example:8443", "https://proxy.example:8443")]
    public void Normalize_AcceptsSupportedProxyFormats(string value, string expected)
    {
        Assert.Equal(expected, ProxyAddress.Normalize(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a proxy")]
    [InlineData("socks5://127.0.0.1:1080")]
    public void Normalize_RejectsUnsupportedProxyFormats(string value)
    {
        Assert.Empty(ProxyAddress.Normalize(value));
        Assert.Null(ProxyAddress.Create(value));
    }
}
