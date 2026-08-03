using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Emerde.Core;

public static class WindowAppearance
{
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private static readonly DependencyProperty IsBorderlessEnabledProperty = DependencyProperty.RegisterAttached(
        "IsBorderlessEnabled",
        typeof(bool),
        typeof(WindowAppearance),
        new PropertyMetadata(false));
    private static readonly DependencyProperty ClientAreaBorderProperty = DependencyProperty.RegisterAttached(
        "ClientAreaBorder",
        typeof(ClientAreaBorder),
        typeof(WindowAppearance),
        new PropertyMetadata(null));

    public static void EnableBorderless(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if ((bool)window.GetValue(IsBorderlessEnabledProperty))
        {
            return;
        }

        window.SetValue(IsBorderlessEnabledProperty, true);
        window.SourceInitialized += RefreshBorderless;
        window.Loaded += RefreshBorderless;
        window.Activated += RefreshBorderless;
        window.StateChanged += RefreshBorderless;
        ApplyBorderless(window);
    }

    public static void ApplyBorderless(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetCurrentValue(Window.BorderBrushProperty, System.Windows.Media.Brushes.Transparent);
        window.SetCurrentValue(Window.BorderThicknessProperty, new Thickness(0));
        ClientAreaBorder? clientAreaBorder = window.GetValue(ClientAreaBorderProperty) as ClientAreaBorder;
        if (clientAreaBorder == null || !clientAreaBorder.IsLoaded)
        {
            clientAreaBorder = EnumerateVisualDescendants(window).OfType<ClientAreaBorder>().FirstOrDefault();
            window.SetValue(ClientAreaBorderProperty, clientAreaBorder);
        }
        if (clientAreaBorder != null)
        {
            clientAreaBorder.SetCurrentValue(System.Windows.Controls.Border.BorderBrushProperty, System.Windows.Media.Brushes.Transparent);
            clientAreaBorder.SetCurrentValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(0));
            clientAreaBorder.SetCurrentValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(0));
        }

        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int borderColor = DwmColorNone;
        int visibleFrameBorderThickness = 0;
        _ = Interop.DwmSetWindowAttribute(handle, Interop.DwmWindowAttribute.BorderColor, ref borderColor, sizeof(int));
        _ = Interop.DwmSetWindowAttribute(handle, Interop.DwmWindowAttribute.VisibleFrameBorderThickness, ref visibleFrameBorderThickness, sizeof(int));
    }

    private static void RefreshBorderless(object? sender, EventArgs args)
    {
        if (sender is not Window window)
        {
            return;
        }

        ApplyBorderless(window);
        _ = window.Dispatcher.BeginInvoke(
            () => ApplyBorderless(window),
            DispatcherPriority.Loaded);
    }

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
    {
        if (root is not Visual && root is not Visual3D)
        {
            yield break;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in EnumerateVisualDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
