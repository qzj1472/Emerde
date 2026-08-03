using Emerde.Plugins;
using Emerde.ViewModels;

namespace Emerde.DouyinPublisher;

public sealed class ExtensionEntry : IEmerdeExtension
{
    private DouyinPublisherPanel? panel;
    private DouyinPublisherWorker? worker;

    public ValueTask InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        MainViewModel mainViewModel = context.GetHostObject(ExtensionContractNames.MainViewModel) as MainViewModel
            ?? throw new InvalidOperationException("Emerde 主界面尚未就绪");
        IExtensionPlatformCookieProvider? cookieProvider = context.GetHostObject(ExtensionContractNames.PlatformCookies) as IExtensionPlatformCookieProvider;
        IExtensionDialogService dialogService = context.GetHostObject(ExtensionContractNames.DialogService) as IExtensionDialogService
            ?? throw new InvalidOperationException("Emerde 投稿弹窗服务尚未就绪");
        PublisherStateStore stateStore = new(context.DataDirectory);
        PublisherOptions options = PublisherOptions.From(context.Settings);
        worker = new DouyinPublisherWorker(context, stateStore, cookieProvider, options);
        panel = new DouyinPublisherPanel(
            mainViewModel,
            stateStore,
            worker,
            () => !string.IsNullOrWhiteSpace(cookieProvider?.GetCookie("Douyin")));
        context.RegisterUi(ExtensionContractNames.ExtensionDetail, panel);
        context.RegisterOverride(ExtensionContractNames.VideoListActions, new DouyinPublisherVideoAction(context, stateStore, options, dialogService));
        context.Subscribe<ExtensionMediaFinalizedEvent>(
            ExtensionEventNames.MediaFinalized,
            (payload, token) => stateStore.EnqueueAsync(payload, options.CreateAutomaticTaskOptions(DateTimeOffset.Now), token));
        context.RegisterCleanup(DisposeRuntimeAsync);
        worker.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask ShutdownAsync(CancellationToken cancellationToken)
    {
        await DisposeRuntimeAsync();
    }

    private async ValueTask DisposeRuntimeAsync()
    {
        panel?.Dispose();
        panel = null;
        if (worker != null)
        {
            await worker.DisposeAsync();
            worker = null;
        }
    }
}
