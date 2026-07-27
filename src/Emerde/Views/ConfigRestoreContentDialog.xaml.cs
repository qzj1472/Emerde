using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Emerde.Views;

public sealed partial class ConfigRestoreContentDialog : System.Windows.Controls.UserControl
{
    public ObservableCollection<ConfigRestoreOption> Options { get; } = [];

    public ConfigRestoreOption? SelectedOption => OptionsListBox.SelectedItem as ConfigRestoreOption;

    public event EventHandler? SelectionChanged;

    public event EventHandler? ImportButtonClicked;

    public event EventHandler<ConfigFileDroppedEventArgs>? ConfigFileDropped;

    public ConfigRestoreContentDialog(IEnumerable<ConfigRestoreOption> options)
    {
        InitializeComponent();
        foreach (ConfigRestoreOption option in options)
        {
            Options.Add(option);
        }
        OptionsListBox.SelectedIndex = Options.Count > 0 ? 0 : -1;
        UpdateEdgeFadeVisibility();
    }

    private void OptionsListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ConfigRestoreContentLoaded(object sender, RoutedEventArgs e)
    {
        UpdateEdgeFadeVisibility();
    }

    private void ConfigRestoreContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateEdgeFadeVisibility();
    }

    private void OptionsScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateEdgeFadeVisibility(sender as ScrollViewer);
    }

    public void AddOptionAndSelect(ConfigRestoreOption option)
    {
        Options.Insert(0, option);
        OptionsListBox.SelectedItem = option;
        OptionsListBox.ScrollIntoView(option);
        UpdateEdgeFadeVisibility();
    }

    public bool SelectOptionByFilePath(string filePath)
    {
        string normalizedPath = NormalizePath(filePath);
        ConfigRestoreOption? option = Options.FirstOrDefault(item =>
            string.Equals(NormalizePath(item.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (option == null)
        {
            return false;
        }

        OptionsListBox.SelectedItem = option;
        OptionsListBox.ScrollIntoView(option);
        return true;
    }

    private void ImportButtonClick(object sender, RoutedEventArgs e)
    {
        ImportButtonClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ConfigRestoreDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = TryGetDraggedConfigFile(e.Data, out _) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void ConfigRestoreDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (TryGetDraggedConfigFile(e.Data, out string? filePath) && !string.IsNullOrWhiteSpace(filePath))
        {
            ConfigFileDropped?.Invoke(this, new ConfigFileDroppedEventArgs(filePath));
        }

        e.Handled = true;
    }

    private void OptionsListBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindVisualChild<ScrollViewer>(OptionsListBox, "OptionsScrollViewer") is not ScrollViewer scrollViewer)
        {
            return;
        }

        double offset = scrollViewer.VerticalOffset - e.Delta * 0.45d;
        scrollViewer.ScrollToVerticalOffset(Math.Clamp(offset, 0d, scrollViewer.ScrollableHeight));
        e.Handled = true;
    }

    internal static bool TryGetDraggedConfigFile(System.Windows.IDataObject data, out string? filePath)
    {
        filePath = null;
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop) || data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files)
        {
            return false;
        }

        filePath = files.FirstOrDefault(path => File.Exists(path) && IsYamlFile(path));
        return !string.IsNullOrWhiteSpace(filePath);
    }

    private static bool IsYamlFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateEdgeFadeVisibility(ScrollViewer? scrollViewer = null)
    {
        scrollViewer ??= FindVisualChild<ScrollViewer>(OptionsListBox, "OptionsScrollViewer");
        if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0.5d)
        {
            OptionsTopFade.Visibility = Visibility.Collapsed;
            OptionsBottomFade.Visibility = Visibility.Collapsed;
            return;
        }

        OptionsTopFade.Visibility = scrollViewer.VerticalOffset > 0.5d ? Visibility.Visible : Visibility.Collapsed;
        OptionsBottomFade.Visibility = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 0.5d
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string? name = null)
        where T : FrameworkElement
    {
        if (parent is not Visual and not System.Windows.Media.Media3D.Visual3D)
        {
            return null;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild &&
                (string.IsNullOrWhiteSpace(name) || string.Equals(typedChild.Name, name, StringComparison.Ordinal)))
            {
                return typedChild;
            }

            T? nestedChild = FindVisualChild<T>(child, name);
            if (nestedChild != null)
            {
                return nestedChild;
            }
        }

        return null;
    }
}

public sealed record ConfigRestoreOption(
    string Title,
    string Subtitle,
    string Detail,
    string FilePath,
    string BadgeText,
    ConfigRestoreOptionAction Action)
{
    public bool IsDefault => Action == ConfigRestoreOptionAction.Reset;

    public bool IsImported => Action == ConfigRestoreOptionAction.Import;
}

public enum ConfigRestoreOptionAction
{
    Restore,
    Import,
    Reset,
}

public sealed class ConfigFileDroppedEventArgs(string filePath) : EventArgs
{
    public string FilePath { get; } = filePath;
}
