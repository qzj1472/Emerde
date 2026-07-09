using Emerde.Controls;
using Emerde.Core;
using Emerde.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using WpfPoint = System.Windows.Point;

namespace Emerde.Views;

public partial class SettingsWindow : System.Windows.Controls.UserControl
{
    private const int InitialSettingsElementCount = 4;

    private readonly Dictionary<CardExpander, object?> deferredCardExpanderContents = [];
    private readonly Queue<UIElement> deferredStartupSections = [];
    private readonly Stopwatch startupRestoreStopwatch = new();
    private long maxStartupRestoreBatchMilliseconds;
    private int startupRestoreBatchCount;
    private bool startupSectionsQueued;

    public SettingsViewModel ViewModel { get; }

    public SettingsWindow()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        DataContext = ViewModel = new();
        long viewModelElapsed = stopwatch.ElapsedMilliseconds;
        ViewModel.OwnerWindow = Application.Current.MainWindow;
        InitializeComponent();
        long initializeElapsed = stopwatch.ElapsedMilliseconds;
        int deferredCount = DeferCollapsedCardExpanderContent(SettingsContentRoot);
        int deferredSectionCount = DeferStartupSections();
        Loaded += SettingsDialogLoaded;
        IsVisibleChanged += SettingsDialogIsVisibleChanged;
        Unloaded += SettingsDialogUnloaded;
        AppSessionLogger.Write(
            $"perf SettingsDialog ctor vm={viewModelElapsed} ms init={initializeElapsed - viewModelElapsed} ms defer={stopwatch.ElapsedMilliseconds - initializeElapsed} ms deferredCards={deferredCount} deferredSections={deferredSectionCount} total={stopwatch.ElapsedMilliseconds} ms");
    }

    private void SettingsDialogLoaded(object sender, RoutedEventArgs e)
    {
        QueueStartupSectionRestore();
    }

    private void SettingsDialogIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            QueueStartupSectionRestore();
        }
    }

    private void SettingsDialogUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SettingsDialogLoaded;
        IsVisibleChanged -= SettingsDialogIsVisibleChanged;
        Unloaded -= SettingsDialogUnloaded;

        foreach (CardExpander expander in deferredCardExpanderContents.Keys.ToArray())
        {
            DependencyPropertyDescriptor
                .FromProperty(CardExpander.IsExpandedProperty, typeof(CardExpander))
                ?.RemoveValueChanged(expander, CardExpanderIsExpandedChanged);
        }

        deferredCardExpanderContents.Clear();
        deferredStartupSections.Clear();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        if (e.OriginalSource is not DependencyObject source || IsInteractiveElement(source))
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        CardExpander? expander = FindVisualAncestor<CardExpander>(source);
        if (expander == null || !IsPointInsideHeader(expander, e.GetPosition(expander)))
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        expander.IsExpanded = !expander.IsExpanded;
        e.Handled = true;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private static bool IsPointInsideHeader(CardExpander expander, WpfPoint point)
    {
        if (expander.Template.FindName("HeaderChrome", expander) is not FrameworkElement header)
        {
            return point.Y >= 0 && point.Y <= 62;
        }

        WpfPoint topLeft = header.TranslatePoint(new WpfPoint(0, 0), expander);
        return point.X >= topLeft.X
            && point.X <= topLeft.X + header.ActualWidth
            && point.Y >= topLeft.Y
            && point.Y <= topLeft.Y + header.ActualHeight;
    }

    private static bool IsInteractiveElement(DependencyObject source)
    {
        for (DependencyObject? current = source; current != null; current = GetVisualParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.Primitives.Selector
                or System.Windows.Controls.Slider
                or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Primitives.Thumb
                or System.Windows.Controls.Primitives.ToggleButton
                or Wpf.Ui.Controls.TextBox
                or Wpf.Ui.Controls.NumberBox
                or Wpf.Ui.Controls.ToggleSwitch)
            {
                return true;
            }

            if (current is CompactNumberBox)
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindVisualAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (DependencyObject? current = source; current != null; current = GetVisualParent(current))
        {
            if (current is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static DependencyObject? GetVisualParent(DependencyObject source)
    {
        return source is Visual or Visual3D ? VisualTreeHelper.GetParent(source) : null;
    }

    private int DeferStartupSections()
    {
        List<UIElement> sections = SettingsStackPanel.Children
            .OfType<UIElement>()
            .Skip(InitialSettingsElementCount)
            .ToList();

        foreach (UIElement section in sections)
        {
            SettingsStackPanel.Children.Remove(section);
            deferredStartupSections.Enqueue(section);
        }

        return sections.Count;
    }

    private void QueueStartupSectionRestore()
    {
        if (startupSectionsQueued || deferredStartupSections.Count == 0)
        {
            return;
        }

        startupSectionsQueued = true;
        startupRestoreStopwatch.Restart();
        maxStartupRestoreBatchMilliseconds = 0;
        startupRestoreBatchCount = 0;
        _ = Dispatcher.BeginInvoke(RestoreNextStartupSection, DispatcherPriority.ContextIdle);
    }

    private void RestoreNextStartupSection()
    {
        if (!IsVisible)
        {
            startupSectionsQueued = false;
            return;
        }

        Stopwatch batchStopwatch = Stopwatch.StartNew();
        const int batchSize = 1;
        int restored = 0;
        while (restored < batchSize && deferredStartupSections.TryDequeue(out UIElement? section))
        {
            SettingsStackPanel.Children.Add(section);
            restored++;
        }

        batchStopwatch.Stop();
        startupRestoreBatchCount++;
        maxStartupRestoreBatchMilliseconds = Math.Max(maxStartupRestoreBatchMilliseconds, batchStopwatch.ElapsedMilliseconds);

        if (deferredStartupSections.Count > 0)
        {
            _ = Dispatcher.BeginInvoke(RestoreNextStartupSection, DispatcherPriority.Background);
            return;
        }

        AppSessionLogger.Write(
            $"perf SettingsDialog deferred sections restored in {startupRestoreStopwatch.ElapsedMilliseconds} ms batches={startupRestoreBatchCount} maxBatch={maxStartupRestoreBatchMilliseconds} ms");
    }

    private int DeferCollapsedCardExpanderContent(DependencyObject root)
    {
        int deferredCount = 0;
        foreach (CardExpander expander in FindLogicalDescendants<CardExpander>(root))
        {
            DependencyPropertyDescriptor
                .FromProperty(CardExpander.IsExpandedProperty, typeof(CardExpander))
                ?.AddValueChanged(expander, CardExpanderIsExpandedChanged);

            if (DeferCardExpanderContent(expander))
            {
                deferredCount++;
            }
        }

        return deferredCount;
    }

    private void CardExpanderIsExpandedChanged(object? sender, EventArgs e)
    {
        if (sender is not CardExpander expander)
        {
            return;
        }

        if (expander.IsExpanded)
        {
            RestoreCardExpanderContent(expander);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => _ = DeferCardExpanderContent(expander), DispatcherPriority.Background);
    }

    private void RestoreCardExpanderContent(CardExpander expander)
    {
        if (!deferredCardExpanderContents.Remove(expander, out object? content))
        {
            return;
        }

        expander.Content = content;
    }

    private bool DeferCardExpanderContent(CardExpander expander)
    {
        if (expander.IsExpanded ||
            expander.Content == null ||
            deferredCardExpanderContents.ContainsKey(expander))
        {
            return false;
        }

        deferredCardExpanderContents[expander] = expander.Content;
        expander.Content = null;
        return true;
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (object childObject in LogicalTreeHelper.GetChildren(root))
        {
            if (childObject is not DependencyObject child)
            {
                continue;
            }

            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T descendant in FindLogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
