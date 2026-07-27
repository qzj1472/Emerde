using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Emerde.Controls;

public static class MotionAssist
{
    internal const int ExitDurationMilliseconds = 180;

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

    private static readonly DependencyProperty MotionStateProperty =
        DependencyProperty.RegisterAttached("MotionState", typeof(MotionState), typeof(MotionAssist), new PropertyMetadata(null));

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

    public static void PlayEntrance(FrameworkElement element)
    {
        MotionState state = EnsureState(element);
        if (!ShouldAnimate())
        {
            state.EntranceAnimationGeneration++;
            state.PulseAnimationGeneration++;
            ResetEntrance(element, state);
            ResetPulse(state);
            return;
        }

        if (!element.IsLoaded || !element.IsVisible || element.Visibility != Visibility.Visible)
        {
            return;
        }

        double targetOpacity = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        double offsetY = GetEntranceOffsetY(element);
        double scale = Math.Clamp(GetEntranceScale(element), 0.9d, 1.05d);
        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Max(0, GetEntranceDelay(element)));
        IEasingFunction easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        int generation = ++state.EntranceAnimationGeneration;
        state.PulseAnimationGeneration++;

        element.BeginAnimation(UIElement.OpacityProperty, null);
        state.EntranceTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        state.EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        state.EntranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ResetPulse(state);

