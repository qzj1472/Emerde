using Emerde.Properties;
using System.Globalization;
using System.Windows;

namespace Emerde;

internal static class Locale
{
    public static event EventHandler? CultureChanged;

    public static CultureInfo Fallback { get; } = new CultureInfo("en-US");

    public static CultureInfo Culture
    {
        get => CultureInfo.CurrentUICulture;
        set => SetCulture(value);
    }

    private static void SetCulture(CultureInfo? value)
    {
        CultureInfo culture = value ?? Fallback;

        while (Resources.ResourceManager.GetResourceSet(culture, true, false) is null)
        {
            if (culture.Parent == CultureInfo.InvariantCulture)
            {
                culture = Fallback;
                break;
            }
            culture = culture.Parent;
        }

        I18NExtension.Culture
            = CultureInfo.CurrentCulture
            = CultureInfo.CurrentUICulture
            = culture;

        CultureChanged?.Invoke(null, EventArgs.Empty);
    }
}

internal static class LocaleExtension
{
    public static string Tr(this string key)
    {
        try
        {
            string? translated = I18NExtension.Translate(key);
            if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, key, StringComparison.Ordinal))
            {
                return translated;
            }

            return Resources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? translated ?? key;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine(e);
            return key;
        }
    }

    public static string Tr(this string key, params object[] args)
    {
        return string.Format(Tr(key)?.ToString() ?? string.Empty, args);
    }
}

internal static class CultureInfoExtension
{
    public static bool IsUseWordSpace(this CultureInfo culture)
    {
        string language = culture.TwoLetterISOLanguageName;
        return !Array.Exists(["zh", "ja", "ko"], lang => lang == language);
    }

    public static string WordSpace(this CultureInfo culture)
    {
        return culture.IsUseWordSpace() ? " " : string.Empty;
    }

}
