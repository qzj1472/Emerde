using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using Emerde.Core;

namespace Emerde.Plugins;

public static class ExtensionHostRuntime
{
    internal static readonly TimeSpan EventHandlerTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(8);
    private static readonly ConcurrentDictionary<string, object> HostObjects = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object RegistrationLock = new();
    private static readonly List<OverrideRegistration> Overrides = [];
    private static readonly List<EventRegistration> EventSubscriptions = [];
    private static readonly ObservableCollection<ExtensionUiContribution> UiItems = [];

    public static ReadOnlyObservableCollection<ExtensionUiContribution> UiContributions { get; } = new(UiItems);

    public static event EventHandler? UiContributionsChanged;

    public static IDisposable RegisterHostObject(string contractName, object instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentNullException.ThrowIfNull(instance);
        HostObjects[contractName] = instance;
        return new ActionRegistration(() => HostObjects.TryRemove(new KeyValuePair<string, object>(contractName, instance)));
    }

    public static object? GetHostObject(string contractName)
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
        EventRegistration[] subscriptions;
        lock (RegistrationLock)
        {
            subscriptions = EventSubscriptions
                .Where(item => string.Equals(item.EventName, eventName, StringComparison.OrdinalIgnoreCase)
                    && item.PayloadType.IsInstanceOfType(payload))
                .ToArray();
        }

        foreach (EventRegistration subscription in subscriptions)
        {
            try
            {
                await subscription.Handler(payload, cancellationToken).AsTask().WaitAsync(EventHandlerTimeout, cancellationToken);
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
    }

    public static IDisposable RegisterOverride(string extensionId, string contractName, object implementation, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentNullException.ThrowIfNull(implementation);
        OverrideRegistration registration = new(extensionId, contractName, implementation, priority);
        lock (RegistrationLock)
        {
            Overrides.Add(registration);
        }
        return new ActionRegistration(() =>
        {
            lock (RegistrationLock)
            {
                Overrides.Remove(registration);
            }
        });
    }

    public static bool TryGetOverride<T>(string contractName, out T? implementation) where T : class
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

    public static IDisposable RegisterUi(string extensionId, string regionName, FrameworkElement content, int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionName);
        ArgumentNullException.ThrowIfNull(content);
        ExtensionUiContribution contribution = new(extensionId, regionName, content, order);
        InvokeUi(() =>
        {
            lock (RegistrationLock)
            {
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

    internal static ExtensionUiContribution[] GetUiContributionsSnapshot()
    {
        lock (RegistrationLock)
        {
            return UiItems.ToArray();
        }
    }

    internal static void RemoveExtensionRegistrations(string extensionId)
    {
        lock (RegistrationLock)
        {
            Overrides.RemoveAll(item => string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));
            EventSubscriptions.RemoveAll(item => string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));
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
        EventHandler? handlers = UiContributionsChanged;
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
        if (HostObjectPermissions.TryGetValue(contractName, out string? permission) && !Permissions.Contains(permission))
        {
            Log("warn", "permission_denied", "extension host object access denied", new { contractName, permission });
            return null;
        }
        return ExtensionHostRuntime.GetHostObject(contractName);
    }

    public IDisposable RegisterOverride(string contractName, object implementation, int priority = 0)
    {
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
        IDisposable registration = ExtensionHostRuntime.RegisterUi(ExtensionId, regionName, content, order);
        RegisterCleanup(() =>
        {
            registration.Dispose();
            return ValueTask.CompletedTask;
        });
        return registration;
    }

    public IDisposable Subscribe<T>(string eventName, ExtensionEventHandler<T> handler)
    {
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
