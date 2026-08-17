using Emerde.Core;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Emerde.Controls;

public partial class UiXNotificationHost : System.Windows.Controls.UserControl
{
    private const int NotificationEntranceDurationMilliseconds = 420;
    private const int NotificationGestureCommitDurationMilliseconds = 260;
    private const int NotificationGestureReturnDurationMilliseconds = 280;
    private const int NotificationGestureFallbackDelayMilliseconds = 80;
    private const double NotificationSwipeCommitDistance = 72d;
    private const double NotificationSwipeOvershoot = 40d;
    public static readonly DependencyProperty IsFeedbackEnabledProperty = DependencyProperty.Register(
        nameof(IsFeedbackEnabled),
        typeof(bool),
        typeof(UiXNotificationHost),
        new PropertyMetadata(true, OnIsFeedbackEnabledChanged));

    private readonly HashSet<Guid> animatedNotifications = [];
    private readonly Dictionary<Border, NotificationDragState> notificationDrags = [];
    private IDisposable? registration;
    private Window? ownerWindow;

    public UiXNotificationHost()
    {
        InitializeComponent();
        Loaded += HostLoaded;
        Unloaded += HostUnloaded;
    }

    public ObservableCollection<AppFeedbackNotification> VisibleNotifications { get; } = [];

    public ObservableCollection<AppFeedbackNotification> HistoryNotifications { get; } = [];

    public bool IsFeedbackEnabled
    {
        get => (bool)GetValue(IsFeedbackEnabledProperty);
        set => SetValue(IsFeedbackEnabledProperty, value);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return null;
    }

    private void HostLoaded(object sender, RoutedEventArgs e)
    {
        AttachHost();
    }

