using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using Emerde.Core;
using Emerde.Models;

namespace Emerde.Plugins;

public static class ExtensionContractNames
{
    public const string StreamResolver = "core.stream-resolver";
    public const string Monitor = "core.monitor";
    public const string Recorder = "core.recorder";
    public const string RecorderStop = "core.recorder-stop";
    public const string RecorderReconnect = "core.recorder-reconnect";
    public const string PostProcessing = "core.post-processing";
    public const string MainWindow = "host.main-window";
    public const string MainViewModel = "host.main-view-model";
    public const string Application = "host.application";
    public const string MainContentOverlay = "ui.main-content-overlay";
    public const string ExtensionDetail = "ui.extension-detail";
    public const string VideoListToolbar = "ui.video-list-toolbar";
    public const string VideoListActions = "ui.video-list-actions";
    public const string HomeToolbar = "ui.home-toolbar";
    public const string HomeRoomActions = "ui.home-room-actions";
    public const string PlatformCookies = "host.platform-cookies";
    public const string VideoSelection = "host.video-selection";
    public const string DialogService = "host.dialog-service";
    public const string HomeCardTemplate = "ui.home-card-template";
    public const string PreviewService = "host.preview-service";
    public const string MediaService = "host.media-service";
    public const string RecordingService = "host.recording-service";
    public const string NavigationService = "host.navigation-service";
    public const string NotificationService = "host.notification-service";
    public const string LogService = "host.log-service";
    public const string LogExportService = "host.log-export-service";
    public const string UpdateService = "host.update-service";
}

public static class ExtensionEventNames
{
    public const string MediaFinalized = "media.finalized";
    public const string PreviewStateChanged = "preview.state-changed";
    public const string MediaOperationChanged = "media.operation-changed";
    public const string RecordingLifecycle = "recording.lifecycle";
}

public static class ExtensionPermissionNames
{
    public const string PlatformCookieRead = "credentials.platform-cookie.read";
    public const string UiModify = "ui.modify";
    public const string CoreOverride = "core.override";
    public const string MediaFinalizedRead = "events.media-finalized.read";
    public const string ShortcutRegister = "shortcuts.register";
    public const string PreviewControl = "preview.control";
    public const string MediaControl = "media.control";
    public const string RecordingControl = "recording.control";
    public const string NotificationWrite = "notifications.write";
    public const string LogWrite = "logs.write";
    public const string LogExport = "logs.export";
    public const string UpdateOpen = "updates.open";
    public const string PreviewEventsRead = "events.preview.read";
    public const string MediaOperationsRead = "events.media-operations.read";
    public const string RecordingEventsRead = "events.recording.read";
}

public sealed record ExtensionPreviewStateChangedEvent(
    string EventId,
    string Change,
    ExtensionPreviewState State,
    DateTimeOffset OccurredAt);

public sealed record ExtensionMediaOperationChangedEvent(
    string EventId,
    string OperationId,
    string Operation,
    bool IsActive,
    IReadOnlyList<string> Paths,
    DateTimeOffset OccurredAt);

public sealed record ExtensionRecordingLifecycleEvent(
    string EventId,
    string RecordingId,
    string Phase,
    string RoomUrl,
    string NickName,
    string PlatformName,
    string OutputPath,
    int Attempt,
    DateTimeOffset OccurredAt);

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

public sealed record ExtensionRecorderStopRequest(
    string RoomUrl,
    string NickName,
    string PlatformName,
    string OutputPath,
    bool DeferPostProcessing);

public delegate bool ExtensionRecorderStopOverride(ExtensionRecorderStopRequest request, Func<bool> next);

public sealed record ExtensionRecorderReconnectRequest(
    string RoomUrl,
    string NickName,
    string PlatformName,
    string OutputPath,
    int Attempt,
    int ExitCode,
    bool HadMediaProgress,
    TimeSpan ProposedDelay);

public sealed record ExtensionRecorderReconnectDecision(bool ShouldRetry, TimeSpan Delay);

public delegate ExtensionRecorderReconnectDecision ExtensionRecorderReconnectOverride(
    ExtensionRecorderReconnectRequest request,
    Func<ExtensionRecorderReconnectDecision> next);

public sealed record ExtensionPostProcessingRequest(
    string RoomUrl,
    string NickName,
    string PlatformName,
    IReadOnlyList<string> PendingPaths);

