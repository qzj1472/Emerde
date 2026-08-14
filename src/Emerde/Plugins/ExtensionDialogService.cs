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
            Content = UiXDialogContent.IsEnabled
                ? UiXDialogContent.CreateTaskSurface(request.Content, request.UseWideLayout ? 0d : 420d)
                : request.Content,
            PrimaryButtonText = request.PrimaryButtonText,
            CloseButtonText = request.CloseButtonText,
            SecondaryButtonText = request.SecondaryButtonText,
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.TryFindResource("EmerdeContentDialogStyle") as Style,
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
                if (!LocalSettingsContentDialog.TryGetDialogVisualSize(
                        owner,
                        Math.Clamp(request.WideHeightRatio, 0.6d, 0.9d),
                        out double targetWidth,
                        out double targetHeight))
                {
                    return;
                }
                dialog.Resources["EmerdeWideContentDialog"] = true;
                LocalSettingsContentDialog.ApplyWideDialogVisualSize(dialog, targetWidth, targetHeight);
                FrameworkElement content = dialog.Content as FrameworkElement ?? request.Content;
                content.Width = double.NaN;
                content.Height = double.NaN;
                content.MinWidth = 0d;
                content.MinHeight = 0d;
                content.MaxWidth = double.PositiveInfinity;
                content.MaxHeight = double.PositiveInfinity;
                content.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                content.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            }
            ApplyWideLayout();
            wideLayoutHandler = (_, _) => ApplyWideLayout();
            dialog.Loaded += wideLayoutHandler;
        }
        using DialogBlurScope blurScope = UiXDialogContent.IsEnabled
            ? DialogBlurScope.ForLightDismiss(owner, dialog)
            : DialogBlurScope.ForDialog(owner, dialog);
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

}
