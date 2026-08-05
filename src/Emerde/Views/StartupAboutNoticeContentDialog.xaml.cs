using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

public partial class StartupAboutNoticeContentDialog : ContentDialog
{
    public string NoticeTitle { get; }

    public string Message { get; }

    public StartupAboutNoticeContentDialog(string title, string message)
    {
        NoticeTitle = title;
        Message = message;
        DataContext = this;
        InitializeComponent();
    }
}
