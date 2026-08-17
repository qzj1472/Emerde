using Windows.System;
using Emerde.Core;
using Emerde.Models;
using Emerde.ViewModels;
using Emerde.Views;
using System.Windows;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Plugins;

internal sealed class ExtensionPreviewService(MainWindow window, MainViewModel viewModel) : IExtensionPreviewService
{
    public ExtensionPreviewState GetState()
    {
        return ExtensionUiDispatcher.Invoke(() => CreateState(window, viewModel));
    }

    public Task<bool> PlayAsync(string roomUrl, CancellationToken cancellationToken = default)
    {
        return ExtensionUiDispatcher.InvokeAsync(() => viewModel.PlayPreviewForExtensionAsync(roomUrl, cancellationToken), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return ExtensionUiDispatcher.InvokeAsync(() => viewModel.StopPreviewForExtensionAsync(cancellationToken), cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return ExtensionUiDispatcher.InvokeAsync(() => viewModel.RefreshPreviewForExtensionAsync(cancellationToken), cancellationToken);
    }

    public Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        return ExtensionUiDispatcher.InvokeAsync(() => viewModel.SetPreviewPausedForExtensionAsync(paused, cancellationToken), cancellationToken);
    }

    public void SetMuted(bool muted)
    {
        ExtensionUiDispatcher.Invoke(() => viewModel.SetPreviewMutedForExtension(muted));
    }

    public void SetVolume(int volume)
    {
        ExtensionUiDispatcher.Invoke(() => viewModel.SetPreviewVolumeForExtension(volume));
    }

    public void SetFullScreen(bool fullScreen)
    {
        ExtensionUiDispatcher.Invoke(() => window.SetPreviewFullScreenForExtension(fullScreen));
    }

    internal static ExtensionPreviewState CreateState(MainWindow window, MainViewModel viewModel)
    {
        return new ExtensionPreviewState(
            viewModel.IsPreviewing,
            viewModel.IsPreviewPaused,
            viewModel.IsPreviewMuted,
            viewModel.PreviewVolume,
            window.IsPreviewFullScreenActive,
            viewModel.PreviewingRoom?.RoomUrl ?? string.Empty,
            viewModel.PreviewingRoom?.NickName ?? string.Empty);
    }
}

internal sealed class ExtensionRecordingService : IExtensionRecordingService
{
    public IReadOnlyList<ExtensionRecordingState> GetStates()
    {
        return GlobalMonitor.RoomStatus.Values
            .Select(status => new ExtensionRecordingState(
                status.RoomUrl,
                status.NickName,
                status.PlatformName,
                status.StreamStatus == StreamStatus.Streaming,
                status.RecordStatus == RecordStatus.Recording,
                status.Recorder.FileName ?? string.Empty))
            .OrderBy(status => status.NickName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task StartAsync(string roomUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomUrl);
        EnsureRoomExists(roomUrl);
        GlobalMonitor.ClearRoomRecordStartPause(roomUrl);
        GlobalMonitor.SetTemporaryRoomRecord(roomUrl, true);
        GlobalMonitor.Start();
        await GlobalMonitor.RunRoomAsync(roomUrl, cancellationToken);
    }

    public void Stop(string roomUrl, bool deferPostProcessing = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomUrl);
        RoomStatus status = EnsureRoomExists(roomUrl);
        GlobalMonitor.SetTemporaryRoomRecord(roomUrl, false);
        status.Recorder.Stop(deferPostProcessing);
    }

    public Task RefreshAsync(string roomUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomUrl);
        EnsureRoomExists(roomUrl);
        return GlobalMonitor.RunRoomAsync(roomUrl, cancellationToken);
    }

    public void ReleaseTemporaryOverride(string roomUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomUrl);
        GlobalMonitor.ClearTemporaryRoomRecord(roomUrl);
    }

    public Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RecordingRecoveryService.ProcessPendingAsync(cancellationToken);
    }

    private static RoomStatus EnsureRoomExists(string roomUrl)
    {
        return GlobalMonitor.RoomStatus.TryGetValue(roomUrl, out RoomStatus? status)
            ? status
            : throw new KeyNotFoundException($"Room '{roomUrl}' is not registered.");
    }
}

internal sealed class ExtensionNavigationService(MainViewModel viewModel) : IExtensionNavigationService
{
    private static readonly string[] BuiltInPageIds = ["home", "videos", "settings", "extensions", "about"];

    public string CurrentPageId
    {
        get
        {
            return ExtensionUiDispatcher.Invoke(GetCurrentPageId);
        }
    }

    private string GetCurrentPageId()
    {
        int index = viewModel.SelectedMainPageIndex;
        if (index >= 0 && index < BuiltInPageIds.Length)
        {
            return BuiltInPageIds[index];
        }
        ExtensionPageContribution[] pages = ExtensionHostRuntime.GetPagesSnapshot();
        int extensionIndex = index - BuiltInPageIds.Length;
        return extensionIndex >= 0 && extensionIndex < pages.Length ? pages[extensionIndex].Page.Id : string.Empty;
    }

