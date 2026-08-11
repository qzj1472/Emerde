namespace Emerde.Installer;

internal enum InstallationOperation
{
    Install,
    Upgrade,
    Repair,
    Uninstall,
}

internal sealed record InstallationRequest(
    string InstallRoot,
    bool CreateShortcuts,
    bool AutoStart);

internal sealed record InstallationState(
    string InstallRoot,
    bool CreateShortcuts,
    bool AutoStart,
    string Version);

internal sealed record RepairFileState(
    string RelativePath,
    long Length,
    string Sha256,
    string Component);

internal sealed record RepairState(
    string Version,
    IReadOnlyList<RepairFileState> Files);

internal sealed record UpgradeNoticeState(
    string Version,
    string PreviousVersion,
    DateTime InstalledAtUtc,
    bool Pending);

internal sealed record InstallationInfo(
    string InstallRoot,
    string Version,
    InstallationState State);

internal readonly record struct InstallationProgress(int Percentage, string Status);
