using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Emerde.Core;

internal static class WindowSizing
{
    private const double ScreenRatio = 0.85d;
    private const double MainWindowWidthRatio = 0.70d;
    private const double MainWindowMaximumWidthRatio = 0.85d;
    private const double MainWindowDpiWidthCompensation = 0.30d;
    private const double MainWindowAspectRatio = 14d / 9d;
    private const double MainBaseWidth = 1440d;
    private const double MainBaseHeight = 926d;
    private const double DialogMarginShortSideRatio = 0.10d;

    public static void UseRelativeScreenSize(Window window, double baseWidth, double baseHeight)
    {
        window.SourceInitialized += (_, _) => ApplyScreenRelative(window, baseWidth, baseHeight);
    }

    public static void UseMainWindowAspectSize(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyMainWindowAspect(window);
    }

    public static void UseRelativeMainWindowSize(Window window, double baseWidth, double baseHeight)
    {
        window.SourceInitialized += (_, _) =>
        {
            ApplyMainWindowRelative(window, baseWidth, baseHeight);
            TrackMainWindowRelativePlacement(window, baseWidth, baseHeight);
        };
    }

    public static async Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog, Window? owner = null)
    {
        ApplyContentDialogSizeLimit(dialog, owner);
        RoutedEventHandler? loadedHandler = null;
        loadedHandler = (_, _) =>
        {
            dialog.Loaded -= loadedHandler;
            ApplyContentDialogSizeLimit(dialog, owner);
        };
        dialog.Loaded += loadedHandler;

        try
        {
            return owner is { IsLoaded: true }
                ? await dialog.ShowAsync(owner)
                : await dialog.ShowAsync();
        }
        finally
        {
            dialog.Loaded -= loadedHandler;
        }
    }

    public static void ApplyContentDialogSizeLimit(ContentDialog dialog, Window? owner = null)
    {
        if (dialog.Content is Views.LocalSettingsContentDialog)
        {
            return;
        }

        Window? reference = owner ?? Application.Current?.MainWindow;
        if (reference == null)
        {
            return;
        }

        double referenceWidth = GetWindowActualWidth(reference);
        double referenceHeight = GetWindowActualHeight(reference);
        if (referenceWidth <= 1d || referenceHeight <= 1d)
        {
            return;
        }

        double margin = Math.Min(referenceWidth, referenceHeight) * DialogMarginShortSideRatio;
        double maxWidth = Math.Max(320d, Math.Floor(referenceWidth - margin * 2d));
        double maxHeight = Math.Max(240d, Math.Floor(referenceHeight - margin * 2d));

        dialog.MaxWidth = maxWidth;
        dialog.MaxHeight = maxHeight;
        if (!double.IsNaN(dialog.Width) && dialog.Width > maxWidth)
        {
            dialog.Width = maxWidth;
        }
        if (!double.IsNaN(dialog.Height) && dialog.Height > maxHeight)
        {
            dialog.Height = maxHeight;
        }
        if (dialog.MinWidth > maxWidth)
        {
            dialog.MinWidth = 0d;
        }
        if (dialog.MinHeight > maxHeight)
        {
            dialog.MinHeight = 0d;
        }

        if (dialog.Content is FrameworkElement content)
        {
            double contentMaxWidth = Math.Max(1d, maxWidth - 40d);
            double contentMaxHeight = Math.Max(1d, maxHeight - 132d);
            content.MaxWidth = contentMaxWidth;
            content.MaxHeight = contentMaxHeight;
            if (content.MinWidth > contentMaxWidth)
            {
                content.MinWidth = 0d;
            }
            if (content.MinHeight > contentMaxHeight)
            {
                content.MinHeight = 0d;
            }
            if (!double.IsNaN(content.Width) && content.Width > contentMaxWidth)
            {
                content.Width = contentMaxWidth;
            }
            if (!double.IsNaN(content.Height) && content.Height > contentMaxHeight)
            {
                content.Height = contentMaxHeight;
            }
        }
    }

    private static void ApplyScreenRelative(Window window, double baseWidth, double baseHeight)
    {
        if (baseWidth <= 0 || baseHeight <= 0)
        {
            return;
        }

        System.Windows.Forms.Screen screen = GetTargetScreen(window);
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        double maxWidth = Math.Max(1d, screen.WorkingArea.Width * ScreenRatio / dpi.DpiScaleX);
        double maxHeight = Math.Max(1d, screen.WorkingArea.Height * ScreenRatio / dpi.DpiScaleY);
        double scale = Math.Min(maxWidth / baseWidth, maxHeight / baseHeight);

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return;
        }

        double userScale = GetUserDisplayScale();
        double width = Math.Max(1d, Math.Floor(baseWidth * scale * userScale));
        double height = Math.Max(1d, Math.Floor(baseHeight * scale * userScale));
        window.Width = width;
        window.Height = height;
        window.Left = screen.WorkingArea.Left / dpi.DpiScaleX + (screen.WorkingArea.Width / dpi.DpiScaleX - width) / 2d;
        window.Top = screen.WorkingArea.Top / dpi.DpiScaleY + (screen.WorkingArea.Height / dpi.DpiScaleY - height) / 2d;
    }

    private static void ApplyMainWindowAspect(Window window)
    {
        System.Windows.Forms.Screen screen = GetTargetScreen(window);
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        double widthRatio = CalculateMainWindowWidthRatio(dpi.DpiScaleX);
        double width = Math.Max(1d, Math.Floor(screen.WorkingArea.Width * widthRatio / dpi.DpiScaleX));
        double height = Math.Max(1d, Math.Floor(width / MainWindowAspectRatio));
        double maxHeight = Math.Max(1d, screen.WorkingArea.Height / dpi.DpiScaleY);

        if (height > maxHeight)
        {
            height = Math.Floor(maxHeight);
            width = Math.Floor(height * MainWindowAspectRatio);
        }

        window.Width = width;
        window.Height = height;
        window.Left = screen.WorkingArea.Left / dpi.DpiScaleX + (screen.WorkingArea.Width / dpi.DpiScaleX - width) / 2d;
        window.Top = screen.WorkingArea.Top / dpi.DpiScaleY + (screen.WorkingArea.Height / dpi.DpiScaleY - height) / 2d;
    }

    internal static double CalculateMainWindowWidthRatio(double dpiScale)
    {
        if (double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0d)
        {
            return MainWindowWidthRatio;
        }

        double compensatedRatio = MainWindowWidthRatio + Math.Max(0d, dpiScale - 1d) * MainWindowDpiWidthCompensation;
        return Math.Clamp(compensatedRatio, MainWindowWidthRatio, MainWindowMaximumWidthRatio);
    }

    private static void ApplyMainWindowRelative(Window window, double baseWidth, double baseHeight)
    {
        if (baseWidth <= 0 || baseHeight <= 0)
        {
            return;
        }

        Window? reference = GetMainWindowReference(window);
        System.Windows.Forms.Screen screen = GetTargetScreen(window);
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        double referenceWidth = GetReferenceWidth(reference, screen, dpi);
        double referenceHeight = GetReferenceHeight(reference, screen, dpi);
        double userScale = GetUserDisplayScale();
        double width = Math.Max(1d, Math.Floor(referenceWidth * baseWidth / MainBaseWidth * userScale));
        double height = Math.Max(1d, Math.Floor(referenceHeight * baseHeight / MainBaseHeight * userScale));
        double maxWidth = Math.Max(1d, screen.WorkingArea.Width * ScreenRatio / dpi.DpiScaleX);
        double maxHeight = Math.Max(1d, screen.WorkingArea.Height * ScreenRatio / dpi.DpiScaleY);
        double scale = Math.Min(1d, Math.Min(maxWidth / width, maxHeight / height));

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return;
        }

        width = Math.Max(1d, Math.Floor(width * scale));
        height = Math.Max(1d, Math.Floor(height * scale));
        window.Width = width;
        window.Height = height;
        CenterWindow(window, reference, screen, dpi, width, height);
    }

    private static void TrackMainWindowRelativePlacement(Window window, double baseWidth, double baseHeight)
    {
        Window? reference = GetMainWindowReference(window);
        if (reference == null)
        {
            return;
        }

        void UpdatePlacement(object? sender, EventArgs e)
        {
            if (window.IsVisible && window.WindowState != WindowState.Minimized)
            {
                ApplyMainWindowRelative(window, baseWidth, baseHeight);
            }
        }

        SizeChangedEventHandler sizeChanged = (_, e) => UpdatePlacement(reference, e);
        reference.SizeChanged += sizeChanged;
        reference.LocationChanged += UpdatePlacement;
        reference.StateChanged += UpdatePlacement;
        window.Closed += (_, _) =>
        {
            reference.SizeChanged -= sizeChanged;
            reference.LocationChanged -= UpdatePlacement;
            reference.StateChanged -= UpdatePlacement;
        };
    }

    private static Window? GetMainWindowReference(Window window)
    {
        if (window.Owner != null)
        {
            return window.Owner;
        }

        Window? mainWindow = Application.Current?.MainWindow;
        return mainWindow != null && mainWindow != window ? mainWindow : null;
    }

    private static double GetReferenceWidth(Window? reference, System.Windows.Forms.Screen screen, DpiScale dpi)
    {
        if (reference == null)
        {
            return screen.WorkingArea.Width * ScreenRatio / dpi.DpiScaleX;
        }

        return reference.ActualWidth > 1d ? reference.ActualWidth : reference.Width;
    }

    private static double GetReferenceHeight(Window? reference, System.Windows.Forms.Screen screen, DpiScale dpi)
    {
        if (reference == null)
        {
            return screen.WorkingArea.Height * ScreenRatio / dpi.DpiScaleY;
        }

        return reference.ActualHeight > 1d ? reference.ActualHeight : reference.Height;
    }

    private static double GetWindowActualWidth(Window window)
    {
        if (window.ActualWidth > 1d)
        {
            return window.ActualWidth;
        }

        return !double.IsNaN(window.Width) && window.Width > 1d ? window.Width : 0d;
    }

    private static double GetWindowActualHeight(Window window)
    {
        if (window.ActualHeight > 1d)
        {
            return window.ActualHeight;
        }

        return !double.IsNaN(window.Height) && window.Height > 1d ? window.Height : 0d;
    }

    private static void CenterWindow(Window window, Window? reference, System.Windows.Forms.Screen screen, DpiScale dpi, double width, double height)
    {
        Rect viewport = GetReferenceViewport(reference, screen, dpi);
        window.Left = Clamp(viewport.Left + (viewport.Width - width) / 2d, viewport.Left, viewport.Right - width);
        window.Top = Clamp(viewport.Top + (viewport.Height - height) / 2d, viewport.Top, viewport.Bottom - height);
    }

    private static Rect GetReferenceViewport(Window? reference, System.Windows.Forms.Screen screen, DpiScale dpi)
    {
        double screenLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        double screenTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        double screenWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
        double screenHeight = screen.WorkingArea.Height / dpi.DpiScaleY;

        if (reference == null || !reference.IsVisible || reference.WindowState == WindowState.Minimized || reference.WindowState == WindowState.Maximized)
        {
            return new Rect(screenLeft, screenTop, screenWidth, screenHeight);
        }

        return new Rect(reference.Left, reference.Top, GetReferenceWidth(reference, screen, dpi), GetReferenceHeight(reference, screen, dpi));
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Clamp(value, min, Math.Max(min, max));
    }

    private static double GetUserDisplayScale()
    {
        return Math.Clamp(Configurations.DisplayScale.Get(), 80, 200) / 100d;
    }

    private static System.Windows.Forms.Screen GetTargetScreen(Window window)
    {
        nint handle = nint.Zero;

        if (window.Owner != null)
        {
            handle = new WindowInteropHelper(window.Owner).Handle;
        }

        if (handle == nint.Zero && Application.Current?.MainWindow != null && Application.Current.MainWindow != window)
        {
            handle = new WindowInteropHelper(Application.Current.MainWindow).Handle;
        }

        if (handle == nint.Zero)
        {
            handle = new WindowInteropHelper(window).Handle;
        }

        return handle == nint.Zero
            ? System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens.First()
            : System.Windows.Forms.Screen.FromHandle(handle);
    }
}
