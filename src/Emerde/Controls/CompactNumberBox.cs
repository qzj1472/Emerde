using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Emerde.Controls;

public sealed class CompactNumberBox : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double?),
        typeof(CompactNumberBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(CompactNumberBox),
        new PropertyMetadata(double.MinValue, OnLimitChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(CompactNumberBox),
        new PropertyMetadata(double.MaxValue, OnLimitChanged));

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange),
        typeof(double),
        typeof(CompactNumberBox),
        new PropertyMetadata(1d));

    public static readonly DependencyProperty MaxDecimalPlacesProperty = DependencyProperty.Register(
        nameof(MaxDecimalPlaces),
        typeof(int),
        typeof(CompactNumberBox),
        new PropertyMetadata(0, OnValueChanged));

    private WpfTextBox? textBox;
    private RepeatButton? increaseButton;
    private RepeatButton? decreaseButton;
    private bool isUpdatingText;

    static CompactNumberBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CompactNumberBox), new FrameworkPropertyMetadata(typeof(CompactNumberBox)));
    }

    public double? Value
    {
        get => (double?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public int MaxDecimalPlaces
    {
        get => (int)GetValue(MaxDecimalPlacesProperty);
        set => SetValue(MaxDecimalPlacesProperty, value);
    }

    public override void OnApplyTemplate()
    {
        DetachTemplateEvents();

        base.OnApplyTemplate();

        textBox = GetTemplateChild("PART_TextBox") as WpfTextBox;
        increaseButton = GetTemplateChild("PART_IncreaseButton") as RepeatButton;
        decreaseButton = GetTemplateChild("PART_DecreaseButton") as RepeatButton;

        if (textBox != null)
        {
            textBox.TextChanged += TextBoxTextChanged;
            textBox.LostKeyboardFocus += TextBoxLostKeyboardFocus;
            textBox.PreviewKeyDown += TextBoxPreviewKeyDown;
            textBox.PreviewMouseWheel += TextBoxPreviewMouseWheel;
            textBox.GotKeyboardFocus += TextBoxGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown += TextBoxPreviewMouseLeftButtonDown;
        }

        if (increaseButton != null)
        {
            increaseButton.Click += IncreaseButtonClick;
        }

        if (decreaseButton != null)
        {
            decreaseButton.Click += DecreaseButtonClick;
        }

        UpdateTextFromValue();
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is CompactNumberBox numberBox)
        {
            numberBox.CoerceValueIntoRange();
            numberBox.UpdateTextFromValue();
        }
    }

    private static void OnLimitChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is CompactNumberBox numberBox)
        {
            numberBox.CoerceValueIntoRange();
            numberBox.UpdateTextFromValue();
        }
    }

    private void TextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (isUpdatingText || textBox == null)
        {
            return;
        }

        string text = textBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (TryParse(text, out double parsed))
        {
            SetCurrentValue(ValueProperty, Clamp(parsed));
        }
    }

    private void TextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateTextFromValue(force: true);
    }

    private void TextBoxPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            ChangeValue(SmallChange);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            ChangeValue(-SmallChange);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            UpdateTextFromValue();
        }
    }

    private void TextBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (textBox?.IsKeyboardFocusWithin != true)
        {
            return;
        }

        ChangeValue(e.Delta > 0 ? SmallChange : -SmallChange);
        e.Handled = true;
    }

    private void TextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        textBox?.SelectAll();
    }

    private void TextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (textBox?.IsKeyboardFocusWithin == true)
        {
            return;
        }

        textBox?.Focus();
        textBox?.SelectAll();
        e.Handled = true;
    }

    private void IncreaseButtonClick(object sender, RoutedEventArgs e)
    {
        ChangeValue(SmallChange);
    }

    private void DecreaseButtonClick(object sender, RoutedEventArgs e)
    {
        ChangeValue(-SmallChange);
    }

    private void ChangeValue(double delta)
    {
        double current = Value ?? 0d;
        SetCurrentValue(ValueProperty, Clamp(current + delta));
        UpdateTextFromValue(force: true);
    }

    private void CoerceValueIntoRange()
    {
        if (Value is not double value)
        {
            return;
        }

        double clamped = Clamp(value);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            SetCurrentValue(ValueProperty, clamped);
        }
    }

    private double Clamp(double value)
    {
        double min = Math.Min(Minimum, Maximum);
        double max = Math.Max(Minimum, Maximum);
        return Math.Clamp(value, min, max);
    }

    private void UpdateTextFromValue(bool force = false)
    {
        if (textBox == null || !force && textBox.IsKeyboardFocusWithin && !isUpdatingText)
        {
            return;
        }

        try
        {
            isUpdatingText = true;
            textBox.Text = FormatValue(Value);
        }
        finally
        {
            isUpdatingText = false;
        }
    }

    private string FormatValue(double? value)
    {
        if (value is not double numericValue)
        {
            return string.Empty;
        }

        int decimalPlaces = Math.Max(0, MaxDecimalPlaces);
        if (decimalPlaces == 0)
        {
            return Math.Round(numericValue).ToString("0", CultureInfo.CurrentCulture);
        }

        string format = "0." + new string('#', decimalPlaces);
        return Math.Round(numericValue, decimalPlaces).ToString(format, CultureInfo.CurrentCulture);
    }

    private static bool TryParse(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void DetachTemplateEvents()
    {
        if (textBox != null)
        {
            textBox.TextChanged -= TextBoxTextChanged;
            textBox.LostKeyboardFocus -= TextBoxLostKeyboardFocus;
            textBox.PreviewKeyDown -= TextBoxPreviewKeyDown;
            textBox.PreviewMouseWheel -= TextBoxPreviewMouseWheel;
            textBox.GotKeyboardFocus -= TextBoxGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown -= TextBoxPreviewMouseLeftButtonDown;
        }

        if (increaseButton != null)
        {
            increaseButton.Click -= IncreaseButtonClick;
        }

        if (decreaseButton != null)
        {
            decreaseButton.Click -= DecreaseButtonClick;
        }
    }
}
