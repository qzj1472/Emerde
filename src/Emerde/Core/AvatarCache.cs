using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Emerde.Core;

internal static class AvatarCache
{
    private const int MaximumAvatarBytes = 5 * 1024 * 1024;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

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
            using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                return GetCachedAvatarSource(roomUrl);
            }

            if (response.Content.Headers.ContentLength > MaximumAvatarBytes)
            {
                return GetCachedAvatarSource(roomUrl);
            }

            byte[] bytes = await ReadLimitedAsync(response.Content, token);
            if (bytes.Length == 0)
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
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
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
            return info.Exists && info.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
