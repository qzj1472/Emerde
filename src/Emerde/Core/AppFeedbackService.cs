using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Emerde.Core;

public enum AppFeedbackKind
{
    Success,
    Information,
    Warning,
    Error,
    Task,
}

internal sealed record AppFeedbackAction(string Label, Func<CancellationToken, Task> ExecuteAsync);

internal sealed record AppFeedbackRequest(
    AppFeedbackKind Kind,
    string Title,
    string? Body = null,
    string? Key = null,
    object? Owner = null,
    AppFeedbackAction? Action = null,
    double? Progress = null,
    bool IsTaskCompleted = false,
    bool IsPersistent = false);

public sealed record AppFeedbackNotification(
    Guid Id,
    string? Key,
    AppFeedbackKind Kind,
    string Title,
    string Body,
    string? ActionLabel,
    double? Progress,
    bool IsTaskCompleted,
    bool IsPersistent,
    bool IsActionRunning,
    int RepetitionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    public bool HasAction => !string.IsNullOrWhiteSpace(ActionLabel);

    public bool IsTask => Kind == AppFeedbackKind.Task;

    public bool IsIndeterminate => IsTask && Progress == null && !IsTaskCompleted;

    public double ProgressValue => Math.Clamp(Progress ?? (IsTaskCompleted ? 1d : 0d), 0d, 1d) * 100d;
}

internal sealed record AppFeedbackHostSnapshot(
    IReadOnlyList<AppFeedbackNotification> Visible,
    IReadOnlyList<AppFeedbackNotification> History);

