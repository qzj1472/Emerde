using Emerde.ViewModels;

namespace Emerde.Tests;

public sealed class SettingsViewModelTests
{
    [Theory]
    [InlineData("127.0.0.1:7890", "http://127.0.0.1:7890/")]
    [InlineData("localhost:8080", "http://localhost:8080/")]
    [InlineData("proxy.example.com:3128", "http://proxy.example.com:3128/")]
    [InlineData("http://localhost:65535", "http://localhost:65535/")]
    [InlineData("[::1]:7890", "http://[::1]:7890/")]
    public void TryCreateProxyUri_AcceptsHostAndPort(string value, string expected)
    {
        bool result = SettingsViewModel.TryCreateProxyUri(value, out Uri? proxyUri, out string errorKey);

        Assert.True(result);
        Assert.Equal(expected, proxyUri?.AbsoluteUri);
        Assert.Equal(string.Empty, errorKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:notaport")]
    [InlineData("http://localhost")]
    public void TryCreateProxyUri_RejectsInvalidEndpoint(string value)
    {
        bool result = SettingsViewModel.TryCreateProxyUri(value, out Uri? proxyUri, out string errorKey);

        Assert.False(result);
        Assert.Null(proxyUri);
        Assert.False(string.IsNullOrWhiteSpace(errorKey));
    }
}
