using System.Globalization;
using System.IO;
using System.Text;
using Emerde.Plugins;

namespace Emerde.DouyinPublisher;

internal sealed record PublisherOptions(
    bool AutoPublish,
    bool ConfirmBeforePublish,
    PublisherTaskOptions AutomaticTaskOptions,
    int ScheduleDelayMinutes,
    int MaximumAttempts)
{
    public string TitleTemplate => AutomaticTaskOptions.TitleTemplate;

    public string DescriptionTemplate => AutomaticTaskOptions.DescriptionTemplate;

    public string Topics => AutomaticTaskOptions.Topics;

    public static PublisherOptions From(IReadOnlyDictionary<string, string> settings)
    {
        string declaration = ReadText(settings, "declaration", string.Empty);
        if (string.Equals(declaration, "不声明", StringComparison.Ordinal))
        {
            declaration = string.Empty;
        }
        PublisherTaskOptions automaticTaskOptions = new PublisherTaskOptions(
            ReadText(settings, "title_template", "{title}"),
            ReadText(settings, "description_template", string.Empty),
            ReadText(settings, "topics", string.Empty),
            ReadText(settings, "cover_path", string.Empty),
            ReadText(settings, "official_activity", string.Empty),
            ReadText(settings, "collection_name", string.Empty),
            declaration,
            ReadText(settings, "video_chapters", string.Empty),
            ReadText(settings, "tags", string.Empty),
            ReadText(settings, "location", string.Empty),
            ReadText(settings, "hotspot", string.Empty),
            PublisherVisibility.FromSetting(ReadText(settings, "visibility", "公开")),
            ReadBoolean(settings, "allow_save", true),
            null).Normalize();
        bool scheduled = string.Equals(ReadText(settings, "publish_time", "立即发布"), "定时发布", StringComparison.Ordinal);
        return new PublisherOptions(
            ReadBoolean(settings, "auto_publish", true),
            ReadBoolean(settings, "confirm_before_publish", false),
            automaticTaskOptions,
            scheduled ? Math.Clamp(ReadInt32(settings, "schedule_delay_minutes", 120), 10, 10080) : 0,
            Math.Clamp(ReadInt32(settings, "max_retries", 3) + 1, 1, 11));
    }

    public PublisherTaskOptions CreateAutomaticTaskOptions(DateTimeOffset now)
    {
        return AutomaticTaskOptions with
        {
            ScheduledAt = ScheduleDelayMinutes > 0 ? now.AddMinutes(ScheduleDelayMinutes) : null,
        };
    }

    private static bool ReadBoolean(IReadOnlyDictionary<string, string> settings, string key, bool fallback)
    {
        return settings.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed)
            ? parsed
            : fallback;
    }

    private static int ReadInt32(IReadOnlyDictionary<string, string> settings, string key, int fallback)
    {
        return settings.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? parsed
            : fallback;
    }

    private static string ReadText(IReadOnlyDictionary<string, string> settings, string key, string fallback)
    {
        return settings.TryGetValue(key, out string? value) ? value : fallback;
    }
}

