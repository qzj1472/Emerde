using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;

namespace Emerde.Controls;

public static class ScrollEdgeFade
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ScrollEdgeFade),
        new PropertyMetadata(false, IsEnabledChanged));

    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
        "Adorner",
        typeof(ScrollEdgeFadeAdorner),
        typeof(ScrollEdgeFade));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void IsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is ScrollViewer scrollViewer)
        {
            if ((bool)e.NewValue)
            {
                Attach(scrollViewer);
            }
            else
            {
                Detach(scrollViewer);
            }
            return;
        }

        if (element is WpfComboBox comboBox)
        {
            comboBox.DropDownOpened -= ComboBoxDropDownOpened;
            if ((bool)e.NewValue)
            {
                comboBox.DropDownOpened += ComboBoxDropDownOpened;
            }
        }
    }

    private static void ComboBoxDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not WpfComboBox comboBox)
        {
            return;
        }

        comboBox.ApplyTemplate();
        if (comboBox.Template.FindName("PART_Popup", comboBox) is not Popup popup || popup.Child == null)
        {
            return;
        }

        ScrollViewer? scrollViewer = FindVisualChild<ScrollViewer>(popup.Child);
        if (scrollViewer != null)
        {
            Attach(scrollViewer);
        }
    }

    private static void Attach(ScrollViewer scrollViewer)
    {
        scrollViewer.Loaded -= ScrollViewerLoaded;
        scrollViewer.Unloaded -= ScrollViewerUnloaded;
        scrollViewer.ScrollChanged -= ScrollViewerScrollChanged;
        scrollViewer.SizeChanged -= ScrollViewerSizeChanged;
        scrollViewer.IsVisibleChanged -= ScrollViewerIsVisibleChanged;
        scrollViewer.Loaded += ScrollViewerLoaded;
        scrollViewer.Unloaded += ScrollViewerUnloaded;
        scrollViewer.ScrollChanged += ScrollViewerScrollChanged;
        scrollViewer.SizeChanged += ScrollViewerSizeChanged;
        scrollViewer.IsVisibleChanged += ScrollViewerIsVisibleChanged;
        if (scrollViewer.IsLoaded)
        {
            EnsureAdorner(scrollViewer);
        }
    }

    private static void Detach(ScrollViewer scrollViewer)
    {
        scrollViewer.Loaded -= ScrollViewerLoaded;
        scrollViewer.Unloaded -= ScrollViewerUnloaded;
        scrollViewer.ScrollChanged -= ScrollViewerScrollChanged;
        scrollViewer.SizeChanged -= ScrollViewerSizeChanged;
        scrollViewer.IsVisibleChanged -= ScrollViewerIsVisibleChanged;
        RemoveAdorner(scrollViewer);
    }

    private static void ScrollViewerLoaded(object sender, RoutedEventArgs e) => EnsureAdorner((ScrollViewer)sender);

    private static void ScrollViewerUnloaded(object sender, RoutedEventArgs e) => RemoveAdorner((ScrollViewer)sender);

    private static void ScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateAdorner((ScrollViewer)sender);

    private static void ScrollViewerSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAdorner((ScrollViewer)sender);

    private static void ScrollViewerIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ScrollViewer scrollViewer = (ScrollViewer)sender;
        if ((bool)e.NewValue)
        {
            EnsureAdorner(scrollViewer);
        }
        else
        {
            RemoveAdorner(scrollViewer);
        }
    }

    private static void EnsureAdorner(ScrollViewer scrollViewer)
    {
        if (!scrollViewer.IsVisible || scrollViewer.ActualWidth <= 0d || scrollViewer.ActualHeight <= 0d)
        {
            RemoveAdorner(scrollViewer);
            return;
        }

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(scrollViewer);
        if (layer == null)
        {
            RemoveAdorner(scrollViewer);
            return;
        }

        ScrollEdgeFadeAdorner? adorner = GetAdorner(scrollViewer);
        if (adorner?.Layer != layer)
        {
            RemoveAdorner(scrollViewer);
            adorner = new ScrollEdgeFadeAdorner(scrollViewer, layer);
            scrollViewer.SetValue(AdornerProperty, adorner);
            layer.Add(adorner);
        }
        adorner.InvalidateVisual();
    }

    private static void UpdateAdorner(ScrollViewer scrollViewer)
    {
        GetAdorner(scrollViewer)?.InvalidateVisual();
    }

    private static void RemoveAdorner(ScrollViewer scrollViewer)
    {
        ScrollEdgeFadeAdorner? adorner = GetAdorner(scrollViewer);
        if (adorner != null)
        {
            adorner.Layer.Remove(adorner);
            scrollViewer.ClearValue(AdornerProperty);
        }
    }

    private static ScrollEdgeFadeAdorner? GetAdorner(ScrollViewer scrollViewer)
    {
        return scrollViewer.GetValue(AdornerProperty) as ScrollEdgeFadeAdorner;
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                return typed;
            }

            T? nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed class ScrollEdgeFadeAdorner : Adorner
    {
        private const double FadeSize = 22d;
        private const double ScrollBarClearance = 14d;

        public ScrollEdgeFadeAdorner(ScrollViewer adornedElement, AdornerLayer layer)
            : base(adornedElement)
        {
            Layer = layer;
            IsHitTestVisible = false;
        }

        public AdornerLayer Layer { get; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (AdornedElement is not ScrollViewer scrollViewer
                || !scrollViewer.IsVisible
                || scrollViewer.ScrollableHeight <= 0d
                || ActualWidth <= 0d
                || ActualHeight <= 0d)
            {
                return;
            }

            double width = Math.Max(0d, ActualWidth - (scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible ? ScrollBarClearance : 0d));
            if (width <= 0d)
            {
                return;
            }

            Color surface = ResolveSurfaceColor(scrollViewer);
            if (scrollViewer.VerticalOffset > 0.5d)
            {
                drawingContext.DrawRectangle(
                    CreateBrush(surface, true),
                    null,
                    new Rect(0d, 0d, width, Math.Min(FadeSize, ActualHeight)));
            }

            if (scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 0.5d)
            {
                double height = Math.Min(FadeSize, ActualHeight);
                drawingContext.DrawRectangle(
                    CreateBrush(surface, false),
                    null,
                    new Rect(0d, Math.Max(0d, ActualHeight - height), width, height));
            }
        }

        private static MediaBrush CreateBrush(Color color, bool top)
        {
            GradientStopCollection stops = top
                ? [new GradientStop(color, 0d), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1d)]
                : [new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0d), new GradientStop(color, 1d)];
            LinearGradientBrush brush = new(stops, new WpfPoint(0d, 0d), new WpfPoint(0d, 1d));
            brush.Freeze();
            return brush;
        }

        private static Color ResolveSurfaceColor(FrameworkElement element)
        {
            List<(Color Color, double Opacity)> surfaces = [];
            DependencyObject? current = element;
            while (current is FrameworkElement frameworkElement)
            {
                SolidColorBrush? brush = frameworkElement switch
                {
                    Border border => border.Background as SolidColorBrush,
                    System.Windows.Controls.Panel panel => panel.Background as SolidColorBrush,
                    Control control => control.Background as SolidColorBrush,
                    _ => null,
                };
                if (brush != null && brush.Color.A > 0 && brush.Opacity > 0d)
                {
                    double opacity = brush.Color.A / 255d * brush.Opacity;
                    surfaces.Add((brush.Color, opacity));
                    if (opacity >= 0.999d)
                    {
                        break;
                    }
                }
                current = VisualTreeHelper.GetParent(current);
            }

            Color result = element.TryFindResource("UiXWindowFallbackBrush") is SolidColorBrush fallback
                ? fallback.Color
                : Color.FromRgb(241, 244, 245);
            for (int index = surfaces.Count - 1; index >= 0; index--)
            {
                (Color color, double opacity) = surfaces[index];
                result = Color.FromRgb(
                    Blend(result.R, color.R, opacity),
                    Blend(result.G, color.G, opacity),
                    Blend(result.B, color.B, opacity));
            }
            return Color.FromRgb(result.R, result.G, result.B);
        }

        private static byte Blend(byte background, byte foreground, double opacity)
        {
            return (byte)Math.Clamp(Math.Round(background * (1d - opacity) + foreground * opacity), 0d, 255d);
        }
    }
}
