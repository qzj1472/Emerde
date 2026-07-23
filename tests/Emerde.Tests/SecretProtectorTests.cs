using Emerde.Core;

namespace Emerde.Tests;

public sealed class SecretProtectorTests
{
    [Fact]
    public void Protect_RoundTripsForCurrentWindowsUser()
    {
        string protectedValue = SecretProtector.Protect("secret-value");

        Assert.StartsWith("dpapi:", protectedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", protectedValue, StringComparison.Ordinal);
        Assert.Equal("secret-value", SecretProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void Unprotect_PreservesLegacyPlainText()
    {
        Assert.Equal("legacy", SecretProtector.Unprotect("legacy"));
    }

    [Fact]
    public void TryUnprotect_ReportsInvalidProtectedValue()
    {
        Assert.False(SecretProtector.TryUnprotect("dpapi:not-base64", out string result));
        Assert.Equal(string.Empty, result);
    }
}