    public IReadOnlyList<string> GetPageIds()
    {
        return BuiltInPageIds
            .Concat(ExtensionHostRuntime.GetPagesSnapshot().Select(item => item.Page.Id))
            .ToArray();
    }

    public bool Navigate(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        return ExtensionUiDispatcher.Invoke(() => NavigateCore(pageId));
    }

    private bool NavigateCore(string pageId)
    {
        int builtInIndex = Array.FindIndex(BuiltInPageIds, item => string.Equals(item, pageId, StringComparison.OrdinalIgnoreCase));
        if (builtInIndex >= 0)
        {
            viewModel.SelectedMainPageIndex = builtInIndex;
            return true;
        }
        ExtensionPageContribution[] pages = ExtensionHostRuntime.GetPagesSnapshot();
        int extensionIndex = Array.FindIndex(pages, item => string.Equals(item.Page.Id, pageId, StringComparison.OrdinalIgnoreCase));
        if (extensionIndex < 0)
        {
            return false;
        }
        viewModel.SelectedMainPageIndex = BuiltInPageIds.Length + extensionIndex;
        return true;
    }
}

internal sealed class ExtensionNotificationService : IExtensionNotificationService
{
    public void Show(ExtensionNotificationLevel level, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ExtensionUiDispatcher.Invoke(() => ShowCore(level, message));
    }

    private static void ShowCore(ExtensionNotificationLevel level, string message)
    {
        switch (level)
        {
            case ExtensionNotificationLevel.Success:
                AppFeedback.Success(message);
                break;
            case ExtensionNotificationLevel.Warning:
                AppFeedback.Warning(message);
                break;
            case ExtensionNotificationLevel.Error:
                AppFeedback.Error(message);
                break;
            default:
                AppFeedback.Information(message);
                break;
        }
    }

    public void ShowSystem(string title, string message, string detail = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ExtensionUiDispatcher.Invoke(() => Notifier.AddNotice(title, message, detail));
    }
}

internal sealed class ExtensionLogService : IExtensionLogService
{
    public void Write(string level, string category, string action, string message, object? data = null)
    {
        AppSessionLogger.Event(level, category, action, message, data);
    }

    public void WriteException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        AppSessionLogger.WriteException(exception);
    }
}

internal sealed class ExtensionLogExportService : IExtensionLogExportService
{
    public string ExportToday(string targetDirectory)
    {
        return LogExporter.ExportToday(targetDirectory);
    }

    public string ExportAll(string targetDirectory)
    {
        return LogExporter.ExportAll(targetDirectory);
    }
}

internal sealed class ExtensionUpdateService : IExtensionUpdateService
{
    public string Version => AppConfig.Version;

    public string BuildId => AppConfig.BuildId;

    public string ProjectUrl => AppConfig.Url;

    public async Task OpenProjectPageAsync()
    {
        await Launcher.LaunchUriAsync(new Uri(ProjectUrl));
    }
}

internal sealed class ExtensionMediaService : IExtensionMediaService
{
    public async Task<ExtensionMediaOperationResult> TranscodeAsync(
        string sourcePath,
        string targetFormat,
        bool optimizeAudio,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFormat);
        string? outputPath = null;
        using CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool success = await new Converter().ExecuteWithCompletionAsync(
            sourcePath,
            new ConverterOptions(targetFormat, optimizeAudio),
            path => outputPath = path,
            source);
        return new ExtensionMediaOperationResult(
            success,
            success && !string.IsNullOrWhiteSpace(outputPath) ? [outputPath] : [],
            success ? string.Empty : "Transcode failed.");
    }

    public async Task<ExtensionMediaOperationResult> SplitAsync(
        string sourcePath,
        int segmentSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        MediaFileWorkflowResult result = await MediaFileWorkflow.SplitAsync(sourcePath, segmentSeconds, cancellationToken);
        return new ExtensionMediaOperationResult(result.Success, result.OutputPaths, result.Error);
    }

    public async Task<ExtensionMediaOperationResult> MergeAsync(
        IReadOnlyList<string> sourcePaths,
        string targetDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        MediaFileWorkflowResult result = await MediaFileWorkflow.MergeAsync(sourcePaths, targetDirectory, progress, cancellationToken);
        return new ExtensionMediaOperationResult(result.Success, result.OutputPaths, result.Error);
    }

    public int Cancel(string operation, IReadOnlyList<string>? paths = null)
    {
        if (!Enum.TryParse(operation, true, out MediaOperationKind kind))
        {
            return 0;
        }
        return paths is { Count: > 0 }
            ? MediaOperationRegistry.Cancel(kind, paths)
            : MediaOperationRegistry.Cancel(kind);
    }

}

internal static class ExtensionUiDispatcher
{
    public static void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.Invoke(action);
    }

    public static T Invoke<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
        {
            return action();
        }
        return dispatcher.Invoke(action);
    }

    public static Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
        {
            return action();
        }
        return dispatcher.InvokeAsync(action).Task.Unwrap().WaitAsync(cancellationToken);
    }

    public static Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
        {
            return action();
        }
        return dispatcher.InvokeAsync(action).Task.Unwrap().WaitAsync(cancellationToken);
    }
}
