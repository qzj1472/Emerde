using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using Wpf.Ui.Appearance;

namespace Emerde.UI.Markup;

[Localizability(LocalizationCategory.Ignore)]
[Ambient]
[UsableDuringInitialization(true)]
public partial class ThemesDictionary : ResourceDictionary
{
    public ApplicationTheme Theme
    {
        set
        {
            Wpf.Ui.Markup.ThemesDictionary? wpfUiThemes = FindWpfUiThemesDictionary(Application.Current?.Resources)
                ?? FindWpfUiThemesDictionary(this);
            if (wpfUiThemes is not null)
            {
                wpfUiThemes.Theme = value;
            }
        }
    }

    public ThemesDictionary()
    {
        EnsureApplicationWpfUiDictionaries();
        InitializeComponent();
        if (Application.Current?.Resources is null)
        {
            if (!HasWpfUiTheme(MergedDictionaries))
            {
                MergedDictionaries.Insert(0, new Wpf.Ui.Markup.ThemesDictionary());
            }

            if (!HasWpfUiControls(MergedDictionaries))
            {
                MergedDictionaries.Insert(HasWpfUiTheme(MergedDictionaries) ? 1 : 0, new Wpf.Ui.Markup.ControlsDictionary());
            }
        }
        OverlayControl(typeof(Button));
        OverlayControl(typeof(ToggleButton));
        OverlayControl(typeof(Wpf.Ui.Controls.Button));
    }

    private void OverlayControl(Type type)
    {
        Style? basedOn = FindBaseStyle(type);
        if (basedOn is null)
        {
            return;
        }

        Style style = new(type, basedOn);
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 34d));
        this[type] = style;
    }

    private Style? FindBaseStyle(Type type)
    {
        if (this[type] is Style local)
        {
            return local;
        }

        return Application.Current?.Resources[type] as Style;
    }

    private static void EnsureApplicationWpfUiDictionaries()
    {
        Collection<ResourceDictionary>? merged = Application.Current?.Resources.MergedDictionaries;
        if (merged is null)
        {
            return;
        }

        if (!HasWpfUiTheme(merged))
        {
            merged.Add(new Wpf.Ui.Markup.ThemesDictionary());
        }

        if (!HasWpfUiControls(merged))
        {
            merged.Add(new Wpf.Ui.Markup.ControlsDictionary());
        }
    }

    private static Wpf.Ui.Markup.ThemesDictionary? FindWpfUiThemesDictionary(ResourceDictionary? dictionary)
    {
        if (dictionary is null)
        {
            return null;
        }

        if (dictionary is Wpf.Ui.Markup.ThemesDictionary wpfUiThemes)
        {
            return wpfUiThemes;
        }

        foreach (ResourceDictionary nested in dictionary.MergedDictionaries)
        {
            Wpf.Ui.Markup.ThemesDictionary? match = FindWpfUiThemesDictionary(nested);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool HasWpfUiTheme(Collection<ResourceDictionary> dictionaries)
    {
        return HasDictionary(dictionaries, static dictionary =>
            dictionary is Wpf.Ui.Markup.ThemesDictionary
            || SourceContains(dictionary, "wpf.ui;") && SourceContains(dictionary, "theme"));
    }

    private static bool HasWpfUiControls(Collection<ResourceDictionary> dictionaries)
    {
        return HasDictionary(dictionaries, static dictionary =>
            dictionary is Wpf.Ui.Markup.ControlsDictionary
            || SourceContains(dictionary, "wpf.ui;") && SourceContains(dictionary, "wpf.ui.xaml"));
    }

    private static bool HasDictionary(Collection<ResourceDictionary> dictionaries, Func<ResourceDictionary, bool> match)
    {
        foreach (ResourceDictionary dictionary in dictionaries)
        {
            if (match(dictionary) || HasDictionary(dictionary.MergedDictionaries, match))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SourceContains(ResourceDictionary dictionary, string value)
    {
        string? source = dictionary.Source?.ToString();
        return !string.IsNullOrEmpty(source)
            && source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
