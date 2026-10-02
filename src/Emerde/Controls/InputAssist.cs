using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfPasswordBox = System.Windows.Controls.PasswordBox;
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

    public static readonly DependencyProperty KeyboardOpenOnEnterProperty = DependencyProperty.RegisterAttached(
        "KeyboardOpenOnEnter",
        typeof(bool),
        typeof(InputAssist),
        new PropertyMetadata(false, OnEnterBehaviorChanged));

    public static readonly DependencyProperty SelectAllOnVisibleProperty = DependencyProperty.RegisterAttached(
        "SelectAllOnVisible",
        typeof(bool),
        typeof(InputAssist),
        new PropertyMetadata(false, OnSelectAllOnVisibleChanged));

    public static readonly DependencyProperty CommitFocusedOnPointerDownProperty = DependencyProperty.RegisterAttached(
        "CommitFocusedOnPointerDown",
        typeof(bool),
        typeof(InputAssist),
        new PropertyMetadata(false, OnCommitFocusedOnPointerDownChanged));

    private static readonly DependencyProperty SuppressNextEnterProperty = DependencyProperty.RegisterAttached(
        "SuppressNextEnter",
        typeof(bool),
        typeof(InputAssist),
        new PropertyMetadata(false));

    public static void SetCommitOnEnter(DependencyObject element, bool value) => element.SetValue(CommitOnEnterProperty, value);

    public static bool GetCommitOnEnter(DependencyObject element) => (bool)element.GetValue(CommitOnEnterProperty);

    public static void SetEnterCommand(DependencyObject element, ICommand? value) => element.SetValue(EnterCommandProperty, value);

    public static ICommand? GetEnterCommand(DependencyObject element) => (ICommand?)element.GetValue(EnterCommandProperty);

    public static void SetKeyboardOpenOnEnter(DependencyObject element, bool value) => element.SetValue(KeyboardOpenOnEnterProperty, value);

    public static bool GetKeyboardOpenOnEnter(DependencyObject element) => (bool)element.GetValue(KeyboardOpenOnEnterProperty);

    public static void SetSelectAllOnVisible(DependencyObject element, bool value) => element.SetValue(SelectAllOnVisibleProperty, value);

    public static bool GetSelectAllOnVisible(DependencyObject element) => (bool)element.GetValue(SelectAllOnVisibleProperty);

    public static void SetCommitFocusedOnPointerDown(DependencyObject element, bool value) => element.SetValue(CommitFocusedOnPointerDownProperty, value);

    public static bool GetCommitFocusedOnPointerDown(DependencyObject element) => (bool)element.GetValue(CommitFocusedOnPointerDownProperty);

    private static void OnEnterBehaviorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        element.KeyDown -= InputKeyDown;
        if (element is WpfComboBox comboBox)
        {
            comboBox.PreviewKeyDown -= ComboBoxPreviewKeyDown;
            comboBox.DropDownClosed -= ComboBoxDropDownClosed;
        }

        if (GetCommitOnEnter(element) || GetEnterCommand(element) != null || GetKeyboardOpenOnEnter(element))
        {
            element.KeyDown += InputKeyDown;
            if (element is WpfComboBox selection)
            {
                selection.PreviewKeyDown += ComboBoxPreviewKeyDown;
                selection.DropDownClosed += ComboBoxDropDownClosed;
            }
        }
    }

    private static void ComboBoxPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not WpfComboBox comboBox
            || !GetKeyboardOpenOnEnter(comboBox)
            || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (comboBox.IsDropDownOpen)
        {
            return;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        comboBox.IsDropDownOpen = true;
        comboBox.SetValue(SuppressNextEnterProperty, true);
        e.Handled = true;
        _ = comboBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                comboBox.ClearValue(SuppressNextEnterProperty);
                FocusSelectedComboBoxItem(comboBox);
            }));
    }

    private static void FocusSelectedComboBoxItem(WpfComboBox comboBox)
    {
        if (!comboBox.IsDropDownOpen)
        {
            return;
        }

        if (comboBox.SelectedIndex >= 0
            && comboBox.ItemContainerGenerator.ContainerFromIndex(comboBox.SelectedIndex) is WpfComboBoxItem item
            && item.IsVisible)
        {
            item.Focus();
            return;
        }

        comboBox.Focus();
    }

    private static void InputKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || sender is not FrameworkElement element)
        {
            return;
        }

        if (element is WpfComboBox openedComboBox && (bool)openedComboBox.GetValue(SuppressNextEnterProperty))
        {
            openedComboBox.ClearValue(SuppressNextEnterProperty);
            e.Handled = true;
            return;
        }

        ICommand? command = GetEnterCommand(element);
        if (!ShouldProcessEnter(element, command))
        {
            return;
        }

        if (element is WpfComboBox comboBox && comboBox.IsDropDownOpen)
        {
            comboBox.IsDropDownOpen = false;
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
            e.Handled = true;
        }
    }

    private static void ComboBoxDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not WpfComboBox comboBox)
        {
            return;
        }

        UpdateBindingSources(comboBox);
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

    private static void OnCommitFocusedOnPointerDownChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not UIElement element)
        {
            return;
        }

        element.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(CommitFocusedEditorOnPointerDown));
        if (e.NewValue is true)
        {
            element.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(CommitFocusedEditorOnPointerDown), true);
        }
    }

    private static void CommitFocusedEditorOnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject root
            || e.OriginalSource is not DependencyObject clickedSource
            || Keyboard.FocusedElement is not DependencyObject focusedElement)
        {
            return;
        }

        DependencyObject? focusedEditor = FindEditorAncestor(focusedElement, root);
        if (focusedEditor == null || ReferenceEquals(focusedEditor, FindEditorAncestor(clickedSource, root)))
        {
            return;
        }

        if (focusedEditor is WpfComboBox { IsDropDownOpen: true })
        {
            return;
        }

        UpdateBindingSources(focusedEditor);
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(root, null);
    }

    private static DependencyObject? FindEditorAncestor(DependencyObject source, DependencyObject root)
    {
        for (DependencyObject? current = source; current != null; current = GetParent(current))
        {
            if (current is WpfTextBox
                or WpfPasswordBox
                or WpfComboBox
                or CompactNumberBox
                or Wpf.Ui.Controls.TextBox
                or Wpf.Ui.Controls.PasswordBox
                or Wpf.Ui.Controls.NumberBox)
            {
                return current;
            }

            if (ReferenceEquals(current, root))
            {
                break;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
        {
            DependencyObject? visualParent = System.Windows.Media.VisualTreeHelper.GetParent(element);
            if (visualParent != null)
            {
                return visualParent;
            }
        }

        return LogicalTreeHelper.GetParent(element);
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
