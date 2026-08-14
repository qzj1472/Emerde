using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Emerde.Core;

internal static class UpgradeNoticeService
{
    private const string BinaryDirectoryName = "bin";
    private const string MaintenanceDirectoryName = "maintenance";
    private const string UpgradeNoticeFileName = "upgrade-notice.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static UpgradeNoticeState? GetPendingNotice()
    {
        if (IsDevelopmentBuild())
        {
            return TryReadDevelopmentNotice(
                GetCurrentVersion(),
                AppConfig.BuildId,
                Configurations.LastShownUpgradeNoticeDebugBuildId.Get() ?? string.Empty);
        }

        return TryReadPendingNotice(
            AppContext.BaseDirectory,
            GetCurrentVersion(),
            Configurations.LastShownUpgradeNoticeId.Get() ?? string.Empty);
    }

    public static void MarkShown(UpgradeNoticeState notice)
    {
        if (IsDevelopmentNotice(notice))
        {
            Configurations.LastShownUpgradeNoticeDebugBuildId.Set(notice.NoticeId);
            _ = ConfigurationSaveScheduler.TrySaveNow();
            return;
        }

        Configurations.LastShownUpgradeNoticeId.Set(notice.NoticeId);
        Configurations.LastShownUpgradeNoticeVersion.Set(notice.Version);
        _ = ConfigurationSaveScheduler.TrySaveNow();
        TryUpdatePendingState(
            notice.NoticePath,
            new UpgradeNoticeFileState(notice.NoticeId, notice.Version, notice.PreviousVersion, notice.InstalledAtUtc, false));
    }

    internal static UpgradeNoticeState? TryReadPendingNotice(
        string baseDirectory,
        string currentVersion,
        string lastShownNoticeId)
    {
        string? installRoot = TryGetInstallRoot(baseDirectory);
        if (installRoot is null)
        {
            return null;
        }

        string noticePath = Path.Combine(installRoot, MaintenanceDirectoryName, UpgradeNoticeFileName);
        UpgradeNoticeFileState? state = ReadState(noticePath);
        if (state is not { Pending: true }
            || string.IsNullOrWhiteSpace(state.Version)
            || !string.Equals(state.Version, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string noticeId = GetNoticeId(state);
        if (string.Equals(noticeId, lastShownNoticeId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new UpgradeNoticeState(
            noticeId,
            state.Version,
            state.PreviousVersion ?? string.Empty,
            state.InstalledAtUtc,
            noticePath,
            state.Pending);
    }

    internal static UpgradeNoticeState? TryReadDevelopmentNotice(
        string currentVersion,
        string buildId,
        string lastShownBuildId)
    {
        if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(buildId))
        {
            return null;
        }

        string noticeId = $"debug:{currentVersion}:{buildId}";
        if (string.Equals(buildId, lastShownBuildId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(noticeId, lastShownBuildId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new UpgradeNoticeState(
            noticeId,
            currentVersion,
            string.Empty,
            DateTime.UtcNow,
            string.Empty,
            true);
    }

    private static bool IsDevelopmentBuild()
    {
        return string.Equals(AppConfig.BuildConfiguration, "Debug", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDevelopmentNotice(UpgradeNoticeState notice)
    {
        return string.IsNullOrWhiteSpace(notice.NoticePath)
            && notice.NoticeId.StartsWith("debug:", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNoticeId(UpgradeNoticeFileState state)
    {
        return !string.IsNullOrWhiteSpace(state.NoticeId)
            ? state.NoticeId
            : $"legacy:{state.Version}:{state.InstalledAtUtc.ToUniversalTime().Ticks}";
    }

    internal static string? TryGetInstallRoot(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(baseDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }

        DirectoryInfo? directory = new(Path.TrimEndingDirectorySeparator(fullPath));
        return directory is { Parent: not null }
            && string.Equals(directory.Name, BinaryDirectoryName, StringComparison.OrdinalIgnoreCase)
            ? directory.Parent.FullName
            : null;
    }

    internal static string GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "0.0.0.0";
    }

    private static UpgradeNoticeFileState? ReadState(string noticePath)
    {
        try
        {
            return File.Exists(noticePath)
                ? JsonSerializer.Deserialize<UpgradeNoticeFileState>(File.ReadAllText(noticePath), JsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(exception);
            return null;
        }
    }

    private static void TryUpdatePendingState(string noticePath, UpgradeNoticeFileState state)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(noticePath) && File.Exists(noticePath))
            {
                File.WriteAllText(noticePath, JsonSerializer.Serialize(state, JsonOptions));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(exception);
        }
    }
}

public sealed record UpgradeNoticeState(
    string NoticeId,
    string Version,
    string PreviousVersion,
    DateTime InstalledAtUtc,
    string NoticePath,
    bool Pending);

internal sealed record UpgradeNoticeFileState(
    string? NoticeId,
    string Version,
    string? PreviousVersion,
    DateTime InstalledAtUtc,
    bool Pending);
