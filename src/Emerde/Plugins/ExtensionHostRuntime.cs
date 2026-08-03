using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Emerde.Core;

namespace Emerde.Plugins;

public static class ExtensionHostRuntime
{
    internal static readonly TimeSpan EventHandlerTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(8);
    private static readonly ConcurrentDictionary<string, object> HostObjects = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EventPublishGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object RegistrationLock = new();
    private static readonly List<OverrideRegistration> Overrides = [];
    private static readonly List<EventRegistration> EventSubscriptions = [];
    private static readonly ObservableCollection<ExtensionUiContribution> UiItems = [];
    private static readonly List<ExtensionPageContribution> Pages = [];
    private static readonly List<ShortcutRegistration> Shortcuts = [];

    public static ReadOnlyObservableCollection<ExtensionUiContribution> UiContributions { get; } = new(UiItems);

    public static event EventHandler? UiContributionsChanged;

    internal static event EventHandler? OverridesChanged;

    internal static event EventHandler? PagesChanged;

    internal static IDisposable RegisterHostObject(string contractName, object instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentNullException.ThrowIfNull(instance);
        HostObjects[contractName] = instance;
        return new ActionRegistration(() => HostObjects.TryRemove(new KeyValuePair<string, object>(contractName, instance)));
    }

    internal static object? GetHostObject(string contractName)
    {
        return HostObjects.TryGetValue(contractName, out object? instance) ? instance : null;
    }

    internal static IDisposable Subscribe<T>(string extensionId, string eventName, ExtensionEventHandler<T> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        EventRegistration registration = new(
            extensionId,
            eventName,
            typeof(T),
            (payload, cancellationToken) => handler((T)payload, cancellationToken));
        lock (RegistrationLock)
        {
            EventSubscriptions.Add(registration);
        }
        return new ActionRegistration(() =>
        {
            lock (RegistrationLock)
            {
                EventSubscriptions.Remove(registration);
            }
        });
    }

    internal static async Task PublishAsync<T>(string eventName, T payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(payload);
        SemaphoreSlim publishGate = EventPublishGates.GetOrAdd(eventName, static _ => new SemaphoreSlim(1, 1));
        await publishGate.WaitAsync(cancellationToken);
        try
        {
            EventRegistration[] subscriptions;
            lock (RegistrationLock)
            {
                subscriptions = EventSubscriptions
                    .Where(item => string.Equals(item.EventName, eventName, StringComparison.OrdinalIgnoreCase)
                        && item.PayloadType.IsInstanceOfType(payload))
                    .ToArray();
            }
            await Task.WhenAll(subscriptions.Select(subscription => InvokeEventHandlerAsync(
                subscription,
                eventName,
                payload,
                cancellationToken)));
        }
        finally
        {
            publishGate.Release();
        }
    }

    private static async Task InvokeEventHandlerAsync<T>(
        EventRegistration subscription,
        string eventName,
        T payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await subscription.Handler(payload!, cancellationToken).AsTask().WaitAsync(EventHandlerTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            AppSessionLogger.Event("error", "extension", "event_handler_failed", "extension event handler failed", new
            {
                extensionId = subscription.ExtensionId,
                eventName,
                exception = e.GetType().FullName,
                e.Message,
            });
        }
    }

    internal static IDisposable RegisterOverride(string extensionId, string contractName, object implementation, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentNullException.ThrowIfNull(implementation);
        OverrideRegistration registration = new(extensionId, contractName, implementation, priority);
        lock (RegistrationLock)
        {
            Overrides.Add(registration);
        }
        RaiseObservers(OverridesChanged);
        return new ActionRegistration(() =>
        {
            bool removed;
            lock (RegistrationLock)
            {
                removed = Overrides.Remove(registration);
            }
            if (removed)
            {
                RaiseObservers(OverridesChanged);
            }
        });
    }

