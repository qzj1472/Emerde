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
        return AppFeedbackService.Current.TaskFeedback(title, body, key, owner, progress);
    }

    public static bool CompleteTask(string key, string title, string? body = null, bool succeeded = true, object? owner = null)
    {
        return AppFeedbackService.Current.CompleteTask(key, title, body, succeeded, owner);
    }

    private static Guid Show(AppFeedbackKind kind, string title, string? body, string? key, object? owner)
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
}