internal sealed class AppFeedbackService : IDisposable
{
    private const int MaximumVisibleCount = 2;
    private const int MaximumHistoryCount = 20;
    private static readonly Regex WindowsPathPattern = new(
        "(?<![a-zA-Z0-9])(?:[a-zA-Z]:\\\\|\\\\\\\\)[^\\r\\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Lazy<AppFeedbackService> Shared = new(() => new AppFeedbackService());
    private readonly object syncRoot = new();
    private readonly Dictionary<Guid, HostRegistration> hosts = [];
    private readonly List<FeedbackState> feedback = [];
    private readonly System.Threading.Timer expirationTimer;
    private long activationSequence;
    private bool disposed;

    internal AppFeedbackService()
    {
        expirationTimer = new System.Threading.Timer(ExpireNotifications, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public static AppFeedbackService Current => Shared.Value;

    public IDisposable RegisterHost(object owner, Dispatcher dispatcher, Action<AppFeedbackHostSnapshot> snapshotChanged)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(snapshotChanged);

        HostRegistration registration;
        IReadOnlyList<Delivery> deliveries;
        lock (syncRoot)
        {
            ThrowIfDisposed();
            registration = new HostRegistration(Guid.NewGuid(), owner, dispatcher, snapshotChanged)
            {
                ActivationOrder = ++activationSequence,
            };
            hosts.Add(registration.Id, registration);
            ClaimPendingFeedbackLocked(registration);
            ApplyHostPauseStateLocked(registration);
            ScheduleExpirationLocked();
            deliveries = CreateDeliveriesLocked([registration.Id]);
        }

        Dispatch(deliveries);
        return new HostRegistrationLease(this, registration.Id);
    }

    public Guid Success(string title, string? body = null, string? key = null, object? owner = null, AppFeedbackAction? action = null)
    {
        return Show(new AppFeedbackRequest(AppFeedbackKind.Success, title, body, key, owner, action));
    }

    public Guid Information(string title, string? body = null, string? key = null, object? owner = null, AppFeedbackAction? action = null)
    {
        return Show(new AppFeedbackRequest(AppFeedbackKind.Information, title, body, key, owner, action));
    }

    public Guid Warning(string title, string? body = null, string? key = null, object? owner = null, AppFeedbackAction? action = null)
    {
        return Show(new AppFeedbackRequest(AppFeedbackKind.Warning, title, body, key, owner, action));
    }

    public Guid Error(string title, string? body = null, string? key = null, object? owner = null, AppFeedbackAction? action = null)
    {
        return Show(new AppFeedbackRequest(AppFeedbackKind.Error, title, body, key, owner, action, IsPersistent: true));
    }

    public Guid TaskFeedback(
        string title,
        string? body = null,
        string? key = null,
        object? owner = null,
        double? progress = null,
        AppFeedbackAction? action = null)
    {
        return Show(new AppFeedbackRequest(AppFeedbackKind.Task, title, body, key, owner, action, progress, IsPersistent: true));
    }

    public Guid Show(AppFeedbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Feedback title cannot be empty.", nameof(request));
        }

        IReadOnlyList<Delivery> deliveries;
        Guid id;
        lock (syncRoot)
        {
            ThrowIfDisposed();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            HostRegistration? host = ResolveHostLocked(request.Owner);
            FeedbackState? existing = FindByKeyLocked(request.Key, request.Owner, host?.Id);
            if (existing == null)
            {
                existing = CreateStateLocked(request, host, now);
                feedback.Add(existing);
            }
            else
            {
                UpdateStateLocked(existing, request, host, now);
            }

            MakeVisibleLocked(existing, now);
            TrimHistoryLocked();
            ScheduleExpirationLocked();
            id = existing.Id;
            deliveries = CreateDeliveriesLocked(GetAffectedHostIdsLocked(existing.HostId));
        }

        Dispatch(deliveries);
        return id;
    }

    public bool CompleteTask(string key, string title, string? body = null, bool succeeded = true, object? owner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        IReadOnlyList<Delivery> deliveries;
        bool found;
        lock (syncRoot)
        {
            ThrowIfDisposed();
            HostRegistration? host = ResolveHostLocked(owner);
            FeedbackState? state = FindByKeyLocked(key, owner, host?.Id);
            found = state != null;
            if (!found)
            {
                return false;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            state!.Kind = succeeded ? AppFeedbackKind.Success : AppFeedbackKind.Warning;
            state.Title = title.Trim();
            state.Body = body?.Trim() ?? string.Empty;
            state.Progress = 1d;
            state.IsTaskCompleted = true;
            state.IsPersistent = RequiresPersistentDisplay(state.Kind, state.Title, state.Body, false);
            state.UpdatedAt = now;
            state.RepetitionCount++;
            ResetExpirationLocked(state, now);
            MakeVisibleLocked(state, now);
            ScheduleExpirationLocked();
            deliveries = CreateDeliveriesLocked(GetAffectedHostIdsLocked(state.HostId));
        }

        Dispatch(deliveries);
        return true;
    }

    public bool UpdateTask(
        string key,
        string title,
        string? body = null,
        double? progress = null,
        object? owner = null,
        AppFeedbackAction? action = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Guid id = Show(new AppFeedbackRequest(
            AppFeedbackKind.Task,
            title,
            body,
            key,
            owner,
            action,
            progress,
            IsPersistent: true));
        return id != Guid.Empty;
    }

    public void Dismiss(Guid id)
    {
        IReadOnlyList<Delivery> deliveries;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            FeedbackState? state = feedback.FirstOrDefault(item => item.Id == id);
            if (state == null)
            {
                return;
            }

            feedback.Remove(state);
            ScheduleExpirationLocked();
            deliveries = CreateDeliveriesLocked(GetAffectedHostIdsLocked(state.HostId));
        }

        Dispatch(deliveries);
    }

    public void Archive(Guid id)
    {
        Hide(id);
    }

    private void Hide(Guid id)
    {
        IReadOnlyList<Delivery> deliveries;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            FeedbackState? state = feedback.FirstOrDefault(item => item.Id == id);
            if (state == null || !state.IsVisible)
            {
                return;
            }

            state.IsVisible = false;
            state.IsHovered = false;
            state.Deadline = null;
            ScheduleExpirationLocked();
            deliveries = CreateDeliveriesLocked(GetAffectedHostIdsLocked(state.HostId));
        }

        Dispatch(deliveries);
    }

    public void SetHovered(Guid id, bool isHovered)
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            FeedbackState? state = feedback.FirstOrDefault(item => item.Id == id);
            if (state == null || state.IsHovered == isHovered)
            {
                return;
            }

