using Fischless.Configuration;

namespace Emerde.Core;

internal static class AppFeedback
{
    public static Guid Success(string title, string? body = null, string? key = null, object? owner = null)
    {
        return Show(AppFeedbackKind.Success, title, body, key, owner);
    }

    public static Guid Information(string title, string? body = null, string? key = null, object? owner = null)
    {
        return Show(AppFeedbackKind.Information, title, body, key, owner);
    }

    public static Guid Warning(string title, string? body = null, string? key = null, object? owner = null)
    {
        return Show(AppFeedbackKind.Warning, title, body, key, owner);
    }

    public static Guid Error(string title, string? body = null, string? key = null, object? owner = null)
    {
        return Show(AppFeedbackKind.Error, title, body, key, owner);
    }

    public static Guid Task(string title, string? body = null, string? key = null, object? owner = null, double? progress = null)
    {
        if (!IsUiXEnabled())
        {
            Wpf.Ui.Violeta.Controls.Toast.Information(Combine(title, body));
            return Guid.Empty;
        }

        return AppFeedbackService.Current.TaskFeedback(title, body, key, owner, progress);
    }

    public static bool CompleteTask(string key, string title, string? body = null, bool succeeded = true, object? owner = null)
    {
        if (!IsUiXEnabled())
        {
            string message = Combine(title, body);
            if (succeeded)
            {
                Wpf.Ui.Violeta.Controls.Toast.Success(message);
            }
            else
            {
                Wpf.Ui.Violeta.Controls.Toast.Warning(message);
            }
            return true;
        }

        return AppFeedbackService.Current.CompleteTask(key, title, body, succeeded, owner);
    }

    private static Guid Show(AppFeedbackKind kind, string title, string? body, string? key, object? owner)
    {
        if (IsUiXEnabled())
        {
            return kind switch
            {
                AppFeedbackKind.Success => AppFeedbackService.Current.Success(title, body, key, owner),
                AppFeedbackKind.Information => AppFeedbackService.Current.Information(title, body, key, owner),
                AppFeedbackKind.Warning => AppFeedbackService.Current.Warning(title, body, key, owner),
                AppFeedbackKind.Error => AppFeedbackService.Current.Error(title, body, key, owner),
                _ => AppFeedbackService.Current.Information(title, body, key, owner),
            };
        }

        string message = Combine(title, body);
        switch (kind)
        {
            case AppFeedbackKind.Success:
                Wpf.Ui.Violeta.Controls.Toast.Success(message);
                break;
            case AppFeedbackKind.Warning:
                Wpf.Ui.Violeta.Controls.Toast.Warning(message);
                break;
            case AppFeedbackKind.Error:
                Wpf.Ui.Violeta.Controls.Toast.Error(message);
                break;
            default:
                Wpf.Ui.Violeta.Controls.Toast.Information(message);
                break;
        }
        return Guid.Empty;
    }

    private static bool IsUiXEnabled()
    {
        return Configurations.IsUiXEnabled.Get();
    }

    private static string Combine(string title, string? body)
    {
        return string.IsNullOrWhiteSpace(body) ? title : $"{title}{Environment.NewLine}{body}";
    }
}