        element.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(targetOpacity, 220, delay, easing, 0d));
        state.EntranceTranslate.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(0d, 260, delay, easing, offsetY));
        state.EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(1d, 260, delay, easing, scale));
        DoubleAnimation scaleYAnimation = CreateAnimation(1d, 260, delay, easing, scale);
        scaleYAnimation.Completed += (_, _) =>
        {
            if (state.EntranceAnimationGeneration == generation)
            {
                ResetEntrance(element, state);
            }
        };
        state.EntranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
    }

    public static void PlayPulse(FrameworkElement element)
    {
        if (!ShouldAnimate() || element.Visibility != Visibility.Visible)
        {
            if (element.GetValue(MotionStateProperty) is MotionState existing)
            {
                existing.PulseAnimationGeneration++;
                element.BeginAnimation(UIElement.OpacityProperty, null);
                ResetPulse(existing);
            }
            return;
        }

        MotionState state = EnsureState(element);
        IEasingFunction easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        state.EntranceAnimationGeneration++;
        ResetEntrance(element, state);
        int generation = ++state.PulseAnimationGeneration;
        double targetOpacity = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        element.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(targetOpacity, 240, TimeSpan.Zero, easing, targetOpacity * 0.72d));
        state.PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(1d, 240, TimeSpan.Zero, easing, 0.992d));
        DoubleAnimation scaleYAnimation = CreateAnimation(1d, 240, TimeSpan.Zero, easing, 0.992d);
        scaleYAnimation.Completed += (_, _) =>
        {
            if (state.PulseAnimationGeneration == generation)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                ResetPulse(state);
            }
        };
        state.PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
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
        ResetPulse(state);

        double currentOpacity = element.Opacity;
        double currentOffsetY = state.EntranceTranslate.Y;
        double currentScaleX = state.EntranceScale.ScaleX;
        double currentScaleY = state.EntranceScale.ScaleY;
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
        state.EntranceScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreateAnimation(0.985d, ExitDurationMilliseconds, TimeSpan.Zero, easing, currentScaleX));
        state.EntranceScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateAnimation(0.985d, ExitDurationMilliseconds, TimeSpan.Zero, easing, currentScaleY));

        await Task.WhenAny(completion.Task, Task.Delay(ExitDurationMilliseconds + 100));
        element.Unloaded -= unloadedHandler;
    }

    public static async Task PlayContentDialogExitTransformAsync(FrameworkElement element)
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
        ResetPulse(state);

        double currentScaleX = state.EntranceScale.ScaleX;
        double currentScaleY = state.EntranceScale.ScaleY;
        IEasingFunction easing = new SineEase { EasingMode = EasingMode.EaseIn };
        DoubleAnimation scaleXAnimation = CreateAnimation(1.015d, ExitDurationMilliseconds, TimeSpan.Zero, easing, currentScaleX);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler unloadedHandler = (_, _) => completion.TrySetResult();
        scaleXAnimation.Completed += (_, _) => completion.TrySetResult();
        element.Unloaded += unloadedHandler;

        state.EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
        state.EntranceScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateAnimation(1.015d, ExitDurationMilliseconds, TimeSpan.Zero, easing, currentScaleY));

        await Task.WhenAny(completion.Task, Task.Delay(ExitDurationMilliseconds + 100));
        element.Unloaded -= unloadedHandler;
    }

    public static void ResetExit(FrameworkElement element)
    {
        if (element.GetValue(MotionStateProperty) is MotionState state)
        {
            state.EntranceAnimationGeneration++;
            state.PulseAnimationGeneration++;
            ResetEntrance(element, state);
            ResetPulse(state);
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
            element.MouseEnter += PressMouseEnter;
            element.MouseLeave += PressMouseLeave;
            element.PreviewMouseLeftButtonDown += PressMouseLeftButtonDown;
            element.PreviewMouseLeftButtonUp += PressMouseLeftButtonUp;
            element.LostMouseCapture += PressLostMouseCapture;
            element.IsEnabledChanged += PressIsEnabledChanged;
        }
        else
        {
            element.MouseEnter -= PressMouseEnter;
            element.MouseLeave -= PressMouseLeave;
            element.PreviewMouseLeftButtonDown -= PressMouseLeftButtonDown;
            element.PreviewMouseLeftButtonUp -= PressMouseLeftButtonUp;
            element.LostMouseCapture -= PressLostMouseCapture;
            element.IsEnabledChanged -= PressIsEnabledChanged;
            ResetInteractionScale(element);
        }
    }

    private static void OnEntranceTriggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && IsTriggerActive(e.NewValue))
        {
            QueueEntrance(element);
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
            element.Dispatcher.BeginInvoke(new Action(() => PlayPulse(element)));
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
        MotionState state = EnsureState(element);
        if (state.EntranceOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        state.EntranceOperation = element.Dispatcher.BeginInvoke(new Action(() =>
        {
            state.EntranceOperation = null;
            PlayEntrance(element);
        }), DispatcherPriority.Render);
    }

    private static void PressMouseEnter(object sender, WpfMouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsEnabled)
        {
            AnimateInteractionScale(element, GetHoverScale(element), 150);
        }
    }

    private static void PressMouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            AnimateInteractionScale(element, 1d, 180);
        }
    }

    private static void PressMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsEnabled)
        {
            AnimateInteractionScale(element, GetPressScale(element), 90);
        }
    }

    private static void PressMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsEnabled)
        {
            AnimateInteractionScale(element, element.IsMouseOver ? GetHoverScale(element) : 1d, 130);
        }
    }

    private static void PressLostMouseCapture(object sender, WpfMouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            AnimateInteractionScale(element, element.IsEnabled && element.IsMouseOver ? GetHoverScale(element) : 1d, 130);
        }
    }

    private static void PressIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element && !element.IsEnabled)
        {
            ResetInteractionScale(element);
        }
    }

    private static void AnimateInteractionScale(FrameworkElement element, double scale, int durationMs)
    {
        if (!ShouldAnimate())
        {
            ResetInteractionScale(element);
            return;
        }

        MotionState state = EnsureState(element);
        scale = Math.Clamp(scale, 0.9d, 1.05d);
        IEasingFunction easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        DoubleAnimation animation = CreateAnimation(scale, durationMs, TimeSpan.Zero, easing);
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
        group.Children.Add(state.EntranceScale);
        group.Children.Add(state.InteractionScale);
        group.Children.Add(state.PulseScale);
        group.Children.Add(state.SpinRotate);
        group.Children.Add(state.EntranceTranslate);

        element.RenderTransform = group;
        if (element.RenderTransformOrigin == default)
        {
            element.RenderTransformOrigin = new System.Windows.Point(0.5d, 0.5d);
        }
        element.Unloaded += MotionElementUnloaded;
        element.SetValue(MotionStateProperty, state);
        return state;
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
        ResetPulse(state);
    }

    private static void ResetEntrance(FrameworkElement element, MotionState state)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        state.EntranceTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        state.EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        state.EntranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        state.EntranceTranslate.Y = 0d;
        state.EntranceScale.ScaleX = 1d;
        state.EntranceScale.ScaleY = 1d;
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

    private static void ResetPulse(MotionState state)
    {
        state.PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        state.PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        state.PulseScale.ScaleX = 1d;
        state.PulseScale.ScaleY = 1d;
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

        public ScaleTransform EntranceScale { get; } = new(1d, 1d);

        public ScaleTransform InteractionScale { get; } = new(1d, 1d);

        public ScaleTransform PulseScale { get; } = new(1d, 1d);

        public RotateTransform SpinRotate { get; } = new();

        public TranslateTransform EntranceTranslate { get; } = new();
    }
}
