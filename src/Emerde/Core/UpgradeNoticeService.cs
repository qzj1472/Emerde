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
        return TryReadPendingNotice(
            AppContext.BaseDirectory,
            GetCurrentVersion(),
            Configurations.LastShownUpgradeNoticeVersion.Get() ?? string.Empty);
    }

    public static void MarkShown(UpgradeNoticeState notice)
    {
        Configurations.LastShownUpgradeNoticeVersion.Set(notice.Version);
        ConfigurationSaveScheduler.Request();
        TryUpdatePendingState(
            notice.NoticePath,
            new UpgradeNoticeFileState(notice.Version, notice.PreviousVersion, notice.InstalledAtUtc, false));
    }

    internal static UpgradeNoticeState? TryReadPendingNotice(
        string baseDirectory,
        string currentVersion,
        string lastShownVersion)
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
            || !string.Equals(state.Version, currentVersion, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.Version, lastShownVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new UpgradeNoticeState(
            state.Version,
            state.PreviousVersion ?? string.Empty,
            state.InstalledAtUtc,
            noticePath,
            state.Pending);
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
    string Version,
    string PreviousVersion,
    DateTime InstalledAtUtc,
    string NoticePath,
    bool Pending);

internal sealed record UpgradeNoticeFileState(
    string Version,
    string? PreviousVersion,
    DateTime InstalledAtUtc,
    bool Pending);