            state.IsHovered = isHovered;
            UpdatePauseStateLocked(state);
            ScheduleExpirationLocked();
        }
    }

    public void SetHostActive(object owner, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            HostRegistration? registration = hosts.Values.FirstOrDefault(item => ReferenceEquals(item.Owner, owner));
            if (registration == null || registration.IsActive == isActive)
            {
                return;
            }

            registration.IsActive = isActive;
            if (isActive)
            {
                registration.ActivationOrder = ++activationSequence;
            }
            ApplyHostPauseStateLocked(registration);
            ScheduleExpirationLocked();
        }
    }

    public async Task ExecuteActionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        AppFeedbackAction? action;
        IReadOnlyList<Delivery> deliveries;
        lock (syncRoot)
        {
            ThrowIfDisposed();
            FeedbackState? state = feedback.FirstOrDefault(item => item.Id == id);
            if (state?.Action == null || state.IsActionRunning)
            {
                return;
            }

            state.IsActionRunning = true;
            action = state.Action;
            deliveries = CreateDeliveriesLocked(GetAffectedHostIdsLocked(state.HostId));
        }
        Dispatch(deliveries);

        try
        {
            await action.ExecuteAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            IReadOnlyList<Delivery> failureDeliveries;
            lock (syncRoot)
            {
                FeedbackState? state = feedback.FirstOrDefault(item => item.Id == id);
                if (state == null)
                {
                    return;
                }

                state.Kind = AppFeedbackKind.Error;
                state.Body = exception.Message;
                state.IsPersistent = true;
                state.IsVisible = true;
                state.UpdatedAt = DateTimeOffset.UtcNow;
                state.Deadline = null;
                failureDeliveries = CreateDeliveriesLocked(GetAffectedHostIdsLocked(state.HostId));
            }
            Dispatch(failureDeliveries);
        }
        finally
        {
            IReadOnlyList<Delivery> finalDeliveries;
            lock (syncRoot)
            {
                FeedbackState? state = feedback.FirstOrDefault(item => item.Id == id);
                if (state != null)
                {
                    state.IsActionRunning = false;
                    finalDeliveries = CreateDeliveriesLocked(GetAffectedHostIdsLocked(state.HostId));
                }
                else
                {
                    finalDeliveries = [];
                }
            }
            Dispatch(finalDeliveries);
        }
    }

    public AppFeedbackHostSnapshot GetSnapshot(object? owner = null)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            HostRegistration? host = owner == null ? ResolveHostLocked(null) : ResolveHostLocked(owner);
            return CreateSnapshotLocked(host?.Id, includePending: host == null);
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (HostRegistration host in hosts.Values)
            {
                Volatile.Write(ref host.IsRegistered, 0);
            }
            hosts.Clear();
            feedback.Clear();
            expirationTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        expirationTimer.Dispose();
    }

    internal static TimeSpan? CalculateDisplayDuration(AppFeedbackKind kind, string title, string? body = null, bool isTaskCompleted = false)
    {
        string combined = string.Join(' ', new[] { title, body }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (kind == AppFeedbackKind.Error || ContainsPath(combined))
        {
            return null;
        }

        if (kind == AppFeedbackKind.Task && !isTaskCompleted)
        {
            return null;
        }

        double length = CalculateChineseReadingLength(combined);
        if (length <= 12d)
        {
            return TimeSpan.FromSeconds(3);
        }
        if (length <= 30d)
        {
            return TimeSpan.FromSeconds(5);
        }
        if (length <= 60d)
        {
            return TimeSpan.FromSeconds(8);
        }
        return TimeSpan.FromSeconds(Math.Min(12d, 8d + Math.Ceiling((length - 60d) / 10d)));
    }

    internal static double CalculateChineseReadingLength(string text)
    {
        double length = 0d;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            bool fullWidth = value is >= 0x2E80 and <= 0x9FFF
                or >= 0xF900 and <= 0xFAFF
                or >= 0xFF01 and <= 0xFF60
                or >= 0xFFE0 and <= 0xFFE6;
            length += fullWidth ? 1d : 0.5d;
        }
        return length;
    }

    private static bool ContainsPath(string text)
    {
        return WindowsPathPattern.IsMatch(text);
    }

    private static bool RequiresPersistentDisplay(AppFeedbackKind kind, string title, string body, bool requested)
    {
        return requested || CalculateDisplayDuration(kind, title, body) == null;
    }

    private FeedbackState CreateStateLocked(AppFeedbackRequest request, HostRegistration? host, DateTimeOffset now)
    {
        string title = request.Title.Trim();
        string body = request.Body?.Trim() ?? string.Empty;
        bool persistent = RequiresPersistentDisplay(request.Kind, title, body, request.IsPersistent)
            || request.Kind == AppFeedbackKind.Task && !request.IsTaskCompleted;
        TimeSpan? duration = persistent
            ? null
            : CalculateDisplayDuration(request.Kind, title, body, request.IsTaskCompleted);
        return new FeedbackState
        {
            Id = Guid.NewGuid(),
            Key = string.IsNullOrWhiteSpace(request.Key) ? null : request.Key.Trim(),
            Kind = request.Kind,
            Title = title,
            Body = body,
            Action = request.Action,
            Progress = NormalizeProgress(request.Progress),
            IsTaskCompleted = request.IsTaskCompleted,
            IsPersistent = persistent,
            IsVisible = true,
            HostId = host?.Id,
            RequestedOwner = request.Owner == null ? null : new WeakReference<object>(request.Owner),
            CreatedAt = now,
            UpdatedAt = now,
            RepetitionCount = 1,
            Remaining = duration,
        };
    }

    private void UpdateStateLocked(FeedbackState state, AppFeedbackRequest request, HostRegistration? host, DateTimeOffset now)
    {
        state.Kind = request.Kind;
        state.Title = request.Title.Trim();
        state.Body = request.Body?.Trim() ?? string.Empty;
        state.Action = request.Action;
        state.Progress = NormalizeProgress(request.Progress);
        state.IsTaskCompleted = request.IsTaskCompleted;
        state.IsPersistent = RequiresPersistentDisplay(request.Kind, state.Title, state.Body, request.IsPersistent)
            || request.Kind == AppFeedbackKind.Task && !request.IsTaskCompleted;
        state.HostId = host?.Id ?? state.HostId;
        state.RequestedOwner = request.Owner == null ? state.RequestedOwner : new WeakReference<object>(request.Owner);
        state.UpdatedAt = now;
        state.RepetitionCount++;
        ResetExpirationLocked(state, now);
    }

    private static double? NormalizeProgress(double? progress)
    {
        return progress == null ? null : Math.Clamp(progress.Value, 0d, 1d);
    }

    private void ResetExpirationLocked(FeedbackState state, DateTimeOffset now)
    {
        state.Deadline = null;
        state.Remaining = state.IsPersistent
            ? null
            : CalculateDisplayDuration(state.Kind, state.Title, state.Body, state.IsTaskCompleted);
        if (CanRunExpirationLocked(state))
        {
            state.Deadline = now + state.Remaining.GetValueOrDefault();
        }
    }

    private void MakeVisibleLocked(FeedbackState state, DateTimeOffset now)
    {
        state.IsVisible = true;
        Guid? hostId = state.HostId;
        FeedbackState[] visible = feedback
            .Where(item => item.IsVisible && item.HostId == hostId)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .ToArray();
        foreach (FeedbackState overflow in visible.Skip(MaximumVisibleCount))
        {
            overflow.IsVisible = false;
            overflow.IsHovered = false;
            overflow.Deadline = null;
        }

        if (state.IsVisible && state.Deadline == null && CanRunExpirationLocked(state))
        {
            state.Deadline = now + state.Remaining.GetValueOrDefault();
        }
    }

    private bool CanRunExpirationLocked(FeedbackState state)
    {
        if (!state.IsVisible || state.IsPersistent || state.Remaining == null || state.IsHovered)
        {
            return false;
        }

        return state.HostId is Guid hostId && hosts.TryGetValue(hostId, out HostRegistration? host) && host.IsActive;
    }

    private void UpdatePauseStateLocked(FeedbackState state)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (CanRunExpirationLocked(state))
        {
            state.Deadline ??= now + state.Remaining.GetValueOrDefault();
            return;
        }

        if (state.Deadline is DateTimeOffset deadline)
        {
            state.Remaining = deadline > now ? deadline - now : TimeSpan.Zero;
            state.Deadline = null;
        }
    }

    private void ApplyHostPauseStateLocked(HostRegistration host)
    {
        foreach (FeedbackState state in feedback.Where(item => item.HostId == host.Id))
        {
            UpdatePauseStateLocked(state);
        }
    }

    private HostRegistration? ResolveHostLocked(object? owner)
    {
        if (owner != null)
        {
            return hosts.Values.FirstOrDefault(item => ReferenceEquals(item.Owner, owner));
        }

        return hosts.Values
            .OrderByDescending(item => item.IsActive)
            .ThenByDescending(item => item.ActivationOrder)
            .FirstOrDefault();
    }

    private FeedbackState? FindByKeyLocked(string? key, object? owner, Guid? hostId)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string normalized = key.Trim();
        return feedback
            .Where(item => string.Equals(item.Key, normalized, StringComparison.Ordinal))
            .Where(item => hostId != null
                ? item.HostId == hostId
                : item.HostId == null && OwnersMatch(item.RequestedOwner, owner))
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();
    }

    private static bool OwnersMatch(WeakReference<object>? reference, object? owner)
    {
        if (reference == null)
        {
            return owner == null;
        }
        return reference.TryGetTarget(out object? target) && ReferenceEquals(target, owner);
    }

    private void ClaimPendingFeedbackLocked(HostRegistration host)
    {
        foreach (FeedbackState state in feedback.Where(item => item.HostId == null))
        {
            if (state.RequestedOwner == null || OwnersMatch(state.RequestedOwner, host.Owner))
            {
                state.HostId = host.Id;
            }
        }
    }

    private IReadOnlyList<Guid> GetAffectedHostIdsLocked(Guid? hostId)
    {
        if (hostId is Guid id && hosts.ContainsKey(id))
        {
            return [id];
        }
        return [];
    }

    private IReadOnlyList<Delivery> CreateDeliveriesLocked(IEnumerable<Guid> hostIds)
    {
        return hostIds
            .Distinct()
            .Where(hosts.ContainsKey)
            .Select(id =>
            {
                HostRegistration host = hosts[id];
                return new Delivery(host, CreateSnapshotLocked(id, includePending: false));
            })
            .ToArray();
    }

    private AppFeedbackHostSnapshot CreateSnapshotLocked(Guid? hostId, bool includePending)
    {
        IEnumerable<FeedbackState> scoped = feedback.Where(item => item.HostId == hostId || includePending && item.HostId == null);
        AppFeedbackNotification[] history = scoped
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .Take(MaximumHistoryCount)
            .Select(ToNotification)
            .ToArray();
        AppFeedbackNotification[] visible = scoped
            .Where(item => item.IsVisible)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .Take(MaximumVisibleCount)
            .Select(ToNotification)
            .ToArray();
        return new AppFeedbackHostSnapshot(visible, history);
    }

    private static AppFeedbackNotification ToNotification(FeedbackState state)
    {
        return new AppFeedbackNotification(
            state.Id,
            state.Key,
            state.Kind,
            state.Title,
            state.Body,
            state.Action?.Label,
            state.Progress,
            state.IsTaskCompleted,
            state.IsPersistent,
            state.IsActionRunning,
            state.RepetitionCount,
            state.CreatedAt,
            state.UpdatedAt);
    }

    private void TrimHistoryLocked()
    {
        foreach (IGrouping<Guid?, FeedbackState> group in feedback.GroupBy(item => item.HostId))
        {
            FeedbackState[] excess = group
                .OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.CreatedAt)
                .Skip(MaximumHistoryCount)
                .ToArray();
            foreach (FeedbackState state in excess)
            {
                feedback.Remove(state);
            }
        }
    }

    private void ScheduleExpirationLocked()
    {
        if (disposed)
        {
            return;
        }

        DateTimeOffset? deadline = feedback
            .Where(item => item.IsVisible && item.Deadline != null)
            .Select(item => item.Deadline)
            .Min();
        if (deadline == null)
        {
            expirationTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        TimeSpan due = deadline.Value - DateTimeOffset.UtcNow;
        if (due < TimeSpan.FromMilliseconds(10))
        {
            due = TimeSpan.FromMilliseconds(10);
        }
        expirationTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void ExpireNotifications(object? state)
    {
        IReadOnlyList<Delivery> deliveries;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            Guid[] affected = feedback
                .Where(item => item.IsVisible && item.Deadline <= now)
                .Select(item =>
                {
                    item.IsVisible = false;
                    item.Deadline = null;
                    return item.HostId;
                })
                .Where(id => id != null)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
            ScheduleExpirationLocked();
            deliveries = CreateDeliveriesLocked(affected);
        }
        Dispatch(deliveries);
    }

    private static void Dispatch(IEnumerable<Delivery> deliveries)
    {
        foreach (Delivery delivery in deliveries)
        {
            if (delivery.Host.Dispatcher.HasShutdownStarted || delivery.Host.Dispatcher.HasShutdownFinished)
            {
                continue;
            }

            if (delivery.Host.Dispatcher.CheckAccess())
            {
                DeliverSnapshot(delivery);
            }
            else
            {
                try
                {
                    delivery.Host.Dispatcher.BeginInvoke(
                        DispatcherPriority.DataBind,
                        new Action(() => DeliverSnapshot(delivery)));
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private static void DeliverSnapshot(Delivery delivery)
    {
        if (Volatile.Read(ref delivery.Host.IsRegistered) == 0)
        {
            return;
        }

        try
        {
            delivery.Host.SnapshotChanged(delivery.Snapshot);
        }
        catch (Exception exception)
        {
            AppSessionLogger.WriteException(exception);
        }
    }

    private void UnregisterHost(Guid hostId)
    {
        lock (syncRoot)
        {
            if (disposed || !hosts.Remove(hostId, out HostRegistration? host))
            {
                return;
            }
            Volatile.Write(ref host.IsRegistered, 0);

            foreach (FeedbackState state in feedback.Where(item => item.HostId == hostId))
            {
                state.HostId = null;
                state.RequestedOwner = new WeakReference<object>(host.Owner);
                state.Deadline = null;
            }
            ScheduleExpirationLocked();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed class FeedbackState
    {
        public required Guid Id { get; init; }
        public string? Key { get; init; }
        public required AppFeedbackKind Kind { get; set; }
        public required string Title { get; set; }
        public required string Body { get; set; }
        public AppFeedbackAction? Action { get; set; }
        public double? Progress { get; set; }
        public bool IsTaskCompleted { get; set; }
        public bool IsPersistent { get; set; }
        public bool IsActionRunning { get; set; }
        public bool IsVisible { get; set; }
        public bool IsHovered { get; set; }
        public Guid? HostId { get; set; }
        public WeakReference<object>? RequestedOwner { get; set; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; set; }
        public int RepetitionCount { get; set; }
        public TimeSpan? Remaining { get; set; }
        public DateTimeOffset? Deadline { get; set; }
    }

    private sealed class HostRegistration(Guid id, object owner, Dispatcher dispatcher, Action<AppFeedbackHostSnapshot> snapshotChanged)
    {
        public Guid Id { get; } = id;
        public object Owner { get; } = owner;
        public Dispatcher Dispatcher { get; } = dispatcher;
        public Action<AppFeedbackHostSnapshot> SnapshotChanged { get; } = snapshotChanged;
        public bool IsActive { get; set; } = true;
        public long ActivationOrder { get; set; }
        public int IsRegistered = 1;
    }

    private sealed class HostRegistrationLease(AppFeedbackService service, Guid hostId) : IDisposable
    {
        private AppFeedbackService? service = service;

        public void Dispose()
        {
            Interlocked.Exchange(ref service, null)?.UnregisterHost(hostId);
        }
    }

    private sealed record Delivery(HostRegistration Host, AppFeedbackHostSnapshot Snapshot);
}
