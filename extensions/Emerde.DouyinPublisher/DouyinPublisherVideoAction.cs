using Emerde.Plugins;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.DouyinPublisher;

internal sealed class DouyinPublisherVideoAction : IExtensionVideoAction
{
    private readonly IExtensionContext context;
    private readonly PublisherStateStore stateStore;
    private readonly PublisherOptions options;
    private readonly IExtensionDialogService dialogService;

    public DouyinPublisherVideoAction(
        IExtensionContext context,
        PublisherStateStore stateStore,
        PublisherOptions options,
        IExtensionDialogService dialogService)
    {
        this.context = context;
        this.stateStore = stateStore;
        this.options = options;
        this.dialogService = dialogService;
    }

    public string Id => "douyin.publish";

    public string Label => "投稿到抖音";

    public int Order => 100;

    public bool CanExecute(IReadOnlyList<ExtensionVideoFileInfo> files)
    {
        return files.Count > 0;
    }

    public async Task ExecuteAsync(IReadOnlyList<ExtensionVideoFileInfo> files, CancellationToken cancellationToken)
    {
        PublisherTaskOptions defaults = PublisherTaskOptions.CreateDefault(options, files);
        DouyinPublishOptionsPanel panel = new(defaults, files.Count);
        ExtensionDialogResult dialogResult = await dialogService.ShowAsync(
            new ExtensionDialogRequest(
                "投稿到抖音",
                panel,
                "开始投稿",
                "取消",
                Validate: panel.ValidateOptions,
                ShowValidation: panel.ShowValidation,
                UseWideLayout: true),
            cancellationToken);
        if (dialogResult != ExtensionDialogResult.Primary)
        {
            return;
        }
        PublisherTaskOptions taskOptions = panel.GetOptions();
        ManualEnqueueResult result = await stateStore.EnqueueManualAsync(files, cancellationToken, taskOptions);
        context.Log("info", "manual_publish", "selected videos submitted to douyin publisher", result);
        if (result.Queued > 0)
        {
            string skipped = result.Skipped > 0 ? $"，跳过 {result.Skipped} 个" : string.Empty;
            Toast.Success($"已开始投稿 {result.Queued} 个视频{skipped}");
            return;
        }
        if (result.Missing > 0)
        {
            Toast.Warning("所选视频文件不存在");
            return;
        }
        Toast.Warning("所选视频已经在投稿任务中");
    }
}
