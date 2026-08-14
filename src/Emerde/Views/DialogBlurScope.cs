using Emerde.Core;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Wpf.Ui.Violeta.Controls;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfControl = System.Windows.Controls.Control;
using WpfPanel = System.Windows.Controls.Panel;
using WpfShape = System.Windows.Shapes.Shape;

namespace Emerde.Views;

internal sealed class DialogBlurScope : IDisposable
{
    internal const int BlurEntranceDurationMilliseconds = 320;
    internal const int BackdropEntranceDurationMilliseconds = 240;
    internal const int ExitDurationMilliseconds = 190;
    internal const int OwnerEnablePumpMaximumTicks = 12;
    internal const int DialogMaskClearPumpMaximumTicks = 16;

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    private static readonly string[] BackdropRootNames =
    [
        "MainDialogOverlay",
        "SettingsDialogOverlay",
        "VideoListDialogOverlay",
    ];

    private static readonly string[] BlurRootNames =
    [
        "MainContentRoot",
        "SettingsContentRoot",
        "VideoListContentRoot",
    ];

    private readonly WpfPanel? backdrop;
    private readonly WpfBrush? previousBackdropBackground;
    private readonly double previousBackdropOpacity;
    private readonly Visibility previousBackdropVisibility;
    private readonly bool previousBackdropHitTestVisible;
    private readonly MouseButtonEventHandler? backdropMouseDownHandler;
    private readonly UIElement? blurTarget;
    private readonly System.Windows.Media.Effects.Effect? previousBlurEffect;
    private readonly Window? ownerWindow;
    private readonly bool previousOwnerIsEnabled;
    private readonly DispatcherTimer? ownerEnableTimer;
    private readonly DispatcherTimer? dialogMaskClearTimer;
    private readonly ContentDialog? contentDialog;
    private readonly BlurEffect? activeBlurEffect;
    private readonly double targetBlurRadius;
    private readonly Action? lightDismissAction;
    private static int activeDialogCount;
    private bool isDisposed;
    private bool isExitAnimating;
    private bool isExitComplete;
    private Task? exitAnimationTask;

    public static bool HasActiveDialog => Volatile.Read(ref activeDialogCount) > 0;

