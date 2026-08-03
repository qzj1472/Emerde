using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Emerde.Core;

namespace Emerde.Plugins;

public sealed class InstalledExtensionInfo
{
    public required ExtensionManifest Manifest { get; init; }

    public required string InstallDirectory { get; init; }

    public required bool IsEnabled { get; init; }

    public required bool IsLoaded { get; init; }

    public required bool IsValid { get; init; }

    public required string ValidationError { get; init; }
}

public sealed class ExtensionInstallResult
{
    public required InstalledExtensionInfo Extension { get; init; }

    public required bool IsUpdate { get; init; }
}

public sealed partial class ExtensionService
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumPackageEntryCount = 4096;
    private const long MaximumPackageExpandedBytes = 2L * 1024L * 1024L * 1024L;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, LoadedExtension> loadedExtensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Process> runningProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly string extensionsDirectory;
    private readonly string extensionStateFilePath;
    private ExtensionStateDocument state = new();
    private bool isInitialized;

    public static ExtensionService Default { get; } = new(AppPaths.ExtensionsDirectory, AppPaths.ExtensionStateFilePath);

    internal ExtensionService(string extensionsDirectory, string? extensionStateFilePath = null)
    {
        this.extensionsDirectory = Path.GetFullPath(extensionsDirectory);
        this.extensionStateFilePath = extensionStateFilePath ?? Path.Combine(this.extensionsDirectory, "extensions-state.json");
    }

    public static bool IsSupportedPackage(string path)
    {
        string extension = Path.GetExtension(path);
        return File.Exists(path)
            && (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".emerde-extension", StringComparison.OrdinalIgnoreCase));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (isInitialized)
            {
                return;
            }
            Directory.CreateDirectory(extensionsDirectory);
            state = await LoadStateAsync(cancellationToken);
            bool stateChanged = false;
            foreach (InstalledExtensionInfo extension in GetInstalledExtensionsCore().Where(item => item.IsEnabled && item.IsValid))
            {
                try
                {
                    await LoadInProcessExtensionIfNeededAsync(extension, cancellationToken);
                }
                catch (Exception e)
                {
                    AppSessionLogger.WriteException(e);
                    GetState(extension.Manifest.Id).Enabled = false;
                    stateChanged = true;
                }
            }
            if (stateChanged)
            {
                await SaveStateAsync(cancellationToken);
            }
            isInitialized = true;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<InstalledExtensionInfo>> GetInstalledExtensionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return GetInstalledExtensionsCore();
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<ExtensionInstallResult> InstallAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!IsSupportedPackage(packagePath))
        {
            throw new InvalidDataException("只支持 .emerde-extension 或 .zip 扩展包");
        }
        await EnsureInitializedAsync(cancellationToken);
        await operationGate.WaitAsync(cancellationToken);
        string stagingDirectory = Path.Combine(extensionsDirectory, $".installing-{Guid.NewGuid():N}");
        string backupDirectory = string.Empty;
        string targetDirectory = string.Empty;
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            ExtractPackage(packagePath, stagingDirectory);
            string packageRoot = FindPackageRoot(stagingDirectory);
            ExtensionManifest manifest = ReadManifest(packageRoot);
            ValidateManifest(manifest, packageRoot);
            targetDirectory = Path.Combine(extensionsDirectory, manifest.Id);
            bool isUpdate = Directory.Exists(targetDirectory);
            bool wasEnabled = GetState(manifest.Id).Enabled;
            InstalledExtensionInfo? previousExtension = isUpdate ? CreateInstalledInfo(targetDirectory) : null;
            if (wasEnabled)
            {
                await UnloadExtensionAsync(manifest.Id, cancellationToken);
            }
            if (isUpdate)
            {
                backupDirectory = Path.Combine(extensionsDirectory, $".replacing-{manifest.Id}-{Guid.NewGuid():N}");
                Directory.Move(targetDirectory, backupDirectory);
            }
            try
            {
                Directory.Move(packageRoot, targetDirectory);
                InstalledExtensionInfo installed = CreateInstalledInfo(targetDirectory);
                if (wasEnabled)
                {
                    await LoadInProcessExtensionIfNeededAsync(installed, cancellationToken);
                }
                TryDeleteDirectory(backupDirectory);
            }
            catch
            {
                TryDeleteDirectory(targetDirectory);
                if (!string.IsNullOrWhiteSpace(backupDirectory) && Directory.Exists(backupDirectory))
                {
                    Directory.Move(backupDirectory, targetDirectory);
                }
                if (wasEnabled && previousExtension != null && Directory.Exists(targetDirectory))
                {
                    await LoadInProcessExtensionIfNeededAsync(CreateInstalledInfo(targetDirectory), cancellationToken);
                }
                throw;
            }
            await SaveStateAsync(cancellationToken);
            AppSessionLogger.Event("info", "extension", isUpdate ? "updated" : "installed", "extension package installed", new
            {
                manifest.Id,
                manifest.Name,
                manifest.Version,
                packagePath,
                isUpdate,
            });
            return new ExtensionInstallResult
            {
                Extension = CreateInstalledInfo(targetDirectory),
                IsUpdate = isUpdate,
            };
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
            operationGate.Release();
        }
    }

    public async Task SetEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            InstalledExtensionInfo extension = GetExtensionCore(extensionId);
            if (!extension.IsValid)
            {
                throw new InvalidOperationException(extension.ValidationError);
            }
            ExtensionPersistedState persistedState = GetState(extensionId);
            bool previousEnabled = persistedState.Enabled;
            try
            {
                if (enabled)
                {
                    await LoadInProcessExtensionIfNeededAsync(extension, cancellationToken);
                }
                else
                {
                    await UnloadExtensionAsync(extensionId, cancellationToken);
                }
                persistedState.Enabled = enabled;
                await SaveStateAsync(cancellationToken);
            }
            catch
            {
                persistedState.Enabled = previousEnabled;
                try
                {
                    if (previousEnabled)
                    {
                        await LoadInProcessExtensionIfNeededAsync(CreateInstalledInfo(extension.InstallDirectory), CancellationToken.None);
                    }
                    else
                    {
                        await UnloadExtensionAsync(extensionId, CancellationToken.None);
                    }
                }
                catch (Exception restoreException)
                {
                    persistedState.Enabled = false;
                    AppSessionLogger.WriteException(restoreException);
                }
                try
                {
                    await SaveStateAsync(CancellationToken.None);
                }
                catch (Exception stateException) when (stateException is IOException or UnauthorizedAccessException)
                {
                    AppSessionLogger.WriteException(stateException);
                }
                throw;
            }
            AppSessionLogger.Event("info", "extension", enabled ? "enabled" : "disabled", "extension enabled state changed", new { extensionId, enabled });
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task SaveSettingsAsync(string extensionId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            InstalledExtensionInfo extension = GetExtensionCore(extensionId);
            ValidateSettings(extension.Manifest, settings);
            ExtensionPersistedState persistedState = GetState(extensionId);
            Dictionary<string, string> previousSettings = new(persistedState.Settings, StringComparer.OrdinalIgnoreCase);
            persistedState.Settings = settings.ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            if (!extension.IsLoaded)
            {
                await SaveStateAsync(cancellationToken);
                return;
            }
            await UnloadExtensionAsync(extensionId, cancellationToken);
            try
            {
                await LoadInProcessExtensionIfNeededAsync(CreateInstalledInfo(extension.InstallDirectory), cancellationToken);
                await SaveStateAsync(cancellationToken);
            }
            catch
            {
                persistedState.Settings = previousSettings;
                await UnloadExtensionAsync(extensionId, CancellationToken.None);
                if (persistedState.Enabled)
                {
                    try
                    {
                        await LoadInProcessExtensionIfNeededAsync(CreateInstalledInfo(extension.InstallDirectory), CancellationToken.None);
                    }
                    catch (Exception restoreException)
                    {
                        persistedState.Enabled = false;
                        AppSessionLogger.WriteException(restoreException);
                    }
                }
                try
                {
                    await SaveStateAsync(CancellationToken.None);
                }
                catch (Exception stateException) when (stateException is IOException or UnauthorizedAccessException)
                {
                    AppSessionLogger.WriteException(stateException);
                }
                throw;
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            InstalledExtensionInfo extension = GetExtensionCore(extensionId);
            return BuildEffectiveSettings(extension.Manifest, GetState(extensionId));
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task UninstallAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            InstalledExtensionInfo extension = GetExtensionCore(extensionId);
            await UnloadExtensionAsync(extensionId, cancellationToken);
            string removedDirectory = Path.Combine(extensionsDirectory, $".removing-{extensionId}-{Guid.NewGuid():N}");
            Directory.Move(extension.InstallDirectory, removedDirectory);
            TryDeleteDirectory(removedDirectory);
            TryDeleteDirectory(Path.Combine(extensionsDirectory, ".data", extensionId));
            state.Extensions.Remove(extensionId);
            await SaveStateAsync(cancellationToken);
            AppSessionLogger.Event("info", "extension", "uninstalled", "extension uninstalled", new { extensionId });
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<ExtensionExecutionResult> ExecuteAsync(string extensionId, string method, object? payload = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        await EnsureInitializedAsync(cancellationToken);
        InstalledExtensionInfo extension;
        IReadOnlyDictionary<string, string> settings;
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            extension = GetExtensionCore(extensionId);
            if (!extension.IsEnabled)
            {
                return Failure("扩展未启用");
            }
            if (!extension.IsValid)
            {
                return Failure(extension.ValidationError);
            }
            if (!string.Equals(extension.Manifest.ExecutionMode, "process", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("应用内扩展不使用进程调用协议");
            }
            settings = BuildEffectiveSettings(extension.Manifest, GetState(extensionId));
        }
        finally
        {
            operationGate.Release();
        }
        return await ExecuteProcessExtensionAsync(extension, method, payload, settings, cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            foreach (string extensionId in loadedExtensions.Keys.ToArray())
            {
                await UnloadExtensionAsync(extensionId, cancellationToken);
            }
            foreach (Process process in runningProcesses.Values.ToArray())
            {
                TryKill(process);
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!isInitialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private IReadOnlyList<InstalledExtensionInfo> GetInstalledExtensionsCore()
    {
        if (!Directory.Exists(extensionsDirectory))
        {
            return [];
        }
        return Directory.EnumerateDirectories(extensionsDirectory)
            .Where(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            .Select(CreateInstalledInfo)
            .OrderBy(item => item.Manifest.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private InstalledExtensionInfo GetExtensionCore(string extensionId)
    {
        return GetInstalledExtensionsCore().FirstOrDefault(item => string.Equals(item.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new DirectoryNotFoundException($"扩展不存在：{extensionId}");
    }

    private InstalledExtensionInfo CreateInstalledInfo(string installDirectory)
    {
        ExtensionManifest manifest;
        string validationError = string.Empty;
        try
        {
            manifest = ReadManifest(installDirectory);
            ValidateManifest(manifest, installDirectory);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            manifest = new ExtensionManifest
            {
                Id = Path.GetFileName(installDirectory),
                Name = Path.GetFileName(installDirectory),
                Version = "?",
                Description = "扩展清单无效",
            };
            validationError = e.Message;
        }
        ExtensionPersistedState persistedState = GetState(manifest.Id);
        return new InstalledExtensionInfo
        {
            Manifest = manifest,
            InstallDirectory = installDirectory,
            IsEnabled = persistedState.Enabled,
            IsLoaded = loadedExtensions.ContainsKey(manifest.Id),
            IsValid = string.IsNullOrWhiteSpace(validationError),
            ValidationError = validationError,
        };
    }

    private static ExtensionManifest ReadManifest(string directory)
    {
        string manifestPath = Path.Combine(directory, "extension.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException("扩展包缺少 extension.json");
        }
        string json = File.ReadAllText(manifestPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<ExtensionManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("无法读取扩展清单");
    }

    private static void ValidateManifest(ExtensionManifest manifest, string packageRoot)
    {
        manifest.Id ??= string.Empty;
        manifest.Name ??= string.Empty;
        manifest.Version ??= string.Empty;
        manifest.Description ??= string.Empty;
        manifest.Author ??= string.Empty;
        manifest.Icon ??= string.Empty;
        manifest.Homepage ??= string.Empty;
        manifest.ExecutionMode ??= string.Empty;
        manifest.EntryPoint ??= string.Empty;
        manifest.EntryType ??= string.Empty;
        manifest.Runtime ??= string.Empty;
        manifest.Arguments ??= [];
        manifest.MinimumHostVersion ??= string.Empty;
        manifest.Capabilities ??= [];
        manifest.Permissions ??= [];
        manifest.Settings ??= [];
        if (manifest.Settings.Any(item => item == null))
        {
            throw new InvalidDataException("扩展设置定义不能为空");
        }
        foreach (ExtensionSettingDefinition setting in manifest.Settings)
        {
            setting.Key ??= string.Empty;
            setting.Label ??= string.Empty;
            setting.Description ??= string.Empty;
            setting.Section ??= string.Empty;
            setting.VisibleWhenKey ??= string.Empty;
            setting.VisibleWhenValue ??= string.Empty;
            setting.Type ??= string.Empty;
            setting.DefaultValue ??= string.Empty;
            setting.Options ??= [];
        }
        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持扩展清单版本 {manifest.SchemaVersion}");
        }
        if (!ExtensionIdRegex().IsMatch(manifest.Id))
        {
            throw new InvalidDataException("扩展 ID 只能包含小写字母、数字、点和连字符，长度为 3 到 100");
        }
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80)
        {
            throw new InvalidDataException("扩展名称不能为空且不能超过 80 个字符");
        }
        if (!VersionRegex().IsMatch(manifest.Version))
        {
            throw new InvalidDataException("扩展版本必须使用语义化版本格式");
        }
        if (!string.IsNullOrWhiteSpace(manifest.Icon))
        {
            string iconPath = GetContainedPath(packageRoot, manifest.Icon);
            if (!File.Exists(iconPath))
            {
                throw new InvalidDataException($"扩展图标不存在：{manifest.Icon}");
            }
            if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" }.Contains(Path.GetExtension(iconPath), StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("扩展图标仅支持 PNG、JPEG、BMP、GIF 或 ICO");
            }
            if (new FileInfo(iconPath).Length > 5L * 1024L * 1024L)
            {
                throw new InvalidDataException("扩展图标不能超过 5 MB");
            }
        }
        if (!string.IsNullOrWhiteSpace(manifest.MinimumHostVersion)
            && !Version.TryParse(manifest.MinimumHostVersion, out _))
        {
            throw new InvalidDataException("minimum_host_version 格式无效");
        }
        if (Version.TryParse(manifest.MinimumHostVersion, out Version? minimumHostVersion)
            && (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0)) < minimumHostVersion)
        {
            throw new InvalidDataException($"扩展需要 Emerde {minimumHostVersion} 或更高版本");
        }
        if (!manifest.ExecutionMode.Equals("in_process", StringComparison.OrdinalIgnoreCase)
            && !manifest.ExecutionMode.Equals("process", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("execution_mode 只能是 in_process 或 process");
        }
        string entryPoint = GetContainedPath(packageRoot, manifest.EntryPoint);
        if (!File.Exists(entryPoint))
        {
            throw new InvalidDataException($"扩展入口不存在：{manifest.EntryPoint}");
        }
        if (manifest.ExecutionMode.Equals("in_process", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(manifest.EntryType) || !entryPoint.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("应用内扩展必须提供 DLL 入口和 entry_type");
        }
        if (manifest.ExecutionMode.Equals("process", StringComparison.OrdinalIgnoreCase)
            && !new[] { "executable", "powershell", "python", "node" }.Contains(manifest.Runtime, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("不支持该扩展运行时");
        }
        if (manifest.TimeoutSeconds is < 5 or > 86400)
        {
            throw new InvalidDataException("扩展超时时间必须在 5 到 86400 秒之间");
        }
        string[] settingKeys = manifest.Settings.Select(item => item.Key).ToArray();
        if (settingKeys.Any(key => !SettingKeyRegex().IsMatch(key))
            || settingKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != settingKeys.Length)
        {
            throw new InvalidDataException("扩展设置键无效或重复");
        }
        if (manifest.Settings.Any(item => !new[] { "text", "password", "boolean", "number", "choice" }.Contains(item.Type, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("扩展设置包含不支持的类型");
        }
        if (manifest.Settings.Any(item => item.Column is < 0 or > 1))
        {
            throw new InvalidDataException("扩展设置列必须为 0 或 1");
        }
        if (manifest.Settings.Any(item => !string.IsNullOrWhiteSpace(item.VisibleWhenKey)
            && (!settingKeys.Contains(item.VisibleWhenKey, StringComparer.OrdinalIgnoreCase)
                || item.Key.Equals(item.VisibleWhenKey, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("扩展设置的条件显示依赖无效");
        }
        if (manifest.Settings.Any(item => item.Type.Equals("choice", StringComparison.OrdinalIgnoreCase) && item.Options.Length == 0))
        {
            throw new InvalidDataException("选择类型设置必须提供 options");
        }
    }

    private async Task LoadInProcessExtensionIfNeededAsync(InstalledExtensionInfo extension, CancellationToken cancellationToken)
    {
        if (!extension.Manifest.ExecutionMode.Equals("in_process", StringComparison.OrdinalIgnoreCase)
            || loadedExtensions.ContainsKey(extension.Manifest.Id))
        {
            return;
        }
        string entryPath = GetContainedPath(extension.InstallDirectory, extension.Manifest.EntryPoint);
        ExtensionAssemblyLoadContext loadContext = new(entryPath);
        ExtensionContext? context = null;
        try
        {
            Assembly assembly = loadContext.LoadEntryAssembly();
            Type entryType = assembly.GetType(extension.Manifest.EntryType, throwOnError: true, ignoreCase: false)
                ?? throw new TypeLoadException(extension.Manifest.EntryType);
            if (!typeof(IEmerdeExtension).IsAssignableFrom(entryType))
            {
                throw new InvalidDataException($"{extension.Manifest.EntryType} 未实现 IEmerdeExtension");
            }
            IEmerdeExtension module = (IEmerdeExtension)(Activator.CreateInstance(entryType)
                ?? throw new InvalidOperationException("无法创建扩展入口实例"));
            string dataDirectory = Path.Combine(extensionsDirectory, ".data", extension.Manifest.Id);
            Directory.CreateDirectory(dataDirectory);
            context = new ExtensionContext(
                extension.Manifest.Id,
                extension.InstallDirectory,
                dataDirectory,
                BuildEffectiveSettings(extension.Manifest, GetState(extension.Manifest.Id)),
                extension.Manifest.Permissions);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(extension.Manifest.TimeoutSeconds, 30)));
            await module.InitializeAsync(context, timeout.Token);
            loadedExtensions[extension.Manifest.Id] = new LoadedExtension(module, context, loadContext);
        }
        catch
        {
            if (context != null)
            {
                await context.DisposeAsync();
            }
            loadContext.Unload();
            throw;
        }
    }

    private async Task UnloadExtensionAsync(string extensionId, CancellationToken cancellationToken)
    {
        if (runningProcesses.TryRemove(extensionId, out Process? process))
        {
            TryKill(process);
            process.Dispose();
        }
        if (!loadedExtensions.TryRemove(extensionId, out LoadedExtension? loaded))
        {
            ExtensionHostRuntime.RemoveExtensionRegistrations(extensionId);
            return;
        }
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await loaded.Module.ShutdownAsync(timeout.Token);
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }
        await loaded.Context.DisposeAsync();
        loaded.LoadContext.Unload();
    }

    private async Task<ExtensionExecutionResult> ExecuteProcessExtensionAsync(
        InstalledExtensionInfo extension,
        string method,
        object? payload,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = BuildProcessStartInfo(extension);
        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!runningProcesses.TryAdd(extension.Manifest.Id, process))
        {
            return Failure("扩展已有任务正在运行");
        }
        string requestId = Guid.NewGuid().ToString("N");
        try
        {
            if (!process.Start())
            {
                return Failure("扩展进程启动失败");
            }
            ExtensionProcessRequest request = new()
            {
                RequestId = requestId,
                Method = method,
                Settings = settings,
                Payload = JsonSerializer.SerializeToElement(payload, JsonOptions),
            };
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            process.StandardInput.Close();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(extension.Manifest.TimeoutSeconds));
            await process.WaitForExitAsync(timeout.Token);
            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            ExtensionProcessResponse? response = ParseResponse(stdout, requestId);
            if (response == null)
            {
                string message = string.IsNullOrWhiteSpace(stderr) ? $"扩展返回了无效响应，退出码 {process.ExitCode}" : stderr.Trim();
                return new ExtensionExecutionResult(false, message, EmptyJson(), process.ExitCode);
            }
            return new ExtensionExecutionResult(response.Success, response.Message, response.Data, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Failure(cancellationToken.IsCancellationRequested ? "扩展任务已取消" : "扩展任务超时");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            TryKill(process);
            AppSessionLogger.WriteException(e);
            return Failure(e.Message);
        }
        finally
        {
            runningProcesses.TryRemove(new KeyValuePair<string, Process>(extension.Manifest.Id, process));
        }
    }

    private static ProcessStartInfo BuildProcessStartInfo(InstalledExtensionInfo extension)
    {
        string entryPath = GetContainedPath(extension.InstallDirectory, extension.Manifest.EntryPoint);
        ProcessStartInfo startInfo = new()
        {
            WorkingDirectory = extension.InstallDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        switch (extension.Manifest.Runtime.ToLowerInvariant())
        {
            case "executable":
                startInfo.FileName = entryPath;
                break;
            case "powershell":
                startInfo.FileName = FindRuntime(extension.InstallDirectory, "pwsh.exe", "powershell.exe");
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(entryPath);
                break;
            case "python":
                startInfo.FileName = FindRuntime(extension.InstallDirectory, "python.exe");
                startInfo.ArgumentList.Add(entryPath);
                break;
            case "node":
                startInfo.FileName = FindRuntime(extension.InstallDirectory, "node.exe");
                startInfo.ArgumentList.Add(entryPath);
                break;
        }
        foreach (string argument in extension.Manifest.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static string FindRuntime(string extensionDirectory, params string[] names)
    {
        foreach (string name in names)
        {
            string bundledPath = Path.Combine(extensionDirectory, "runtime", name);
            if (File.Exists(bundledPath))
            {
                return bundledPath;
            }
        }
        foreach (string name in names)
        {
            string? path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory.Trim(), name))
                .FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }
        throw new FileNotFoundException($"找不到扩展运行时：{string.Join(" / ", names)}");
    }

    private static ExtensionProcessResponse? ParseResponse(string stdout, string requestId)
    {
        string[] lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Array.Reverse(lines);
        foreach (string line in lines)
        {
            try
            {
                ExtensionProcessResponse? response = JsonSerializer.Deserialize<ExtensionProcessResponse>(line, JsonOptions);
                if (response?.ProtocolVersion == 1 && string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                {
                    return response;
                }
            }
            catch (JsonException)
            {
            }
        }
        return null;
    }

    private static void ExtractPackage(string packagePath, string stagingDirectory)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaximumPackageEntryCount)
        {
            throw new InvalidDataException("扩展包文件数量超过限制");
        }
        long expandedBytes = 0;
        string root = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumPackageExpandedBytes)
            {
                throw new InvalidDataException("扩展包解压后体积超过限制");
            }
            string destinationPath = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("扩展包包含越界路径");
            }
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: false);
        }
    }

    private static string FindPackageRoot(string stagingDirectory)
    {
        if (File.Exists(Path.Combine(stagingDirectory, "extension.json")))
        {
            return stagingDirectory;
        }
        string[] manifests = Directory.EnumerateFiles(stagingDirectory, "extension.json", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(stagingDirectory, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length <= 2)
            .ToArray();
        if (manifests.Length != 1)
        {
            throw new InvalidDataException("扩展包必须在根目录或唯一的一级目录中包含 extension.json");
        }
        return Path.GetDirectoryName(manifests[0])!;
    }

    private static string GetContainedPath(string rootDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("扩展入口必须是包内相对路径");
        }
        string root = Path.GetFullPath(rootDirectory) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("扩展入口超出扩展目录");
        }
        return fullPath;
    }

    private ExtensionPersistedState GetState(string extensionId)
    {
        if (!state.Extensions.TryGetValue(extensionId, out ExtensionPersistedState? extensionState))
        {
            extensionState = new ExtensionPersistedState();
            state.Extensions[extensionId] = extensionState;
        }
        return extensionState;
    }

    private static IReadOnlyDictionary<string, string> BuildEffectiveSettings(ExtensionManifest manifest, ExtensionPersistedState persistedState)
    {
        Dictionary<string, string> settings = manifest.Settings.ToDictionary(item => item.Key, item => item.DefaultValue ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in persistedState.Settings)
        {
            ExtensionSettingDefinition? definition = manifest.Settings.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (definition != null)
            {
                settings[definition.Key] = definition.Sensitive ? SecretProtector.Unprotect(value) : value;
            }
        }
        return settings;
    }

    private static void ValidateSettings(ExtensionManifest manifest, IReadOnlyDictionary<string, string> settings)
    {
        foreach (ExtensionSettingDefinition definition in manifest.Settings)
        {
            settings.TryGetValue(definition.Key, out string? value);
            value ??= string.Empty;
            if (definition.Required && string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"请填写 {definition.Label}");
            }
            if (definition.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase) && !bool.TryParse(value, out _))
            {
                throw new InvalidDataException($"{definition.Label} 必须是布尔值");
            }
            if (definition.Type.Equals("number", StringComparison.OrdinalIgnoreCase) && !double.TryParse(value, out _))
            {
                throw new InvalidDataException($"{definition.Label} 必须是数字");
            }
            if (definition.Type.Equals("choice", StringComparison.OrdinalIgnoreCase) && !definition.Options.Contains(value, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"{definition.Label} 的选项无效");
            }
        }
    }

    private async Task<ExtensionStateDocument> LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(extensionStateFilePath))
        {
            return new ExtensionStateDocument();
        }
        try
        {
            await using FileStream stream = new(extensionStateFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            ExtensionStateDocument document = await JsonSerializer.DeserializeAsync<ExtensionStateDocument>(stream, JsonOptions, cancellationToken)
                ?? new ExtensionStateDocument();
            Dictionary<string, ExtensionPersistedState> normalizedExtensions = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string extensionId, ExtensionPersistedState persistedState) in document.Extensions ?? [])
            {
                if (persistedState != null)
                {
                    normalizedExtensions[extensionId] = NormalizePersistedState(persistedState);
                }
            }
            document.Extensions = normalizedExtensions;
            return document;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            return new ExtensionStateDocument();
        }
    }

    private static ExtensionPersistedState NormalizePersistedState(ExtensionPersistedState persistedState)
    {
        Dictionary<string, string> normalizedSettings = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in persistedState.Settings ?? [])
        {
            normalizedSettings[key] = value ?? string.Empty;
        }
        persistedState.Settings = normalizedSettings;
        return persistedState;
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(extensionsDirectory);
        ExtensionStateDocument protectedState = new()
        {
            Extensions = state.Extensions.ToDictionary(pair => pair.Key, pair => ProtectState(pair.Key, pair.Value), StringComparer.OrdinalIgnoreCase),
        };
        string temporaryPath = extensionStateFilePath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, protectedState, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, extensionStateFilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    AppSessionLogger.WriteException(e);
                }
            }
        }
    }

    private ExtensionPersistedState ProtectState(string extensionId, ExtensionPersistedState source)
    {
        ExtensionManifest? manifest = GetInstalledExtensionsCore()
            .FirstOrDefault(item => string.Equals(item.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase))?.Manifest;
        Dictionary<string, string> settings = new(source.Settings, StringComparer.OrdinalIgnoreCase);
        if (manifest != null)
        {
            foreach (ExtensionSettingDefinition definition in manifest.Settings.Where(item => item.Sensitive))
            {
                if (settings.TryGetValue(definition.Key, out string? value))
                {
                    settings[definition.Key] = SecretProtector.Protect(value);
                }
            }
        }
        return new ExtensionPersistedState { Enabled = source.Enabled, Settings = settings };
    }

    private static ExtensionExecutionResult Failure(string message) => new(false, message, EmptyJson());

    private static JsonElement EmptyJson() => JsonSerializer.SerializeToElement(new { });

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception firstException) when (firstException is IOException or UnauthorizedAccessException)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception retryException) when (retryException is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(retryException);
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,98}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionIdRegex();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SettingKeyRegex();

    private sealed class ExtensionStateDocument
    {
        [JsonPropertyName("extensions")]
        public Dictionary<string, ExtensionPersistedState> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ExtensionPersistedState
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("settings")]
        public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record LoadedExtension(IEmerdeExtension Module, ExtensionContext Context, ExtensionAssemblyLoadContext LoadContext);

    private sealed class ExtensionAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;
        private readonly string mainAssemblyPath;

        public ExtensionAssemblyLoadContext(string mainAssemblyPath) : base($"Emerde.Extension.{Path.GetFileNameWithoutExtension(mainAssemblyPath)}.{Guid.NewGuid():N}", true)
        {
            this.mainAssemblyPath = mainAssemblyPath;
            resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        public Assembly LoadEntryAssembly()
        {
            return LoadManagedAssembly(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            Assembly contractAssembly = typeof(IEmerdeExtension).Assembly;
            if (string.Equals(assemblyName.Name, contractAssembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
            {
                return contractAssembly;
            }
            string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath == null ? null : LoadManagedAssembly(assemblyPath);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath == null ? nint.Zero : LoadUnmanagedDllFromPath(libraryPath);
        }

        private Assembly LoadManagedAssembly(string assemblyPath)
        {
            using FileStream stream = new(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return LoadFromStream(stream);
        }
    }
}
