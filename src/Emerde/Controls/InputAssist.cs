using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Emerde.Controls;

public static class InputAssist
{
    public static readonly DependencyProperty CommitOnEnterProperty = DependencyProperty.RegisterAttached(
        "CommitOnEnter",
        typeof(bool),
        typeof(InputAssist),
        new PropertyMetadata(false, OnEnterBehaviorChanged));

    public static readonly DependencyProperty EnterCommandProperty = DependencyProperty.RegisterAttached(
        "EnterCommand",
        typeof(ICommand),
        typeof(InputAssist),
        new PropertyMetadata(null, OnEnterBehaviorChanged));

    public static readonly DependencyProperty SelectAllOnVisibleProperty = DependencyProperty.RegisterAttached(
        "SelectAllOnVisible",
        typeof(bool),
        typeof(InputAssist),
        new PropertyMetadata(false, OnSelectAllOnVisibleChanged));

    public static void SetCommitOnEnter(DependencyObject element, bool value) => element.SetValue(CommitOnEnterProperty, value);

    public static bool GetCommitOnEnter(DependencyObject element) => (bool)element.GetValue(CommitOnEnterProperty);

    public static void SetEnterCommand(DependencyObject element, ICommand? value) => element.SetValue(EnterCommandProperty, value);

    public static ICommand? GetEnterCommand(DependencyObject element) => (ICommand?)element.GetValue(EnterCommandProperty);

    public static void SetSelectAllOnVisible(DependencyObject element, bool value) => element.SetValue(SelectAllOnVisibleProperty, value);

    public static bool GetSelectAllOnVisible(DependencyObject element) => (bool)element.GetValue(SelectAllOnVisibleProperty);

    private static void OnEnterBehaviorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        element.KeyDown -= InputKeyDown;
        if (GetCommitOnEnter(element) || GetEnterCommand(element) != null)
        {
            element.KeyDown += InputKeyDown;
        }
    }

    private static void InputKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || sender is not FrameworkElement element)
        {
            return;
        }

        ICommand? command = GetEnterCommand(element);
        if (!ShouldProcessEnter(element, command))
        {
            return;
        }

        UpdateBindingSources(element);

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
            return;
        }

        if (GetCommitOnEnter(element))
        {
            Keyboard.ClearFocus();
        }
    }

    internal static bool ShouldProcessEnter(FrameworkElement element, ICommand? command)
    {
        return command != null || element is not WpfTextBox { AcceptsReturn: true };
    }

    internal static void UpdateBindingSources(DependencyObject element)
    {
        LocalValueEnumerator values = element.GetLocalValueEnumerator();
        while (values.MoveNext())
        {
            if (values.Current.Value is BindingExpression bindingExpression)
            {
                bindingExpression.UpdateSource();
            }
        }
    }

    private static void OnSelectAllOnVisibleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not WpfTextBox textBox)
        {
            return;
        }

        textBox.IsVisibleChanged -= TextBoxIsVisibleChanged;
        if (e.NewValue is true)
        {
            textBox.IsVisibleChanged += TextBoxIsVisibleChanged;
            SelectAllWhenVisible(textBox);
        }
    }

    private static void TextBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is WpfTextBox textBox)
        {
            SelectAllWhenVisible(textBox);
        }
    }

    private static void SelectAllWhenVisible(WpfTextBox textBox)
    {
        if (!textBox.IsVisible)
        {
            return;
        }

        IInputElement? focusedElement = Keyboard.FocusedElement;
        _ = textBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!textBox.IsVisible || !textBox.IsEnabled)
                {
                    return;
                }

                if (!textBox.IsKeyboardFocusWithin && !ReferenceEquals(Keyboard.FocusedElement, focusedElement))
                {
                    return;
                }

                if (textBox.IsKeyboardFocusWithin || textBox.Focus())
                {
                    textBox.SelectAll();
                }
            }));
    }
}