    private static void OnIsFeedbackEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UiXNotificationHost host || !host.IsLoaded)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            host.AttachHost();
        }
        else
        {
            host.DetachHost();
        }
    }

    private void AttachHost()
    {
        if (!IsFeedbackEnabled)
        {
            return;
        }

        Window? window = Window.GetWindow(this);
        if (window == null || ReferenceEquals(ownerWindow, window) && registration != null)
        {
            return;
        }

        DetachHost();
        ownerWindow = window;
        ownerWindow.Activated += OwnerWindowActivated;
        ownerWindow.Deactivated += OwnerWindowDeactivated;
        ownerWindow.Closed += OwnerWindowClosed;
        registration = AppFeedbackService.Current.RegisterHost(ownerWindow, Dispatcher, ApplySnapshot);
        AppFeedbackService.Current.SetHostActive(ownerWindow, ownerWindow.IsActive);
    }

    private void HostUnloaded(object sender, RoutedEventArgs e)
    {
        DetachHost();
    }

    private void OwnerWindowActivated(object? sender, EventArgs e)
    {
        if (ownerWindow != null)
        {
            AppFeedbackService.Current.SetHostActive(ownerWindow, true);
        }
    }

    private void OwnerWindowDeactivated(object? sender, EventArgs e)
    {
        if (ownerWindow != null)
        {
            AppFeedbackService.Current.SetHostActive(ownerWindow, false);
        }
    }

    private void OwnerWindowClosed(object? sender, EventArgs e)
    {
        DetachHost();
    }

    private void DetachHost()
    {
        registration?.Dispose();
        registration = null;
        if (ownerWindow != null)
        {
            ownerWindow.Activated -= OwnerWindowActivated;
            ownerWindow.Deactivated -= OwnerWindowDeactivated;
            ownerWindow.Closed -= OwnerWindowClosed;
            ownerWindow = null;
        }
        VisibleNotifications.Clear();
        HistoryNotifications.Clear();
        animatedNotifications.Clear();
        foreach (NotificationDragState drag in notificationDrags.Values)
        {
            drag.CompletionTimer?.Stop();
        }
        notificationDrags.Clear();
    }

    private void ApplySnapshot(AppFeedbackHostSnapshot snapshot)
    {
        SynchronizeCollection(HistoryNotifications, snapshot.History);
        for (int index = VisibleNotifications.Count - 1; index >= 0; index--)
        {
            if (snapshot.Visible.All(item => item.Id != VisibleNotifications[index].Id))
            {
                VisibleNotifications.RemoveAt(index);
            }
        }

        for (int index = 0; index < snapshot.Visible.Count; index++)
        {
            AppFeedbackNotification notification = snapshot.Visible[index];
            int currentIndex = IndexOf(notification.Id);
            if (currentIndex < 0)
            {
                VisibleNotifications.Insert(Math.Min(index, VisibleNotifications.Count), notification);
            }
            else
            {
                if (VisibleNotifications[currentIndex] != notification)
                {
                    VisibleNotifications[currentIndex] = notification;
                }
                if (currentIndex != index && index < VisibleNotifications.Count)
                {
                    VisibleNotifications.Move(currentIndex, index);
                }
            }
        }
    }

    private static void SynchronizeCollection(
        ObservableCollection<AppFeedbackNotification> target,
        IReadOnlyList<AppFeedbackNotification> source)
    {
        for (int index = target.Count - 1; index >= 0; index--)
        {
            if (source.All(item => item.Id != target[index].Id))
            {
                target.RemoveAt(index);
            }
        }

        for (int index = 0; index < source.Count; index++)
        {
            AppFeedbackNotification notification = source[index];
            int currentIndex = -1;
            for (int targetIndex = 0; targetIndex < target.Count; targetIndex++)
            {
                if (target[targetIndex].Id == notification.Id)
                {
                    currentIndex = targetIndex;
                    break;
                }
            }
            if (currentIndex < 0)
            {
                target.Insert(Math.Min(index, target.Count), notification);
            }
            else
            {
                if (target[currentIndex] != notification)
                {
                    target[currentIndex] = notification;
                }
                if (currentIndex != index && index < target.Count)
                {
                    target.Move(currentIndex, index);
                }
            }
        }
    }

    private int IndexOf(Guid id)
    {
        for (int index = 0; index < VisibleNotifications.Count; index++)
        {
            if (VisibleNotifications[index].Id == id)
            {
                return index;
            }
        }
        return -1;
    }

    private void NotificationCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not AppFeedbackNotification notification)
        {
            return;
        }
        TranslateTransform transform = new();
        card.RenderTransform = transform;
        if (!animatedNotifications.Add(notification.Id) || !SystemParameters.ClientAreaAnimation)
        {
            card.Opacity = 1d;
            transform.Y = 0d;
            return;
        }

        CubicEase easing = new() { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(NotificationEntranceDurationMilliseconds))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        DoubleAnimation translationAnimation = new(-8d, 0d, TimeSpan.FromMilliseconds(NotificationEntranceDurationMilliseconds))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop,
        };
        translationAnimation.Completed += (_, _) =>
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = 0d;
            card.BeginAnimation(OpacityProperty, null);
            card.Opacity = 1d;
        };
        transform.BeginAnimation(TranslateTransform.YProperty, translationAnimation);
    }

    private void NotificationCardMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AppFeedbackNotification notification })
        {
            AppFeedbackService.Current.SetHovered(notification.Id, true);
        }
    }

    private void NotificationCardMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border card
            && notificationDrags.TryGetValue(card, out NotificationDragState? drag)
            && (card.IsMouseCaptured || drag.IsCompleting))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: AppFeedbackNotification notification })
        {
            AppFeedbackService.Current.SetHovered(notification.Id, false);
        }
    }

    private void NotificationCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border card
            || card.DataContext is not AppFeedbackNotification notification
            || IsNotificationButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (notificationDrags.TryGetValue(card, out NotificationDragState? existing))
        {
            if (existing.IsCompleting)
            {
                e.Handled = true;
                return;
            }
            ResetNotificationGesture(card, existing);
        }

        TranslateTransform transform = EnsureNotificationTransform(card);
        StopNotificationCardAnimations(card, transform);
        notificationDrags[card] = new NotificationDragState(notification.Id, e.GetPosition(card), transform);
        card.CaptureMouse();
        e.Handled = true;
    }

    private void NotificationCardMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border card
            || !notificationDrags.TryGetValue(card, out NotificationDragState? drag)
            || drag.IsCompleting
            || !card.IsMouseCaptured)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(card);
        System.Windows.Vector offset = current - drag.StartPoint;
        if (drag.Axis == null && (Math.Abs(offset.X) >= 8d || Math.Abs(offset.Y) >= 8d))
        {
            drag.Axis = Math.Abs(offset.X) >= Math.Abs(offset.Y) ? NotificationDragAxis.Horizontal : NotificationDragAxis.Vertical;
        }

        if (drag.Axis == NotificationDragAxis.Horizontal)
        {
            drag.Transform.X = Math.Max(0d, offset.X);
            drag.Transform.Y = 0d;
            card.Opacity = CalculateDragOpacity(drag.Transform.X);
        }
        else if (drag.Axis == NotificationDragAxis.Vertical)
        {
            drag.Transform.X = 0d;
            drag.Transform.Y = Math.Min(0d, offset.Y);
            card.Opacity = CalculateDragOpacity(-drag.Transform.Y);
        }

        e.Handled = true;
    }

    private void NotificationCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border card || !notificationDrags.TryGetValue(card, out NotificationDragState? drag))
        {
            return;
        }

        bool dismiss = drag.Axis == NotificationDragAxis.Horizontal
            && drag.Transform.X >= NotificationSwipeCommitDistance;
        bool archive = drag.Axis == NotificationDragAxis.Vertical
            && drag.Transform.Y <= -NotificationSwipeCommitDistance;
        drag.IsReleaseInProgress = true;
        card.ReleaseMouseCapture();
        drag.IsReleaseInProgress = false;
        if (dismiss || archive)
        {
            CompleteNotificationGesture(card, drag, archive);
        }
        else
        {
            AnimateNotificationBack(card, drag);
        }

        e.Handled = true;
    }

    private void NotificationCardLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border card
            && notificationDrags.TryGetValue(card, out NotificationDragState? drag)
            && !drag.IsCompleting
            && !drag.IsReleaseInProgress)
        {
            AnimateNotificationBack(card, drag);
        }
    }

    private void CompleteNotificationGesture(Border card, NotificationDragState drag, bool archive)
    {
        drag.IsCompleting = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            FinalizeNotificationGesture(card, drag, archive);
            return;
        }

        double target = archive
            ? -Math.Max(card.ActualHeight, 120d) - NotificationSwipeOvershoot
            : Math.Max(card.ActualWidth, 240d) + NotificationSwipeOvershoot;
        DependencyProperty property = archive ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        DoubleAnimation animation = new()
        {
            From = archive ? drag.Transform.Y : drag.Transform.X,
            To = target,
            Duration = TimeSpan.FromMilliseconds(NotificationGestureCommitDurationMilliseconds),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        DoubleAnimation opacityAnimation = new(card.Opacity, 0d, TimeSpan.FromMilliseconds(NotificationGestureCommitDurationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        animation.Completed += (_, _) => FinalizeNotificationGesture(card, drag, archive);
        drag.CompletionTimer = CreateGestureFallbackTimer(
            NotificationGestureCommitDurationMilliseconds,
            () => FinalizeNotificationGesture(card, drag, archive));
        drag.Transform.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        card.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        drag.CompletionTimer.Start();
    }

    private void AnimateNotificationBack(Border card, NotificationDragState drag)
    {
        drag.IsCompleting = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            CompleteNotificationReturn(card, drag);
            return;
        }

        DependencyProperty? property = drag.Axis switch
        {
            NotificationDragAxis.Horizontal => TranslateTransform.XProperty,
            NotificationDragAxis.Vertical => TranslateTransform.YProperty,
            _ => null,
        };
        if (property == null)
        {
            CompleteNotificationReturn(card, drag);
            return;
        }

        DoubleAnimation returnAnimation = new(0d, TimeSpan.FromMilliseconds(NotificationGestureReturnDurationMilliseconds))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        DoubleAnimation opacityAnimation = new(card.Opacity, 1d, TimeSpan.FromMilliseconds(NotificationGestureReturnDurationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        returnAnimation.Completed += (_, _) => CompleteNotificationReturn(card, drag);
        drag.CompletionTimer = CreateGestureFallbackTimer(
            NotificationGestureReturnDurationMilliseconds,
            () => CompleteNotificationReturn(card, drag));
        drag.Transform.BeginAnimation(property, returnAnimation, HandoffBehavior.SnapshotAndReplace);
        card.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        drag.CompletionTimer.Start();
    }

    private void CompleteNotificationReturn(Border card, NotificationDragState drag)
    {
        if (!TryFinalizeNotificationGesture(card, drag))
        {
            return;
        }

        AppFeedbackService.Current.SetHovered(drag.NotificationId, card.IsMouseOver);
    }

    private void FinalizeNotificationGesture(Border card, NotificationDragState drag, bool archive)
    {
        if (!TryFinalizeNotificationGesture(card, drag))
        {
            return;
        }

        if (archive)
        {
            AppFeedbackService.Current.Archive(drag.NotificationId);
        }
        else
        {
            AppFeedbackService.Current.Dismiss(drag.NotificationId);
        }
    }

    private bool TryFinalizeNotificationGesture(Border card, NotificationDragState drag)
    {
        if (drag.IsFinalized)
        {
            return false;
        }

        drag.IsFinalized = true;
        ResetNotificationGesture(card, drag);
        return true;
    }

    private void ResetNotificationGesture(Border card, NotificationDragState drag)
    {
        drag.CompletionTimer?.Stop();
        drag.CompletionTimer = null;
        if (notificationDrags.TryGetValue(card, out NotificationDragState? current) && ReferenceEquals(current, drag))
        {
            notificationDrags.Remove(card);
        }
        StopNotificationCardAnimations(card, drag.Transform);
    }

    private static void StopNotificationCardAnimations(Border card, TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = 0d;
        transform.Y = 0d;
        card.BeginAnimation(OpacityProperty, null);
        card.Opacity = 1d;
    }

    private static DispatcherTimer CreateGestureFallbackTimer(int durationMilliseconds, Action callback)
    {
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(durationMilliseconds + NotificationGestureFallbackDelayMilliseconds),
        };
        timer.Tick += (_, _) => callback();
        return timer;
    }

    private static double CalculateDragOpacity(double distance)
    {
        double progress = Math.Clamp(distance / NotificationSwipeCommitDistance, 0d, 1d);
        return 1d - progress * 0.22d;
    }

    private static TranslateTransform EnsureNotificationTransform(Border card)
    {
        if (card.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }

        transform = new TranslateTransform();
        card.RenderTransform = transform;
        return transform;
    }

    private static bool IsNotificationButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Button)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void DismissButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { CommandParameter: Guid id })
        {
            AppFeedbackService.Current.Dismiss(id);
        }
    }

    private async void ActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { CommandParameter: Guid id })
        {
            await AppFeedbackService.Current.ExecuteActionAsync(id);
        }
    }

    private enum NotificationDragAxis
    {
        Horizontal,
        Vertical,
    }

    private sealed class NotificationDragState(Guid notificationId, System.Windows.Point startPoint, TranslateTransform transform)
    {
        public Guid NotificationId { get; } = notificationId;
        public System.Windows.Point StartPoint { get; } = startPoint;
        public TranslateTransform Transform { get; } = transform;
        public NotificationDragAxis? Axis { get; set; }
        public bool IsCompleting { get; set; }
        public bool IsReleaseInProgress { get; set; }
        public bool IsFinalized { get; set; }
        public DispatcherTimer? CompletionTimer { get; set; }
    }
}
