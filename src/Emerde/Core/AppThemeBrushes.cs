using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Emerde.Core;

internal static class AppThemeBrushes
{
    internal const int DarkThemeTransitionDurationMilliseconds = 220;
    internal const int LightThemeTransitionDurationMilliseconds = 275;

    public static void Apply()
    {
        if (Application.Current == null)
        {
            return;
        }

        bool isLightTheme = IsLightTheme();
        int durationMilliseconds = GetTransitionDurationMilliseconds(isLightTheme);
        SetBrush("EmerdeShellBackgroundBrush", isLightTheme ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x14, 0x14, 0x14), durationMilliseconds);
        SetBrush("EmerdeSurfaceBrush", isLightTheme ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1C, 0x1C, 0x1C), durationMilliseconds);
        SetBrush("EmerdePanelBrush", isLightTheme ? Color.FromRgb(0xF8, 0xF8, 0xF8) : Color.FromRgb(0x20, 0x20, 0x20), durationMilliseconds);
        SetBrush("EmerdeCardBrush", isLightTheme ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x24, 0x24, 0x24), durationMilliseconds);
        SetBrush("EmerdeExtensionInputBorderBrush", isLightTheme ? Color.FromArgb(0x24, 0x00, 0x00, 0x00) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), durationMilliseconds);
        SetBrush("EmerdeAboutCardTitleBrush", isLightTheme ? Color.FromRgb(0x56, 0x56, 0x56) : Color.FromRgb(0xD6, 0xD6, 0xD6), durationMilliseconds);
        SetBrush("EmerdeAboutShortcutKeyBrush", isLightTheme ? Color.FromRgb(0x66, 0x66, 0x66) : Color.FromRgb(0xC4, 0xC4, 0xC4), durationMilliseconds);
        SetBrush("EmerdeAboutShortcutDescriptionBrush", isLightTheme ? Color.FromRgb(0x73, 0x73, 0x73) : Color.FromRgb(0xAD, 0xAD, 0xAD), durationMilliseconds);
        SetBrush("ControlElevationBorderBrush", isLightTheme ? Color.FromArgb(0x24, 0x00, 0x00, 0x00) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), durationMilliseconds);
        SetBrush("CircleElevationBorderBrush", isLightTheme ? Color.FromArgb(0x24, 0x00, 0x00, 0x00) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), durationMilliseconds);
        SetBrush("AccentControlElevationBorderBrush", Colors.Transparent, durationMilliseconds);
    }

    internal static int GetTransitionDurationMilliseconds(bool isLightTheme)
    {
        return isLightTheme
            ? LightThemeTransitionDurationMilliseconds
            : DarkThemeTransitionDurationMilliseconds;
    }

    private static void SetBrush(string key, Color color, int durationMilliseconds)
    {
        if (Application.Current.Resources[key] is SolidColorBrush current
            && !current.HasAnimatedProperties
            && current.Color == color)
        {
            return;
        }

        SolidColorBrush brush = new(color);
        if (Application.Current.Resources[key] is SolidColorBrush previous
            && SystemParameters.ClientAreaAnimation)
        {
            brush.Color = previous.Color;
            if (TryAnimateBrushTo(key, brush, color, durationMilliseconds))
            {
                Application.Current.Resources[key] = brush;
                return;
            }

            brush.Color = color;
        }

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }
        Application.Current.Resources[key] = brush;
    }

    private static bool TryAnimateBrushTo(string key, SolidColorBrush brush, Color color, int durationMilliseconds)
    {
        ColorAnimation animation = new(color, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            if (!ReferenceEquals(Application.Current?.Resources[key], brush))
            {
                return;
            }

            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }
        };
        try
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
            return true;
        }
        catch (InvalidOperationException)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            return false;
        }
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
