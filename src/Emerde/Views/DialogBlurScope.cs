using Emerde.Core;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfControl = System.Windows.Controls.Control;
using WpfPanel = System.Windows.Controls.Panel;
using WpfShape = System.Windows.Shapes.Shape;

namespace Emerde.Views;

internal sealed class DialogBlurScope : IDisposable
{
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
    private readonly Visibility previousBackdropVisibility;
    private readonly bool previousBackdropHitTestVisible;
    private readonly MouseButtonEventHandler? backdropMouseDownHandler;
    private readonly UIElement? blurTarget;
    private readonly System.Windows.Media.Effects.Effect? previousBlurEffect;
    private readonly Window? ownerWindow;
    private readonly bool previousOwnerIsEnabled;
    private readonly DispatcherTimer? ownerEnableTimer;
    private readonly DispatcherTimer? dialogMaskClearTimer;

    public DialogBlurScope(Window? owner = null, double radius = 8d, object? dialog = null, bool isLightDismissEnabled = false)
    {
        WpfBrush backdropBrush = CreateBackdropBrush();
        ApplyBuiltInSmoke(dialog, backdropBrush);
        AttachDialogMask(dialog, backdropBrush, isLightDismissEnabled);
        dialogMaskClearTimer = dialog is FrameworkElement dialogElement
            ? StartDialogMaskClearPump(dialogElement)
            : null;

        Window? window = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        ownerWindow = window;
        previousOwnerIsEnabled = window?.IsEnabled ?? true;
        ownerEnableTimer = StartOwnerEnablePump(window);

        blurTarget = FindBlurTarget(window);
        if (blurTarget != null && radius > 0d)
        {
            previousBlurEffect = blurTarget.Effect;
            blurTarget.Effect = new System.Windows.Media.Effects.BlurEffect()
            {
                Radius = radius,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            };
        }

        backdrop = FindBackdrop(window);
        if (backdrop != null)
        {
            previousBackdropBackground = backdrop.Background;
            previousBackdropVisibility = backdrop.Visibility;
            previousBackdropHitTestVisible = backdrop.IsHitTestVisible;
            backdrop.Background = backdropBrush;
            backdrop.IsHitTestVisible = true;
            backdropMouseDownHandler = (_, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, backdrop))
                {
                    e.Handled = true;
                    if (isLightDismissEnabled && dialog != null)
                    {
                        HideDialog(dialog);
                    }
                }
            };
            backdrop.MouseDown += backdropMouseDownHandler;
            backdrop.Visibility = Visibility.Visible;
        }
    }

    public static DialogBlurScope ForLightDismiss(Window? owner, object dialog, double radius = 8d)
    {
        return new DialogBlurScope(owner, radius, dialog, true);
    }

    public static DialogBlurScope ForDialog(Window? owner, object dialog, double radius = 8d)
    {
        return new DialogBlurScope(owner, radius, dialog);
    }

    public void Dispose()
    {
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
            backdrop.IsHitTestVisible = previousBackdropHitTestVisible;
            backdrop.Visibility = previousBackdropVisibility;
        }

        if (blurTarget != null)
        {
            blurTarget.Effect = previousBlurEffect;
        }
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
        timer.Tick += (_, _) =>
        {
            EnableOwnerWindow(window);
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
        timer.Tick += (_, _) =>
        {
            ClearDialogMaskVisuals(dialogElement);
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
            if (IsDialogMaskElement(node, transparentBrush))
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

    private static void AttachDialogMask(object? dialog, WpfBrush backdropBrush, bool isLightDismissEnabled)
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
                            HideDialog(dialog);
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
                baseBorder.Background = transparentBrush;
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
}
