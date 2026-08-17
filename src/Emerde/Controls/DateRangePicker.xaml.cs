using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Emerde.Controls;

public partial class DateRangePicker : WpfUserControl
{
    public static readonly DependencyProperty StartDateProperty = DependencyProperty.Register(
        nameof(StartDate),
        typeof(DateTime?),
        typeof(DateRangePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDateRangeChanged));

    public static readonly DependencyProperty EndDateProperty = DependencyProperty.Register(
        nameof(EndDate),
        typeof(DateTime?),
        typeof(DateRangePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDateRangeChanged));

    public static readonly DependencyProperty SeparatorTextProperty = DependencyProperty.Register(
        nameof(SeparatorText),
        typeof(string),
        typeof(DateRangePicker),
        new PropertyMetadata("-", OnDateRangeChanged));

    public static readonly DependencyProperty UseUiXVisualsProperty = DependencyProperty.Register(
        nameof(UseUiXVisuals),
        typeof(bool),
        typeof(DateRangePicker),
        new PropertyMetadata(true));

    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText),
        typeof(string),
        typeof(DateRangePicker),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    private DateTime displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? anchorDate;
    private bool awaitingSecondPoint;

    public DateRangePicker()
    {
        InitializeComponent();
        BuildWeekdayHeaders();
        UpdateDisplayText();
        BuildMonths();
    }

    public DateTime? StartDate
    {
        get => (DateTime?)GetValue(StartDateProperty);
        set => SetValue(StartDateProperty, value);
    }

    public DateTime? EndDate
    {
        get => (DateTime?)GetValue(EndDateProperty);
        set => SetValue(EndDateProperty, value);
    }

    public string SeparatorText
    {
        get => (string)GetValue(SeparatorTextProperty);
        set => SetValue(SeparatorTextProperty, value);
    }

    public bool UseUiXVisuals
    {
        get => (bool)GetValue(UseUiXVisualsProperty);
        set => SetValue(UseUiXVisualsProperty, value);
    }

    public string DisplayText => (string)GetValue(DisplayTextProperty);

    public ObservableCollection<string> WeekdayHeaders { get; } = [];

    public ObservableCollection<DateRangeDay> MonthDays { get; } = [];

    internal static DateRangeSelectionResult SelectPoint(DateTime? anchorDate, bool awaitingSecondPoint, DateTime point)
    {
        DateTime normalizedPoint = point.Date;
        if (!awaitingSecondPoint || !anchorDate.HasValue)
        {
            return new DateRangeSelectionResult(normalizedPoint, normalizedPoint, normalizedPoint, true, false);
        }

        DateTime normalizedAnchor = anchorDate.Value.Date;
        if (normalizedAnchor == normalizedPoint)
        {
            return new DateRangeSelectionResult(null, null, null, false, false);
        }

        DateTime start = normalizedAnchor < normalizedPoint ? normalizedAnchor : normalizedPoint;
        DateTime end = normalizedAnchor > normalizedPoint ? normalizedAnchor : normalizedPoint;
        return new DateRangeSelectionResult(start, end, null, false, true);
    }

    private static void OnDateRangeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DateRangePicker picker)
        {
            return;
        }

        picker.NormalizeDateValue(e.Property);
        picker.UpdateDisplayText();
        picker.BuildMonths();
    }

    private void NormalizeDateValue(DependencyProperty property)
    {
        DateTime? value = property == StartDateProperty ? StartDate : EndDate;
        if (!value.HasValue || value.Value.TimeOfDay == TimeSpan.Zero)
        {
            return;
        }

        SetCurrentValue(property, value.Value.Date);
    }

    private void RangePopupOpened(object? sender, EventArgs e)
    {
        if (StartDate.HasValue && EndDate.HasValue && StartDate.Value.Date == EndDate.Value.Date)
        {
            anchorDate = StartDate.Value.Date;
            awaitingSecondPoint = true;
        }
        else
        {
            anchorDate = null;
            awaitingSecondPoint = false;
        }

        DateTime selected = StartDate?.Date ?? EndDate?.Date ?? DateTime.Today;
        displayMonth = new DateTime(selected.Year, selected.Month, 1);
        BuildMonths();
    }

    private void RangeDayClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: DateTime point })
        {
            return;
        }

        DateRangeSelectionResult result = SelectPoint(anchorDate, awaitingSecondPoint, point);
        anchorDate = result.AnchorDate;
        awaitingSecondPoint = result.AwaitingSecondPoint;
        SetCurrentValue(StartDateProperty, result.StartDate);
        SetCurrentValue(EndDateProperty, result.EndDate);
        BuildMonths();
    }

    private void PreviousMonthClick(object sender, RoutedEventArgs e)
    {
        displayMonth = displayMonth.AddMonths(-1);
        BuildMonths();
    }

    private void NextMonthClick(object sender, RoutedEventArgs e)
    {
        displayMonth = displayMonth.AddMonths(1);
        BuildMonths();
    }

    private void RangePanelPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        RangeToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        RangeToggle.Focus();
        e.Handled = true;
    }

    private void BuildWeekdayHeaders()
    {
        WeekdayHeaders.Clear();
        string[] names = CultureInfo.CurrentUICulture.DateTimeFormat.ShortestDayNames;
        foreach (string name in names)
        {
            WeekdayHeaders.Add(name);
        }
    }

    private void BuildMonths()
    {
        if (MonthTitle == null)
        {
            return;
        }

        CultureInfo culture = CultureInfo.CurrentUICulture;
        MonthTitle.Text = displayMonth.ToString("Y", culture);
        PopulateMonth(MonthDays, displayMonth);
    }

    private void PopulateMonth(ObservableCollection<DateRangeDay> target, DateTime month)
    {
        target.Clear();
        DateTime first = new(month.Year, month.Month, 1);
        DateTime cursor = first.AddDays(-(int)first.DayOfWeek);
        DateTime? start = StartDate?.Date;
        DateTime? end = EndDate?.Date;
        if (start.HasValue && end.HasValue && start > end)
        {
            (start, end) = (end, start);
        }

        for (int index = 0; index < 42; index++)
        {
            DateTime date = cursor.AddDays(index);
            target.Add(new DateRangeDay(
                date,
                date.Day.ToString(CultureInfo.CurrentUICulture),
                date.Month == month.Month && date.Year == month.Year,
                date == DateTime.Today,
                GetPosition(date, start, end)));
        }
    }

    private void UpdateDisplayText()
    {
        string text = string.Empty;
        if (StartDate.HasValue && EndDate.HasValue)
        {
            string start = FormatDate(StartDate.Value.Date);
            string end = FormatDate(EndDate.Value.Date);
            text = StartDate.Value.Date == EndDate.Value.Date
                ? start
                : $"{start} {SeparatorText} {end}";
        }
        else if (StartDate.HasValue || EndDate.HasValue)
        {
            text = FormatDate((StartDate ?? EndDate)!.Value.Date);
        }

        SetValue(DisplayTextPropertyKey, text);
    }

    private static string FormatDate(DateTime date)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        return date.ToString(culture.DateTimeFormat.ShortDatePattern, culture);
    }

    private static DateRangePosition GetPosition(DateTime date, DateTime? start, DateTime? end)
    {
        if (!start.HasValue || !end.HasValue || date < start || date > end)
        {
            return DateRangePosition.None;
        }

        if (start == end)
        {
            return DateRangePosition.Single;
        }

        if (date == start)
        {
            return DateRangePosition.Start;
        }

        return date == end ? DateRangePosition.End : DateRangePosition.Middle;
    }
}

public sealed record DateRangeDay(
    DateTime Date,
    string DayNumber,
    bool IsCurrentMonth,
    bool IsToday,
    DateRangePosition Position);

public enum DateRangePosition
{
    None,
    Single,
    Start,
    Middle,
    End
}

internal readonly record struct DateRangeSelectionResult(
    DateTime? StartDate,
    DateTime? EndDate,
    DateTime? AnchorDate,
    bool AwaitingSecondPoint,
    bool IsComplete);
