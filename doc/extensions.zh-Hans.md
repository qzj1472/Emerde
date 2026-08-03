# Emerde 扩展开发

扩展包使用 `.emerde-extension` 或 `.zip` 格式。将扩展包拖到 Emerde 的“扩展”页面即可安装或更新；安装过程会先解压到临时目录，校验完成后才替换旧版本。

扩展运行在完全信任环境。应用内扩展可以访问宿主对象、监控、录制、文件与界面。只安装可信来源的扩展。

## 清单

每个扩展包根目录必须有 `extension.json`。

```json
{
  "schema_version": 1,
  "id": "example.extension",
  "name": "示例扩展",
  "version": "1.0.0",
  "description": "扩展说明",
  "author": "作者",
  "icon": "icon.png",
  "execution_mode": "in_process",
  "entry_point": "Example.Extension.dll",
  "entry_type": "Example.Extension.Entry",
  "minimum_host_version": "1.6.7.0",
  "capabilities": ["monitor", "recording", "ui"],
  "permissions": ["monitor.override", "recorder.override", "ui.modify"],
  "timeout_seconds": 30,
  "settings": [
    {
      "key": "enabled_mode",
      "label": "工作模式",
      "type": "choice",
      "default": "safe",
      "options": ["safe", "advanced"]
    }
  ]
}
```

`id` 只能使用小写字母、数字、点和连字符。`version` 使用 `1.0.0` 形式。应用内扩展的入口必须是 DLL，并声明实现类的完整名称。

`icon` 为可选的扩展包内相对路径，支持 PNG、JPEG、BMP、GIF 和 ICO，文件不能超过 5 MB。扩展详情会按照 `settings` 定义直接生成文本框、密码框、选项和开关；保存后重新加载扩展设置。

`execution_mode` 可选值：

- `in_process`：加载到 Emerde 进程，拥有完整的扩展接口和高权限。
- `process`：独立进程，使用标准输入输出 JSON 协议；适合需要单独运行时或浏览器自动化的扩展。

## 应用内扩展

引用 Emerde 的程序集，实现 `IEmerdeExtension`：

```csharp
using Emerde.Plugins;

namespace Example.Extension;

public sealed class Entry : IEmerdeExtension
{
    public ValueTask InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        context.RegisterCleanup(() => ValueTask.CompletedTask);
        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

所有通过 `RegisterOverride`、`RegisterUi` 和 `RegisterCleanup` 注册的项目都会在扩展停用、卸载或软件退出时按反向顺序清理。扩展自行修改的静态状态、后台线程或事件订阅也必须用 `RegisterCleanup` 恢复。

## 核心覆盖点

使用 `RegisterOverride` 注册以下类型的实现：

| 合同名 | 实现类型 | 作用 |
|---|---|---|
| `core.stream-resolver` | `ExtensionStreamResolverOverride` | 修改或接管直播流解析 |
| `core.monitor` | `ExtensionMonitorOverride` | 修改或接管整轮监控调度 |
| `core.recorder` | `ExtensionRecorderOverride` | 修改录制启动参数或接管录制启动 |

每个覆盖点都会获得 `next` 回调。调用它会继续 Emerde 默认逻辑；不调用则由扩展自行处理。扩展抛出异常时，宿主会记录异常并执行默认逻辑。

```csharp
context.RegisterOverride(
    ExtensionContractNames.Recorder,
    (ExtensionRecorderOverride)((request, next) =>
    {
        request.StartInfo.Title = $"{request.StartInfo.Title} - 已处理";
        return next();
    }));
```

## UI 和宿主对象

`GetHostObject` 可取得以下对象：

- `host.application`
- `host.main-window`
- `host.main-view-model`
- `ui.main-content-overlay`

`RegisterUi(ExtensionContractNames.ExtensionDetail, element)` 会将 WPF 控件显示在扩展详情页，并在停用时自动移除。需要改动其他区域时，应保存原状态并通过 `RegisterCleanup` 恢复。

## 最终媒体事件

扩展可订阅 `ExtensionEventNames.MediaFinalized`。事件只在录制恢复、分段合并和自动转码完成并确定最终文件后发布；启动时恢复的任务同样会发布。`EventId` 在同一恢复任务和最终路径下保持稳定，扩展应将它持久化并用于幂等去重。

```csharp
context.Subscribe<ExtensionMediaFinalizedEvent>(
    ExtensionEventNames.MediaFinalized,
    async (payload, cancellationToken) =>
    {
        await queue.EnqueueAsync(payload, cancellationToken);
    });
```

读取平台 Cookie 需要在清单中声明 `credentials.platform-cookie.read`，并通过 `ExtensionContractNames.PlatformCookies` 获取 `IExtensionPlatformCookieProvider`。Cookie 不应写入扩展日志、命令行或明文配置文件。应用内扩展运行在完全信任环境，权限声明用于宿主接口授权和向用户说明能力，不构成进程级安全沙箱。

## 独立进程扩展

进程型扩展在清单中设置 `execution_mode` 为 `process`。支持 `executable`、`powershell`、`python` 和 `node` 运行时。运行时可以放在扩展包的 `runtime` 目录；不存在时才查找系统环境。

Emerde 通过标准输入传入一行 JSON：

```json
{
  "protocol_version": 1,
  "request_id": "请求标识",
  "method": "health.check",
  "settings": {},
  "payload": {}
}
```

扩展最后输出一行对应响应：

```json
{
  "protocol_version": 1,
  "request_id": "请求标识",
  "success": true,
  "message": "正常",
  "data": {}
}
```

独立进程不能直接修改 Emerde 内部 UI 或监控逻辑，但适合浏览器自动化、上传、网盘和外部服务集成。Emerde 主程序不内置任何平台的自动投稿逻辑；自动投稿应作为独立扩展单独开发，并把所需运行时放在扩展包内。
