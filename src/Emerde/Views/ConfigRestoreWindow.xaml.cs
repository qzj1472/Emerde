using Emerde.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Emerde.Views;

public sealed partial class ConfigRestoreWindow : Window, INotifyPropertyChanged
{
    private const double DialogBaseWidth = 750d;
    private const double DialogBaseHeight = 608d;
    private const double DialogMinimumWidth = 620d;
    private const double DialogMinimumHeight = 486d;
    private const double DialogWindowHorizontalMargin = 96d;
    private const double DialogWindowVerticalMargin = 96d;
    private string primaryButtonText = string.Empty;
    private bool isCloseAnimating;
    private bool isCloseComplete;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConfigRestoreWindow(ConfigRestoreContentDialog content, Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        RestoreContentHost.Content = content;
        ApplyDialogSize(owner);
        Closing += WindowClosing;
    }

    public string PrimaryButtonText
    {
        get => primaryButtonText;
        set
        {
            if (primaryButtonText == value)
            {
                return;
            }

            primaryButtonText = value;
            OnPropertyChanged();
        }
    }

    private void ApplyDialogSize(Window? owner)
    {
        double ownerWidth = owner?.ActualWidth > 1d ? owner.ActualWidth : owner?.Width ?? SystemParameters.WorkArea.Width;
        double ownerHeight = owner?.ActualHeight > 1d ? owner.ActualHeight : owner?.Height ?? SystemParameters.WorkArea.Height;
        double maxWidth = Math.Max(DialogMinimumWidth, ownerWidth - DialogWindowHorizontalMargin);
        double maxHeight = Math.Max(DialogMinimumHeight, ownerHeight - DialogWindowVerticalMargin);

        Width = Math.Min(DialogBaseWidth, maxWidth);
        Height = Math.Min(DialogBaseHeight, maxHeight);
        MinWidth = Width;
        MinHeight = Height;
        MaxWidth = Width;
        MaxHeight = Height;
    }

    internal Func<Task>? BackgroundExitAnimation { get; set; }

    private async void PrimaryButtonClick(object sender, RoutedEventArgs e)
    {
        await RequestCloseAsync(true);
    }

    private async void CancelButtonClick(object sender, RoutedEventArgs e)
    {
        await RequestCloseAsync(false);
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (isCloseComplete)
        {
            return;
        }

        e.Cancel = true;
        await RequestCloseAsync(null);
    }

    private async Task RequestCloseAsync(bool? result)
    {
        if (isCloseAnimating || isCloseComplete)
        {
            return;
        }

        isCloseAnimating = true;
        DialogSurface.IsHitTestVisible = false;
        try
        {
            Task backgroundExit = BackgroundExitAnimation?.Invoke() ?? Task.CompletedTask;
            await Task.WhenAll(MotionAssist.PlayExitAsync(DialogSurface), backgroundExit);
        }
        catch
        {
            MotionAssist.ResetExit(DialogSurface);
        }
        finally
        {
            isCloseComplete = true;
            isCloseAnimating = false;
        }

        if (result.HasValue)
        {
            DialogResult = result;
        }
        else
        {
            Close();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
