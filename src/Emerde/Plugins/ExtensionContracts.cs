using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Emerde.Core;
using Emerde.Models;

namespace Emerde.Plugins;

public static class ExtensionContractNames
{
    public const string StreamResolver = "core.stream-resolver";
    public const string Monitor = "core.monitor";
    public const string Recorder = "core.recorder";
    public const string MainWindow = "host.main-window";
    public const string MainViewModel = "host.main-view-model";
    public const string Application = "host.application";
    public const string MainContentOverlay = "ui.main-content-overlay";
    public const string ExtensionDetail = "ui.extension-detail";
    public const string VideoListToolbar = "ui.video-list-toolbar";
    public const string VideoListActions = "ui.video-list-actions";
    public const string PlatformCookies = "host.platform-cookies";
    public const string VideoSelection = "host.video-selection";
    public const string DialogService = "host.dialog-service";
}

public static class ExtensionEventNames
{
    public const string MediaFinalized = "media.finalized";
}

public static class ExtensionPermissionNames
{
    public const string PlatformCookieRead = "credentials.platform-cookie.read";
}

public sealed record ExtensionMediaFinalizedEvent(
    string EventId,
    string RecordingId,
    string RoomUrl,
    string NickName,
    string PlatformName,
    string Title,
    string FilePath,
    long FileSize,
    string Container,
    DateTime RecordedAt,
    DateTimeOffset FinalizedAt,
    bool WasTranscoded,
    bool WasMerged);

public delegate ValueTask ExtensionEventHandler<in T>(T payload, CancellationToken cancellationToken);

public interface IExtensionPlatformCookieProvider
{
    string GetCookie(string platformName);
}

public sealed record ExtensionVideoFileInfo(
    string FilePath,
    string RoomUrl,
    string NickName,
    string PlatformName,
    string Title,
    long FileSize,
    DateTime RecordedAt,
    DateTime LastWriteTimeUtc);

public interface IExtensionVideoSelectionProvider
{
    IReadOnlyList<ExtensionVideoFileInfo> GetSelectedFiles();
}

public interface IExtensionVideoAction
{
    string Id { get; }

    string Label { get; }

    int Order { get; }

    bool CanExecute(IReadOnlyList<ExtensionVideoFileInfo> files);

    Task ExecuteAsync(IReadOnlyList<ExtensionVideoFileInfo> files, CancellationToken cancellationToken);
}

public enum ExtensionDialogResult
{
    None,
    Primary,
    Secondary,
    Close,
}

public sealed record ExtensionDialogRequest(
    string Title,
    FrameworkElement Content,
    string PrimaryButtonText,
    string CloseButtonText,
    string SecondaryButtonText = "",
    Func<string?>? Validate = null,
    Action<string>? ShowValidation = null,
    bool UseWideLayout = false,
    double WideHeightRatio = 0.82d);

public interface IExtensionDialogService
{
    Task<ExtensionDialogResult> ShowAsync(
        ExtensionDialogRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ExtensionStreamResolveRequest(
    string Url,
    string? PreferredQuality,
    bool BypassDouyinThrottle,
    bool PrioritizeDouyin,
    bool AllowDouyinWebViewFallback,
    CancellationToken CancellationToken);

public delegate ISpiderResult? ExtensionStreamResolverOverride(ExtensionStreamResolveRequest request, Func<ISpiderResult?> next);

public sealed record ExtensionMonitorRequest(IReadOnlyList<Room> Rooms, bool Force, bool? RecordingLaneOnly, CancellationToken CancellationToken);

public delegate Task ExtensionMonitorOverride(ExtensionMonitorRequest request, Func<Task> next);

public sealed record ExtensionRecorderStartRequest(Room Room, RoomStatus RoomStatus, RoomRecordingOptions Settings, RecorderStartInfo StartInfo);

public delegate bool ExtensionRecorderOverride(ExtensionRecorderStartRequest request, Func<bool> next);

public sealed class ExtensionManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    [JsonPropertyName("execution_mode")]
    public string ExecutionMode { get; set; } = "in_process";

    [JsonPropertyName("entry_point")]
    public string EntryPoint { get; set; } = string.Empty;

    [JsonPropertyName("entry_type")]
    public string EntryType { get; set; } = string.Empty;

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = "executable";

    [JsonPropertyName("arguments")]
    public string[] Arguments { get; set; } = [];

    [JsonPropertyName("minimum_host_version")]
    public string MinimumHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public string[] Capabilities { get; set; } = [];

    [JsonPropertyName("permissions")]
    public string[] Permissions { get; set; } = [];

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 900;

    [JsonPropertyName("settings")]
    public ExtensionSettingDefinition[] Settings { get; set; } = [];

}

public sealed class ExtensionSettingDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("section")]
    public string Section { get; set; } = string.Empty;

    [JsonPropertyName("column")]
    public int Column { get; set; }

    [JsonPropertyName("visible_when_key")]
    public string VisibleWhenKey { get; set; } = string.Empty;

    [JsonPropertyName("visible_when_value")]
    public string VisibleWhenValue { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("default")]
    public string DefaultValue { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("sensitive")]
    public bool Sensitive { get; set; }

    [JsonPropertyName("options")]
    public string[] Options { get; set; } = [];
}

public interface IEmerdeExtension
{
    ValueTask InitializeAsync(IExtensionContext context, CancellationToken cancellationToken);

    ValueTask ShutdownAsync(CancellationToken cancellationToken);
}

public interface IExtensionContext
{
    string ExtensionId { get; }

    string ExtensionDirectory { get; }

    string DataDirectory { get; }

    Version HostVersion { get; }

    IReadOnlyDictionary<string, string> Settings { get; }

    IReadOnlySet<string> Permissions { get; }

    object? GetHostObject(string contractName);

    IDisposable RegisterOverride(string contractName, object implementation, int priority = 0);

    IDisposable RegisterUi(string regionName, FrameworkElement content, int order = 0);

    IDisposable Subscribe<T>(string eventName, ExtensionEventHandler<T> handler);

    IDisposable RegisterCleanup(Func<ValueTask> cleanup);

    void Log(string level, string eventName, string message, object? data = null);
}

public sealed class ExtensionProcessRequest
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; } = 1;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("settings")]
    public IReadOnlyDictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

public sealed class ExtensionProcessResponse
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}

public sealed record ExtensionExecutionResult(bool Success, string Message, JsonElement Data, int ExitCode = 0);

public sealed record ExtensionUiContribution(string ExtensionId, string RegionName, FrameworkElement Content, int Order);
