using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Violeta.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Emerde.Core;

internal static class ContentDialogFocusService
{
    private const string PrimaryButtonName = "PrimaryButton";
    private const string SecondaryButtonName = "SecondaryButton";
    private const string CloseButtonName = "CloseButton";

    public static void Attach(ContentDialog dialog)
    {
        RoutedEventHandler loadedHandler = null!;
        loadedHandler = (_, _) =>
        {
            dialog.Loaded -= loadedHandler;
            dialog.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Configure(dialog));
        };
        dialog.Loaded += loadedHandler;
    }

    private static void Configure(ContentDialog dialog)
    {
        dialog.ApplyTemplate();
        List<WpfButton> buttons = GetActionButtons(dialog);
        if (buttons.Count == 0)
        {
            return;
        }

        foreach (WpfButton button in buttons)
        {
            button.FocusVisualStyle = Application.Current?.TryFindResource("EmerdeFocusVisualStyle") as Style;
            button.GotKeyboardFocus += ActionButtonGotKeyboardFocus;
            button.LostKeyboardFocus += ActionButtonLostKeyboardFocus;
        }

        dialog.PreviewKeyDown += DialogPreviewKeyDown;
        WpfButton? defaultButton = GetDefaultButton(dialog, buttons);
        (defaultButton ?? buttons[0]).Focus();
        UpdateButtonEmphasis(buttons, Keyboard.FocusedElement as WpfButton);
    }

    private static void ActionButtonGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is WpfButton button && FindDialog(button) is ContentDialog dialog)
        {
            UpdateButtonEmphasis(GetActionButtons(dialog), button);
        }
    }

    private static void ActionButtonLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is WpfButton button && FindDialog(button) is ContentDialog dialog)
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.BorderBrushProperty);
            button.ClearValue(Control.BorderThicknessProperty);
            button.ClearValue(Control.ForegroundProperty);
            UpdateButtonEmphasis(GetActionButtons(dialog), Keyboard.FocusedElement as WpfButton);
        }
    }

    private static void DialogPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not ContentDialog dialog || e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        List<WpfButton> buttons = GetActionButtons(dialog);
        WpfButton? focusedButton = Keyboard.FocusedElement as WpfButton;
        int currentIndex = focusedButton == null ? -1 : buttons.IndexOf(focusedButton);
        if (currentIndex < 0 || buttons.Count < 2)
        {
            return;
        }

        int nextIndex = e.Key == Key.Right
            ? (currentIndex + 1) % buttons.Count
            : (currentIndex - 1 + buttons.Count) % buttons.Count;
        buttons[nextIndex].Focus();
        e.Handled = true;
    }

    private static void UpdateButtonEmphasis(IReadOnlyList<WpfButton> buttons, WpfButton? focusedButton)
    {
        foreach (WpfButton button in buttons)
        {
            if (!button.IsEnabled)
            {
                continue;
            }

            if (ReferenceEquals(button, focusedButton))
            {
                button.SetResourceReference(Control.BackgroundProperty, "EmerdeBrandFillBrush");
                button.SetResourceReference(Control.BorderBrushProperty, "EmerdeBrandFillBrush");
                button.BorderThickness = new Thickness(1);
                button.SetResourceReference(Control.ForegroundProperty, "UiXTextOnAccentBrush");
            }
            else
            {
                button.SetResourceReference(Control.BackgroundProperty, "ControlFillColorDefaultBrush");
                button.SetResourceReference(Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                button.BorderThickness = new Thickness(1);
                button.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            }
        }
    }

    private static WpfButton? GetDefaultButton(ContentDialog dialog, IReadOnlyList<WpfButton> buttons)
    {
        string name = dialog.DefaultButton switch
        {
            ContentDialogButton.Primary => PrimaryButtonName,
            ContentDialogButton.Secondary => SecondaryButtonName,
            ContentDialogButton.Close => CloseButtonName,
            _ => string.Empty,
        };
        return buttons.FirstOrDefault(button => string.Equals(button.Name, name, StringComparison.Ordinal));
    }

    private static List<WpfButton> GetActionButtons(ContentDialog dialog)
    {
        WpfButton[] candidates = [
            FindButton(dialog, PrimaryButtonName),
            FindButton(dialog, SecondaryButtonName),
            FindButton(dialog, CloseButtonName),
        ];
        return candidates
        .Where(button => button is { Visibility: Visibility.Visible, IsEnabled: true })
        .OrderBy(GetHorizontalPosition)
        .ToList();
    }

    private static WpfButton FindButton(ContentDialog dialog, string name)
    {
        if (dialog.Template?.FindName(name, dialog) is WpfButton button)
        {
            return button;
        }

        return FindVisualDescendant<WpfButton>(dialog, candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))!;
    }

    private static double GetHorizontalPosition(WpfButton button)
    {
        try
        {
            return button.TranslatePoint(new System.Windows.Point(0, 0), FindDialog(button)!).X;
        }
        catch
        {
            return 0d;
        }
    }

    private static ContentDialog? FindDialog(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is ContentDialog dialog)
            {
                return dialog;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed && predicate(typed))
            {
                return typed;
            }

            if (FindVisualDescendant(child, predicate) is T descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
