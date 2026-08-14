using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Emerde.Core;

internal static class WindowSizing
{
    private const string DialogMinWidthResource = "ContentDialogMinWidth";
    private const string DialogMinHeightResource = "ContentDialogMinHeight";
    private const string DialogMaxWidthResource = "ContentDialogMaxWidth";
    private const string DialogMaxHeightResource = "ContentDialogMaxHeight";
    private const double ScreenRatio = 0.85d;
    private const double MainWindowWidthRatio = 0.70d;
    private const double MainWindowMaximumWidthRatio = 0.85d;
    private const double MainWindowDpiWidthCompensation = 0.30d;
    private const double MainWindowAspectRatio = 14d / 9d;
    private const double MainBaseWidth = 1440d;
    private const double MainBaseHeight = 926d;
    private static int openContentDialogCount;

    public static bool HasOpenContentDialog => openContentDialogCount > 0;

    internal static double RoundLayoutValue(double value)
    {
        return double.IsFinite(value)
            ? Math.Round(value, MidpointRounding.AwayFromZero)
            : value;
    }

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

    public static async Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog, Window? owner = null, double? fixedWidth = null, double? fixedHeight = null)
    {
        if (HasOpenContentDialog)
        {
            return default;
        }

        openContentDialogCount++;
        RemoveContentDialogSizeLimits(dialog);
        if (fixedWidth is > 0d && double.IsFinite(fixedWidth.Value))
        {
            ApplyFixedContentDialogWidth(dialog, fixedWidth.Value);
        }
        if (fixedHeight is > 0d && double.IsFinite(fixedHeight.Value))
        {
            ApplyFixedContentDialogHeight(dialog, fixedHeight.Value);
        }
        try
        {
            return owner is { IsLoaded: true }
                ? await dialog.ShowAsync(owner)
                : await dialog.ShowAsync();
        }
        finally
        {
            openContentDialogCount = Math.Max(0, openContentDialogCount - 1);
        }
    }

    public static void RemoveContentDialogSizeLimits(ContentDialog dialog)
    {
        dialog.MinWidth = 0d;
        dialog.MinHeight = 0d;
        dialog.MaxWidth = double.PositiveInfinity;
        dialog.MaxHeight = double.PositiveInfinity;
        dialog.Resources[DialogMinWidthResource] = 0d;
        dialog.Resources[DialogMinHeightResource] = 0d;
        dialog.Resources[DialogMaxWidthResource] = double.PositiveInfinity;
        dialog.Resources[DialogMaxHeightResource] = double.PositiveInfinity;
    }

    internal static void ApplyFixedContentDialogWidth(ContentDialog dialog, double width)
    {
        dialog.Width = width;
        dialog.MinWidth = width;
        dialog.MaxWidth = width;
        dialog.Resources[DialogMinWidthResource] = width;
        dialog.Resources[DialogMaxWidthResource] = width;
    }

    internal static void ApplyFixedContentDialogHeight(ContentDialog dialog, double height)
    {
        dialog.Height = height;
        dialog.MinHeight = height;
        dialog.MaxHeight = height;
        dialog.Resources[DialogMinHeightResource] = height;
        dialog.Resources[DialogMaxHeightResource] = height;
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
        double width = Math.Max(1d, RoundLayoutValue(baseWidth * scale * userScale));
        double height = Math.Max(1d, RoundLayoutValue(baseHeight * scale * userScale));
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
        double width = Math.Max(1d, RoundLayoutValue(screen.WorkingArea.Width * widthRatio / dpi.DpiScaleX));
        double height = Math.Max(1d, RoundLayoutValue(width / MainWindowAspectRatio));
        double maxHeight = Math.Max(1d, screen.WorkingArea.Height / dpi.DpiScaleY);

        if (height > maxHeight)
        {
            height = RoundLayoutValue(maxHeight);
            width = RoundLayoutValue(height * MainWindowAspectRatio);
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
        double width = Math.Max(1d, RoundLayoutValue(referenceWidth * baseWidth / MainBaseWidth * userScale));
        double height = Math.Max(1d, RoundLayoutValue(referenceHeight * baseHeight / MainBaseHeight * userScale));
        double maxWidth = Math.Max(1d, screen.WorkingArea.Width * ScreenRatio / dpi.DpiScaleX);
        double maxHeight = Math.Max(1d, screen.WorkingArea.Height * ScreenRatio / dpi.DpiScaleY);
        double scale = Math.Min(1d, Math.Min(maxWidth / width, maxHeight / height));

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return;
        }

        width = Math.Max(1d, RoundLayoutValue(width * scale));
        height = Math.Max(1d, RoundLayoutValue(height * scale));
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
