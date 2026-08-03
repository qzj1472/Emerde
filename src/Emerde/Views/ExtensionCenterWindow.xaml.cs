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
}
