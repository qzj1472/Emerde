using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Emerde.Controls;

public static class MotionAssist
{
    internal const int PressDurationMilliseconds = 90;
    internal const int PressReleaseDurationMilliseconds = 150;
    internal const int EntranceDurationMilliseconds = 420;
    internal const int ExitDurationMilliseconds = 280;
    internal const int PulseDurationMilliseconds = 360;
    internal const int StateTransitionEnterDurationMilliseconds = 320;
    internal const int StateTransitionExitDurationMilliseconds = 240;
    internal const int NavigationIndicatorDurationMilliseconds = 300;

    public static readonly DependencyProperty IsEntranceEnabledProperty =
        DependencyProperty.RegisterAttached("IsEntranceEnabled", typeof(bool), typeof(MotionAssist), new PropertyMetadata(false, OnIsEntranceEnabledChanged));

    public static readonly DependencyProperty EntranceOffsetYProperty =
        DependencyProperty.RegisterAttached("EntranceOffsetY", typeof(double), typeof(MotionAssist), new PropertyMetadata(10d));

    public static readonly DependencyProperty EntranceScaleProperty =
        DependencyProperty.RegisterAttached("EntranceScale", typeof(double), typeof(MotionAssist), new PropertyMetadata(0.985d));

    public static readonly DependencyProperty EntranceDelayProperty =
        DependencyProperty.RegisterAttached("EntranceDelay", typeof(int), typeof(MotionAssist), new PropertyMetadata(0));

    public static readonly DependencyProperty EntranceTriggerProperty =
        DependencyProperty.RegisterAttached("EntranceTrigger", typeof(object), typeof(MotionAssist), new PropertyMetadata(null, OnEntranceTriggerChanged));

    public static readonly DependencyProperty IsDataContextEntranceEnabledProperty =
        DependencyProperty.RegisterAttached("IsDataContextEntranceEnabled", typeof(bool), typeof(MotionAssist), new PropertyMetadata(false, OnIsDataContextEntranceEnabledChanged));

    public static readonly DependencyProperty IsPressEnabledProperty =
        DependencyProperty.RegisterAttached("IsPressEnabled", typeof(bool), typeof(MotionAssist), new PropertyMetadata(false, OnIsPressEnabledChanged));

    public static readonly DependencyProperty HoverScaleProperty =
        DependencyProperty.RegisterAttached("HoverScale", typeof(double), typeof(MotionAssist), new PropertyMetadata(1.012d));

    public static readonly DependencyProperty PressScaleProperty =
        DependencyProperty.RegisterAttached("PressScale", typeof(double), typeof(MotionAssist), new PropertyMetadata(0.985d));

    public static readonly DependencyProperty IsPulseActiveProperty =
        DependencyProperty.RegisterAttached("IsPulseActive", typeof(bool), typeof(MotionAssist), new PropertyMetadata(false, OnIsPulseActiveChanged));

    public static readonly DependencyProperty IsSpinActiveProperty =
        DependencyProperty.RegisterAttached("IsSpinActive", typeof(bool), typeof(MotionAssist), new PropertyMetadata(false, OnIsSpinActiveChanged));

    public static readonly DependencyProperty IsStateTransitionActiveProperty =
        DependencyProperty.RegisterAttached("IsStateTransitionActive", typeof(bool), typeof(MotionAssist), new PropertyMetadata(false, OnIsStateTransitionActiveChanged));

    public static readonly DependencyProperty IsUiXScopeProperty =
        DependencyProperty.RegisterAttached(
            "IsUiXScope",
            typeof(bool),
            typeof(MotionAssist),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    private static readonly DependencyProperty MotionStateProperty =
        DependencyProperty.RegisterAttached("MotionState", typeof(MotionState), typeof(MotionAssist), new PropertyMetadata(null));

    private static readonly DependencyProperty StateTransitionStateProperty =
        DependencyProperty.RegisterAttached("StateTransitionState", typeof(StateTransitionState), typeof(MotionAssist), new PropertyMetadata(null));

    public static bool GetIsEntranceEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEntranceEnabledProperty);

