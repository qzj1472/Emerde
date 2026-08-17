using Emerde.Plugins;
using Emerde.Core;
using System.IO.Compression;
using System.Text;

namespace Emerde.Tests;

public sealed class ExtensionContractsTests
{
    [Fact]
    public void FailedRestartRestoresGlobalMonitorBeforeMaintenanceWorkers()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "TrayIconManager.cs"));
        int failureBranch = source.IndexOf("if (!restarted)", StringComparison.Ordinal);
        int resourceShutdown = source.IndexOf("await ShutdownRestartResourcesAsync();", StringComparison.Ordinal);
        int restartAttempt = source.IndexOf("RuntimeHelper.Restart", StringComparison.Ordinal);
        int resourceResume = source.IndexOf("await ResumeRestartResourcesAsync();", failureBranch, StringComparison.Ordinal);
        int monitorRestart = source.IndexOf("GlobalMonitor.Start();", failureBranch, StringComparison.Ordinal);
        int recoveryRestart = source.IndexOf("RecordingRecoveryService.QueueRun();", failureBranch, StringComparison.Ordinal);

        Assert.True(failureBranch >= 0);
        Assert.True(resourceShutdown >= 0 && resourceShutdown < restartAttempt);
        Assert.True(resourceResume > failureBranch && resourceResume < monitorRestart);
        Assert.True(monitorRestart > failureBranch);
        Assert.True(recoveryRestart > monitorRestart);
    }

    [Fact]
    public void RemoveExtension_UsesSharedDialogBlurScope()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "ExtensionCenterViewModel.cs"));
        int methodStart = source.IndexOf("private async Task RemoveExtension", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("[RelayCommand]", methodStart + 1, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        Assert.Contains("DialogBlurScope.ForLightDismiss", method);
        Assert.Contains("WindowSizing.ShowContentDialogAsync", method);
    }

    [Fact]
    public void MainWindow_StartsRecoveryAfterExtensionsInitialize()
    {
        string mainWindow = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        string mainViewModel = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        int extensionInitialization = mainWindow.IndexOf("await ExtensionService.Default.InitializeAsync()", StringComparison.Ordinal);
        int recoveryStartup = mainWindow.IndexOf("RecordingRecoveryService.QueueRun()", StringComparison.Ordinal);

        Assert.True(extensionInitialization >= 0);
        Assert.True(recoveryStartup > extensionInitialization);
        Assert.DoesNotContain("RecordingRecoveryService.QueueRun()", mainViewModel, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    [Fact]
    public void ExtensionContractNames_AreDistinct()
    {
        string[] contracts =
        [
            ExtensionContractNames.StreamResolver,
            ExtensionContractNames.Monitor,
            ExtensionContractNames.Recorder,
            ExtensionContractNames.RecorderStop,
            ExtensionContractNames.RecorderReconnect,
            ExtensionContractNames.PostProcessing,
            ExtensionContractNames.MainWindow,
            ExtensionContractNames.MainViewModel,
            ExtensionContractNames.Application,
            ExtensionContractNames.MainContentOverlay,
            ExtensionContractNames.ExtensionDetail,
            ExtensionContractNames.VideoListToolbar,
            ExtensionContractNames.VideoListActions,
            ExtensionContractNames.HomeToolbar,
            ExtensionContractNames.HomeRoomActions,
            ExtensionContractNames.PlatformCookies,
            ExtensionContractNames.VideoSelection,
            ExtensionContractNames.DialogService,
            ExtensionContractNames.HomeCardTemplate,
            ExtensionContractNames.PreviewService,
            ExtensionContractNames.MediaService,
            ExtensionContractNames.RecordingService,
            ExtensionContractNames.NavigationService,
            ExtensionContractNames.NotificationService,
            ExtensionContractNames.LogService,
            ExtensionContractNames.LogExportService,
            ExtensionContractNames.UpdateService,
        ];

        Assert.Equal(contracts.Length, contracts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ExtensionEventNames_AreDistinct()
    {
        string[] events =
        [
            ExtensionEventNames.MediaFinalized,
            ExtensionEventNames.PreviewStateChanged,
            ExtensionEventNames.MediaOperationChanged,
            ExtensionEventNames.RecordingLifecycle,
        ];

        Assert.Equal(events.Length, events.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MainWindow_RegistersEveryExtensionHostService()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        string[] contracts =
        [
            ExtensionContractNames.PreviewService,
            ExtensionContractNames.MediaService,
            ExtensionContractNames.RecordingService,
            ExtensionContractNames.NavigationService,
            ExtensionContractNames.NotificationService,
            ExtensionContractNames.LogService,
            ExtensionContractNames.LogExportService,
            ExtensionContractNames.UpdateService,
        ];

        foreach (string contract in contracts)
        {
            Assert.Contains($"RegisterHostObject(ExtensionContractNames.{GetContractFieldName(contract)}", source, StringComparison.Ordinal);
        }
    }

    private static string GetContractFieldName(string contract)
    {
        return typeof(ExtensionContractNames)
            .GetFields()
            .Single(field => Equals(field.GetValue(null), contract))
            .Name;
    }

    [Fact]
    public async Task ExtensionHostRuntime_PublishesEventsAndRemovesDisposedSubscriptions()
    {
        string eventName = $"test.event.{Guid.NewGuid():N}";
        int received = 0;
        ExtensionContext context = new("test.event-extension", string.Empty, string.Empty, new Dictionary<string, string>());
        IDisposable subscription = context.Subscribe<string>(eventName, (payload, _) =>
        {
            received += payload.Length;
            return ValueTask.CompletedTask;
        });

        await ExtensionHostRuntime.PublishAsync(eventName, "first");
        subscription.Dispose();
        await ExtensionHostRuntime.PublishAsync(eventName, "second");
        await context.DisposeAsync();

        Assert.Equal(5, received);
    }

    [Fact]
    public void ExtensionHostRuntime_ReturnsEveryVideoActionByPriority()
    {
        string contract = $"test.video-actions.{Guid.NewGuid():N}";
        TestVideoAction first = new("first", 10);
        TestVideoAction second = new("second", 20);
        using IDisposable firstRegistration = ExtensionHostRuntime.RegisterOverride("test.first", contract, first, 1);
        using IDisposable secondRegistration = ExtensionHostRuntime.RegisterOverride("test.second", contract, second, 2);

        IReadOnlyList<IExtensionVideoAction> actions = ExtensionHostRuntime.GetOverrides<IExtensionVideoAction>(contract);

        Assert.Equal([second, first], actions);
    }

    [Fact]
    public async Task ExtensionHostRuntime_IsolatesFailingEventHandlers()
    {
        string eventName = $"test.event.{Guid.NewGuid():N}";
        bool completed = false;
        using IDisposable failing = ExtensionHostRuntime.Subscribe<string>(
            "test.failing-extension",
            eventName,
            (_, _) => ValueTask.FromException(new InvalidOperationException("failure")));
        using IDisposable succeeding = ExtensionHostRuntime.Subscribe<string>(
            "test.succeeding-extension",
            eventName,
            (_, _) =>
            {
                completed = true;
                return ValueTask.CompletedTask;
            });

        await ExtensionHostRuntime.PublishAsync(eventName, "payload");

        Assert.True(completed);
    }

    [Fact]
    public async Task ExtensionHostRuntime_IsolatesFailingUiObservers()
    {
        await RunStaAsync(() =>
        {
            bool completed = false;
            EventHandler failing = (_, _) => throw new InvalidOperationException("failure");
            EventHandler succeeding = (_, _) => completed = true;
            ExtensionHostRuntime.UiContributionsChanged += failing;
            ExtensionHostRuntime.UiContributionsChanged += succeeding;
            try
            {
                using IDisposable registration = ExtensionHostRuntime.RegisterUi(
                    $"test.ui-extension.{Guid.NewGuid():N}",
                    $"test.ui-region.{Guid.NewGuid():N}",
                    new System.Windows.Controls.Border());

                Assert.True(completed);
            }
            finally
            {
                ExtensionHostRuntime.UiContributionsChanged -= failing;
                ExtensionHostRuntime.UiContributionsChanged -= succeeding;
            }
        });
    }

    [Fact]
    public async Task ExtensionContext_RequiresPermissionForPlatformCookies()
    {
        object provider = new();
        using IDisposable registration = ExtensionHostRuntime.RegisterHostObject(ExtensionContractNames.PlatformCookies, provider);
        ExtensionContext denied = new("test.denied", string.Empty, string.Empty, new Dictionary<string, string>());
        ExtensionContext allowed = new(
            "test.allowed",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(),
            [ExtensionPermissionNames.PlatformCookieRead]);

        Assert.Null(denied.GetHostObject(ExtensionContractNames.PlatformCookies));
        Assert.Same(provider, allowed.GetHostObject(ExtensionContractNames.PlatformCookies));

        await denied.DisposeAsync();
        await allowed.DisposeAsync();
    }

    [Theory]
    [InlineData(ExtensionContractNames.PreviewService, ExtensionPermissionNames.PreviewControl)]
    [InlineData(ExtensionContractNames.MediaService, ExtensionPermissionNames.MediaControl)]
    [InlineData(ExtensionContractNames.RecordingService, ExtensionPermissionNames.RecordingControl)]
    [InlineData(ExtensionContractNames.NavigationService, ExtensionPermissionNames.UiModify)]
    [InlineData(ExtensionContractNames.NotificationService, ExtensionPermissionNames.NotificationWrite)]
    [InlineData(ExtensionContractNames.LogService, ExtensionPermissionNames.LogWrite)]
    [InlineData(ExtensionContractNames.LogExportService, ExtensionPermissionNames.LogExport)]
    [InlineData(ExtensionContractNames.UpdateService, ExtensionPermissionNames.UpdateOpen)]
    public async Task ExtensionContext_RequiresPermissionForHostServices(string contractName, string permission)
    {
        object service = new();
        using IDisposable registration = ExtensionHostRuntime.RegisterHostObject(contractName, service);
        ExtensionContext denied = new("test.service-denied", string.Empty, string.Empty, new Dictionary<string, string>());
        ExtensionContext allowed = new(
            "test.service-allowed",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(),
            [permission]);

        Assert.Null(denied.GetHostObject(contractName));
        Assert.Same(service, allowed.GetHostObject(contractName));

        await denied.DisposeAsync();
        await allowed.DisposeAsync();
    }

    [Fact]
    public async Task ExtensionContext_RequiresPermissionsForUiCoreAndMediaEvents()
    {
        ExtensionContext denied = new("test.denied-permissions", string.Empty, string.Empty, new Dictionary<string, string>());

        Assert.Throws<UnauthorizedAccessException>(() => denied.RegisterOverride(
            ExtensionContractNames.StreamResolver,
            (ExtensionStreamResolverOverride)((_, next) => next())));
        await RunStaAsync(() => Assert.Throws<UnauthorizedAccessException>(() => denied.RegisterUi(
            ExtensionContractNames.ExtensionDetail,
            new System.Windows.Controls.Border())));
        Assert.Throws<UnauthorizedAccessException>(() => denied.Subscribe<ExtensionMediaFinalizedEvent>(
            ExtensionEventNames.MediaFinalized,
            (_, _) => ValueTask.CompletedTask));

        await denied.DisposeAsync();
    }

    [Theory]
    [InlineData(ExtensionEventNames.PreviewStateChanged)]
    [InlineData(ExtensionEventNames.MediaOperationChanged)]
    [InlineData(ExtensionEventNames.RecordingLifecycle)]
    public async Task ExtensionContext_RejectsUndeclaredLifecycleEvents(string eventName)
    {
        ExtensionContext denied = new("test.lifecycle-denied", string.Empty, string.Empty, new Dictionary<string, string>());

        Assert.Throws<UnauthorizedAccessException>(() => denied.Subscribe<object>(
            eventName,
            (_, _) => ValueTask.CompletedTask));

        await denied.DisposeAsync();
    }

    [Fact]
    public async Task ExtensionHostRuntime_PublishesMediaOperationLifecycle()
    {
        string operationPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.ts");
        TaskCompletionSource<ExtensionMediaOperationChangedEvent> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ExtensionMediaOperationChangedEvent> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ExtensionContext context = new(
            "test.media-lifecycle",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(),
            [ExtensionPermissionNames.MediaOperationsRead]);
        using IDisposable subscription = context.Subscribe<ExtensionMediaOperationChangedEvent>(
            ExtensionEventNames.MediaOperationChanged,
            (payload, _) =>
            {
                if (!payload.Paths.Contains(operationPath, StringComparer.OrdinalIgnoreCase))
                {
                    return ValueTask.CompletedTask;
                }
                if (payload.IsActive)
                {
                    started.TrySetResult(payload);
                }
                else
                {
                    stopped.TrySetResult(payload);
                }
                return ValueTask.CompletedTask;
            });

        IDisposable operation = MediaOperationRegistry.Register(
            MediaOperationKind.Split,
            () => [operationPath]);
        ExtensionMediaOperationChangedEvent startedPayload = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        operation.Dispose();
        ExtensionMediaOperationChangedEvent stoppedPayload = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(nameof(MediaOperationKind.Split), startedPayload.Operation);
        Assert.True(startedPayload.IsActive);
        Assert.False(stoppedPayload.IsActive);
        Assert.NotEqual(startedPayload.EventId, stoppedPayload.EventId);
        Assert.Equal(startedPayload.OperationId, stoppedPayload.OperationId);
        await context.DisposeAsync();
    }

    [Fact]
    public void RecordingLifecycleContract_ExposesStableRecordingIdentity()
    {
        Assert.NotNull(typeof(ExtensionRecordingLifecycleEvent).GetProperty(nameof(ExtensionRecordingLifecycleEvent.RecordingId)));
    }

    [Fact]
    public async Task ExtensionHostRuntime_OrdersAndRemovesPages()
    {
        await RunStaAsync(() =>
        {
            string suffix = Guid.NewGuid().ToString("N");
            using IDisposable later = ExtensionHostRuntime.RegisterPage(
                $"test.page-later.{suffix}",
                new ExtensionPageDefinition($"later.{suffix}", "Later", string.Empty, new System.Windows.Controls.Border(), 20));
            IDisposable earlier = ExtensionHostRuntime.RegisterPage(
                $"test.page-earlier.{suffix}",
                new ExtensionPageDefinition($"earlier.{suffix}", "Earlier", string.Empty, new System.Windows.Controls.Border(), 10));

            Assert.Equal([$"earlier.{suffix}", $"later.{suffix}"], ExtensionHostRuntime.GetPagesSnapshot().Select(item => item.Page.Id));

            earlier.Dispose();

            Assert.DoesNotContain(ExtensionHostRuntime.GetPagesSnapshot(), item => item.Page.Id == $"earlier.{suffix}");
        });
    }

    [Fact]
    public async Task ExtensionHostRuntime_RejectsReusedUiElements()
    {
        await RunStaAsync(() =>
        {
            string suffix = Guid.NewGuid().ToString("N");
            System.Windows.Controls.Border content = new();
            using IDisposable first = ExtensionHostRuntime.RegisterUi(
                $"test.ui-first.{suffix}",
                ExtensionContractNames.HomeToolbar,
                content);

            Assert.Throws<InvalidOperationException>(() => ExtensionHostRuntime.RegisterUi(
                $"test.ui-second.{suffix}",
                ExtensionContractNames.VideoListToolbar,
                content));
        });
    }

    [Fact]
    public void ExtensionHostRuntime_UsesShortcutPriorityAndRestoresAfterDispose()
    {
        List<string> calls = [];
        string suffix = Guid.NewGuid().ToString("N");
        using IDisposable low = ExtensionHostRuntime.RegisterShortcut(
            $"test.shortcut-low.{suffix}",
            new ExtensionShortcutDefinition("low", System.Windows.Input.Key.F8, System.Windows.Input.ModifierKeys.Control, () =>
            {
                calls.Add("low");
                return true;
            }, 10));
        IDisposable high = ExtensionHostRuntime.RegisterShortcut(
            $"test.shortcut-high.{suffix}",
            new ExtensionShortcutDefinition("high", System.Windows.Input.Key.F8, System.Windows.Input.ModifierKeys.Control, () =>
            {
                calls.Add("high");
                return true;
            }, 20));

        Assert.True(ExtensionHostRuntime.TryHandleShortcut(System.Windows.Input.Key.F8, System.Windows.Input.ModifierKeys.Control));
        Assert.Equal(["high"], calls);

        high.Dispose();
        calls.Clear();

        Assert.True(ExtensionHostRuntime.TryHandleShortcut(System.Windows.Input.Key.F8, System.Windows.Input.ModifierKeys.Control));
        Assert.Equal(["low"], calls);
    }

    [Fact]
    public void ExtensionHostRuntime_IsolatesFailingShortcuts()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using IDisposable failing = ExtensionHostRuntime.RegisterShortcut(
            $"test.shortcut-failing.{suffix}",
            new ExtensionShortcutDefinition("failing", System.Windows.Input.Key.F9, System.Windows.Input.ModifierKeys.Alt, () => throw new InvalidOperationException("failure"), 20));
        using IDisposable succeeding = ExtensionHostRuntime.RegisterShortcut(
            $"test.shortcut-succeeding.{suffix}",
            new ExtensionShortcutDefinition("succeeding", System.Windows.Input.Key.F9, System.Windows.Input.ModifierKeys.Alt, () => true, 10));

        Assert.True(ExtensionHostRuntime.TryHandleShortcut(System.Windows.Input.Key.F9, System.Windows.Input.ModifierKeys.Alt));
    }

    [Fact]
    public async Task ExtensionHostRuntime_RemovesEveryExtensionContribution()
    {
        await RunStaAsync(() =>
        {
            string extensionId = $"test.remove-all.{Guid.NewGuid():N}";
            string pageId = $"page.{Guid.NewGuid():N}";
            using IDisposable ui = ExtensionHostRuntime.RegisterUi(extensionId, ExtensionContractNames.HomeToolbar, new System.Windows.Controls.Border());
            using IDisposable page = ExtensionHostRuntime.RegisterPage(
                extensionId,
                new ExtensionPageDefinition(pageId, "Page", string.Empty, new System.Windows.Controls.Border()));
            using IDisposable shortcut = ExtensionHostRuntime.RegisterShortcut(
                extensionId,
                new ExtensionShortcutDefinition("shortcut", System.Windows.Input.Key.F10, System.Windows.Input.ModifierKeys.None, () => true));

            ExtensionHostRuntime.RemoveExtensionRegistrations(extensionId);

            Assert.DoesNotContain(ExtensionHostRuntime.GetUiContributionsSnapshot(), item => item.ExtensionId == extensionId);
            Assert.DoesNotContain(ExtensionHostRuntime.GetPagesSnapshot(), item => item.ExtensionId == extensionId);
            Assert.False(ExtensionHostRuntime.TryHandleShortcut(System.Windows.Input.Key.F10, System.Windows.Input.ModifierKeys.None));
        });
    }

    [Fact]
    public async Task ExtensionRecordingService_RejectsUnknownRoomsBeforeChangingState()
    {
        string roomUrl = $"https://example.invalid/{Guid.NewGuid():N}";
        ExtensionRecordingService service = new();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.StartAsync(roomUrl));
        Assert.Throws<KeyNotFoundException>(() => service.Stop(roomUrl));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RefreshAsync(roomUrl));
    }

    [Fact]
    public async Task MediaFileWorkflow_RejectsInvalidInputsWithoutRegisteringOperations()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.ts");

        MediaFileWorkflowResult split = await MediaFileWorkflow.SplitAsync(
            missingPath,
            60,
            CancellationToken.None);
        MediaFileWorkflowResult merge = await MediaFileWorkflow.MergeAsync(
            [],
            Path.GetTempPath(),
            null,
            CancellationToken.None);

        Assert.False(split.Success);
        Assert.False(merge.Success);
        Assert.False(MediaOperationRegistry.IsPathProtected(missingPath));
    }

    [Fact]
    public void MediaFileWorkflow_MergedOutputDoesNotRetainSegmentIdentity()
    {
        VideoRecordingMetadata metadata = new()
        {
            SegmentGroupId = "group",
            SegmentIndex = 1,
            SegmentCount = 3,
            SegmentKind = "stall",
            SegmentReason = VideoRecordingMetadataStore.TimelineStallSegmentReason,
            MediaIssue = "timeline_mismatch",
        };

        MediaFileWorkflow.ClearMergedSegmentIdentity(metadata);

        Assert.Empty(metadata.SegmentGroupId);
        Assert.Equal(-1, metadata.SegmentIndex);
        Assert.Equal(0, metadata.SegmentCount);
        Assert.Empty(metadata.SegmentKind);
        Assert.Empty(metadata.SegmentReason);
        Assert.Equal("timeline_mismatch", metadata.MediaIssue);
    }

    [Fact]
    public async Task MediaFileWorkflow_RejectsConcurrentTargetPathClaims()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.MediaWorkflow.Tests.{Guid.NewGuid():N}");
        string firstSource = Path.Combine(root, "first.ts");
        string secondSource = Path.Combine(root, "second.ts");
        string sharedTarget = Path.Combine(root, "merged.ts");
        using IDisposable? first = await MediaFileWorkflow.TryRegisterOperationAsync(
            MediaOperationKind.Merge,
            [firstSource],
            () => [firstSource, sharedTarget],
            () => { },
            CancellationToken.None);
        using IDisposable? second = await MediaFileWorkflow.TryRegisterOperationAsync(
            MediaOperationKind.Merge,
            [secondSource],
            () => [secondSource, sharedTarget],
            () => { },
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void PreviewExtensionPlay_IsIdempotentForCurrentRoom()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        int methodStart = source.IndexOf("internal async Task<bool> PlayPreviewForExtensionAsync", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("internal async Task StopPreviewForExtensionAsync", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        Assert.Contains("IsPreviewing && IsSameRoom(PreviewingRoom, room)", method, StringComparison.Ordinal);
        Assert.Contains("return true;", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtensionContext_AllowsDeclaredUiCoreAndMediaPermissions()
    {
        ExtensionContext allowed = new(
            "test.allowed-permissions",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(),
            [
                ExtensionPermissionNames.UiModify,
                ExtensionPermissionNames.CoreOverride,
                ExtensionPermissionNames.MediaFinalizedRead,
            ]);

        using IDisposable coreRegistration = allowed.RegisterOverride(
            ExtensionContractNames.StreamResolver,
            (ExtensionStreamResolverOverride)((_, next) => next()));
        using IDisposable eventRegistration = allowed.Subscribe<ExtensionMediaFinalizedEvent>(
            ExtensionEventNames.MediaFinalized,
            (_, _) => ValueTask.CompletedTask);
        await RunStaAsync(() =>
        {
            using IDisposable uiRegistration = allowed.RegisterUi(
                ExtensionContractNames.ExtensionDetail,
                new System.Windows.Controls.Border());
        });

        await allowed.DisposeAsync();
    }

    [Theory]
    [InlineData(ExtensionContractNames.RecorderStop)]
    [InlineData(ExtensionContractNames.RecorderReconnect)]
    [InlineData(ExtensionContractNames.PostProcessing)]
    public async Task ExtensionContext_RequiresCorePermissionForRecorderLifecycleOverrides(string contractName)
    {
        ExtensionContext denied = new("test.recorder-lifecycle-denied", string.Empty, string.Empty, new Dictionary<string, string>());
        ExtensionContext allowed = new(
            "test.recorder-lifecycle-allowed",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(),
            [ExtensionPermissionNames.CoreOverride]);
        object implementation = contractName switch
        {
            ExtensionContractNames.RecorderStop => (ExtensionRecorderStopOverride)((_, next) => next()),
            ExtensionContractNames.RecorderReconnect => (ExtensionRecorderReconnectOverride)((_, next) => next()),
            ExtensionContractNames.PostProcessing => (ExtensionPostProcessingOverride)((_, next) => next()),
            _ => throw new ArgumentOutOfRangeException(nameof(contractName)),
        };

        Assert.Throws<UnauthorizedAccessException>(() => denied.RegisterOverride(contractName, implementation));
        using IDisposable registration = allowed.RegisterOverride(contractName, implementation);

        await denied.DisposeAsync();
        await allowed.DisposeAsync();
    }

    [Fact]
    public async Task ExtensionContext_RejectsUnknownAndMismatchedContracts()
    {
        ExtensionContext context = new(
            "test.contract-validation",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(),
            [
                ExtensionPermissionNames.CoreOverride,
                ExtensionPermissionNames.MediaOperationsRead,
            ]);

        Assert.Null(context.GetHostObject($"host.unknown.{Guid.NewGuid():N}"));
        Assert.Throws<NotSupportedException>(() => context.RegisterOverride(
            $"core.unknown.{Guid.NewGuid():N}",
            new object()));
        Assert.Throws<ArgumentException>(() => context.RegisterOverride(
            ExtensionContractNames.RecorderStop,
            new object()));
        Assert.Throws<ArgumentException>(() => context.Subscribe<string>(
            ExtensionEventNames.MediaOperationChanged,
            (_, _) => ValueTask.CompletedTask));

        await context.DisposeAsync();
    }

    [Fact]
    public async Task ExtensionHostRuntime_DoesNotLetOneEventHandlerDelayOtherExtensions()
    {
        string eventName = $"test.event.parallel.{Guid.NewGuid():N}";
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable blocking = ExtensionHostRuntime.Subscribe<string>(
            "test.blocking-extension",
            eventName,
            async (_, _) => await new ValueTask(release.Task));
        using IDisposable succeeding = ExtensionHostRuntime.Subscribe<string>(
            "test.succeeding-extension",
            eventName,
            (_, _) =>
            {
                completed.TrySetResult();
                return ValueTask.CompletedTask;
            });

        Task publish = ExtensionHostRuntime.PublishAsync(eventName, "payload");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.TrySetResult();
        await publish;
    }

    [Fact]
    public async Task ExtensionHostRuntime_PreservesOrderBetweenConcurrentEventPublishes()
    {
        string eventName = $"test.event.ordered.{Guid.NewGuid():N}";
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<int> received = [];
        using IDisposable subscription = ExtensionHostRuntime.Subscribe<int>(
            "test.ordered-extension",
            eventName,
            async (payload, _) =>
            {
                received.Add(payload);
                if (payload == 1)
                {
                    firstStarted.TrySetResult();
                    await new ValueTask(releaseFirst.Task);
                }
            });

        Task first = ExtensionHostRuntime.PublishAsync(eventName, 1);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task second = ExtensionHostRuntime.PublishAsync(eventName, 2);
        await Task.Delay(50);

        Assert.Equal([1], received);

        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal([1, 2], received);
    }

    [Fact]
    public void RecorderStopOverride_InterceptsAndRestoresDefaultStopPath()
    {
        Recorder recorder = new();
        int intercepted = 0;
        using IDisposable registration = ExtensionHostRuntime.RegisterOverride(
            "test.recorder-stop",
            ExtensionContractNames.RecorderStop,
            (ExtensionRecorderStopOverride)((_, _) =>
            {
                intercepted++;
                return false;
            }));

        recorder.Stop();
        Assert.Equal(1, intercepted);

        registration.Dispose();
        recorder.Stop();

        Assert.Equal(1, intercepted);
    }

    [Fact]
    public async Task PostProcessingOverride_UsesSingleDownstreamExecution()
    {
        int downstreamCalls = 0;
        using IDisposable registration = ExtensionHostRuntime.RegisterOverride(
            "test.post-processing",
            ExtensionContractNames.PostProcessing,
            (ExtensionPostProcessingOverride)(async (_, next) =>
            {
                await next();
                await next();
            }));

        await ExtensionHostRuntime.InvokeOverrideChainAsync<ExtensionPostProcessingOverride>(
            ExtensionContractNames.PostProcessing,
            (implementation, next) => implementation(new ExtensionPostProcessingRequest(string.Empty, string.Empty, string.Empty, []), next),
            () =>
            {
                downstreamCalls++;
                return Task.CompletedTask;
            },
            _ => { });

        Assert.Equal(1, downstreamCalls);
    }

    [Fact]
    public void ExtensionHostRuntime_ComposesSyncOverridesAndCachesDownstream()
    {
        string contract = $"test.chain.sync.{Guid.NewGuid():N}";
        List<string> calls = [];
        int fallbackCalls = 0;
        TestSyncOverride high = next =>
        {
            calls.Add("high");
            return next() + next();
        };
        TestSyncOverride low = next =>
        {
            calls.Add("low");
            return next();
        };
        using IDisposable highRegistration = ExtensionHostRuntime.RegisterOverride("test.high", contract, high, 20);
        using IDisposable lowRegistration = ExtensionHostRuntime.RegisterOverride("test.low", contract, low, 10);

        int result = ExtensionHostRuntime.InvokeOverrideChain<TestSyncOverride, int>(
            contract,
            (implementation, next) => implementation(next),
            () =>
            {
                calls.Add("default");
                return ++fallbackCalls;
            },
            _ => { });

        Assert.Equal(2, result);
        Assert.Equal(1, fallbackCalls);
        Assert.Equal(["high", "low", "default"], calls);
    }

    [Fact]
    public void ExtensionHostRuntime_ContinuesSyncChainAfterExtensionFailure()
    {
        string contract = $"test.chain.failure.{Guid.NewGuid():N}";
        int lowCalls = 0;
        int logged = 0;
        using IDisposable highRegistration = ExtensionHostRuntime.RegisterOverride(
            "test.high",
            contract,
            (TestSyncOverride)(_ => throw new InvalidOperationException("failure")),
            20);
        using IDisposable lowRegistration = ExtensionHostRuntime.RegisterOverride(
            "test.low",
            contract,
            (TestSyncOverride)(next =>
            {
                lowCalls++;
                return next();
            }),
            10);

        int result = ExtensionHostRuntime.InvokeOverrideChain<TestSyncOverride, int>(
            contract,
            (implementation, next) => implementation(next),
            () => 7,
            _ => logged++);

        Assert.Equal(7, result);
        Assert.Equal(1, lowCalls);
        Assert.Equal(1, logged);
    }

    [Fact]
    public void ExtensionHostRuntime_DoesNotRepeatFailingFallback()
    {
        string contract = $"test.chain.fallback-failure.{Guid.NewGuid():N}";
        int fallbackCalls = 0;
        int logged = 0;
        using IDisposable highRegistration = ExtensionHostRuntime.RegisterOverride(
            "test.high",
            contract,
            (TestSyncOverride)(next => next()),
            20);
        using IDisposable lowRegistration = ExtensionHostRuntime.RegisterOverride(
            "test.low",
            contract,
            (TestSyncOverride)(next => next()),
            10);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ExtensionHostRuntime.InvokeOverrideChain<TestSyncOverride, int>(
                contract,
                (implementation, next) => implementation(next),
                () =>
                {
                    fallbackCalls++;
                    throw new InvalidOperationException("default failure");
                },
                _ => logged++));

        Assert.Equal("default failure", error.Message);
        Assert.Equal(1, fallbackCalls);
        Assert.Equal(0, logged);
    }

    [Fact]
    public async Task ExtensionHostRuntime_ComposesAsyncOverridesAndCachesDownstream()
    {
        string contract = $"test.chain.async.{Guid.NewGuid():N}";
        List<string> calls = [];
        int fallbackCalls = 0;
        TestAsyncOverride high = async next =>
        {
            calls.Add("high");
            await next();
            await next();
        };
        TestAsyncOverride low = async next =>
        {
            calls.Add("low");
            await next();
        };
        using IDisposable highRegistration = ExtensionHostRuntime.RegisterOverride("test.high", contract, high, 20);
        using IDisposable lowRegistration = ExtensionHostRuntime.RegisterOverride("test.low", contract, low, 10);

        await ExtensionHostRuntime.InvokeOverrideChainAsync<TestAsyncOverride>(
            contract,
            (implementation, next) => implementation(next),
            () =>
            {
                calls.Add("default");
                fallbackCalls++;
                return Task.CompletedTask;
            },
            _ => { });

        Assert.Equal(1, fallbackCalls);
        Assert.Equal(["high", "low", "default"], calls);
    }

    [Fact]
    public void ExtensionHostRuntime_RemovesOverrideWhenRegistrationIsDisposed()
    {
        const string contract = "test.extension.override";
        object implementation = new();

        using IDisposable registration = ExtensionHostRuntime.RegisterOverride("test.extension", contract, implementation);

        Assert.True(ExtensionHostRuntime.TryGetOverride(contract, out object? resolved));
        Assert.Same(implementation, resolved);

        registration.Dispose();

        Assert.False(ExtensionHostRuntime.TryGetOverride<object>(contract, out _));
    }

    [Theory]
    [InlineData(".emerde-extension")]
    [InlineData(".zip")]
    [InlineData(".dll")]
    public void IsSupportedPackage_RequiresExistingSupportedFile(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.{Guid.NewGuid():N}{extension}");
        try
        {
            Assert.False(ExtensionService.IsSupportedPackage(path));
            File.WriteAllText(path, string.Empty);
            Assert.Equal(extension is ".emerde-extension" or ".zip", ExtensionService.IsSupportedPackage(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtensionService_InstallsPersistsAndUninstallsProcessExtension()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packageSource = Path.Combine(root, "package");
        string packagePath = Path.Combine(root, "sample.emerde-extension");
        string extensionsDirectory = Path.Combine(root, "extensions");
        try
        {
            Directory.CreateDirectory(packageSource);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "runner.exe"), string.Empty);
            await File.WriteAllBytesAsync(Path.Combine(packageSource, "icon.png"), [1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.process-extension",
                  "name": "Sample",
                  "version": "1.0.0",
                  "icon": "icon.png",
                  "execution_mode": "process",
                  "entry_point": "runner.exe",
                  "runtime": "executable",
                  "timeout_seconds": 30,
                  "settings": [
                    {
                      "key": "token",
                      "label": "Token",
                      "type": "password",
                      "sensitive": true
                    }
                  ]
                }
                """, Encoding.UTF8);
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            ExtensionService service = new(extensionsDirectory);

            ExtensionInstallResult installed = await service.InstallAsync(packagePath);
            await service.SetEnabledAsync(installed.Extension.Manifest.Id, true);
            await service.SaveSettingsAsync(installed.Extension.Manifest.Id, new Dictionary<string, string> { ["token"] = "secret" });

            InstalledExtensionInfo extension = Assert.Single(await service.GetInstalledExtensionsAsync());
            Assert.True(extension.IsEnabled);
            Assert.Equal("icon.png", extension.Manifest.Icon);
            Assert.Equal("secret", (await service.GetSettingsAsync(extension.Manifest.Id))["token"]);
            Assert.DoesNotContain("secret", await File.ReadAllTextAsync(Path.Combine(extensionsDirectory, "extensions-state.json")));

            string statePath = Path.Combine(extensionsDirectory, "extensions-state.json");
            await using (FileStream stateLock = new(statePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Assert.ThrowsAnyAsync<IOException>(() => service.SaveSettingsAsync(
                    extension.Manifest.Id,
                    new Dictionary<string, string> { ["token"] = "changed" }));
                Assert.Equal("secret", (await service.GetSettingsAsync(extension.Manifest.Id))["token"]);
            }

            await service.UninstallAsync(extension.Manifest.Id);

            Assert.Empty(await service.GetInstalledExtensionsAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ReadBoundedTextAsync_RetainsProtocolPrefixAndDiagnosticTail()
    {
        int limitNotifications = 0;
        BoundedTextReadResult protocol = await ExtensionService.ReadBoundedTextAsync(
            new StringReader("abcdefgh"),
            5,
            retainTail: false,
            () => limitNotifications++);
        BoundedTextReadResult diagnostics = await ExtensionService.ReadBoundedTextAsync(
            new StringReader("abcdefgh"),
            5,
            retainTail: true,
            null);

        Assert.Equal("abcde", protocol.Text);
        Assert.True(protocol.ExceededLimit);
        Assert.Equal(1, limitNotifications);
        Assert.Equal("defgh", diagnostics.Text);
        Assert.True(diagnostics.ExceededLimit);
    }

    [Fact]
    public async Task ExtensionService_RejectsPackagePathTraversal()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packagePath = Path.Combine(root, "unsafe.emerde-extension");
        try
        {
            Directory.CreateDirectory(root);
            using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("../outside.txt");
                await using Stream stream = entry.Open();
                await stream.WriteAsync("unsafe"u8.ToArray());
            }
            ExtensionService service = new(Path.Combine(root, "extensions"));

            await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(packagePath));

            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_RejectsInvalidMinimumHostVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packageSource = Path.Combine(root, "package");
        string packagePath = Path.Combine(root, "invalid-version.emerde-extension");
        try
        {
            Directory.CreateDirectory(packageSource);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "runner.exe"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.invalid-version",
                  "name": "Sample",
                  "version": "1.0.0",
                  "execution_mode": "process",
                  "entry_point": "runner.exe",
                  "runtime": "executable",
                  "minimum_host_version": "invalid",
                  "timeout_seconds": 30
                }
                """, Encoding.UTF8);
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            ExtensionService service = new(Path.Combine(root, "extensions"));

            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(packagePath));

            Assert.Contains("minimum_host_version", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_TreatsNullManifestCollectionsAsEmpty()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packageSource = Path.Combine(root, "package");
        string packagePath = Path.Combine(root, "null-collections.emerde-extension");
        try
        {
            Directory.CreateDirectory(packageSource);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "runner.exe"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.null-collections",
                  "name": "Sample",
                  "version": "1.0.0",
                  "execution_mode": "process",
                  "entry_point": "runner.exe",
                  "runtime": "executable",
                  "arguments": null,
                  "capabilities": null,
                  "permissions": null,
                  "settings": null,
                  "timeout_seconds": 30
                }
                """, Encoding.UTF8);
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            ExtensionService service = new(Path.Combine(root, "extensions"));

            ExtensionInstallResult installed = await service.InstallAsync(packagePath);

            Assert.Empty(installed.Extension.Manifest.Arguments);
            Assert.Empty(installed.Extension.Manifest.Capabilities);
            Assert.Empty(installed.Extension.Manifest.Permissions);
            Assert.Empty(installed.Extension.Manifest.Settings);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_RestoresLoadedExtensionWhenNewSettingsFail()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packageSource = Path.Combine(root, "package");
        string packagePath = Path.Combine(root, "settings-rollback.emerde-extension");
        ExtensionService? service = null;
        try
        {
            Directory.CreateDirectory(packageSource);
            File.Copy(typeof(SettingsRejectingExtension).Assembly.Location, Path.Combine(packageSource, "TestExtension.dll"));
            await File.WriteAllTextAsync(Path.Combine(packageSource, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.settings-rollback",
                  "name": "Sample",
                  "version": "1.0.0",
                  "execution_mode": "in_process",
                  "entry_point": "TestExtension.dll",
                  "entry_type": "Emerde.Tests.SettingsRejectingExtension",
                  "timeout_seconds": 30,
                  "settings": [
                    {
                      "key": "mode",
                      "label": "Mode",
                      "type": "choice",
                      "default": "working",
                      "options": ["working", "fail"]
                    }
                  ]
                }
                """, Encoding.UTF8);
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            service = new ExtensionService(Path.Combine(root, "extensions"));
            ExtensionInstallResult installed = await service.InstallAsync(packagePath);
            await service.SetEnabledAsync(installed.Extension.Manifest.Id, true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
                installed.Extension.Manifest.Id,
                new Dictionary<string, string> { ["mode"] = "fail" }));

            InstalledExtensionInfo restored = Assert.Single(await service.GetInstalledExtensionsAsync());
            Assert.True(restored.IsEnabled);
            Assert.True(restored.IsLoaded);
            Assert.Equal("working", (await service.GetSettingsAsync(restored.Manifest.Id))["mode"]);

            await service.ShutdownAsync();
            InstalledExtensionInfo reloaded = Assert.Single(await service.GetInstalledExtensionsAsync());
            Assert.True(reloaded.IsLoaded);
        }
        finally
        {
            if (service != null)
            {
                await service.ShutdownAsync();
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_NormalizesCaseVariantStateKeys()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string extensionsDirectory = Path.Combine(root, "extensions");
        try
        {
            Directory.CreateDirectory(extensionsDirectory);
            await File.WriteAllTextAsync(Path.Combine(extensionsDirectory, "extensions-state.json"), """
                {
                  "extensions": {
                    "sample.extension": {
                      "enabled": false,
                      "settings": {
                        "Token": "first",
                        "token": "second"
                      }
                    },
                    "SAMPLE.EXTENSION": {
                      "enabled": true,
                      "settings": null
                    }
                  }
                }
                """, Encoding.UTF8);
            ExtensionService service = new(extensionsDirectory);

            await service.InitializeAsync();

            Assert.Empty(await service.GetInstalledExtensionsAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_LoadsBackupWhenPrimaryStateIsCorrupt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Backup.{Guid.NewGuid():N}");
        string extensionsDirectory = Path.Combine(root, "extensions");
        string extensionDirectory = Path.Combine(extensionsDirectory, "sample.extension");
        try
        {
            Directory.CreateDirectory(extensionDirectory);
            await File.WriteAllTextAsync(Path.Combine(extensionDirectory, "runner.exe"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(extensionDirectory, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.extension",
                  "name": "Sample",
                  "version": "1.0.0",
                  "execution_mode": "process",
                  "entry_point": "runner.exe",
                  "runtime": "executable",
                  "settings": [
                    {
                      "key": "mode",
                      "label": "Mode",
                      "type": "text"
                    }
                  ]
                }
                """, Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(extensionsDirectory, "extensions-state.json"), "{");
            await File.WriteAllTextAsync(Path.Combine(extensionsDirectory, "extensions-state.json.bak"), """
                {
                  "extensions": {
                    "sample.extension": {
                      "enabled": false,
                      "settings": {
                        "mode": "backup"
                      }
                    }
                  }
                }
                """, Encoding.UTF8);
            ExtensionService service = new(extensionsDirectory);

            await service.InitializeAsync();

            Assert.Equal("backup", (await service.GetSettingsAsync("sample.extension"))["mode"]);
            Assert.Single(Directory.GetFiles(extensionsDirectory, "extensions-state.json.invalid-*"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static Task RunStaAsync(Action action)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private delegate int TestSyncOverride(Func<int> next);

    private delegate Task TestAsyncOverride(Func<Task> next);
}

internal sealed class TestVideoAction(string id, int order) : IExtensionVideoAction
{
    public string Id { get; } = id;

    public string Label => Id;

    public int Order { get; } = order;

    public bool CanExecute(IReadOnlyList<ExtensionVideoFileInfo> files) => true;

    public Task ExecuteAsync(IReadOnlyList<ExtensionVideoFileInfo> files, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SettingsRejectingExtension : IEmerdeExtension
{
    public ValueTask InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        if (context.Settings.TryGetValue("mode", out string? mode) && mode == "fail")
        {
            throw new InvalidOperationException("Rejected settings");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
