namespace Emerde.Views;

public sealed partial class UiXExportLogsContent : System.Windows.Controls.UserControl
{
    public bool TodayOnly => TodayOption.IsChecked == true;

    public UiXExportLogsContent()
    {
        InitializeComponent();
    }
}
