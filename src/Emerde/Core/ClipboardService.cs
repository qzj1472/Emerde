using System.Runtime.InteropServices;

namespace Emerde.Core;

internal static class ClipboardService
{
    private const int MaxAttempts = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    public static Task<bool> SetTextAsync(string value)
    {
        return SetTextAsync(value, System.Windows.Clipboard.SetText, RetryDelay, MaxAttempts);
    }

    internal static async Task<bool> SetTextAsync(string value, Action<string> setText, TimeSpan retryDelay, int maxAttempts)
    {
        if (string.IsNullOrWhiteSpace(value) || maxAttempts <= 0)
        {
            return false;
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                setText(value);
                return true;
            }
            catch (ExternalException) when (attempt + 1 < maxAttempts)
            {
                await Task.Delay(retryDelay);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        return false;
    }
}
