using System.Windows;
using System.Windows.Input;
using Emerde.ViewModels;
using Emerde.Plugins;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Emerde.Views;

public partial class ExtensionCenterWindow : System.Windows.Controls.UserControl
{
    private const double ExtensionSettingsTwoColumnEnterWidth = 760d;
    private const double ExtensionSettingsTwoColumnExitWidth = 720d;

    public ExtensionCenterViewModel ViewModel { get; } = new();

    public ExtensionCenterWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += ExtensionCenterWindowLoaded;
    }

    private async void ExtensionCenterWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ExtensionCenterWindowLoaded;
        await ViewModel.InitializeAsync();
    }

    private void ExtensionCenterDragEnter(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    private void ExtensionCenterDragOver(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    private void ExtensionCenterDragLeave(object sender, DragEventArgs e)
    {
        System.Windows.Point position = e.GetPosition(this);
        if (position.X >= 0 && position.X <= ActualWidth && position.Y >= 0 && position.Y <= ActualHeight)
        {
            return;
        }
        ViewModel.IsDragging = false;
    }

    private async void ExtensionCenterDrop(object sender, DragEventArgs e)
    {
        ViewModel.IsDragging = false;
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }
        string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        await ViewModel.InstallFilesAsync(paths);
    }

    private void UpdateDragState(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            ViewModel.IsDragging = false;
            return;
        }
        string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        bool supported = paths.Any(ExtensionService.IsSupportedPackage);
        e.Effects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        ViewModel.IsDragging = supported;
        e.Handled = true;
    }

    internal static bool ResolveExtensionSettingsTwoColumnState(double availableWidth, bool? currentState)
    {
        double threshold = currentState == true
            ? ExtensionSettingsTwoColumnExitWidth
            : ExtensionSettingsTwoColumnEnterWidth;
        return availableWidth >= threshold;
    }

    private void ExtensionSettingsColumnsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || sender is not System.Windows.Controls.Grid grid || grid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        bool? currentState = grid.Tag is bool state ? state : null;
        bool useTwoColumns = ResolveExtensionSettingsTwoColumnState(grid.ActualWidth, currentState);
        if (currentState == useTwoColumns)
        {
            return;
        }

        grid.Tag = useTwoColumns;
        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = useTwoColumns ? new GridLength(24) : new GridLength(0);
        grid.ColumnDefinitions[2].Width = useTwoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        foreach (System.Windows.Controls.ItemsControl itemsControl in grid.Children.OfType<System.Windows.Controls.ItemsControl>())
        {
            bool isSecondary = Equals(itemsControl.Tag, "Secondary");
            System.Windows.Controls.Grid.SetColumn(itemsControl, isSecondary && useTwoColumns ? 2 : 0);
            System.Windows.Controls.Grid.SetRow(itemsControl, isSecondary && !useTwoColumns ? 1 : 0);
            itemsControl.Margin = isSecondary && !useTwoColumns
                ? new Thickness(0, 10, 0, 0)
                : new Thickness(0);
        }
    }
}
