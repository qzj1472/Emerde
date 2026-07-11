namespace Emerde.Core;

internal static class ProxyAddress
{
    public static System.Net.WebProxy? Create(string? value)
    {
        string normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : new System.Net.WebProxy(normalized);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string input = value.Trim();
        string candidate = input.Contains("://", StringComparison.Ordinal) ? input : $"http://{input}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.Scheme is not "http" and not "https")
        {
            return string.Empty;
        }

        return uri.AbsoluteUri.TrimEnd('/');
    }
}