public delegate Task ExtensionPostProcessingOverride(ExtensionPostProcessingRequest request, Func<Task> next);

public sealed record ExtensionPageDefinition(
    string Id,
    string Title,
    string IconGlyph,
    FrameworkElement Content,
    int Order = 0);

public delegate bool ExtensionShortcutHandler();

public sealed record ExtensionShortcutDefinition(
    string Id,
    Key Key,
    ModifierKeys Modifiers,
    ExtensionShortcutHandler Handler,
    int Priority = 0,
    Func<bool>? CanExecute = null);

public sealed record ExtensionPreviewState(
    bool IsActive,
    bool IsPaused,
    bool IsMuted,
    int Volume,
    bool IsFullScreen,
    string RoomUrl,
    string NickName);

public interface IExtensionPreviewService
{
    ExtensionPreviewState GetState();

    Task<bool> PlayAsync(string roomUrl, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default);

    void SetMuted(bool muted);

    void SetVolume(int volume);

    void SetFullScreen(bool fullScreen);
}

public sealed record ExtensionRoomInfo(
    string RoomUrl,
    string NickName,
    string PlatformName,
    bool IsLive,
    bool IsRecording,
    bool CanPreview);

public interface IExtensionRoomAction
{
    string Id { get; }

    string Label { get; }

    int Order { get; }

    bool CanExecute(ExtensionRoomInfo room);

    Task ExecuteAsync(ExtensionRoomInfo room, CancellationToken cancellationToken);
}

public sealed record ExtensionMediaOperationResult(
    bool Success,
    IReadOnlyList<string> OutputPaths,
    string Message = "");

public interface IExtensionMediaService
{
    Task<ExtensionMediaOperationResult> TranscodeAsync(
        string sourcePath,
        string targetFormat,
        bool optimizeAudio,
        CancellationToken cancellationToken = default);

    Task<ExtensionMediaOperationResult> SplitAsync(
        string sourcePath,
        int segmentSeconds,
        CancellationToken cancellationToken = default);

    Task<ExtensionMediaOperationResult> MergeAsync(
        IReadOnlyList<string> sourcePaths,
        string targetDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    int Cancel(string operation, IReadOnlyList<string>? paths = null);
}

public sealed record ExtensionRecordingState(
    string RoomUrl,
    string NickName,
    string PlatformName,
    bool IsLive,
    bool IsRecording,
    string OutputPath);

public interface IExtensionRecordingService
{
    IReadOnlyList<ExtensionRecordingState> GetStates();

    Task StartAsync(string roomUrl, CancellationToken cancellationToken = default);

    void Stop(string roomUrl, bool deferPostProcessing = false);

    Task RefreshAsync(string roomUrl, CancellationToken cancellationToken = default);

    void ReleaseTemporaryOverride(string roomUrl);

    Task ProcessPendingAsync(CancellationToken cancellationToken = default);
}

public interface IExtensionNavigationService
{
    string CurrentPageId { get; }

    IReadOnlyList<string> GetPageIds();

    bool Navigate(string pageId);
}

public enum ExtensionNotificationLevel
{
    Information,
    Success,
    Warning,
    Error,
}

public interface IExtensionNotificationService
{
    void Show(ExtensionNotificationLevel level, string message);

    void ShowSystem(string title, string message, string detail = "");
}

public interface IExtensionLogService
{
    void Write(string level, string category, string action, string message, object? data = null);

    void WriteException(Exception exception);
}

public interface IExtensionLogExportService
{
    string ExportToday(string targetDirectory);

    string ExportAll(string targetDirectory);
}

public interface IExtensionUpdateService
{
    string Version { get; }

    string BuildId { get; }

    string ProjectUrl { get; }

    Task OpenProjectPageAsync();
}

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

    IDisposable RegisterPage(ExtensionPageDefinition page);

    IDisposable RegisterShortcut(ExtensionShortcutDefinition shortcut);

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

internal sealed record BoundedTextReadResult(string Text, bool ExceededLimit);

public sealed record ExtensionUiContribution(string ExtensionId, string RegionName, FrameworkElement Content, int Order);

public sealed record ExtensionPageContribution(string ExtensionId, ExtensionPageDefinition Page);
