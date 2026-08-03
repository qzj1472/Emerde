using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Emerde.Plugins;
using Microsoft.Win32;
using Wpf.Ui.Violeta.Controls.Primitives;

namespace Emerde.DouyinPublisher;

internal sealed class DouyinPublishOptionsPanel : ScrollViewer
{
    private static readonly (string Value, string DisplayName)[] TitleTemplateOptions =
    [
        ("{title}", "直播标题"),
        ("{nickname}", "主播昵称"),
        ("{filename}", "文件名"),
        ("{date}", "日期"),
    ];
    private readonly TextBox titleInput = CreateTextBox();
    private readonly TextBox descriptionInput = CreateTextBox();
    private readonly TextBox topicsInput = CreateTextBox();
    private readonly TextBox coverInput = CreateTextBox();
    private readonly TextBox activityInput = CreateTextBox();
    private readonly TextBox collectionInput = CreateTextBox();
    private readonly ComboBox declarationSelector = CreateComboBox();
    private readonly TextBox chaptersInput = CreateTextBox();
    private readonly TextBox tagsInput = CreateTextBox();
    private readonly TextBox locationInput = CreateTextBox();
    private readonly TextBox hotspotInput = CreateTextBox();
    private readonly ComboBox visibilitySelector = CreateComboBox();
    private readonly CheckBox allowSaveCheckBox = new() { Content = "允许下载作品", IsChecked = true };
    private readonly ComboBox publishTimeSelector = CreateComboBox();
    private readonly DatePicker scheduleDatePicker = new() { Height = 34 };
    private readonly TextBox scheduleTimeInput = CreateTextBox();
    private readonly Grid schedulePanel = new();
    private readonly TextBlock validationText = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(216, 59, 1)),
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 10, 0, 0),
    };

    public DouyinPublishOptionsPanel(PublisherTaskOptions options, int fileCount)
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        CanContentScroll = false;
        PanningMode = PanningMode.VerticalOnly;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        Focusable = false;
        FocusVisualStyle = null;

        titleInput.Text = PublisherTemplateVariables.ToDisplay(options.TitleTemplate);
        titleInput.MaxLength = 120;
        descriptionInput.Text = options.DescriptionTemplate;
        descriptionInput.AcceptsReturn = true;
        descriptionInput.Height = 76;
        descriptionInput.TextWrapping = TextWrapping.Wrap;
        topicsInput.Text = options.Topics;
        coverInput.Text = options.CoverPath;
        activityInput.Text = options.OfficialActivity;
        collectionInput.Text = options.CollectionName;
        string[] declarationOptions = ["不声明", "内容由我原创", "内容由AI生成", "内容为转载"];
        declarationSelector.ItemsSource = declarationOptions;
        string declaration = string.IsNullOrWhiteSpace(options.Declaration) ? "不声明" : options.Declaration;
        declarationSelector.SelectedItem = declarationOptions.Contains(declaration, StringComparer.Ordinal) ? declaration : "不声明";
        chaptersInput.Text = options.VideoChapters;
        chaptersInput.AcceptsReturn = true;
        chaptersInput.Height = 66;
        chaptersInput.TextWrapping = TextWrapping.Wrap;
        tagsInput.Text = options.Tags;
        locationInput.Text = options.Location;
        hotspotInput.Text = options.Hotspot;
        visibilitySelector.ItemsSource = new[] { "公开", "好友可见", "仅自己可见" };
        visibilitySelector.SelectedIndex = options.Visibility switch
        {
            PublisherVisibility.Friends => 1,
            PublisherVisibility.Private => 2,
            _ => 0,
        };
        allowSaveCheckBox.IsChecked = options.AllowSave;
        publishTimeSelector.ItemsSource = new[] { "立即发布", "定时发布" };
        publishTimeSelector.SelectedIndex = options.ScheduledAt.HasValue ? 1 : 0;
        DateTime localSchedule = options.ScheduledAt?.LocalDateTime ?? DateTime.Now.AddHours(2);
        scheduleDatePicker.SelectedDate = localSchedule.Date;
        scheduleDatePicker.SetResourceReference(Control.BackgroundProperty, "EmerdeCardBrush");
        scheduleTimeInput.Text = localSchedule.ToString("HH:mm", CultureInfo.InvariantCulture);
        publishTimeSelector.SelectionChanged += (_, _) => UpdateScheduleVisibility();

        StackPanel root = new() { Margin = new Thickness(0, 2, 8, 2) };
        root.Children.Add(CreateSummary(fileCount));
        Grid columns = new() { Margin = new Thickness(0, 0, 0, 2) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel primaryColumn = new();
        primaryColumn.Children.Add(CreateSectionTitle("基础信息"));
        primaryColumn.Children.Add(CreateRow("作品标题", CreateTitleTemplateEditor(), "选择直播标题、主播昵称、文件名或日期变量，也可以输入自定义文字，最终标题最多 30 个字符。"));
        primaryColumn.Children.Add(CreateRow("作品简介", descriptionInput, "支持与标题相同的变量，留空时不填写简介。"));
        primaryColumn.Children.Add(CreateRow("话题", topicsInput, "用空格或逗号分隔，扩展会自动补充 #。"));
        primaryColumn.Children.Add(CreateCoverRow());
        primaryColumn.Children.Add(CreateRow("官方活动", activityInput, "填写活动的完整名称，留空时不选择。"));
        primaryColumn.Children.Add(CreateRow("添加合集", collectionInput, "填写已有合集名称，留空时不添加合集。"));
        primaryColumn.Children.Add(CreateRow("自主声明", declarationSelector, "选择本次投稿使用的内容声明。"));
        primaryColumn.Children.Add(CreateRow("视频章节", chaptersInput, "每行填写一个时间点和章节名称，留空时不添加。"));
        columns.Children.Add(primaryColumn);
        StackPanel secondaryColumn = new();
        secondaryColumn.Children.Add(CreateSectionTitle("扩展信息"));
        secondaryColumn.Children.Add(CreateRow("添加标签", tagsInput, "填写本次投稿需要添加的标签，留空时不添加。"));
        secondaryColumn.Children.Add(CreateRow("添加地点", locationInput, "填写本次投稿关联的地点，留空时不添加。"));
        secondaryColumn.Children.Add(CreateRow("关联热点", hotspotInput, "填写本次投稿关联的热点名称，留空时不关联。"));
        secondaryColumn.Children.Add(CreateSectionTitle("发布设置"));
        secondaryColumn.Children.Add(CreateRow("谁可以看", visibilitySelector, "选择作品发布后的可见范围。"));
        secondaryColumn.Children.Add(CreateRow("保存权限", allowSaveCheckBox, "允许或禁止其他用户下载本次投稿的作品。"));
        secondaryColumn.Children.Add(CreateRow("发布时间", publishTimeSelector, "立即发布，或选择日期和时间定时发布。"));
        BuildSchedulePanel();
        secondaryColumn.Children.Add(schedulePanel);
        Grid.SetColumn(secondaryColumn, 2);
        columns.Children.Add(secondaryColumn);
        root.Children.Add(columns);
        root.Children.Add(validationText);
        Content = root;
        UpdateScheduleVisibility();
    }

    public PublisherTaskOptions GetOptions()
    {
        DateTimeOffset? scheduledAt = null;
        if (publishTimeSelector.SelectedIndex == 1 && TryReadSchedule(out DateTimeOffset schedule))
        {
            scheduledAt = schedule;
        }
        string visibility = visibilitySelector.SelectedIndex switch
        {
            1 => PublisherVisibility.Friends,
            2 => PublisherVisibility.Private,
            _ => PublisherVisibility.Public,
        };
        string declaration = declarationSelector.SelectedItem as string ?? "不声明";
        return new PublisherTaskOptions(
            PublisherTemplateVariables.ToStorage(titleInput.Text),
            descriptionInput.Text,
            topicsInput.Text,
            coverInput.Text,
            activityInput.Text,
            collectionInput.Text,
            declaration == "不声明" ? string.Empty : declaration,
            chaptersInput.Text,
            tagsInput.Text,
            locationInput.Text,
            hotspotInput.Text,
            visibility,
            allowSaveCheckBox.IsChecked == true,
            scheduledAt).Normalize();
    }

    public string? ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(titleInput.Text))
        {
            return "作品标题不能为空";
        }
        if (!string.IsNullOrWhiteSpace(coverInput.Text))
        {
            string coverPath;
            try
            {
                coverPath = Path.GetFullPath(coverInput.Text.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "封面文件路径无效";
            }
            if (!File.Exists(coverPath))
            {
                return "所选封面文件不存在";
            }
            string extension = Path.GetExtension(coverPath);
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return "封面只支持 JPG、PNG 或 WebP 图片";
            }
        }
        if (publishTimeSelector.SelectedIndex == 1)
        {
            if (!TryReadSchedule(out DateTimeOffset schedule))
            {
                return "请输入有效的定时发布时间";
            }
            if (schedule <= DateTimeOffset.Now.AddMinutes(10))
            {
                return "定时发布时间至少需要晚于当前时间 10 分钟";
            }
        }
        return null;
    }

    public void ShowValidation(string message)
    {
        validationText.Text = message;
        validationText.Visibility = Visibility.Visible;
    }

    private static TextBlock CreateSummary(int fileCount)
    {
        TextBlock text = new()
        {
            Text = fileCount == 1 ? "设置本次投稿内容" : $"为选中的 {fileCount} 个视频设置投稿内容",
            Margin = new Thickness(0, 0, 0, 6),
            FontSize = 13,
            LineHeight = 20,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return text;
    }

    private static TextBlock CreateSectionTitle(string text)
    {
        TextBlock title = new()
        {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 10),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "SystemAccentColorPrimaryBrush");
        return title;
    }

    private static Grid CreateRow(string label, UIElement control, string description)
    {
        Grid row = new() { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        TextBlock labelText = new()
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            ToolTip = description,
        };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        ToolTipService.SetToolTip(control, description);
        row.Children.Add(labelText);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private UIElement CreateTitleTemplateEditor()
    {
        StackPanel panel = new();
        panel.Children.Add(titleInput);
        WrapPanel options = new() { Margin = new Thickness(0, 6, 0, 0) };
        foreach ((string value, string displayName) in TitleTemplateOptions)
        {
            Button button = new()
            {
                Content = displayName,
                Height = 28,
                MinWidth = 0,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 0, 8, 0),
                FocusVisualStyle = null,
                ToolTip = $"插入{displayName}变量",
            };
            button.Click += (_, _) => InsertTitleTemplateOption(PublisherTemplateVariables.ToDisplay(value));
            options.Children.Add(button);
        }
        panel.Children.Add(options);
        return panel;
    }

    private void InsertTitleTemplateOption(string option)
    {
        int start = Math.Clamp(titleInput.SelectionStart, 0, titleInput.Text.Length);
        int length = Math.Clamp(titleInput.SelectionLength, 0, titleInput.Text.Length - start);
        string next = titleInput.Text.Remove(start, length).Insert(start, option);
        if (next.Length > titleInput.MaxLength)
        {
            return;
        }
        titleInput.Text = next;
        titleInput.SelectionStart = start + option.Length;
        titleInput.SelectionLength = 0;
        titleInput.Focus();
    }

    private Grid CreateCoverRow()
    {
        Grid inputRow = new();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition());
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.Children.Add(coverInput);
        Button chooseButton = new()
        {
            Content = "选择",
            Height = 34,
            MinWidth = 68,
            Margin = new Thickness(10, 0, 0, 0),
            FocusVisualStyle = null,
        };
        chooseButton.Click += (_, _) => ChooseCover();
        Grid.SetColumn(chooseButton, 1);
        inputRow.Children.Add(chooseButton);
        return CreateRow("视频封面", inputRow, "选择 JPG、PNG 或 WebP 图片，留空时使用抖音默认封面。");
    }

    private void BuildSchedulePanel()
    {
        schedulePanel.Margin = new Thickness(104, 0, 0, 10);
        schedulePanel.ColumnDefinitions.Add(new ColumnDefinition());
        schedulePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        scheduleDatePicker.Margin = new Thickness(0, 0, 10, 0);
        schedulePanel.Children.Add(scheduleDatePicker);
        scheduleTimeInput.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTipService.SetToolTip(scheduleDatePicker, "选择定时发布日期，发布时间至少晚于当前时间 10 分钟。");
        ToolTipService.SetToolTip(scheduleTimeInput, "输入 24 小时制时间，例如 20:30。");
        Grid.SetColumn(scheduleTimeInput, 1);
        schedulePanel.Children.Add(scheduleTimeInput);
    }

    private void UpdateScheduleVisibility()
    {
        schedulePanel.Visibility = publishTimeSelector.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool TryReadSchedule(out DateTimeOffset schedule)
    {
        schedule = default;
        if (!scheduleDatePicker.SelectedDate.HasValue
            || !TimeSpan.TryParseExact(scheduleTimeInput.Text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan time))
        {
            return false;
        }
        DateTime local = DateTime.SpecifyKind(scheduleDatePicker.SelectedDate.Value.Date + time, DateTimeKind.Unspecified);
        schedule = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return true;
    }

    private void ChooseCover()
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择视频封面",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.webp",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) == true)
        {
            coverInput.Text = dialog.FileName;
        }
    }

    private static TextBox CreateTextBox()
    {
        TextBox textBox = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 34,
            Padding = new Thickness(10, 6, 10, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null,
            Style = Application.Current?.TryFindResource("ExtensionTextInputStyle") as Style,
            Effect = null,
        };
        return textBox;
    }

    private static ComboBox CreateComboBox()
    {
        ComboBox comboBox = new()
        {
            Height = 34,
            FocusVisualStyle = null,
            Style = Application.Current?.TryFindResource("ExtensionChoiceInputStyle") as Style,
            Effect = null,
        };
        comboBox.Resources[typeof(ThemeShadowChrome)] = CreateShadowDisabledStyle();
        comboBox.Resources["ComboBoxDropDownBorderBrush"] = Brushes.Transparent;
        comboBox.Resources["ControlElevationBorderBrush"] = Brushes.Transparent;
        comboBox.DropDownOpened += (_, _) => DisablePopupShadows(comboBox);
        return comboBox;
    }

    private static void DisablePopupShadows(ComboBox comboBox)
    {
        if (comboBox.Template.FindName("PART_Popup", comboBox) is not Popup popup)
        {
            return;
        }

        popup.Resources[typeof(ThemeShadowChrome)] = CreateShadowDisabledStyle();
        popup.Resources["ComboBoxDropDownBorderBrush"] = Brushes.Transparent;
        popup.Resources["ControlElevationBorderBrush"] = Brushes.Transparent;
        popup.Effect = null;
        if (popup.Child is not DependencyObject child)
        {
            return;
        }

        foreach (ThemeShadowChrome shadowChrome in EnumerateVisualTree(child).OfType<ThemeShadowChrome>())
        {
            shadowChrome.IsShadowEnabled = false;
        }
    }

    private static Style CreateShadowDisabledStyle()
    {
        Style style = new(typeof(ThemeShadowChrome));
        style.Setters.Add(new Setter(ThemeShadowChrome.IsShadowEnabledProperty, false));
        return style;
    }

    private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        yield return root;
        if (root is not Visual and not Visual3D)
        {
            yield break;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            foreach (DependencyObject child in EnumerateVisualTree(VisualTreeHelper.GetChild(root, index)))
            {
                yield return child;
            }
        }
    }

}
