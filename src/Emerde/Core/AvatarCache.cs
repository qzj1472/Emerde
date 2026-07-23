using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace Emerde.Core;

internal static class AvatarCache
{
    private const int MaximumAvatarBytes = 5 * 1024 * 1024;

    public static string GetCachedAvatarPath(string roomUrl)
    {
        return GetCachedAvatarPath(roomUrl, GetAvatarDirectory());
    }

    internal static string GetCachedAvatarPath(string roomUrl, string avatarDirectory)
    {
        string hash = HashRoomUrl(roomUrl);
        return Path.Combine(avatarDirectory, $"{hash}.avatar");
    }

    public static string GetCachedAvatarSource(string roomUrl)
    {
        return GetCachedAvatarSource(roomUrl, GetAvatarDirectory());
    }

    internal static string GetCachedAvatarSource(string roomUrl, string avatarDirectory)
    {
        string path = GetCachedAvatarPath(roomUrl, avatarDirectory);
        return IsUsableAvatarFile(path) ? path : string.Empty;
    }

    public static async Task<string> UpdateAsync(string roomUrl, string avatarUrl, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(roomUrl) || string.IsNullOrWhiteSpace(avatarUrl))
        {
            return GetCachedAvatarSource(roomUrl);
        }

        string? tempPath = null;
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, avatarUrl);
            string userAgent = Configurations.UserAgent.Get();
            request.Headers.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(userAgent) ? "Mozilla/5.0" : userAgent);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using HttpResponseMessage response = await ProxyHttpClientPool.GetCurrent().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return GetCachedAvatarSource(roomUrl);
            }

            if (response.Content.Headers.ContentLength > MaximumAvatarBytes)
            {
                return GetCachedAvatarSource(roomUrl);
            }

            byte[] bytes = await ReadLimitedAsync(response.Content, timeout.Token);
            if (!IsDecodableImage(bytes))
            {
                return GetCachedAvatarSource(roomUrl);
            }

            string path = GetCachedAvatarPath(roomUrl);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (IsUsableAvatarFile(path))
            {
                byte[] existing = await File.ReadAllBytesAsync(path, token);
                if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing), SHA256.HashData(bytes)))
                {
                    return path;
                }
            }

            tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tempPath, bytes, token);
            File.Move(tempPath, path, true);
            tempPath = null;
            return path;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return GetCachedAvatarSource(roomUrl);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public static void Prune(IEnumerable<string> roomUrls)
    {
        string directory = GetAvatarDirectory();
        if (!Directory.Exists(directory))
        {
            return;
        }

        HashSet<string> retainedPaths = roomUrls
            .Where(roomUrl => !string.IsNullOrWhiteSpace(roomUrl))
            .Select(roomUrl => GetCachedAvatarPath(roomUrl, directory))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] paths;
        try
        {
            paths = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            return;
        }

        foreach (string path in paths)
        {
            if (retainedPaths.Contains(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken token)
    {
        await using Stream input = await content.ReadAsStreamAsync(token);
        using MemoryStream output = new();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await input.ReadAsync(buffer, token);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > MaximumAvatarBytes)
            {
                return [];
            }

            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    internal static string HashRoomUrl(string roomUrl)
    {
        string normalized = NormalizeRoomUrl(roomUrl);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string GetAvatarDirectory()
    {
        return Path.Combine(AppPaths.CacheDirectory, "avatars");
    }

    private static string NormalizeRoomUrl(string? roomUrl)
    {
        if (string.IsNullOrWhiteSpace(roomUrl))
        {
            return string.Empty;
        }

        string value = roomUrl.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return value.ToLowerInvariant();
        }

        string host = uri.Host.ToLowerInvariant();
        string path = uri.AbsolutePath.Trim('/').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(path) ? host : $"{host}/{path}";
    }

    private static bool IsUsableAvatarFile(string path)
    {
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumAvatarBytes)
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(path);
            return IsDecodableImage(bytes);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsSupportedImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return true;
        }
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return true;
        }
        if (bytes.Length >= 6 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return true;
        }
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return true;
        }
        return bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
    }

    internal static bool IsDecodableImage(ReadOnlySpan<byte> bytes)
    {
        if (!IsSupportedImage(bytes))
        {
            return false;
        }

        try
        {
            using MemoryStream stream = new(bytes.ToArray(), writable: false);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0
                && decoder.Frames[0].PixelWidth > 0
                && decoder.Frames[0].PixelHeight > 0;
        }
        catch
        {
            return false;
        }
    }
}