    internal static bool TryGetOverride<T>(string contractName, out T? implementation) where T : class
    {
        lock (RegistrationLock)
        {
            implementation = Overrides
                .Where(item => string.Equals(item.ContractName, contractName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Priority)
                .Select(item => item.Implementation)
                .OfType<T>()
                .FirstOrDefault();
        }
        return implementation != null;
    }

    internal static IReadOnlyList<T> GetOverrides<T>(string contractName) where T : class
    {
        lock (RegistrationLock)
        {
            return Overrides
                .Where(item => string.Equals(item.ContractName, contractName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Priority)
                .Select(item => item.Implementation)
                .OfType<T>()
                .ToArray();
        }
    }

    internal static TResult InvokeOverrideChain<TOverride, TResult>(
        string contractName,
        Func<TOverride, Func<TResult>, TResult> invoke,
        Func<TResult> fallback,
        Action<Exception> onException,
        Func<Exception, bool>? shouldHandleException = null)
        where TOverride : class
    {
        IReadOnlyList<TOverride> overrides = GetOverrides<TOverride>(contractName);
        Func<TResult> next = fallback;

        for (int index = overrides.Count - 1; index >= 0; index--)
        {
            TOverride current = overrides[index];
            Func<TResult> downstream = next;
            Lazy<TResult> downstreamResult = new(downstream, LazyThreadSafetyMode.ExecutionAndPublication);
            next = () =>
            {
                Exception? downstreamFailure = null;
                TResult InvokeDownstream()
                {
                    try
                    {
                        return downstreamResult.Value;
                    }
                    catch (Exception exception)
                    {
                        downstreamFailure = exception;
                        throw;
                    }
                }

                try
                {
                    return invoke(current, InvokeDownstream);
                }
                catch (Exception exception) when ((shouldHandleException?.Invoke(exception) ?? true)
                    && !ReferenceEquals(exception, downstreamFailure))
                {
                    onException(exception);
                    return downstreamResult.Value;
                }
            };
        }

        return next();
    }

    internal static async Task InvokeOverrideChainAsync<TOverride>(
        string contractName,
        Func<TOverride, Func<Task>, Task> invoke,
        Func<Task> fallback,
        Action<Exception> onException,
        Func<Exception, bool>? shouldHandleException = null)
        where TOverride : class
    {
        IReadOnlyList<TOverride> overrides = GetOverrides<TOverride>(contractName);
        Func<Task> next = fallback;

        for (int index = overrides.Count - 1; index >= 0; index--)
        {
            TOverride current = overrides[index];
            Func<Task> downstream = next;
            Lazy<Task> downstreamTask = new(downstream, LazyThreadSafetyMode.ExecutionAndPublication);
            next = async () =>
            {
                Exception? downstreamFailure = null;
                async Task InvokeDownstreamAsync()
                {
                    try
                    {
                        await downstreamTask.Value;
                    }
                    catch (Exception exception)
                    {
                        downstreamFailure = exception;
                        throw;
                    }
                }

                try
                {
                    await invoke(current, InvokeDownstreamAsync);
                }
                catch (Exception exception) when ((shouldHandleException?.Invoke(exception) ?? true)
                    && !ReferenceEquals(exception, downstreamFailure))
                {
                    onException(exception);
                    await downstreamTask.Value;
                }
            };
        }

        await next();
    }

    internal static IDisposable RegisterUi(string extensionId, string regionName, FrameworkElement content, int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        ArgumentNullException.ThrowIfNull(content);
        ExtensionUiContribution contribution = new(extensionId, regionName, content, order);
        InvokeUi(() =>
        {
            lock (RegistrationLock)
            {
                if (content.Parent != null || UiItems.Any(item => ReferenceEquals(item.Content, content)))
                {
                    throw new InvalidOperationException("The UI element is already attached or registered.");
                }
                UiItems.Add(contribution);
                RaiseUiContributionsChanged();
            }
        });
        return new ActionRegistration(() => InvokeUi(() =>
        {
            lock (RegistrationLock)
            {
                UiItems.Remove(contribution);
                RaiseUiContributionsChanged();
            }
        }));
    }

    internal static IDisposable RegisterPage(string extensionId, ExtensionPageDefinition page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(page.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(page.Title);
        ArgumentNullException.ThrowIfNull(page.Content);
        ExtensionPageContribution contribution = new(extensionId, page);
        InvokeUi(() =>
        {
            lock (RegistrationLock)
            {
                if (Pages.Any(item => string.Equals(item.Page.Id, page.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"Extension page id '{page.Id}' is already registered.");
                }
                if (page.Content.Parent != null || Pages.Any(item => ReferenceEquals(item.Page.Content, page.Content)))
                {
                    throw new InvalidOperationException("The extension page content is already attached or registered.");
                }
                Pages.Add(contribution);
            }
            RaiseObservers(PagesChanged);
        });
        return new ActionRegistration(() => InvokeUi(() =>
        {
            bool removed;
            lock (RegistrationLock)
            {
                removed = Pages.Remove(contribution);
            }
            if (removed)
            {
                RaiseObservers(PagesChanged);
            }
        }));
    }

    internal static IDisposable RegisterShortcut(string extensionId, ExtensionShortcutDefinition shortcut)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcut.Id);
        ArgumentNullException.ThrowIfNull(shortcut.Handler);
        ShortcutRegistration registration = new(extensionId, shortcut);
        lock (RegistrationLock)
        {
            if (Shortcuts.Any(item => string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Shortcut.Id, shortcut.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Shortcut id '{shortcut.Id}' is already registered by extension '{extensionId}'.");
            }
            Shortcuts.Add(registration);
        }
        return new ActionRegistration(() =>
        {
            lock (RegistrationLock)
            {
                Shortcuts.Remove(registration);
            }
        });
    }

    internal static ExtensionPageContribution[] GetPagesSnapshot()
    {
        lock (RegistrationLock)
        {
            return Pages
                .OrderBy(item => item.Page.Order)
                .ThenBy(item => item.Page.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    internal static bool TryHandleShortcut(Key key, ModifierKeys modifiers)
    {
        ShortcutRegistration[] shortcuts;
        lock (RegistrationLock)
        {
            shortcuts = Shortcuts
                .Where(item => item.Shortcut.Key == key && item.Shortcut.Modifiers == modifiers)
                .OrderByDescending(item => item.Shortcut.Priority)
                .ToArray();
        }

        foreach (ShortcutRegistration registration in shortcuts)
        {
            try
            {
                if (registration.Shortcut.CanExecute?.Invoke() == false)
                {
                    continue;
                }
                if (registration.Shortcut.Handler())
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                AppSessionLogger.Event("error", "extension", "shortcut_failed", "extension shortcut failed", new
                {
                    registration.ExtensionId,
                    registration.Shortcut.Id,
                    exception = exception.GetType().FullName,
                    exception.Message,
                });
            }
        }

        return false;
    }

    internal static ExtensionUiContribution[] GetUiContributionsSnapshot()
    {
        lock (RegistrationLock)
        {
            return UiItems.ToArray();
        }
    }

    internal static void RemoveExtensionRegistrations(string extensionId)
    {
        bool overridesRemoved;
        bool pagesRemoved;
        lock (RegistrationLock)
        {
            overridesRemoved = Overrides.RemoveAll(item => string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)) > 0;
            EventSubscriptions.RemoveAll(item => string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));
            pagesRemoved = Pages.RemoveAll(item => string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)) > 0;
            Shortcuts.RemoveAll(item => string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));
        }
        if (overridesRemoved)
        {
            RaiseObservers(OverridesChanged);
        }
        if (pagesRemoved)
        {
            RaiseObservers(PagesChanged);
        }
        InvokeUi(() =>
        {
            lock (RegistrationLock)
            {
                foreach (ExtensionUiContribution item in UiItems.Where(item => string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    UiItems.Remove(item);
                }
                RaiseUiContributionsChanged();
            }
        });
    }

    private static void RaiseUiContributionsChanged()
    {
        RaiseObservers(UiContributionsChanged);
    }

    private static void RaiseObservers(EventHandler? handlers)
    {
        if (handlers == null)
        {
            return;
        }
        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(null, EventArgs.Empty);
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
        }
    }

    private static void InvokeUi(Action action)
    {
        if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.Invoke(action);
    }

    private sealed record OverrideRegistration(string ExtensionId, string ContractName, object Implementation, int Priority);

    private sealed record EventRegistration(
        string ExtensionId,
        string EventName,
        Type PayloadType,
        Func<object, CancellationToken, ValueTask> Handler);

    private sealed record ShortcutRegistration(string ExtensionId, ExtensionShortcutDefinition Shortcut);

    internal sealed class ActionRegistration(Action dispose) : IDisposable
    {
        private Action? disposeAction = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref disposeAction, null)?.Invoke();
        }
    }
}