    public static void SetIsEntranceEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEntranceEnabledProperty, value);

    public static double GetEntranceOffsetY(DependencyObject obj) => (double)obj.GetValue(EntranceOffsetYProperty);

    public static void SetEntranceOffsetY(DependencyObject obj, double value) => obj.SetValue(EntranceOffsetYProperty, value);

    public static double GetEntranceScale(DependencyObject obj) => (double)obj.GetValue(EntranceScaleProperty);

    public static void SetEntranceScale(DependencyObject obj, double value) => obj.SetValue(EntranceScaleProperty, value);

    public static int GetEntranceDelay(DependencyObject obj) => (int)obj.GetValue(EntranceDelayProperty);

    public static void SetEntranceDelay(DependencyObject obj, int value) => obj.SetValue(EntranceDelayProperty, value);

    public static object? GetEntranceTrigger(DependencyObject obj) => obj.GetValue(EntranceTriggerProperty);

    public static void SetEntranceTrigger(DependencyObject obj, object? value) => obj.SetValue(EntranceTriggerProperty, value);

    public static bool GetIsDataContextEntranceEnabled(DependencyObject obj) => (bool)obj.GetValue(IsDataContextEntranceEnabledProperty);

    public static void SetIsDataContextEntranceEnabled(DependencyObject obj, bool value) => obj.SetValue(IsDataContextEntranceEnabledProperty, value);

    public static bool GetIsPressEnabled(DependencyObject obj) => (bool)obj.GetValue(IsPressEnabledProperty);

    public static void SetIsPressEnabled(DependencyObject obj, bool value) => obj.SetValue(IsPressEnabledProperty, value);

    public static double GetHoverScale(DependencyObject obj) => (double)obj.GetValue(HoverScaleProperty);

    public static void SetHoverScale(DependencyObject obj, double value) => obj.SetValue(HoverScaleProperty, value);

    public static double GetPressScale(DependencyObject obj) => (double)obj.GetValue(PressScaleProperty);

    public static void SetPressScale(DependencyObject obj, double value) => obj.SetValue(PressScaleProperty, value);

    public static bool GetIsPulseActive(DependencyObject obj) => (bool)obj.GetValue(IsPulseActiveProperty);

    public static void SetIsPulseActive(DependencyObject obj, bool value) => obj.SetValue(IsPulseActiveProperty, value);

    public static bool GetIsSpinActive(DependencyObject obj) => (bool)obj.GetValue(IsSpinActiveProperty);

    public static void SetIsSpinActive(DependencyObject obj, bool value) => obj.SetValue(IsSpinActiveProperty, value);

    public static bool GetIsStateTransitionActive(DependencyObject obj) => (bool)obj.GetValue(IsStateTransitionActiveProperty);

    public static void SetIsStateTransitionActive(DependencyObject obj, bool value) => obj.SetValue(IsStateTransitionActiveProperty, value);

    public static bool GetIsUiXScope(DependencyObject obj) => (bool)obj.GetValue(IsUiXScopeProperty);

    public static void SetIsUiXScope(DependencyObject obj, bool value) => obj.SetValue(IsUiXScopeProperty, value);

    public static void PrepareEntrance(FrameworkElement element)
    {
        MotionState state = EnsureState(element);
        state.EntranceOperation?.Abort();
        state.EntranceOperation = null;
        state.EntranceAnimationGeneration++;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        state.EntranceTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        element.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0d, 0d, TimeSpan.Zero)
            {
                FillBehavior = FillBehavior.HoldEnd,
            });
        state.EntranceTranslate.Y = GetEntranceOffsetY(element);
    }

    public static void PlayEntrance(FrameworkElement element)
    {
        MotionState state = EnsureState(element);
        if (!ShouldAnimate())
        {
            state.EntranceAnimationGeneration++;
            state.PulseAnimationGeneration++;
            ResetEntrance(element, state);
            ResetPulse(element);
            return;
        }

        if (!element.IsLoaded || !element.IsVisible || element.Visibility != Visibility.Visible)
        {
            return;
        }

        double targetOpacity = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        double offsetY = GetEntranceOffsetY(element);
        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Max(0, GetEntranceDelay(element)));
        IEasingFunction opacityEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
        IEasingFunction movementEasing = new QuarticEase { EasingMode = EasingMode.EaseOut };
        int generation = ++state.EntranceAnimationGeneration;
        state.PulseAnimationGeneration++;

        element.BeginAnimation(UIElement.OpacityProperty, null);
        state.EntranceTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ResetPulse(element);

        element.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(targetOpacity, EntranceDurationMilliseconds - 40, delay, opacityEasing, 0d));
        DoubleAnimation translationAnimation = CreateAnimation(0d, EntranceDurationMilliseconds, delay, movementEasing, offsetY);
        translationAnimation.Completed += (_, _) =>
        {
            if (state.EntranceAnimationGeneration == generation)
            {
                ResetEntrance(element, state);
            }
        };
        state.EntranceTranslate.BeginAnimation(TranslateTransform.YProperty, translationAnimation);
    }

    public static void MoveNavigationIndicator(FrameworkElement indicator, double x, double y, bool animate)
    {
        TranslateTransform transform = EnsureIndicatorTransform(indicator);
        double currentOpacity = indicator.Opacity;
        indicator.BeginAnimation(UIElement.OpacityProperty, null);

        if (!animate || !ShouldAnimate() || !indicator.IsLoaded)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.X = x;
            transform.Y = y;
            indicator.Opacity = 1d;
            return;
        }

        IEasingFunction easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            CreateAnimation(x, NavigationIndicatorDurationMilliseconds, TimeSpan.Zero, easing, transform.X),
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(y, NavigationIndicatorDurationMilliseconds, TimeSpan.Zero, easing, transform.Y),
            HandoffBehavior.SnapshotAndReplace);
        indicator.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(1d, NavigationIndicatorDurationMilliseconds - 60, TimeSpan.Zero, new CubicEase { EasingMode = EasingMode.EaseOut }, currentOpacity),
            HandoffBehavior.SnapshotAndReplace);
    }

    public static void PlayPulse(FrameworkElement element)
    {
        if (!ShouldAnimate() || element.Visibility != Visibility.Visible)
        {
            if (element.GetValue(MotionStateProperty) is MotionState existing)
            {
                existing.PulseAnimationGeneration++;
                ResetPulse(element);
            }
            return;
        }

        MotionState state = EnsureState(element);
        IEasingFunction easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        state.EntranceAnimationGeneration++;
        ResetEntrance(element, state);
        int generation = ++state.PulseAnimationGeneration;
        double targetOpacity = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        DoubleAnimation pulseAnimation = CreateAnimation(targetOpacity, PulseDurationMilliseconds, TimeSpan.Zero, easing, targetOpacity * 0.72d);
        pulseAnimation.Completed += (_, _) =>
        {
            if (state.PulseAnimationGeneration == generation)
            {
                ResetPulse(element);
            }
        };
        element.BeginAnimation(UIElement.OpacityProperty, pulseAnimation);
    }

    public static async Task PlayExitAsync(FrameworkElement element)
    {
        if (!ShouldAnimate() || !element.IsLoaded || !element.IsVisible)
        {
            return;
        }

        MotionState state = EnsureState(element);
        state.EntranceOperation?.Abort();
        state.EntranceOperation = null;
        state.EntranceAnimationGeneration++;
        state.PulseAnimationGeneration++;
        ResetPulse(element);

        double currentOpacity = element.Opacity;
        double currentOffsetY = state.EntranceTranslate.Y;
        IEasingFunction easing = new SineEase { EasingMode = EasingMode.EaseIn };

        DoubleAnimation opacityAnimation = CreateAnimation(0d, ExitDurationMilliseconds, TimeSpan.Zero, easing, currentOpacity);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler unloadedHandler = (_, _) => completion.TrySetResult();
        opacityAnimation.Completed += (_, _) => completion.TrySetResult();
        element.Unloaded += unloadedHandler;

        element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        state.EntranceTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(6d, ExitDurationMilliseconds, TimeSpan.Zero, easing, currentOffsetY));

        await Task.WhenAny(completion.Task, Task.Delay(ExitDurationMilliseconds + 100));
        element.Unloaded -= unloadedHandler;
    }

    public static async Task PlayContentDialogExitTransformAsync(FrameworkElement element)
    {
        await PlayExitAsync(element);
    }

    public static void ResetExit(FrameworkElement element)
    {
        if (element.GetValue(MotionStateProperty) is MotionState state)
        {
            state.EntranceAnimationGeneration++;
            state.PulseAnimationGeneration++;
            ResetEntrance(element, state);
            ResetPulse(element);
        }
    }

    private static void OnIsEntranceEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.Loaded += EntranceLoaded;
            element.IsVisibleChanged += EntranceIsVisibleChanged;
            if (element.IsLoaded && element.IsVisible)
            {
                QueueEntrance(element);
            }
        }
        else
        {
            element.Loaded -= EntranceLoaded;
            element.IsVisibleChanged -= EntranceIsVisibleChanged;
            if (element.GetValue(MotionStateProperty) is MotionState state)
            {
                state.EntranceOperation?.Abort();
                state.EntranceOperation = null;
                ResetEntrance(element, state);
            }
        }
    }

    private static void OnIsPressEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseLeftButtonDown += PressMouseLeftButtonDown;
            element.PreviewMouseLeftButtonUp += PressMouseLeftButtonUp;
            element.LostMouseCapture += PressLostMouseCapture;
            element.IsEnabledChanged += PressIsEnabledChanged;
        }
        else
        {
            element.PreviewMouseLeftButtonDown -= PressMouseLeftButtonDown;
            element.PreviewMouseLeftButtonUp -= PressMouseLeftButtonUp;
            element.LostMouseCapture -= PressLostMouseCapture;
            element.IsEnabledChanged -= PressIsEnabledChanged;
            ResetInteractionScale(element);
        }
    }

    private static void OnEntranceTriggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        MotionState state = EnsureState(element);
        bool allowReplay = state.HasObservedEntranceTrigger;
        state.HasObservedEntranceTrigger = true;
        if (GetIsEntranceEnabled(element) && IsTriggerActive(e.NewValue))
        {
            QueueEntrance(element, allowReplay);
        }
    }

    private static void OnIsDataContextEntranceEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.DataContextChanged += DataContextEntranceChanged;
        }
        else
        {
            element.DataContextChanged -= DataContextEntranceChanged;
        }
    }

    private static void OnIsPulseActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (bool)e.NewValue)
        {
            element.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (GetIsPulseActive(element) && element.IsLoaded && element.IsVisible)
                {
                    PlayPulse(element);
                }
            }));
        }
    }

    private static void OnIsSpinActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.Loaded += SpinLoaded;
            element.Unloaded += SpinUnloaded;
            element.IsVisibleChanged += SpinIsVisibleChanged;
            UpdateSpinAnimation(element);
        }
        else
        {
            element.Loaded -= SpinLoaded;
            element.Unloaded -= SpinUnloaded;
            element.IsVisibleChanged -= SpinIsVisibleChanged;
            StopSpinAnimation(element);
        }
    }

    private static void OnIsStateTransitionActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        StateTransitionState state = EnsureStateTransitionState(element);
        double targetOpacity = (bool)e.NewValue ? 1d : 0d;
        double currentOpacity = element.Opacity;
        int generation = ++state.Generation;

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = targetOpacity;
        if (!ShouldAnimate() || !element.IsLoaded || !element.IsVisible || element.Visibility != Visibility.Visible)
        {
            return;
        }

        int duration = targetOpacity > currentOpacity
            ? StateTransitionEnterDurationMilliseconds
            : StateTransitionExitDurationMilliseconds;
        DoubleAnimation animation = CreateAnimation(
            targetOpacity,
            duration,
            TimeSpan.Zero,
            new CubicEase { EasingMode = EasingMode.EaseOut },
            currentOpacity);
        animation.FillBehavior = FillBehavior.Stop;
        animation.Completed += (_, _) =>
        {
            if (state.Generation == generation)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = targetOpacity;
            }
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void EntranceLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            QueueEntrance(element);
        }
    }

    private static void EntranceIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsVisible)
        {
            QueueEntrance(element);
        }
    }

    private static void DataContextEntranceChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsLoaded && element.IsVisible)
        {
            QueueEntrance(element);
        }
    }

    private static void QueueEntrance(FrameworkElement element)
    {
        QueueEntrance(element, false);
    }

    private static void QueueEntrance(FrameworkElement element, bool allowReplay)
    {
        MotionState state = EnsureState(element);
        if (!allowReplay && state.HasPlayedEntrance)
        {
            return;
        }

        if (state.EntranceOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            state.EntranceReplayRequested |= allowReplay;
            return;
        }

        state.EntranceReplayRequested = allowReplay;
        state.EntranceOperation = element.Dispatcher.BeginInvoke(new Action(() =>
        {
            state.EntranceOperation = null;
            bool replayRequested = state.EntranceReplayRequested;
            state.EntranceReplayRequested = false;
            if (!replayRequested && state.HasPlayedEntrance)
            {
                return;
            }

            if (!element.IsLoaded || !element.IsVisible || element.Visibility != Visibility.Visible)
            {
                return;
            }

            state.HasPlayedEntrance = true;
            PlayEntrance(element);
        }), DispatcherPriority.DataBind);
    }

    private static TranslateTransform EnsureIndicatorTransform(FrameworkElement indicator)
    {
        if (indicator.RenderTransform is TranslateTransform transform && !transform.IsFrozen)
        {
            return transform;
        }

        TranslateTransform replacement = new();
        indicator.RenderTransform = replacement;
        return replacement;
    }

    private static void PressMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsEnabled)
        {
            AnimateInteractionScale(element, GetPressScale(element), PressDurationMilliseconds);
        }
    }

    private static void PressMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            AnimateInteractionScale(element, 1d, PressReleaseDurationMilliseconds);
        }
    }

    private static void PressLostMouseCapture(object sender, WpfMouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            AnimateInteractionScale(element, 1d, PressReleaseDurationMilliseconds);
        }
    }

    private static void PressIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && !element.IsEnabled)
        {
            ResetInteractionScale(element);
        }
    }

    private static void AnimateInteractionScale(FrameworkElement element, double scale, int durationMilliseconds)
    {
        bool hasExplicitPressBehavior = element.ReadLocalValue(IsPressEnabledProperty) is true;
        if (!GetIsUiXScope(element) && !hasExplicitPressBehavior)
        {
            return;
        }

        if (!ShouldAnimate())
        {
            ResetInteractionScale(element);
            return;
        }

        MotionState state = EnsureState(element);
        DoubleAnimation animation = CreateAnimation(
            Math.Clamp(scale, 0.9d, 1.05d),
            durationMilliseconds,
            TimeSpan.Zero,
            new CubicEase { EasingMode = EasingMode.EaseOut });
        state.InteractionScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        state.InteractionScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone());
    }

    private static MotionState EnsureState(FrameworkElement element)
    {
        if (element.GetValue(MotionStateProperty) is MotionState existing)
        {
            return existing;
        }

        MotionState state = new();
        TransformGroup group = new();
        Transform current = element.RenderTransform;
        if (current != Transform.Identity)
        {
            group.Children.Add(current);
        }
        group.Children.Add(state.InteractionScale);
        group.Children.Add(state.SpinRotate);
        group.Children.Add(state.EntranceTranslate);

        element.RenderTransform = group;
        if (element.ReadLocalValue(FrameworkElement.RenderTransformOriginProperty) == DependencyProperty.UnsetValue)
        {
            element.RenderTransformOrigin = new System.Windows.Point(0.5d, 0.5d);
        }
        element.Unloaded += MotionElementUnloaded;
        element.SetValue(MotionStateProperty, state);
        return state;
    }

    private static StateTransitionState EnsureStateTransitionState(FrameworkElement element)
    {
        if (element.GetValue(StateTransitionStateProperty) is StateTransitionState existing)
        {
            return existing;
        }

        StateTransitionState state = new();
        element.Unloaded += StateTransitionElementUnloaded;
        element.SetValue(StateTransitionStateProperty, state);
        return state;
    }

    private static void StateTransitionElementUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.GetValue(StateTransitionStateProperty) is not StateTransitionState state)
        {
            return;
        }

        state.Generation++;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = GetIsStateTransitionActive(element) ? 1d : 0d;
    }

    private static void MotionElementUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.GetValue(MotionStateProperty) is not MotionState state)
        {
            return;
        }

        state.EntranceOperation?.Abort();
        state.EntranceOperation = null;
        state.EntranceAnimationGeneration++;
        state.PulseAnimationGeneration++;
        ResetEntrance(element, state);
        ResetInteractionScale(element);
        ResetPulse(element);
    }

    private static void ResetEntrance(FrameworkElement element, MotionState state)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        state.EntranceTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        state.EntranceTranslate.Y = 0d;
    }

    private static void ResetInteractionScale(FrameworkElement element)
    {
        if (element.GetValue(MotionStateProperty) is not MotionState state)
        {
            return;
        }

        state.InteractionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        state.InteractionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        state.InteractionScale.ScaleX = 1d;
        state.InteractionScale.ScaleY = 1d;
    }

    private static void ResetPulse(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private static void SpinLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            UpdateSpinAnimation(element);
        }
    }

    private static void SpinUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            StopSpinAnimation(element);
        }
    }

    private static void SpinIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            UpdateSpinAnimation(element);
        }
    }

    private static void UpdateSpinAnimation(FrameworkElement element)
    {
        if (!GetIsSpinActive(element) || !ShouldAnimate() || !element.IsLoaded || !element.IsVisible)
        {
            StopSpinAnimation(element);
            return;
        }

        MotionState state = EnsureState(element);
        DoubleAnimation animation = new(0d, 360d, TimeSpan.FromMilliseconds(800))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = null,
        };
        state.SpinRotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private static void StopSpinAnimation(FrameworkElement element)
    {
        if (element.GetValue(MotionStateProperty) is not MotionState state)
        {
            return;
        }

        state.SpinRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        state.SpinRotate.Angle = 0d;
    }

    private static DoubleAnimation CreateAnimation(double to, int durationMs, TimeSpan delay, IEasingFunction easing, double? from = null)
    {
        return new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = delay,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
    }

    private static bool ShouldAnimate()
    {
        return SystemParameters.ClientAreaAnimation;
    }

    internal static bool IsTriggerActive(object? value)
    {
        return value switch
        {
            null => false,
            bool boolValue => boolValue,
            _ => true,
        };
    }

    private sealed class MotionState
    {
        public DispatcherOperation? EntranceOperation { get; set; }

        public int EntranceAnimationGeneration { get; set; }

        public int PulseAnimationGeneration { get; set; }

        public bool HasPlayedEntrance { get; set; }

        public bool HasObservedEntranceTrigger { get; set; }

        public bool EntranceReplayRequested { get; set; }

        public ScaleTransform InteractionScale { get; } = new(1d, 1d);

        public RotateTransform SpinRotate { get; } = new();

        public TranslateTransform EntranceTranslate { get; } = new();
    }

    private sealed class StateTransitionState
    {
        public int Generation { get; set; }
    }
}
