using Emerde.ViewModels;
using Wpf.Ui.Controls;

namespace Emerde.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow()
    {
        DataContext = ViewModel = new();
        Core.WindowSizing.UseRelativeMainWindowSize(this, 700d, 560d);
        InitializeComponent();
    }
}
