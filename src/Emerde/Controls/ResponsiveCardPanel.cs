using System.Windows;
using System.Windows.Controls;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSize = System.Windows.Size;

namespace Emerde.Controls;

public sealed class ResponsiveCardPanel : WpfPanel
{
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns),
        typeof(int),
        typeof(ResponsiveCardPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        value => (int)value > 0);

    public static readonly DependencyProperty TwoColumnMinWidthProperty = DependencyProperty.Register(
        nameof(TwoColumnMinWidth),
        typeof(double),
        typeof(ResponsiveCardPanel),
        new FrameworkPropertyMetadata(760d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsNonNegativeFiniteDouble);

    public static readonly DependencyProperty FourColumnMinWidthProperty = DependencyProperty.Register(
        nameof(FourColumnMinWidth),
        typeof(double),
        typeof(ResponsiveCardPanel),
        new FrameworkPropertyMetadata(960d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsNonNegativeFiniteDouble);

    public static readonly DependencyProperty ColumnHysteresisProperty = DependencyProperty.Register(
        nameof(ColumnHysteresis),
        typeof(double),
        typeof(ResponsiveCardPanel),
        new FrameworkPropertyMetadata(24d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsNonNegativeFiniteDouble);

    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(ResponsiveCardPanel),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsNonNegativeFiniteDouble);

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(ResponsiveCardPanel),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsNonNegativeFiniteDouble);

    private int currentColumns;
    private int measuredColumns = 1;
    private double[] measuredRowHeights = [];
    private int measuredRowCount;
    private double measuredTotalHeight;
    private readonly Dictionary<UIElement, WpfSize> measuredChildSizes = [];
    private ScrollViewer? scrollOwner;
    private bool hasMeasuredContent;
    private bool isMeasureDeferred;

    public ResponsiveCardPanel()
    {
        Loaded += ResponsiveCardPanelLoaded;
        Unloaded += ResponsiveCardPanelUnloaded;
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double TwoColumnMinWidth
    {
        get => (double)GetValue(TwoColumnMinWidthProperty);
        set => SetValue(TwoColumnMinWidthProperty, value);
    }

    public double FourColumnMinWidth
    {
        get => (double)GetValue(FourColumnMinWidthProperty);
        set => SetValue(FourColumnMinWidthProperty, value);
    }

    public double ColumnHysteresis
    {
        get => (double)GetValue(ColumnHysteresisProperty);
        set => SetValue(ColumnHysteresisProperty, value);
    }

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        double availableWidth = double.IsFinite(availableSize.Width)
            ? Math.Max(0d, availableSize.Width)
            : 0d;
        EnsureScrollOwner();
        bool hasInvalidChild = InternalChildren
            .OfType<UIElement>()
            .Any(child => !child.IsMeasureValid);
        bool hasChangedChildSize = InternalChildren
            .OfType<UIElement>()
            .Any(child => !measuredChildSizes.TryGetValue(child, out WpfSize measuredSize)
                || !AreClose(measuredSize.Width, child.DesiredSize.Width)
                || !AreClose(measuredSize.Height, child.DesiredSize.Height));
        if (hasMeasuredContent
            && !hasInvalidChild
            && !hasChangedChildSize
            && !ResizeOptimizedStackPanel.IsNearViewport(this, scrollOwner))
        {
            isMeasureDeferred = true;
            return new WpfSize(availableWidth, Math.Max(0d, measuredTotalHeight));
        }

        isMeasureDeferred = false;
        measuredColumns = ResolveColumns(
            availableWidth,
            currentColumns,
            MaxColumns,
            TwoColumnMinWidth,
            FourColumnMinWidth,
            ColumnHysteresis);
        currentColumns = measuredColumns;

        double itemWidth = CalculateItemWidth(availableWidth, measuredColumns, HorizontalSpacing);
        measuredRowCount = InternalChildren.Count == 0
            ? 0
            : (InternalChildren.Count + measuredColumns - 1) / measuredColumns;
        if (measuredRowHeights.Length < measuredRowCount)
        {
            Array.Resize(ref measuredRowHeights, measuredRowCount);
        }
        Array.Clear(measuredRowHeights, 0, measuredRowCount);

        for (int index = 0; index < InternalChildren.Count; index++)
        {
            UIElement child = InternalChildren[index];
            child.Measure(new WpfSize(itemWidth, double.PositiveInfinity));
            measuredChildSizes[child] = child.DesiredSize;
            int row = index / measuredColumns;
            measuredRowHeights[row] = Math.Max(measuredRowHeights[row], child.DesiredSize.Height);
        }

        measuredTotalHeight = 0d;
        for (int row = 0; row < measuredRowCount; row++)
        {
            measuredTotalHeight += measuredRowHeights[row];
        }
        if (measuredRowCount > 1)
        {
            measuredTotalHeight += VerticalSpacing * (measuredRowCount - 1);
        }
        hasMeasuredContent = true;

        return new WpfSize(availableWidth, Math.Max(0d, measuredTotalHeight));
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        double width = double.IsFinite(finalSize.Width) ? Math.Max(0d, finalSize.Width) : 0d;
        if (isMeasureDeferred)
        {
            return new WpfSize(width, Math.Max(0d, measuredTotalHeight));
        }
        int columns = Math.Max(1, measuredColumns);
        double itemWidth = CalculateItemWidth(width, columns, HorizontalSpacing);
        double y = 0d;

        for (int index = 0; index < InternalChildren.Count; index++)
        {
            int row = index / columns;
            int column = index % columns;
            if (column == 0 && row > 0)
            {
                y += measuredRowHeights[row - 1] + VerticalSpacing;
            }

            double x = column * (itemWidth + HorizontalSpacing);
            double rowHeight = row < measuredRowCount ? measuredRowHeights[row] : 0d;
            InternalChildren[index].Arrange(new Rect(
                Math.Max(0d, x),
                Math.Max(0d, y),
                Math.Max(0d, itemWidth),
                Math.Max(0d, rowHeight)));
        }

        return new WpfSize(width, Math.Max(0d, measuredTotalHeight));
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        hasMeasuredContent = false;
        isMeasureDeferred = false;
        if (visualRemoved is UIElement removed)
        {
            measuredChildSizes.Remove(removed);
        }
    }

    internal static int ResolveColumns(
        double availableWidth,
        int currentColumns,
        int maxColumns,
        double twoColumnMinWidth,
        double fourColumnMinWidth,
        double hysteresis)
    {
        int maximum = Math.Max(1, maxColumns);
        if (maximum == 1)
        {
            return 1;
        }

        double width = double.IsFinite(availableWidth) ? Math.Max(0d, availableWidth) : 0d;
        double twoEnter = Math.Max(0d, twoColumnMinWidth);
        double twoExit = Math.Max(0d, twoEnter - Math.Max(0d, hysteresis));
        double fourEnter = Math.Max(twoEnter, fourColumnMinWidth);
        double fourExit = Math.Max(twoExit, fourEnter - Math.Max(0d, hysteresis));

        if (maximum >= 4 && currentColumns >= 4)
        {
            if (width >= fourExit)
            {
                return 4;
            }
            currentColumns = 2;
        }

        if (currentColumns >= 2)
        {
            if (maximum >= 4 && width >= fourEnter)
            {
                return 4;
            }
            return width >= twoExit ? 2 : 1;
        }

        if (maximum >= 4 && width >= fourEnter)
        {
            return 4;
        }
        return width >= twoEnter ? 2 : 1;
    }

    internal static double CalculateItemWidth(double availableWidth, int columns, double spacing)
    {
        int safeColumns = Math.Max(1, columns);
        double width = double.IsFinite(availableWidth) ? Math.Max(0d, availableWidth) : 0d;
        double gap = double.IsFinite(spacing) ? Math.Max(0d, spacing) : 0d;
        return Math.Max(0d, (width - gap * (safeColumns - 1)) / safeColumns);
    }

    private static bool IsNonNegativeFiniteDouble(object value)
    {
        double number = (double)value;
        return double.IsFinite(number) && number >= 0d;
    }

    private static bool AreClose(double left, double right)
    {
        return left.Equals(right)
            || double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= 0.1d;
    }

    private void ResponsiveCardPanelLoaded(object sender, RoutedEventArgs e)
    {
        EnsureScrollOwner();
    }

    private void ResponsiveCardPanelUnloaded(object sender, RoutedEventArgs e)
    {
        SetScrollOwner(null);
    }

    private void EnsureScrollOwner()
    {
        SetScrollOwner(ResizeOptimizedStackPanel.FindScrollOwner(this));
    }

    private void SetScrollOwner(ScrollViewer? owner)
    {
        if (ReferenceEquals(scrollOwner, owner))
        {
            return;
        }
        if (scrollOwner != null)
        {
            scrollOwner.ScrollChanged -= ScrollOwnerScrollChanged;
        }
        scrollOwner = owner;
        if (scrollOwner != null)
        {
            scrollOwner.ScrollChanged += ScrollOwnerScrollChanged;
        }
    }

    private void ScrollOwnerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (isMeasureDeferred && ResizeOptimizedStackPanel.IsNearViewport(this, scrollOwner))
        {
            InvalidateMeasure();
        }
    }

}
