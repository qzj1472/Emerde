using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

public partial class ExitConfirmationContentDialog : ContentDialog
{
    public string Message { get; }

    public ExitConfirmationContentDialog(string message)
    {
        Message = message;
        DataContext = this;
        InitializeComponent();
    }
}
