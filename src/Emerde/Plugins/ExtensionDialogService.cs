using System.Windows;
using Emerde.Core;
using Emerde.Views;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Plugins;

internal sealed class ExtensionDialogService(Window owner) : IExtensionDialogService
{
    public Task<ExtensionDialogResult> ShowAsync(
        ExtensionDialogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.Dispatcher.CheckAccess())
        {
            return ShowOnUiThreadAsync(request, cancellationToken);
        }
        return owner.Dispatcher.InvokeAsync(() => ShowOnUiThreadAsync(request, cancellationToken)).Task.Unwrap();
    }

    private async Task<ExtensionDialogResult> ShowOnUiThreadAsync(
        ExtensionDialogRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ContentDialog dialog = new()
        {
            Title = request.Title,
            Content = request.Content,
            PrimaryButtonText = request.PrimaryButtonText,
            CloseButtonText = request.CloseButtonText,
            SecondaryButtonText = request.SecondaryButtonText,
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.TryFindResource("DefaultVioletaContentDialogStyle") as Style,
            FocusVisualStyle = null,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            string? validationMessage;
            try
            {
                validationMessage = request.Validate?.Invoke();
            }
            catch (Exception exception)
            {
                validationMessage = exception.Message;
            }
            if (string.IsNullOrWhiteSpace(validationMessage))
            {
                return;
            }
            args.Cancel = true;
            request.ShowValidation?.Invoke(validationMessage);
        };
        RoutedEventHandler? wideLayoutHandler = null;
        if (request.UseWideLayout)
        {
            void ApplyWideLayout()
            {
                if (!TryGetDialogVisualSize(
                        owner,
                        Math.Clamp(request.WideHeightRatio, 0.6d, 0.9d),
                        out double targetWidth,
                        out double targetHeight))
                {
                    return;
                }
                dialog.Resources["EmerdeWideContentDialog"] = true;
                ApplyWideDialogVisualSize(dialog, targetWidth, targetHeight);
                request.Content.Width = double.NaN;
                request.Content.Height = double.NaN;
                request.Content.MinWidth = 0d;
                request.Content.MinHeight = 0d;
                request.Content.MaxWidth = double.PositiveInfinity;
                request.Content.MaxHeight = double.PositiveInfinity;
                request.Content.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                request.Content.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            }
            ApplyWideLayout();
            wideLayoutHandler = (_, _) => ApplyWideLayout();
            dialog.Loaded += wideLayoutHandler;
        }
        using DialogBlurScope blurScope = DialogBlurScope.ForDialog(owner, dialog);
        ContentDialogResult result;
        try
        {
            result = await WindowSizing.ShowContentDialogAsync(dialog, owner);
        }
        finally
        {
            if (wideLayoutHandler != null)
            {
                dialog.Loaded -= wideLayoutHandler;
            }
        }
        return result switch
        {
            ContentDialogResult.Primary => ExtensionDialogResult.Primary,
            ContentDialogResult.Secondary => ExtensionDialogResult.Secondary,
            ContentDialogResult.None => ExtensionDialogResult.Close,
            _ => ExtensionDialogResult.None,
        };
    }

    private static void ApplyWideDialogVisualSize(ContentDialog dialog, double targetWidth, double targetHeight)
    {
        dialog.Width = targetWidth;
        dialog.Height = targetHeight;
        dialog.MinWidth = targetWidth;
        dialog.MinHeight = targetHeight;
        dialog.MaxWidth = targetWidth;
        dialog.MaxHeight = targetHeight;
        dialog.Resources["ContentDialogMinWidth"] = targetWidth;
        dialog.Resources["ContentDialogMinHeight"] = targetHeight;
        dialog.Resources["ContentDialogMaxWidth"] = targetWidth;
        dialog.Resources["ContentDialogMaxHeight"] = targetHeight;
    }

    private static bool TryGetDialogVisualSize(Window owner, double heightRatio, out double targetWidth, out double targetHeight)
    {
        Window? reference = owner ?? Application.Current?.MainWindow;
        double ownerWidth = reference?.ActualWidth > 1d ? reference.ActualWidth : reference?.Width ?? 0d;
        double ownerHeight = reference?.ActualHeight > 1d ? reference.ActualHeight : reference?.Height ?? 0d;
        if (ownerWidth <= 1d || ownerHeight <= 1d)
        {
            targetWidth = 0d;
            targetHeight = 0d;
            return false;
        }

        targetWidth = Math.Max(1d, Math.Floor(ownerWidth * 0.72d));
        targetHeight = Math.Max(1d, Math.Floor(ownerHeight * heightRatio));
        return true;
    }
}
