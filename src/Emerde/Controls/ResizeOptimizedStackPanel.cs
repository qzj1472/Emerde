using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSize = System.Windows.Size;

namespace Emerde.Controls;

public sealed class ResizeOptimizedStackPanel : WpfPanel
{
    private readonly Dictionary<UIElement, double> measuredChildWidths = [];
    private readonly Dictionary<UIElement, WpfSize> measuredChildSizes = [];
    private readonly HashSet<UIElement> deferredChildren = [];
    private ScrollViewer? scrollOwner;

    public ResizeOptimizedStackPanel()
    {
        Loaded += ResizeOptimizedStackPanelLoaded;
        Unloaded += ResizeOptimizedStackPanelUnloaded;
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        bool hasFiniteWidth = double.IsFinite(availableSize.Width);
        double availableWidth = hasFiniteWidth ? Math.Max(0d, availableSize.Width) : double.PositiveInfinity;
        EnsureScrollOwner();
        double desiredWidth = 0d;
        double desiredHeight = 0d;
        foreach (UIElement child in InternalChildren)
        {
            bool hasMeasurement = measuredChildWidths.TryGetValue(child, out double measuredWidth);
            bool hasCurrentMeasurement = hasMeasurement && AreClose(measuredWidth, availableWidth);
            bool hasChangedChildSize = !measuredChildSizes.TryGetValue(child, out WpfSize measuredSize)
                || !AreClose(measuredSize.Width, child.DesiredSize.Width)
                || !AreClose(measuredSize.Height, child.DesiredSize.Height);
            bool shouldMeasure = !hasMeasurement
                || !child.IsMeasureValid
                || hasCurrentMeasurement
                || hasChangedChildSize
                || IsNearViewport(child, scrollOwner);
            if (shouldMeasure)
            {
                child.Measure(new WpfSize(availableWidth, double.PositiveInfinity));
                measuredChildWidths[child] = availableWidth;
                measuredChildSizes[child] = child.DesiredSize;
                deferredChildren.Remove(child);
            }
            else
            {
                deferredChildren.Add(child);
            }
            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
            desiredHeight += child.DesiredSize.Height;
        }

        return new WpfSize(hasFiniteWidth ? availableWidth : desiredWidth, Math.Max(0d, desiredHeight));
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        double width = double.IsFinite(finalSize.Width) ? Math.Max(0d, finalSize.Width) : 0d;
        double y = 0d;
        foreach (UIElement child in InternalChildren)
        {
            double height = Math.Max(0d, child.DesiredSize.Height);
            if (!deferredChildren.Contains(child))
            {
                child.Arrange(new Rect(0d, y, width, height));
            }
            else if (child.IsMeasureValid && child.RenderSize.Width > 0d)
            {
                child.Arrange(new Rect(0d, y, child.RenderSize.Width, height));
            }
            y += height;
        }
        return new WpfSize(width, Math.Max(0d, y));
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        if (visualRemoved is UIElement removed)
        {
            measuredChildWidths.Remove(removed);
            measuredChildSizes.Remove(removed);
            deferredChildren.Remove(removed);
        }
    }

    internal static bool IsNearViewport(UIElement element, ScrollViewer? owner)
    {
        if (owner == null || owner.ViewportHeight <= 0d || element.RenderSize.Height <= 0d)
        {
            return true;
        }

        try
        {
            Rect bounds = element.TransformToAncestor(owner)
                .TransformBounds(new Rect(new System.Windows.Point(0d, 0d), element.RenderSize));
            double overscan = Math.Max(160d, owner.ViewportHeight * 0.75d);
            return bounds.Bottom >= -overscan && bounds.Top <= owner.ViewportHeight + overscan;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    internal static ScrollViewer? FindScrollOwner(DependencyObject element)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(element); current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer owner)
            {
                return owner;
            }
        }
        return null;
    }

    private static bool AreClose(double left, double right)
    {
        return left.Equals(right)
            || double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= 0.1d;
    }

    private void ResizeOptimizedStackPanelLoaded(object sender, RoutedEventArgs e)
    {
        EnsureScrollOwner();
    }

    private void ResizeOptimizedStackPanelUnloaded(object sender, RoutedEventArgs e)
    {
        SetScrollOwner(null);
    }

    private void EnsureScrollOwner()
    {
        SetScrollOwner(FindScrollOwner(this));
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
        if (deferredChildren.Any(child => IsNearViewport(child, scrollOwner)))
        {
            InvalidateMeasure();
        }
    }
}
