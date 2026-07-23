using System.Windows;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSize = System.Windows.Size;

namespace Emerde.Controls;

public sealed class AdaptiveSplitPanel : WpfPanel
{
    private readonly WpfSize[] preferredSizes = new WpfSize[3];
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(AdaptiveSplitPanel),
        new FrameworkPropertyMetadata(32d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(AdaptiveSplitPanel),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

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
        for (int index = 0; index < InternalChildren.Count; index++)
        {
            InternalChildren[index].Measure(index < 3
                ? new WpfSize(double.PositiveInfinity, double.PositiveInfinity)
                : WpfSize.Empty);
            if (index < preferredSizes.Length)
            {
                preferredSizes[index] = InternalChildren[index].DesiredSize;
            }
        }

        WpfSize firstSize = GetPreferredSize(0);
        WpfSize secondSize = GetPreferredSize(1);
        WpfSize thirdSize = GetPreferredSize(2);
        double availableWidth = double.IsInfinity(availableSize.Width)
            ? firstSize.Width + secondSize.Width + thirdSize.Width + Math.Max(0, HorizontalSpacing) * 2
            : Math.Max(0, availableSize.Width);
        AdaptiveSplitLayout layout = CalculateLayout(availableWidth, firstSize, secondSize, thirdSize, HorizontalSpacing, VerticalSpacing);
        RemeasureChild(0, layout.First.Width);
        RemeasureChild(1, layout.Second.Width);
        RemeasureChild(2, layout.Third.Width);
        layout = CalculateLayout(
            availableWidth,
            new WpfSize(firstSize.Width, GetDesiredSize(0).Height),
            new WpfSize(secondSize.Width, GetDesiredSize(1).Height),
            new WpfSize(thirdSize.Width, GetDesiredSize(2).Height),
            HorizontalSpacing,
            VerticalSpacing);
        return new WpfSize(availableWidth, layout.DesiredHeight);
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        AdaptiveSplitLayout layout = CalculateLayout(
            finalSize.Width,
            new WpfSize(GetPreferredSize(0).Width, GetDesiredSize(0).Height),
            new WpfSize(GetPreferredSize(1).Width, GetDesiredSize(1).Height),
            new WpfSize(GetPreferredSize(2).Width, GetDesiredSize(2).Height),
            HorizontalSpacing,
            VerticalSpacing);

        if (InternalChildren.Count > 0)
        {
            InternalChildren[0].Arrange(layout.First);
        }
        if (InternalChildren.Count > 1)
        {
            InternalChildren[1].Arrange(layout.Second);
        }
        if (InternalChildren.Count > 2)
        {
            InternalChildren[2].Arrange(layout.Third);
        }
        for (int index = 3; index < InternalChildren.Count; index++)
        {
            InternalChildren[index].Arrange(Rect.Empty);
        }

        return new WpfSize(finalSize.Width, layout.DesiredHeight);
    }

    private WpfSize GetDesiredSize(int index)
    {
        return InternalChildren.Count > index ? InternalChildren[index].DesiredSize : WpfSize.Empty;
    }

    private WpfSize GetPreferredSize(int index)
    {
        return InternalChildren.Count > index ? preferredSizes[index] : WpfSize.Empty;
    }

    private void RemeasureChild(int index, double width)
    {
        if (InternalChildren.Count > index)
        {
            InternalChildren[index].Measure(new WpfSize(Math.Max(0, width), double.PositiveInfinity));
        }
    }

    internal static AdaptiveSplitLayout CalculateLayout(
        double availableWidth,
        WpfSize firstSize,
        WpfSize secondSize,
        WpfSize thirdSize,
        double horizontalSpacing,
        double verticalSpacing)
    {
        double width = Math.Max(0, availableWidth);
        double horizontalGap = Math.Max(0, horizontalSpacing);
        double verticalGap = Math.Max(0, verticalSpacing);
        double childrenWidth = firstSize.Width + secondSize.Width + thirdSize.Width;
        WpfSize arrangedFirstSize = new(Math.Min(firstSize.Width, width), firstSize.Height);
        WpfSize arrangedSecondSize = new(Math.Min(secondSize.Width, width), secondSize.Height);
        WpfSize arrangedThirdSize = new(Math.Min(thirdSize.Width, width), thirdSize.Height);

        if (childrenWidth + horizontalGap * 2 <= width)
        {
            double equalGap = (width - childrenWidth) / 2;
            return new AdaptiveSplitLayout(
            new Rect(0, 0, arrangedFirstSize.Width, arrangedFirstSize.Height),
            new Rect(firstSize.Width + equalGap, 0, arrangedSecondSize.Width, arrangedSecondSize.Height),
            new Rect(Math.Max(0, width - arrangedThirdSize.Width), 0, arrangedThirdSize.Width, arrangedThirdSize.Height),
                Math.Max(firstSize.Height, Math.Max(secondSize.Height, thirdSize.Height)),
                1);
        }

        double firstRowHeight = firstSize.Height;
        double secondRowY = firstRowHeight + verticalGap;
        if (secondSize.Width + horizontalGap + thirdSize.Width <= width)
        {
            double secondRowHeight = Math.Max(secondSize.Height, thirdSize.Height);
            return new AdaptiveSplitLayout(
                new Rect(0, 0, arrangedFirstSize.Width, arrangedFirstSize.Height),
                new Rect(0, secondRowY, arrangedSecondSize.Width, arrangedSecondSize.Height),
                new Rect(Math.Max(0, width - arrangedThirdSize.Width), secondRowY, arrangedThirdSize.Width, arrangedThirdSize.Height),
                secondRowY + secondRowHeight,
                2);
        }

        double thirdRowY = secondRowY + secondSize.Height + verticalGap;
        return new AdaptiveSplitLayout(
            new Rect(0, 0, arrangedFirstSize.Width, arrangedFirstSize.Height),
            new Rect(0, secondRowY, arrangedSecondSize.Width, arrangedSecondSize.Height),
            new Rect(Math.Max(0, width - arrangedThirdSize.Width), thirdRowY, arrangedThirdSize.Width, arrangedThirdSize.Height),
            thirdRowY + thirdSize.Height,
            3);
    }
}

internal readonly record struct AdaptiveSplitLayout(Rect First, Rect Second, Rect Third, double DesiredHeight, int RowCount);
