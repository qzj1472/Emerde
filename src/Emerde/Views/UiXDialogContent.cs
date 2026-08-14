using Emerde.ViewModels;
using Emerde.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Violeta.Controls;
using WpfFontIcon = Wpf.Ui.Controls.FontIcon;

namespace Emerde.Views;

internal enum UiXDialogTone
{
    Neutral,
    Information,
    Warning,
    Danger,
}

internal static class UiXDialogContent
{
    internal static bool IsEnabled => Application.Current?.MainWindow?.DataContext is MainViewModel { StatusOfIsUiXEnabled: true };

    internal static async Task<bool> ConfirmAsync(
        Window? owner,
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText,
        string glyph,
        UiXDialogTone tone = UiXDialogTone.Warning)
    {
        ContentDialog dialog = new()
        {
            Title = title,
            Content = CreateMessage(message, glyph, tone),
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Close,
            FocusVisualStyle = null,
            Style = Application.Current?.TryFindResource("EmerdeContentDialogStyle") as Style,
        };
        using DialogBlurScope blurScope = DialogBlurScope.ForLightDismiss(owner, dialog);
        return await WindowSizing.ShowContentDialogAsync(dialog, owner) == ContentDialogResult.Primary;
    }

    internal static FrameworkElement CreateMessage(
        string message,
        string glyph,
        UiXDialogTone tone = UiXDialogTone.Information,
        string? detail = null,
        double minimumWidth = 420d)
    {
        Grid grid = new()
        {
            MinWidth = minimumWidth,
            Margin = new Thickness(0, 4, 0, 2),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

        WpfFontIcon icon = CreateFontIcon(glyph, 20d);
        icon.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        Border iconSurface = new()
        {
            Width = 44d,
            Height = 44d,
            Margin = new Thickness(0, 0, 16, 0),
            CornerRadius = new CornerRadius(8d),
            Child = icon,
        };
        ApplyTone(iconSurface, tone);
        grid.Children.Add(iconSurface);

        StackPanel text = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock messageText = new()
        {
            Text = message,
            FontSize = 14d,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 20d,
            TextWrapping = TextWrapping.Wrap,
        };
        messageText.SetResourceReference(TextBlock.ForegroundProperty, "UiXTextPrimaryBrush");
        text.Children.Add(messageText);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            TextBlock detailText = new()
            {
                Text = detail,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 12d,
                LineHeight = 18d,
                TextWrapping = TextWrapping.Wrap,
            };
            detailText.SetResourceReference(TextBlock.ForegroundProperty, "UiXTextSecondaryBrush");
            text.Children.Add(detailText);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    internal static FrameworkElement CreateForm(
        string label,
        FrameworkElement control,
        string glyph,
        string? detail = null,
        double minimumWidth = 440d)
    {
        StackPanel panel = new()
        {
            MinWidth = minimumWidth,
            Margin = new Thickness(0, 4, 0, 2),
        };
        panel.Children.Add(CreateMessage(label, glyph, UiXDialogTone.Neutral, detail, 0d));
        Border controlSurface = new()
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8d),
            Child = control,
        };
        controlSurface.SetResourceReference(Border.BackgroundProperty, "UiXDialogSectionBrush");
        panel.Children.Add(controlSurface);
        return panel;
    }

    internal static Border CreateSection(string title, IEnumerable<UIElement> content, string? glyph = null)
    {
        StackPanel body = new();
        Grid heading = new() { Margin = new Thickness(0, 0, 0, 12) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        if (!string.IsNullOrWhiteSpace(glyph))
        {
            WpfFontIcon icon = CreateFontIcon(glyph, 16d);
            icon.Margin = new Thickness(0, 0, 9, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.SetResourceReference(Control.ForegroundProperty, "UiXTextSecondaryBrush");
            heading.Children.Add(icon);
        }
        TextBlock titleText = new()
        {
            Text = title,
            FontSize = 14d,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "UiXTextPrimaryBrush");
        Grid.SetColumn(titleText, 1);
        heading.Children.Add(titleText);
        body.Children.Add(heading);
        foreach (UIElement element in content)
        {
            body.Children.Add(element);
        }

        Border section = new()
        {
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(8d),
            Child = body,
        };
        section.SetResourceReference(Border.BackgroundProperty, "UiXDialogSectionBrush");
        return section;
    }

    internal static FrameworkElement CreateTaskSurface(FrameworkElement content, double minimumWidth = 420d)
    {
        content.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        Border surface = new()
        {
            MinWidth = minimumWidth,
            Margin = new Thickness(0, 4, 0, 2),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8d),
            Child = content,
        };
        surface.SetResourceReference(Border.BackgroundProperty, "UiXDialogSectionBrush");
        return surface;
    }

    internal static Grid CreateValueRow(string label, FrameworkElement value, bool isLast = false)
    {
        Grid row = new()
        {
            Margin = new Thickness(0, 0, 0, isLast ? 0 : 9),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126d) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        TextBlock labelText = new()
        {
            Text = label,
            FontSize = 12d,
            VerticalAlignment = VerticalAlignment.Top,
            TextWrapping = TextWrapping.Wrap,
        };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "UiXTextSecondaryBrush");
        row.Children.Add(labelText);
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }

    private static void ApplyTone(Border border, UiXDialogTone tone)
    {
        string fill = tone switch
        {
            UiXDialogTone.Warning => "UiXDialogWarningBrush",
            UiXDialogTone.Danger => "UiXDialogDangerBrush",
            UiXDialogTone.Information => "UiXDialogSelectionBrush",
            _ => "UiXDialogSectionBrush",
        };
        string foreground = tone == UiXDialogTone.Danger ? "UiXDangerForegroundBrush" : "UiXTextPrimaryBrush";
        border.SetResourceReference(Border.BackgroundProperty, fill);
        if (border.Child is Control control)
        {
            control.SetResourceReference(Control.ForegroundProperty, foreground);
        }
    }

    private static WpfFontIcon CreateFontIcon(string glyph, double fontSize)
    {
        return new WpfFontIcon
        {
            Glyph = glyph,
            FontFamily = Application.Current?.TryFindResource("SymbolThemeFontFamily") as System.Windows.Media.FontFamily
                ?? new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
            FontSize = fontSize,
        };
    }
}
