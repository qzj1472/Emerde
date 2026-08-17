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
        SetBrush("EmerdeDialogBackgroundBrush", isLightTheme ? Color.FromRgb(0xF1, 0xF4, 0xF5) : Color.FromRgb(0x12, 0x15, 0x19), durationMilliseconds);
        SetBrush("EmerdeDialogOuterBorderBrush", isLightTheme ? Colors.Black : Colors.White, durationMilliseconds);
        SetBrush("UiXShellBrush", isLightTheme ? Color.FromArgb(0xD0, 0xE5, 0xEB, 0xEE) : Color.FromArgb(0xC7, 0x1B, 0x20, 0x26), durationMilliseconds);
        SetBrush("UiXWindowFallbackBrush", isLightTheme ? Color.FromRgb(0xF1, 0xF4, 0xF5) : Color.FromRgb(0x12, 0x15, 0x19), durationMilliseconds);
        SetBrush("UiXNavigationBrush", isLightTheme ? Color.FromArgb(0xD8, 0xE9, 0xEE, 0xF0) : Color.FromArgb(0xD4, 0x18, 0x1C, 0x21), durationMilliseconds);
        SetBrush("UiXPanelBrush", isLightTheme ? Color.FromArgb(0xD0, 0xE5, 0xEB, 0xEE) : Color.FromArgb(0xC7, 0x1B, 0x20, 0x26), durationMilliseconds);
        SetBrush("UiXCardBrush", isLightTheme ? Color.FromArgb(0xE9, 0xEE, 0xF2, 0xF4) : Color.FromArgb(0xE0, 0x20, 0x25, 0x2B), durationMilliseconds);
        SetBrush("UiXCardHoverBrush", isLightTheme ? Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xF0, 0x29, 0x30, 0x38), durationMilliseconds);
        SetBrush("UiXVideoCardBrush", isLightTheme ? Color.FromArgb(0xF6, 0xF2, 0xF5, 0xF6) : Color.FromArgb(0xF0, 0x2B, 0x33, 0x3C), durationMilliseconds);
        SetBrush("UiXVideoCardHoverBrush", isLightTheme ? Color.FromArgb(0xFA, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xF4, 0x35, 0x3F, 0x4A), durationMilliseconds);
        SetBrush("UiXRefreshFlashBrush", isLightTheme ? Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF), durationMilliseconds);
        SetBrush("UiXGroupBrush", isLightTheme ? Color.FromArgb(0xB0, 0xE1, 0xE8, 0xEB) : Color.FromArgb(0xA6, 0x20, 0x25, 0x2B), durationMilliseconds);
        SetBrush("UiXElevatedBrush", isLightTheme ? Color.FromArgb(0xF3, 0xF7, 0xFA, 0xFB) : Color.FromArgb(0xEE, 0x29, 0x2F, 0x36), durationMilliseconds);
        SetBrush("UiXMonitorCardBrush", isLightTheme ? Color.FromArgb(0xF0, 0xD7, 0xE8, 0xFA) : Color.FromArgb(0xE8, 0x18, 0x32, 0x52), durationMilliseconds);
        SetBrush("UiXLiveCardBrush", isLightTheme ? Color.FromArgb(0xF2, 0xC9, 0xEA, 0xD5) : Color.FromArgb(0xE8, 0x19, 0x3E, 0x2A), durationMilliseconds);
        SetBrush("UiXRecordingCardBrush", isLightTheme ? Color.FromArgb(0xF2, 0xF6, 0xCD, 0xD2) : Color.FromArgb(0xE8, 0x48, 0x24, 0x30), durationMilliseconds);
        SetBrush("UiXDangerForegroundBrush", isLightTheme ? Color.FromRgb(0xC4, 0x2B, 0x3A) : Color.FromRgb(0xFF, 0x9B, 0xA6), durationMilliseconds);
        SetBrush("UiXDangerFillBrush", isLightTheme ? Color.FromArgb(0x1A, 0xD1, 0x34, 0x4B) : Color.FromArgb(0x26, 0xFF, 0x75, 0x84), durationMilliseconds);
        SetBrush("UiXMonitorFillBrush", isLightTheme ? Color.FromArgb(0x66, 0x4D, 0x82, 0xC7) : Color.FromArgb(0x48, 0x68, 0xA8, 0xE7), durationMilliseconds);
        SetBrush("UiXLiveFillBrush", isLightTheme ? Color.FromArgb(0x78, 0x3B, 0x9B, 0x61) : Color.FromArgb(0x48, 0x5D, 0xC8, 0x82), durationMilliseconds);
        SetBrush("UiXRecordingFillBrush", isLightTheme ? Color.FromArgb(0x24, 0xC9, 0x4D, 0x5A) : Color.FromArgb(0x30, 0xE8, 0x70, 0x7A), durationMilliseconds);
        SetBrush("UiXTranscodeFillBrush", isLightTheme ? Color.FromArgb(0x22, 0xD9, 0x8B, 0x3D) : Color.FromArgb(0x2C, 0xE2, 0xA3, 0x5D), durationMilliseconds);
        SetBrush("UiXStallFillBrush", isLightTheme ? Color.FromArgb(0x22, 0x4C, 0x8F, 0xD8) : Color.FromArgb(0x2C, 0x68, 0xA8, 0xE7), durationMilliseconds);
        SetBrush("UiXRecordingForegroundBrush", isLightTheme ? Color.FromRgb(0x9F, 0x17, 0x26) : Color.FromRgb(0xFF, 0xA2, 0xAB), durationMilliseconds);
        SetBrush("UiXTranscodeForegroundBrush", isLightTheme ? Color.FromRgb(0x8A, 0x4A, 0x00) : Color.FromRgb(0xFF, 0xC1, 0x7A), durationMilliseconds);
        SetBrush("UiXStallForegroundBrush", isLightTheme ? Color.FromRgb(0x24, 0x5F, 0xB4) : Color.FromRgb(0x9C, 0xC8, 0xFF), durationMilliseconds);
        SetBrush("UiXDividerBrush", isLightTheme ? Color.FromArgb(0x12, 0x16, 0x20, 0x2A) : Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), durationMilliseconds);
        SetBrush("UiXSubtleFillBrush", isLightTheme ? Color.FromArgb(0x0F, 0x16, 0x20, 0x2A) : Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), durationMilliseconds);
        SetBrush("UiXStrokeBrush", isLightTheme ? Color.FromArgb(0x1A, 0x16, 0x20, 0x2A) : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), durationMilliseconds);
        SetBrush("UiXStrongStrokeBrush", isLightTheme ? Color.FromArgb(0x2B, 0x16, 0x20, 0x2A) : Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF), durationMilliseconds);
        SetBrush("UiXSelectionFillBrush", isLightTheme ? Color.FromArgb(0x24, 0x4D, 0x8D, 0xA3) : Color.FromArgb(0x34, 0x75, 0xB7, 0xC8), durationMilliseconds);
        SetBrush("UiXSelectionStrokeBrush", isLightTheme ? Color.FromArgb(0x80, 0x4D, 0x8D, 0xA3) : Color.FromArgb(0x9A, 0x75, 0xB7, 0xC8), durationMilliseconds);
        Color accentColor = Application.Current.Resources["SystemAccentColorPrimaryBrush"] is SolidColorBrush accentBrush
            ? accentBrush.Color
            : Color.FromRgb(0x00, 0x78, 0xD4);
        SetBrush("UiXTextOnAccentBrush", GetTextOnAccentColor(accentColor), durationMilliseconds);
        SetBrush("UiXTextPrimaryBrush", isLightTheme ? Color.FromArgb(0xE8, 0x17, 0x1B, 0x20) : Color.FromArgb(0xF2, 0xF4, 0xF6, 0xF8), durationMilliseconds);
        SetBrush("UiXTextSecondaryBrush", isLightTheme ? Color.FromArgb(0xA8, 0x17, 0x1B, 0x20) : Color.FromArgb(0xB8, 0xE4, 0xE8, 0xEC), durationMilliseconds);
        SetBrush("UiXDialogSurfaceBrush", isLightTheme ? Color.FromRgb(0xF1, 0xF4, 0xF5) : Color.FromRgb(0x12, 0x15, 0x19), durationMilliseconds);
        SetBrush("UiXDialogSectionBrush", isLightTheme ? Color.FromRgb(0xE7, 0xEC, 0xEF) : Color.FromRgb(0x20, 0x25, 0x2B), durationMilliseconds);
        SetBrush("UiXDialogElevatedBrush", isLightTheme ? Color.FromRgb(0xF8, 0xFA, 0xFB) : Color.FromRgb(0x29, 0x2F, 0x36), durationMilliseconds);
        SetBrush("UiXDialogSubtleBrush", isLightTheme ? Color.FromRgb(0xE0, 0xE6, 0xE9) : Color.FromRgb(0x2A, 0x30, 0x37), durationMilliseconds);
        SetBrush("UiXDialogSelectionBrush", isLightTheme ? Color.FromRgb(0xDC, 0xE9, 0xED) : Color.FromRgb(0x23, 0x34, 0x3B), durationMilliseconds);
        SetBrush("UiXDialogWarningBrush", isLightTheme ? Color.FromRgb(0xF3, 0xE2, 0xCE) : Color.FromRgb(0x44, 0x35, 0x25), durationMilliseconds);
        SetBrush("UiXDialogDangerBrush", isLightTheme ? Color.FromRgb(0xF5, 0xDD, 0xE0) : Color.FromRgb(0x45, 0x26, 0x2D), durationMilliseconds);
        SetBrush("UiXDialogLiveBrush", isLightTheme ? Color.FromRgb(0xD5, 0xE8, 0xDB) : Color.FromRgb(0x26, 0x3C, 0x2F), durationMilliseconds);
        SetBrush("UiXDialogRecordingBrush", isLightTheme ? Color.FromRgb(0xF0, 0xDA, 0xDC) : Color.FromRgb(0x43, 0x28, 0x30), durationMilliseconds);
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

    internal static Color GetTextOnAccentColor(Color accentColor)
    {
        static double Linearize(byte channel)
        {
            double value = channel / 255d;
            return value <= 0.04045d
                ? value / 12.92d
                : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        double luminance = 0.2126d * Linearize(accentColor.R)
            + 0.7152d * Linearize(accentColor.G)
            + 0.0722d * Linearize(accentColor.B);
        double whiteContrast = 1.05d / (luminance + 0.05d);
        return whiteContrast >= 4.5d
            ? Colors.White
            : Color.FromRgb(0x17, 0x1B, 0x20);
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

    internal static bool IsLightTheme()
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