internal sealed class ExtensionContext(
    string extensionId,
    string extensionDirectory,
    string dataDirectory,
    IReadOnlyDictionary<string, string> settings,
    IEnumerable<string>? permissions = null) : IExtensionContext, IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<string, string> HostObjectPermissions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ExtensionContractNames.PlatformCookies] = ExtensionPermissionNames.PlatformCookieRead,
        [ExtensionContractNames.Application] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.MainWindow] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.MainViewModel] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.MainContentOverlay] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.VideoSelection] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.DialogService] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.PreviewService] = ExtensionPermissionNames.PreviewControl,
        [ExtensionContractNames.MediaService] = ExtensionPermissionNames.MediaControl,
        [ExtensionContractNames.RecordingService] = ExtensionPermissionNames.RecordingControl,
        [ExtensionContractNames.NavigationService] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.NotificationService] = ExtensionPermissionNames.NotificationWrite,
        [ExtensionContractNames.LogService] = ExtensionPermissionNames.LogWrite,
        [ExtensionContractNames.LogExportService] = ExtensionPermissionNames.LogExport,
        [ExtensionContractNames.UpdateService] = ExtensionPermissionNames.UpdateOpen,
    };
    private static readonly IReadOnlyDictionary<string, string> OverridePermissions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ExtensionContractNames.StreamResolver] = ExtensionPermissionNames.CoreOverride,
        [ExtensionContractNames.Monitor] = ExtensionPermissionNames.CoreOverride,
        [ExtensionContractNames.Recorder] = ExtensionPermissionNames.CoreOverride,
        [ExtensionContractNames.RecorderStop] = ExtensionPermissionNames.CoreOverride,
        [ExtensionContractNames.RecorderReconnect] = ExtensionPermissionNames.CoreOverride,
        [ExtensionContractNames.PostProcessing] = ExtensionPermissionNames.CoreOverride,
        [ExtensionContractNames.VideoListActions] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.HomeRoomActions] = ExtensionPermissionNames.UiModify,
        [ExtensionContractNames.HomeCardTemplate] = ExtensionPermissionNames.UiModify,
    };
    private static readonly IReadOnlyDictionary<string, string> EventPermissions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ExtensionEventNames.MediaFinalized] = ExtensionPermissionNames.MediaFinalizedRead,
        [ExtensionEventNames.PreviewStateChanged] = ExtensionPermissionNames.PreviewEventsRead,
        [ExtensionEventNames.MediaOperationChanged] = ExtensionPermissionNames.MediaOperationsRead,
        [ExtensionEventNames.RecordingLifecycle] = ExtensionPermissionNames.RecordingEventsRead,
    };
    private static readonly IReadOnlyDictionary<string, Type> OverrideTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        [ExtensionContractNames.StreamResolver] = typeof(ExtensionStreamResolverOverride),
        [ExtensionContractNames.Monitor] = typeof(ExtensionMonitorOverride),
        [ExtensionContractNames.Recorder] = typeof(ExtensionRecorderOverride),
        [ExtensionContractNames.RecorderStop] = typeof(ExtensionRecorderStopOverride),
        [ExtensionContractNames.RecorderReconnect] = typeof(ExtensionRecorderReconnectOverride),
        [ExtensionContractNames.PostProcessing] = typeof(ExtensionPostProcessingOverride),
        [ExtensionContractNames.VideoListActions] = typeof(IExtensionVideoAction),
        [ExtensionContractNames.HomeRoomActions] = typeof(IExtensionRoomAction),
        [ExtensionContractNames.HomeCardTemplate] = typeof(DataTemplate),
    };
    private static readonly IReadOnlyDictionary<string, Type> EventTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        [ExtensionEventNames.MediaFinalized] = typeof(ExtensionMediaFinalizedEvent),
        [ExtensionEventNames.PreviewStateChanged] = typeof(ExtensionPreviewStateChangedEvent),
        [ExtensionEventNames.MediaOperationChanged] = typeof(ExtensionMediaOperationChangedEvent),
        [ExtensionEventNames.RecordingLifecycle] = typeof(ExtensionRecordingLifecycleEvent),
    };
    private readonly List<Func<ValueTask>> cleanupActions = [];
    private readonly object cleanupLock = new();

    public string ExtensionId { get; } = extensionId;

    public string ExtensionDirectory { get; } = extensionDirectory;

    public string DataDirectory { get; } = dataDirectory;

    public Version HostVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public IReadOnlyDictionary<string, string> Settings { get; } = settings;

    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(permissions ?? [], StringComparer.OrdinalIgnoreCase);

    public object? GetHostObject(string contractName)
    {
        if (!HostObjectPermissions.TryGetValue(contractName, out string? permission))
        {
            Log("warn", "unsupported_contract", "extension requested an unsupported host object", new { contractName });
            return null;
        }
        if (!Permissions.Contains(permission))
        {
            Log("warn", "permission_denied", "extension host object access denied", new { contractName, permission });
            return null;
        }
        return ExtensionHostRuntime.GetHostObject(contractName);
    }

    public IDisposable RegisterOverride(string contractName, object implementation, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        DemandPermission(OverridePermissions, contractName, "override registration");
        ValidateContractType(OverrideTypes, contractName, implementation.GetType(), "override registration");
        IDisposable registration = ExtensionHostRuntime.RegisterOverride(ExtensionId, contractName, implementation, priority);
        RegisterCleanup(() =>
        {
            registration.Dispose();
            return ValueTask.CompletedTask;
        });
        return registration;
    }

    public IDisposable RegisterUi(string regionName, FrameworkElement content, int order = 0)
    {
        DemandPermission(ExtensionPermissionNames.UiModify, regionName, "UI registration");
        IDisposable registration = ExtensionHostRuntime.RegisterUi(ExtensionId, regionName, content, order);
        RegisterCleanup(() =>
        {
            registration.Dispose();
            return ValueTask.CompletedTask;
        });
        return registration;
    }

    public IDisposable RegisterPage(ExtensionPageDefinition page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DemandPermission(ExtensionPermissionNames.UiModify, page.Id, "page registration");
        IDisposable registration = ExtensionHostRuntime.RegisterPage(ExtensionId, page);
        RegisterCleanup(() =>
        {
            registration.Dispose();
            return ValueTask.CompletedTask;
        });
        return registration;
    }

    public IDisposable RegisterShortcut(ExtensionShortcutDefinition shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        DemandPermission(ExtensionPermissionNames.ShortcutRegister, shortcut.Id, "shortcut registration");
        IDisposable registration = ExtensionHostRuntime.RegisterShortcut(ExtensionId, shortcut);
        RegisterCleanup(() =>
        {
            registration.Dispose();
            return ValueTask.CompletedTask;
        });
        return registration;
    }

    public IDisposable Subscribe<T>(string eventName, ExtensionEventHandler<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        DemandPermission(EventPermissions, eventName, "event subscription");
        ValidateContractType(EventTypes, eventName, typeof(T), "event subscription");
        IDisposable registration = ExtensionHostRuntime.Subscribe(ExtensionId, eventName, handler);
        RegisterCleanup(() =>
        {
            registration.Dispose();
            return ValueTask.CompletedTask;
        });
        return registration;
    }

    public IDisposable RegisterCleanup(Func<ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (cleanupLock)
        {
            cleanupActions.Add(cleanup);
        }
        return new ExtensionHostRuntime.ActionRegistration(() =>
        {
            lock (cleanupLock)
            {
                cleanupActions.Remove(cleanup);
            }
        });
    }

    public void Log(string level, string eventName, string message, object? data = null)
    {
        AppSessionLogger.Event(level, "extension", eventName, message, new { extensionId = ExtensionId, data });
    }

    private void DemandPermission(IReadOnlyDictionary<string, string> permissions, string contractName, string operation)
    {
        if (permissions.TryGetValue(contractName, out string? permission))
        {
            DemandPermission(permission, contractName, operation);
            return;
        }
        if (IsReservedContractName(contractName))
        {
            Log("warn", "unsupported_contract", "extension requested an unsupported reserved contract", new { operation, contractName });
            throw new NotSupportedException($"Extension contract '{contractName}' is not supported for {operation}.");
        }
    }

    private static void ValidateContractType(
        IReadOnlyDictionary<string, Type> contractTypes,
        string contractName,
        Type implementationType,
        string operation)
    {
        if (!contractTypes.TryGetValue(contractName, out Type? expectedType))
        {
            return;
        }
        if (!expectedType.IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"Contract '{contractName}' requires '{expectedType.FullName}' for {operation}, but received '{implementationType.FullName}'.",
                nameof(implementationType));
        }
    }

    private static bool IsReservedContractName(string contractName)
    {
        return contractName.StartsWith("core.", StringComparison.OrdinalIgnoreCase)
            || contractName.StartsWith("host.", StringComparison.OrdinalIgnoreCase)
            || contractName.StartsWith("ui.", StringComparison.OrdinalIgnoreCase)
            || contractName.StartsWith("media.", StringComparison.OrdinalIgnoreCase)
            || contractName.StartsWith("preview.", StringComparison.OrdinalIgnoreCase)
            || contractName.StartsWith("recording.", StringComparison.OrdinalIgnoreCase);
    }

    private void DemandPermission(string permission, string contractName, string operation)
    {
        if (Permissions.Contains(permission))
        {
            return;
        }

        Log("warn", "permission_denied", "extension permission denied", new { operation, contractName, permission });
        throw new UnauthorizedAccessException($"Extension '{ExtensionId}' requires permission '{permission}' for {operation}.");
    }

    public async ValueTask DisposeAsync()
    {
        Func<ValueTask>[] actions;
        lock (cleanupLock)
        {
            actions = cleanupActions.AsEnumerable().Reverse().ToArray();
            cleanupActions.Clear();
        }
        foreach (Func<ValueTask> cleanup in actions)
        {
            try
            {
                await cleanup().AsTask().WaitAsync(ExtensionHostRuntime.CleanupTimeout);
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
        }
        ExtensionHostRuntime.RemoveExtensionRegistrations(ExtensionId);
    }
}

internal sealed class ExtensionPlatformCookieProvider : IExtensionPlatformCookieProvider
{
    public string GetCookie(string platformName)
    {
        return PlatformCookieStore.GetCookie(platformName);
    }
}