    public DialogBlurScope(Window? owner = null, double radius = 8d, object? dialog = null, bool isLightDismissEnabled = false, bool keepOwnerEnabled = true, bool showBackdrop = true, WpfPanel? backdropOverride = null, Action? lightDismissAction = null)
    {
        this.lightDismissAction = lightDismissAction;
        bool animate = ShouldAnimate();
        WpfBrush backdropBrush = CreateBackdropBrush();
        ApplyBuiltInSmoke(dialog, backdropBrush);
        AttachDialogMask(dialog, backdropBrush, isLightDismissEnabled, lightDismissAction);
        dialogMaskClearTimer = dialog is FrameworkElement dialogElement
            ? StartDialogMaskClearPump(dialogElement)
            : null;

        Window? window = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        ownerWindow = window;
        previousOwnerIsEnabled = window?.IsEnabled ?? true;
        ownerEnableTimer = keepOwnerEnabled ? StartOwnerEnablePump(window) : null;

        blurTarget = FindBlurTarget(window);
        if (blurTarget != null && radius > 0d)
        {
            previousBlurEffect = blurTarget.Effect;
            double initialRadius = previousBlurEffect is BlurEffect previousBlur
                ? Math.Max(0d, previousBlur.Radius)
                : 0d;
            double targetRadius = Math.Max(initialRadius, radius);
            BlurEffect blurEffect = new()
            {
                Radius = animate ? initialRadius : targetRadius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance,
            };
            activeBlurEffect = blurEffect;
            targetBlurRadius = targetRadius;
            blurTarget.Effect = blurEffect;
            if (animate && targetRadius > initialRadius)
            {
                blurEffect.BeginAnimation(
                    BlurEffect.RadiusProperty,
                    CreateBlurEntranceAnimation(initialRadius, targetRadius));
            }
        }

        backdrop = showBackdrop ? backdropOverride ?? FindBackdrop(window) : null;
        if (backdrop != null)
        {
            previousBackdropBackground = backdrop.Background;
            previousBackdropOpacity = backdrop.Opacity;
            previousBackdropVisibility = backdrop.Visibility;
            previousBackdropHitTestVisible = backdrop.IsHitTestVisible;
            backdrop.Background = backdropBrush;
            backdrop.Opacity = animate ? 0d : previousBackdropOpacity;
            backdrop.IsHitTestVisible = true;
            backdropMouseDownHandler = (_, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, backdrop))
                {
                    e.Handled = true;
                    if (isLightDismissEnabled && dialog != null)
                    {
                        RequestLightDismiss(dialog);
                    }
                    else if (isLightDismissEnabled)
                    {
                        lightDismissAction?.Invoke();
                    }
                }
            };
            backdrop.MouseDown += backdropMouseDownHandler;
            backdrop.Visibility = Visibility.Visible;
            if (animate)
            {
                backdrop.BeginAnimation(
                    UIElement.OpacityProperty,
                    CreateBackdropEntranceAnimation(previousBackdropOpacity));
            }
        }

        if (dialog is ContentDialog currentDialog)
        {
            contentDialog = currentDialog;
            contentDialog.Closing += ContentDialogClosing;
        }

        Interlocked.Increment(ref activeDialogCount);
    }

    public static DialogBlurScope ForLightDismiss(Window? owner, object dialog, double radius = 8d)
    {
        return new DialogBlurScope(owner, radius, dialog, true);
    }

    public static DialogBlurScope ForDialog(Window? owner, object dialog, double radius = 8d)
    {
        return new DialogBlurScope(owner, radius, dialog);
    }

    public static DialogBlurScope ForMessageBox(Window? owner, double radius = 8d)
    {
        return new DialogBlurScope(owner, radius, null, false, false, false);
    }

    public static DialogBlurScope ForOverlay(Window? owner, WpfPanel backdrop, double radius = 8d)
    {
        return new DialogBlurScope(owner, radius, null, false, true, true, backdrop);
    }

    internal async Task PlayExitAsync()
    {
        if (!ShouldAnimate() || isDisposed)
        {
            return;
        }

        exitAnimationTask ??= PlayExitCoreAsync();
        await exitAnimationTask;
    }

    private async Task PlayExitCoreAsync()
    {
        List<Task> animations = [];
        IEasingFunction easing = new SineEase { EasingMode = EasingMode.EaseIn };
        if (activeBlurEffect != null)
        {
            double previousRadius = previousBlurEffect is BlurEffect previousBlur
                ? Math.Max(0d, previousBlur.Radius)
                : 0d;
            animations.Add(BeginAnimationAsync(
                activeBlurEffect,
                BlurEffect.RadiusProperty,
                CreateExitAnimation(activeBlurEffect.Radius, previousRadius, easing)));
        }

        if (backdrop != null)
        {
            double targetOpacity = previousBackdropVisibility == Visibility.Visible
                ? previousBackdropOpacity
                : 0d;
            animations.Add(BeginAnimationAsync(
                backdrop,
                UIElement.OpacityProperty,
                CreateExitAnimation(backdrop.Opacity, targetOpacity, easing)));
        }

        await Task.WhenAll(animations);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        if (contentDialog != null)
        {
            contentDialog.Closing -= ContentDialogClosing;
        }
        ownerEnableTimer?.Stop();
        dialogMaskClearTimer?.Stop();
        if (ownerWindow != null)
        {
            ownerWindow.IsEnabled = previousOwnerIsEnabled;
        }

        if (backdrop != null)
        {
            if (backdropMouseDownHandler != null)
            {
                backdrop.MouseDown -= backdropMouseDownHandler;
            }

            backdrop.Background = previousBackdropBackground;
            backdrop.BeginAnimation(UIElement.OpacityProperty, null);
            backdrop.Opacity = previousBackdropOpacity;
            backdrop.IsHitTestVisible = previousBackdropHitTestVisible;
            backdrop.Visibility = previousBackdropVisibility;
        }

        if (blurTarget != null)
        {
            activeBlurEffect?.BeginAnimation(BlurEffect.RadiusProperty, null);
            blurTarget.Effect = previousBlurEffect;
        }

        Interlocked.Decrement(ref activeDialogCount);
    }

    internal static DoubleAnimation CreateBlurEntranceAnimation(double from, double to)
    {
        return new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(BlurEntranceDurationMilliseconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
    }

    internal static DoubleAnimation CreateBackdropEntranceAnimation(double to)
    {
        return new DoubleAnimation
        {
            From = 0d,
            To = to,
            Duration = TimeSpan.FromMilliseconds(BackdropEntranceDurationMilliseconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
    }

    internal static DoubleAnimation CreateExitAnimation(double from, double to, IEasingFunction? easing = null)
    {
        return new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(ExitDurationMilliseconds),
            EasingFunction = easing ?? new SineEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd,
        };
    }

    private void ContentDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (isDisposed || isExitComplete || args.Cancel)
        {
            return;
        }

        ownerEnableTimer?.Stop();
        dialogMaskClearTimer?.Stop();

        if (isExitAnimating)
        {
            return;
        }

        isExitAnimating = true;
        _ = CompleteContentDialogExitAsync(args);
    }

    private async Task CompleteContentDialogExitAsync(ContentDialogClosingEventArgs args)
    {
        try
        {
            await PlayExitAsync();
            if (args.Cancel)
            {
                RestoreEntrance();
            }
            else
            {
                isExitComplete = true;
            }
        }
        catch
        {
            RestoreEntrance();
        }
        finally
        {
            isExitAnimating = false;
        }
    }

    private void RestoreEntrance()
    {
        if (!ShouldAnimate())
        {
            exitAnimationTask = null;
            return;
        }

        exitAnimationTask = null;

        IEasingFunction easing = new SineEase { EasingMode = EasingMode.EaseOut };
        if (activeBlurEffect != null)
        {
            activeBlurEffect.BeginAnimation(
                BlurEffect.RadiusProperty,
                CreateEntranceAnimation(activeBlurEffect.Radius, targetBlurRadius, easing));
        }

        if (backdrop != null)
        {
            backdrop.BeginAnimation(
                UIElement.OpacityProperty,
                CreateEntranceAnimation(backdrop.Opacity, previousBackdropOpacity, easing));
        }
    }

    private static DoubleAnimation CreateEntranceAnimation(double from, double to, IEasingFunction easing)
    {
        return new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(ExitDurationMilliseconds),
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
    }

    private static Task BeginAnimationAsync(Animatable target, DependencyProperty property, DoubleAnimation animation)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        animation.Completed += (_, _) => completion.TrySetResult();
        target.BeginAnimation(property, animation);
        return WaitForAnimationAsync(completion.Task);
    }

    private static Task BeginAnimationAsync(UIElement target, DependencyProperty property, DoubleAnimation animation)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        animation.Completed += (_, _) => completion.TrySetResult();
        target.BeginAnimation(property, animation);
        return WaitForAnimationAsync(completion.Task);
    }

    private static async Task WaitForAnimationAsync(Task completion)
    {
        await Task.WhenAny(completion, Task.Delay(ExitDurationMilliseconds + 100));
    }

    private static bool ShouldAnimate()
    {
        return SystemParameters.ClientAreaAnimation;
    }

    public static void ApplyBuiltInSmoke(object? dialog, WpfBrush? backdropBrush = null)
    {
        if (dialog == null)
        {
            return;
        }

        if (dialog is FrameworkElement element)
        {
            WpfBrush transparentBrush = System.Windows.Media.Brushes.Transparent;
            element.Resources["ContentDialogSmokeFill"] = transparentBrush;
            element.Resources["ContentDialogLightDismissOverlayBackground"] = transparentBrush;
            element.Resources["ContentDialogTopOverlay"] = transparentBrush;

            _ = element.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => ApplyDialogTemplateMask(element, transparentBrush)));
        }

        PropertyInfo? smokeLayerBackground = dialog.GetType().GetProperty("SmokeLayerBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (smokeLayerBackground?.CanWrite == true && smokeLayerBackground.PropertyType.IsAssignableFrom(typeof(WpfBrush)))
        {
            smokeLayerBackground.SetValue(dialog, System.Windows.Media.Brushes.Transparent);
        }
    }

    public static WpfBrush CreateBackdropBrush()
    {
        bool isLightTheme = IsLightTheme();
        string resourceKey = isLightTheme ? "DialogMaskLightBrush" : "DialogMaskDarkBrush";
        if (Application.Current?.TryFindResource(resourceKey) is WpfBrush resourceBrush)
        {
            return resourceBrush.CloneCurrentValue();
        }

        return isLightTheme
            ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
    }

    public static void ApplyBackdropBrush(WpfPanel? backdrop)
    {
        if (backdrop != null)
        {
            backdrop.Background = CreateBackdropBrush();
        }
    }

    public static void RefreshActiveBackdropBrushes()
    {
        if (Application.Current == null)
        {
            return;
        }

        WpfBrush backdropBrush = CreateBackdropBrush();
        foreach (Window window in Application.Current.Windows.OfType<Window>())
        {
            foreach (string name in BackdropRootNames)
            {
                if (window.FindName(name) is WpfPanel backdrop && backdrop.Visibility == Visibility.Visible)
                {
                    backdrop.Background = backdropBrush.CloneCurrentValue();
                }
            }
        }
    }

    private static WpfPanel? FindBackdrop(Window? window)
    {
        if (window == null)
        {
            return null;
        }

        foreach (string name in BackdropRootNames)
        {
            if (window.FindName(name) is WpfPanel element)
            {
                return element;
            }
        }

        return null;
    }

    private static UIElement? FindBlurTarget(Window? window)
    {
        if (window == null)
        {
            return null;
        }

        foreach (string name in BlurRootNames)
        {
            if (window.FindName(name) is UIElement element)
            {
                return element;
            }
        }

        return null;
    }

    private static DispatcherTimer? StartOwnerEnablePump(Window? window)
    {
        if (window == null)
        {
            return null;
        }

        DispatcherTimer timer = new(DispatcherPriority.Send, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        int remainingTicks = OwnerEnablePumpMaximumTicks;
        timer.Tick += (_, _) =>
        {
            EnableOwnerWindow(window);
            remainingTicks--;
            if (remainingTicks <= 0)
            {
                timer.Stop();
            }
        };
        EnableOwnerWindow(window);
        timer.Start();
        return timer;
    }

    private static DispatcherTimer StartDialogMaskClearPump(FrameworkElement dialogElement)
    {
        DispatcherTimer timer = new(DispatcherPriority.Send, dialogElement.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(25),
        };
        int remainingTicks = DialogMaskClearPumpMaximumTicks;
        timer.Tick += (_, _) =>
        {
            ClearDialogMaskVisuals(dialogElement);
            remainingTicks--;
            if (remainingTicks <= 0)
            {
                timer.Stop();
            }
        };
        ClearDialogMaskVisuals(dialogElement);
        timer.Start();
        return timer;
    }

    private static void ClearDialogMaskVisuals(FrameworkElement dialogElement)
    {
        WpfBrush transparentBrush = System.Windows.Media.Brushes.Transparent;
        dialogElement.Resources["ContentDialogSmokeFill"] = transparentBrush;
        dialogElement.Resources["ContentDialogLightDismissOverlayBackground"] = transparentBrush;
        dialogElement.Resources["ContentDialogTopOverlay"] = transparentBrush;
        ApplyDialogTemplateMask(dialogElement, transparentBrush);

        foreach (DependencyObject node in EnumerateVisuals(dialogElement))
        {
            if (IsDialogMaskElement(node, transparentBrush) ||
                IsLargeDialogMaskElement(node, dialogElement))
            {
                SetBackground(node, transparentBrush);
                if (node is FrameworkElement element &&
                    element.Name.Equals("LayoutRoot", StringComparison.OrdinalIgnoreCase))
                {
                    element.Opacity = 1d;
                }
            }
        }
    }

    private static void EnableOwnerWindow(Window window)
    {
        window.IsEnabled = true;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = EnableWindow(handle, true);
        }

        ClearOwnerDialogMaskVisuals(window);

        foreach (string name in BlurRootNames)
        {
            if (window.FindName(name) is UIElement element)
            {
                element.IsEnabled = true;
            }
        }
    }

    private static void ClearOwnerDialogMaskVisuals(Window window)
    {
        WpfBrush transparentBrush = System.Windows.Media.Brushes.Transparent;
        foreach (DependencyObject node in EnumerateVisuals(window))
        {
            if (IsOwnerDialogMaskElement(node, window))
            {
                SetBackground(node, transparentBrush);
                if (node is FrameworkElement element &&
                    element.Name.Equals("LayoutRoot", StringComparison.OrdinalIgnoreCase))
                {
                    element.Opacity = 1d;
                }
            }
        }
    }

    private static bool IsOwnerDialogMaskElement(DependencyObject node, Window owner)
    {
        if (node is not FrameworkElement element)
        {
            return false;
        }

        string name = element.Name;
        string typeName = element.GetType().FullName ?? string.Empty;

        if (BackdropRootNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Equals("LayoutRoot", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Smoke", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("LightDismiss", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("ContentDialogAdorner", StringComparison.OrdinalIgnoreCase) ||
               IsLargeSemiTransparentMask(element, owner);
    }

    private static bool IsLargeSemiTransparentMask(FrameworkElement element, Window owner)
    {
        double referenceWidth = Math.Max(1d, Math.Max(owner.ActualWidth, owner.Width));
        double referenceHeight = Math.Max(1d, Math.Max(owner.ActualHeight, owner.Height));
        double elementWidth = GetElementSize(element.ActualWidth, element.RenderSize.Width, element.Width);
        double elementHeight = GetElementSize(element.ActualHeight, element.RenderSize.Height, element.Height);

        if (elementWidth < referenceWidth * 0.72d || elementHeight < referenceHeight * 0.72d)
        {
            return false;
        }

        return GetBackground(element) is SolidColorBrush brush &&
               IsSemiTransparentNeutralMask(brush, element.Opacity);
    }

    private static double GetElementSize(params double[] values)
    {
        foreach (double value in values)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0d)
            {
                return value;
            }
        }

        return 0d;
    }

    private static bool IsSemiTransparentNeutralMask(SolidColorBrush brush, double elementOpacity)
    {
        byte maxChannel = Math.Max(brush.Color.R, Math.Max(brush.Color.G, brush.Color.B));
        byte minChannel = Math.Min(brush.Color.R, Math.Min(brush.Color.G, brush.Color.B));
        if (maxChannel > 48 || maxChannel - minChannel > 8)
        {
            return false;
        }

        double effectiveOpacity = brush.Color.A / 255d * brush.Opacity * elementOpacity;
        return effectiveOpacity > 0d && effectiveOpacity <= 0.72d;
    }

    private static void AttachDialogMask(object? dialog, WpfBrush backdropBrush, bool isLightDismissEnabled, Action? lightDismissAction)
    {
        if (dialog is not UIElement dialogElement)
        {
            return;
        }

        _ = dialogElement.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (dialogElement is FrameworkElement element)
                {
                    ApplyDialogTemplateMask(element, System.Windows.Media.Brushes.Transparent);
                }

                foreach (DependencyObject node in EnumerateVisuals(dialogElement))
                {
                    if (node is not UIElement hitTarget || !IsDialogMaskElement(node, backdropBrush))
                    {
                        continue;
                    }

                    SetBackground(node, System.Windows.Media.Brushes.Transparent);
                    hitTarget.IsHitTestVisible = true;
                    hitTarget.MouseDown += (_, e) =>
                    {
                        if (!ReferenceEquals(e.OriginalSource, hitTarget))
                        {
                            return;
                        }

                        e.Handled = true;
                        if (isLightDismissEnabled)
                        {
                            lightDismissAction?.Invoke();
                            if (lightDismissAction == null)
                            {
                                HideDialog(dialog);
                            }
                        }
                    };
                }
            }));
    }

    private static IEnumerable<DependencyObject> EnumerateVisuals(DependencyObject root)
    {
        Stack<DependencyObject> stack = new();
        stack.Push(root);

        while (stack.Count > 0)
        {
            DependencyObject current = stack.Pop();
            yield return current;

            int childCount;
            try
            {
                childCount = VisualTreeHelper.GetChildrenCount(current);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            for (int index = childCount - 1; index >= 0; index--)
            {
                stack.Push(VisualTreeHelper.GetChild(current, index));
            }
        }
    }

    private static bool IsDialogMaskElement(DependencyObject node, WpfBrush backdropBrush)
    {
        if (node is FrameworkElement element &&
            (element.Name.Equals("LayoutRoot", StringComparison.OrdinalIgnoreCase) ||
             element.Name.Contains("Smoke", StringComparison.OrdinalIgnoreCase) ||
             element.Name.Contains("LightDismiss", StringComparison.OrdinalIgnoreCase) ||
             element.Name.Contains("Overlay", StringComparison.OrdinalIgnoreCase) ||
             element.Name.Contains("Adorner", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return backdropBrush is SolidColorBrush { Color.A: > 0 } &&
               IsSameBrush(GetBackground(node), backdropBrush);
    }

    private static bool IsLargeDialogMaskElement(DependencyObject node, FrameworkElement dialogElement)
    {
        if (node is not FrameworkElement element)
        {
            return false;
        }

        double dialogWidth = GetElementSize(dialogElement.ActualWidth, dialogElement.RenderSize.Width, dialogElement.Width);
        double dialogHeight = GetElementSize(dialogElement.ActualHeight, dialogElement.RenderSize.Height, dialogElement.Height);
        double elementWidth = GetElementSize(element.ActualWidth, element.RenderSize.Width, element.Width);
        double elementHeight = GetElementSize(element.ActualHeight, element.RenderSize.Height, element.Height);

        if (dialogWidth <= 1d || dialogHeight <= 1d || elementWidth < dialogWidth * 0.9d || elementHeight < dialogHeight * 0.9d)
        {
            return false;
        }

        return GetBackground(element) is SolidColorBrush brush &&
               IsSemiTransparentNeutralMask(brush, element.Opacity);
    }

    private static WpfBrush? GetBackground(DependencyObject node)
    {
        return node switch
        {
            WpfBorder border => border.Background,
            WpfControl control => control.Background,
            WpfPanel panel => panel.Background,
            WpfShape shape => shape.Fill,
            _ => null,
        };
    }

    private static void SetBackground(DependencyObject node, WpfBrush brush)
    {
        switch (node)
        {
            case WpfBorder border:
                border.Background = brush;
                break;
            case WpfControl control:
                control.Background = brush;
                break;
            case WpfPanel panel:
                panel.Background = brush;
                break;
            case WpfShape shape:
                shape.Fill = brush;
                break;
        }
    }

    private static void ApplyDialogTemplateMask(FrameworkElement dialogElement, WpfBrush transparentBrush)
    {
        if (dialogElement is WpfControl control)
        {
            control.ApplyTemplate();
            object? layoutRootObject = control.Template?.FindName("LayoutRoot", control);
            object? smokeLayerObject = control.Template?.FindName("SmokeLayerBackground", control);
            object? baseBorderObject = control.Template?.FindName("BaseBorder", control);

            if (layoutRootObject is WpfPanel layoutRoot)
            {
                layoutRoot.Background = transparentBrush;
            }

            if (smokeLayerObject is WpfShape smokeLayer)
            {
                smokeLayer.Fill = transparentBrush;
            }

            if (baseBorderObject is WpfBorder baseBorder)
            {
                baseBorder.SetBinding(
                    WpfBorder.BackgroundProperty,
                    new System.Windows.Data.Binding(nameof(WpfControl.Background)) { Source = control });
            }
        }
    }

    private static bool IsSameBrush(WpfBrush? actualBrush, WpfBrush expectedBrush)
    {
        if (ReferenceEquals(actualBrush, expectedBrush))
        {
            return true;
        }

        return actualBrush is SolidColorBrush actual &&
               expectedBrush is SolidColorBrush expected &&
               actual.Color == expected.Color &&
               Math.Abs(actual.Opacity - expected.Opacity) < 0.001d;
    }

    private static bool IsLightTheme()
    {
        string configuredTheme = Configurations.Theme.Get();
        if (configuredTheme.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (configuredTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        object? appsUseLightTheme = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            null);
        if (appsUseLightTheme is int intValue)
        {
            return intValue != 0;
        }

        if (Application.Current?.TryFindResource("SolidBackgroundFillColorBaseBrush") is SolidColorBrush backgroundBrush)
        {
            Color color = backgroundBrush.Color;
            double luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
            return luminance > 0.5d;
        }

        return false;
    }

    private static void HideDialog(object dialog)
    {
        MethodInfo? hideWithResult = dialog.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
            {
                if (method.Name != "Hide")
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsEnum;
            });

        if (hideWithResult != null)
        {
            Type resultType = hideWithResult.GetParameters()[0].ParameterType;
            object result = Enum.GetNames(resultType).Contains("None")
                ? Enum.Parse(resultType, "None")
                : Enum.ToObject(resultType, 0);
            hideWithResult.Invoke(dialog, [result]);
            return;
        }

        MethodInfo? hide = dialog.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "Hide" && method.GetParameters().Length == 0);
        hide?.Invoke(dialog, null);
    }

    private void RequestLightDismiss(object dialog)
    {
        if (lightDismissAction != null)
        {
            lightDismissAction();
            return;
        }

        HideDialog(dialog);
    }
}
