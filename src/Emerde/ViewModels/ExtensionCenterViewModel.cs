using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Emerde.Plugins;
using Emerde.Core;
using Emerde.Views;
using WindowsAPICodePack.Dialogs;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.ViewModels;

public partial class ExtensionCenterViewModel : ObservableObject, IDisposable
{
    private readonly ExtensionService extensionService = ExtensionService.Default;
    private bool initialized;
    private bool disposed;

    public ObservableCollection<ExtensionCardViewModel> Extensions { get; } = [];

    public ObservableCollection<ExtensionUiContribution> SelectedUiContributions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtensions))]
    [NotifyPropertyChangedFor(nameof(EnabledExtensionCount))]
    private ExtensionCardViewModel? selectedExtension;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isDragging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperationMessage))]
    private string operationMessage = string.Empty;

    [ObservableProperty]
    private bool operationFailed;

    public bool HasExtensions => Extensions.Count > 0;

    public int EnabledExtensionCount => Extensions.Count(item => item.IsEnabled);

    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);

    public ExtensionCenterViewModel()
    {
        ExtensionHostRuntime.UiContributionsChanged += UiContributionsChanged;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        ExtensionHostRuntime.UiContributionsChanged -= UiContributionsChanged;
        SelectedUiContributions.Clear();
    }

    partial void OnSelectedExtensionChanged(ExtensionCardViewModel? value)
    {
        foreach (ExtensionCardViewModel extension in Extensions)
        {
            extension.IsExpanded = false;
        }
        UpdateSelectedUiContributions();
    }

    [RelayCommand]
    private void ToggleExtensionDetails(ExtensionCardViewModel? extension)
    {
        if (extension == null)
        {
            return;
        }
        bool isExpanded = !extension.IsExpanded;
        SelectedExtension = extension;
        extension.IsExpanded = isExpanded;
        UpdateSelectedUiContributions();
    }

    public async Task InitializeAsync()
    {
        if (initialized)
        {
            return;
        }
        try
        {
            await extensionService.InitializeAsync();
            await ReloadCoreAsync();
            initialized = true;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            SetOperationError(e.Message);
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await ReloadCoreAsync();
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            SetOperationError(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Install()
    {
        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = false,
            Multiselect = true,
            Title = "选择 Emerde 扩展包",
        };
        dialog.Filters.Add(new CommonFileDialogFilter("Emerde 扩展包", "*.emerde-extension;*.zip"));
        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }
        await InstallFilesAsync(dialog.FileNames);
    }

    public async Task InstallFilesAsync(IEnumerable<string> paths)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        int installedCount = 0;
        List<string> errors = [];
        try
        {
            foreach (string path in paths.Where(ExtensionService.IsSupportedPackage).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await extensionService.InstallAsync(path);
                    installedCount++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
                {
                    errors.Add($"{Path.GetFileName(path)}：{e.Message}");
                }
            }
            await ReloadCoreAsync();
            if (errors.Count == 0)
            {
                SetOperationMessage(installedCount > 0 ? $"已安装 {installedCount} 个扩展" : "没有找到可安装的扩展包");
            }
            else
            {
                SetOperationError(string.Join(Environment.NewLine, errors));
            }
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            SetOperationError(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenExtensionsFolder()
    {
        Directory.CreateDirectory(Emerde.Core.AppPaths.ExtensionsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = Emerde.Core.AppPaths.ExtensionsDirectory,
            UseShellExecute = true,
        });
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectExtension(ExtensionCardViewModel? extension)
    {
        SelectedExtension = extension;
        UpdateSelectedUiContributions();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ToggleExtension(ExtensionCardViewModel? extension)
    {
        if (extension == null || IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await extensionService.SetEnabledAsync(extension.Id, !extension.IsEnabled);
            SetOperationMessage(extension.IsEnabled ? "扩展已停用" : "扩展已启用");
            await ReloadCoreAsync();
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            SetOperationError(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveExtension(ExtensionCardViewModel? extension)
    {
        if (extension == null || IsBusy)
        {
            return;
        }
        Window? owner = System.Windows.Application.Current?.MainWindow;
        ContentDialog dialog = new()
        {
            Title = "移除扩展",
            Content = $"确定移除扩展“{extension.Name}”吗？扩展设置也会被移除。",
            PrimaryButtonText = "是",
            CloseButtonText = "否",
            DefaultButton = ContentDialogButton.Close,
            FocusVisualStyle = null,
            Style = System.Windows.Application.Current?.TryFindResource("DefaultVioletaContentDialogStyle") as System.Windows.Style,
        };
        using DialogBlurScope blurScope = DialogBlurScope.ForLightDismiss(owner, dialog);
        ContentDialogResult result = await WindowSizing.ShowContentDialogAsync(dialog, owner);
        if (result != ContentDialogResult.Primary)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await extensionService.UninstallAsync(extension.Id);
            SetOperationMessage("扩展已移除");
            await ReloadCoreAsync();
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            SetOperationError(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettings(ExtensionCardViewModel? extension)
    {
        if (extension == null || IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await extensionService.SaveSettingsAsync(extension.Id, extension.Settings.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));
            SetOperationMessage("扩展设置已保存");
            await ReloadCoreAsync();
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            SetOperationError(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task HealthCheck(ExtensionCardViewModel? extension)
    {
        if (extension == null || IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            ExtensionExecutionResult result = await extensionService.ExecuteAsync(extension.Id, "health.check", new { hostVersion = extensionService.GetType().Assembly.GetName().Version?.ToString() });
            if (result.Success)
            {
                SetOperationMessage(string.IsNullOrWhiteSpace(result.Message) ? "扩展响应正常" : result.Message);
            }
            else
            {
                SetOperationError(result.Message);
            }
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            SetOperationError(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadCoreAsync()
    {
        string? selectedId = SelectedExtension?.Id;
        IReadOnlyList<InstalledExtensionInfo> installed = await extensionService.GetInstalledExtensionsAsync();
        Extensions.Clear();
        foreach (InstalledExtensionInfo extension in installed)
        {
            ExtensionCardViewModel card = new(extension);
            card.ApplySettings(await extensionService.GetSettingsAsync(extension.Manifest.Id));
            Extensions.Add(card);
        }
        SelectedExtension = Extensions.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? Extensions.FirstOrDefault();
        UpdateSelectedUiContributions();
        OnPropertyChanged(nameof(HasExtensions));
        OnPropertyChanged(nameof(EnabledExtensionCount));
    }

    private void UiContributionsChanged(object? sender, EventArgs e)
    {
        if (disposed)
        {
            return;
        }
        UpdateSelectedUiContributions();
    }

    private void UpdateSelectedUiContributions()
    {
        SelectedUiContributions.Clear();
        if (SelectedExtension == null)
        {
            return;
        }
        foreach (ExtensionUiContribution contribution in ExtensionHostRuntime.GetUiContributionsSnapshot()
                     .Where(item => item.RegionName.Equals(ExtensionContractNames.ExtensionDetail, StringComparison.OrdinalIgnoreCase)
                         && item.ExtensionId.Equals(SelectedExtension.Id, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Order))
        {
            SelectedUiContributions.Add(contribution);
        }
    }

    private void SetOperationMessage(string message)
    {
        OperationFailed = false;
        OperationMessage = message;
    }

    private void SetOperationError(string message)
    {
        OperationFailed = true;
        OperationMessage = message;
    }
}

public partial class ExtensionCardViewModel : ObservableObject
{
    public ExtensionManifest Manifest { get; }

    public string InstallDirectory { get; }

    public string Id => Manifest.Id;

    public string Name => Manifest.Name;

    public string Version => Manifest.Version;

    public string Description => string.IsNullOrWhiteSpace(Manifest.Description) ? "未提供扩展说明" : Manifest.Description;

    public string Author => string.IsNullOrWhiteSpace(Manifest.Author) ? "未知作者" : Manifest.Author;

    public ImageSource? IconSource { get; }

    public bool HasIcon => IconSource != null;

    public string ExecutionText => Manifest.ExecutionMode.Equals("in_process", StringComparison.OrdinalIgnoreCase) ? "应用内运行 · 高权限" : $"独立进程 · {Manifest.Runtime}";

    public string CapabilityText => Manifest.Capabilities.Length == 0 ? "未声明能力" : string.Join(" · ", Manifest.Capabilities);

    public string PermissionText => Manifest.Permissions.Length == 0 ? "未声明权限" : string.Join("、", Manifest.Permissions);

    public bool RequiresTrustWarning => Manifest.ExecutionMode.Equals("in_process", StringComparison.OrdinalIgnoreCase);

    public bool CanHealthCheck => Manifest.ExecutionMode.Equals("process", StringComparison.OrdinalIgnoreCase);

    public string ToggleActionText => IsEnabled ? "停用" : "启用";

    public string DetailsActionText => IsExpanded ? "收起详情" : "详细信息";

    public string StateText => !IsValid ? "清单无效" : IsLoaded ? "运行中" : IsEnabled ? "已启用" : "已停用";

    public bool HasSettings => Settings.Count > 0;

    public bool HasSecondarySettings => SecondarySettings.Count > 0;

    public bool HasOnlyPrimarySettings => !HasSecondarySettings;

    public bool IsValid { get; }

    public string ValidationError { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleActionText))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsActionText))]
    private bool isExpanded;

    public ObservableCollection<ExtensionSettingViewModel> Settings { get; } = [];

    public ObservableCollection<ExtensionSettingViewModel> PrimarySettings { get; } = [];

    public ObservableCollection<ExtensionSettingViewModel> SecondarySettings { get; } = [];

    public ExtensionCardViewModel(InstalledExtensionInfo info)
    {
        Manifest = info.Manifest;
        InstallDirectory = info.InstallDirectory;
        IsValid = info.IsValid;
        ValidationError = info.ValidationError;
        IsEnabled = info.IsEnabled;
        IsLoaded = info.IsLoaded;
        IconSource = LoadIcon(info.Manifest, info.InstallDirectory);
        string primarySection = string.Empty;
        string secondarySection = string.Empty;
        foreach (ExtensionSettingDefinition definition in Manifest.Settings)
        {
            string previousSection = definition.Column == 1 ? secondarySection : primarySection;
            bool showSectionHeader = !string.IsNullOrWhiteSpace(definition.Section)
                && !definition.Section.Equals(previousSection, StringComparison.Ordinal);
            ExtensionSettingViewModel setting = new(definition, showSectionHeader);
            Settings.Add(setting);
            if (definition.Column == 1)
            {
                SecondarySettings.Add(setting);
                secondarySection = definition.Section;
            }
            else
            {
                PrimarySettings.Add(setting);
                primarySection = definition.Section;
            }
        }
        foreach (ExtensionSettingViewModel setting in Settings)
        {
            setting.PropertyChanged += SettingPropertyChanged;
        }
        UpdateConditionalSettingVisibility();
    }

    public void ApplySettings(IReadOnlyDictionary<string, string> values)
    {
        foreach (ExtensionSettingViewModel setting in Settings)
        {
            if (values.TryGetValue(setting.Key, out string? value))
            {
                setting.Value = value;
            }
        }
    }

    private void SettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExtensionSettingViewModel.Value))
        {
            UpdateConditionalSettingVisibility();
        }
    }

    private void UpdateConditionalSettingVisibility()
    {
        foreach (ExtensionSettingViewModel setting in Settings)
        {
            string dependencyKey = setting.Definition.VisibleWhenKey;
            if (string.IsNullOrWhiteSpace(dependencyKey))
            {
                setting.IsVisible = true;
                continue;
            }
            ExtensionSettingViewModel? dependency = Settings.FirstOrDefault(candidate => candidate.Key.Equals(dependencyKey, StringComparison.OrdinalIgnoreCase));
            setting.IsVisible = dependency != null
                && dependency.Value.Equals(setting.Definition.VisibleWhenValue, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ImageSource? LoadIcon(ExtensionManifest manifest, string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(manifest.Icon))
        {
            return null;
        }
        try
        {
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 84;
            image.UriSource = new Uri(Path.Combine(installDirectory, manifest.Icon), UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}

public partial class ExtensionSettingViewModel : ObservableObject
{
    public ExtensionSettingDefinition Definition { get; }

    public string Key => Definition.Key;

    public string Label => Definition.Label;

    public string Description => Definition.Description;

    public string Section => Definition.Section;

    public bool ShowSectionHeader { get; }

    public bool IsBoolean => Definition.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase);

    public bool IsChoice => Definition.Type.Equals("choice", StringComparison.OrdinalIgnoreCase);

    public bool IsPassword => Definition.Type.Equals("password", StringComparison.OrdinalIgnoreCase);

    public bool HasTemplateOptions => !IsBoolean && !IsChoice && !IsPassword && TemplateOptions.Count > 0;

    public ObservableCollection<string> Options { get; }

    public ObservableCollection<ExtensionTemplateOptionViewModel> TemplateOptions { get; }

    public string DisplayValue
    {
        get => ExtensionTemplateOptionViewModel.ToDisplayValue(Value);
        set
        {
            string storageValue = ExtensionTemplateOptionViewModel.ToStorageValue(value);
            if (!string.Equals(Value, storageValue, StringComparison.Ordinal))
            {
                Value = storageValue;
            }
        }
    }

    [ObservableProperty]
    private string value;

    [ObservableProperty]
    private bool booleanValue;

    [ObservableProperty]
    private bool isVisible = true;

    public ExtensionSettingViewModel(ExtensionSettingDefinition definition, bool showSectionHeader = false)
    {
        Definition = definition;
        ShowSectionHeader = showSectionHeader;
        Options = new ObservableCollection<string>(definition.Options);
        TemplateOptions = new ObservableCollection<ExtensionTemplateOptionViewModel>(definition.Options.Select(ExtensionTemplateOptionViewModel.Create));
        value = definition.DefaultValue ?? string.Empty;
        booleanValue = bool.TryParse(value, out bool parsed) && parsed;
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayValue));
        if (IsBoolean && bool.TryParse(value, out bool parsed))
        {
            BooleanValue = parsed;
        }
    }

    partial void OnBooleanValueChanged(bool value)
    {
        if (IsBoolean)
        {
            Value = value.ToString();
        }
    }

    [RelayCommand]
    private void AppendTemplateOption(ExtensionTemplateOptionViewModel? option)
    {
        if (option == null || string.IsNullOrWhiteSpace(option.Value))
        {
            return;
        }

        Value = string.IsNullOrEmpty(Value) || char.IsWhiteSpace(Value[^1])
            ? Value + option.Value
            : Value + " " + option.Value;
    }
}

public sealed record ExtensionTemplateOptionViewModel(string Value, string DisplayName)
{
    private static readonly (string Value, string DisplayName)[] KnownOptions =
    [
        ("{title}", "直播标题"),
        ("{nickname}", "主播昵称"),
        ("{filename}", "文件名"),
        ("{date}", "日期"),
    ];

    public string DisplayValue => DisplayName == Value ? Value : $"{{{DisplayName}}}";

    public static ExtensionTemplateOptionViewModel Create(string value)
    {
        string displayName = KnownOptions.FirstOrDefault(option => option.Value.Equals(value, StringComparison.OrdinalIgnoreCase)).DisplayName ?? value;
        return new ExtensionTemplateOptionViewModel(value, displayName);
    }

    public static string ToDisplayValue(string value)
    {
        return KnownOptions.Aggregate(value, (current, option) => current.Replace(option.Value, $"{{{option.DisplayName}}}", StringComparison.OrdinalIgnoreCase));
    }

    public static string ToStorageValue(string value)
    {
        return KnownOptions.Aggregate(value, (current, option) => current.Replace($"{{{option.DisplayName}}}", option.Value, StringComparison.OrdinalIgnoreCase));
    }
}
