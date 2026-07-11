using System.Windows;
using System.Windows.Media;

namespace Emerde.Core;

internal static class AppThemeBrushes
{
    public static void Apply()
    {
        if (Application.Current == null)
        {
            return;
        }

        bool isLightTheme = IsLightTheme();
        SetBrush("EmerdeShellBackgroundBrush", isLightTheme ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x14, 0x14, 0x14));
        SetBrush("EmerdeSurfaceBrush", isLightTheme ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1C, 0x1C, 0x1C));
        SetBrush("EmerdePanelBrush", isLightTheme ? Color.FromRgb(0xF8, 0xF8, 0xF8) : Color.FromRgb(0x20, 0x20, 0x20));
        SetBrush("EmerdeCardBrush", isLightTheme ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x24, 0x24, 0x24));
        SetBrush("ControlElevationBorderBrush", isLightTheme ? Color.FromArgb(0x24, 0x00, 0x00, 0x00) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        SetBrush("CircleElevationBorderBrush", isLightTheme ? Color.FromArgb(0x24, 0x00, 0x00, 0x00) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        SetBrush("AccentControlElevationBorderBrush", Colors.Transparent);
    }

    private static void SetBrush(string key, Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
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

        return appsUseLightTheme is not int intValue || intValue != 0;
    }
}