internal sealed record PublisherTaskOptions(
    string TitleTemplate,
    string DescriptionTemplate,
    string Topics,
    string CoverPath,
    string OfficialActivity,
    string CollectionName,
    string Declaration,
    string VideoChapters,
    string Tags,
    string Location,
    string Hotspot,
    string Visibility,
    bool AllowSave,
    DateTimeOffset? ScheduledAt)
{
    public static PublisherTaskOptions CreateDefault(
        PublisherOptions options,
        IReadOnlyList<ExtensionVideoFileInfo> files)
    {
        PublisherTaskOptions defaults = options.CreateAutomaticTaskOptions(DateTimeOffset.Now);
        string titleTemplate = defaults.TitleTemplate;
        string descriptionTemplate = defaults.DescriptionTemplate;
        if (files.Count == 1)
        {
            ExtensionVideoFileInfo selected = files[0];
            PublisherQueueItem preview = new(
                "preview",
                selected.RoomUrl,
                selected.NickName,
                selected.Title,
                selected.FilePath,
                selected.FileSize,
                DateTimeOffset.UtcNow,
                PublisherQueueStatus.Pending,
                "manual");
            titleTemplate = PublisherTextFormatter.BuildTitle(defaults.TitleTemplate, preview);
            descriptionTemplate = PublisherTextFormatter.BuildDescription(defaults.DescriptionTemplate, string.Empty, preview);
        }
        return defaults with
        {
            TitleTemplate = titleTemplate,
            DescriptionTemplate = descriptionTemplate,
        };
    }

    public PublisherTaskOptions Normalize()
    {
        return this with
        {
            TitleTemplate = TitleTemplate?.Trim() ?? string.Empty,
            DescriptionTemplate = DescriptionTemplate?.Trim() ?? string.Empty,
            Topics = Topics?.Trim() ?? string.Empty,
            CoverPath = CoverPath?.Trim() ?? string.Empty,
            OfficialActivity = OfficialActivity?.Trim() ?? string.Empty,
            CollectionName = CollectionName?.Trim() ?? string.Empty,
            Declaration = Declaration?.Trim() ?? string.Empty,
            VideoChapters = VideoChapters?.Trim() ?? string.Empty,
            Tags = Tags?.Trim() ?? string.Empty,
            Location = Location?.Trim() ?? string.Empty,
            Hotspot = Hotspot?.Trim() ?? string.Empty,
            Visibility = PublisherVisibility.Normalize(Visibility),
        };
    }
}

internal static class PublisherVisibility
{
    public const string Public = "public";
    public const string Friends = "friends";
    public const string Private = "private";

    public static string Normalize(string? value)
    {
        return value is Public or Friends or Private ? value : Public;
    }

    public static string FromSetting(string? value)
    {
        return value switch
        {
            Friends or "好友可见" => Friends,
            Private or "仅自己可见" => Private,
            _ => Public,
        };
    }

    public static string ToDisplayText(string value)
    {
        return Normalize(value) switch
        {
            Friends => "好友可见",
            Private => "仅自己可见",
            _ => "公开",
        };
    }
}

internal static class PublisherTemplateVariables
{
    private static readonly (string Value, string DisplayValue)[] Variables =
    [
        ("{title}", "{直播标题}"),
        ("{nickname}", "{主播昵称}"),
        ("{filename}", "{文件名}"),
        ("{date}", "{日期}"),
    ];

    public static string ToDisplay(string value)
    {
        return Variables.Aggregate(value, (current, variable) => current.Replace(variable.Value, variable.DisplayValue, StringComparison.OrdinalIgnoreCase));
    }

    public static string ToStorage(string value)
    {
        return Variables.Aggregate(value, (current, variable) => current.Replace(variable.DisplayValue, variable.Value, StringComparison.OrdinalIgnoreCase));
    }
}

internal static class PublisherTextFormatter
{
    public static string BuildTitle(string template, PublisherQueueItem item)
    {
        string fallback = Path.GetFileNameWithoutExtension(item.FilePath);
        string title = ApplyTemplate(string.IsNullOrWhiteSpace(template) ? "{title}" : template, item);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = string.IsNullOrWhiteSpace(item.Title) ? fallback : item.Title;
        }
        return Truncate(CollapseWhitespace(title), 30);
    }

    public static string BuildDescription(string template, string topics, PublisherQueueItem item)
    {
        string description = ApplyTemplate(template, item).Trim();
        string topicText = string.Join(' ', topics
            .Split([' ', ',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(topic => topic.StartsWith('#') ? topic : $"#{topic}"));
        string combined = string.Join(' ', new[] { description, topicText }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return Truncate(combined, 1000);
    }

    private static string ApplyTemplate(string template, PublisherQueueItem item)
    {
        return template
            .Replace("{title}", item.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{nickname}", item.NickName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{filename}", Path.GetFileNameWithoutExtension(item.FilePath), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", item.QueuedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string value)
    {
        StringBuilder builder = new(value.Length);
        bool previousWhitespace = false;
        foreach (char character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }
                previousWhitespace = true;
                continue;
            }
            previousWhitespace = false;
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static string Truncate(string value, int maximumTextElements)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(value);
        int count = 0;
        int end = 0;
        while (count < maximumTextElements && enumerator.MoveNext())
        {
            end = enumerator.ElementIndex + enumerator.GetTextElement().Length;
            count++;
        }
        return end >= value.Length ? value : value[..end];
    }
}
