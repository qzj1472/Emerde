using Emerde.ViewModels;
using Wpf.Ui.Controls;

namespace Emerde.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow()
    {
        DataContext = ViewModel = new();
        InitializeComponent();
    }
}
