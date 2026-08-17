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
    private const int MaximumProcessStandardOutputCharacters = 4 * 1024 * 1024;
    private const int MaximumProcessStandardErrorCharacters = 64 * 1024;
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
            throw new InvalidDataException("ExtensionPackageUnsupported".Tr());
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
                try
                {
                    await SaveStateAsync(cancellationToken);
                }
                catch
                {
                    persistedState.Settings = previousSettings;
                    throw;
                }
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
                return Failure("ExtensionNotEnabled".Tr());
            }
            if (!extension.IsValid)
            {
                return Failure(extension.ValidationError);
            }
            if (!string.Equals(extension.Manifest.ExecutionMode, "process", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("ExtensionInProcessProtocolUnsupported".Tr());
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
            isInitialized = false;
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
            ?? throw new DirectoryNotFoundException("ExtensionNotFound".Tr(extensionId));
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
                Description = "ExtensionManifestInvalid".Tr(),
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
            throw new InvalidDataException("ExtensionManifestMissing".Tr());
        }
        string json = File.ReadAllText(manifestPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<ExtensionManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("ExtensionManifestUnreadable".Tr());
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
            throw new InvalidDataException("ExtensionSettingDefinitionNull".Tr());
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
            throw new InvalidDataException("ExtensionManifestSchemaUnsupported".Tr(manifest.SchemaVersion));
        }
        if (!ExtensionIdRegex().IsMatch(manifest.Id))
        {
            throw new InvalidDataException("ExtensionIdInvalid".Tr());
        }
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 80)
        {
            throw new InvalidDataException("ExtensionNameInvalid".Tr());
        }
        if (!VersionRegex().IsMatch(manifest.Version))
        {
            throw new InvalidDataException("ExtensionVersionInvalid".Tr());
        }
        if (!string.IsNullOrWhiteSpace(manifest.Icon))
        {
            string iconPath = GetContainedPath(packageRoot, manifest.Icon);
            if (!File.Exists(iconPath))
            {
                throw new InvalidDataException("ExtensionIconMissing".Tr(manifest.Icon));
            }
            if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" }.Contains(Path.GetExtension(iconPath), StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("ExtensionIconFormatUnsupported".Tr());
            }
            if (new FileInfo(iconPath).Length > 5L * 1024L * 1024L)
            {
                throw new InvalidDataException("ExtensionIconTooLarge".Tr());
            }
        }
        if (!string.IsNullOrWhiteSpace(manifest.MinimumHostVersion)
            && !Version.TryParse(manifest.MinimumHostVersion, out _))
        {
            throw new InvalidDataException("ExtensionMinimumHostVersionInvalid".Tr());
        }
        if (Version.TryParse(manifest.MinimumHostVersion, out Version? minimumHostVersion)
            && (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0)) < minimumHostVersion)
        {
            throw new InvalidDataException("ExtensionMinimumHostVersionUnsupported".Tr(minimumHostVersion));
        }
        if (!manifest.ExecutionMode.Equals("in_process", StringComparison.OrdinalIgnoreCase)
            && !manifest.ExecutionMode.Equals("process", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ExtensionExecutionModeInvalid".Tr());
        }
        string entryPoint = GetContainedPath(packageRoot, manifest.EntryPoint);
        if (!File.Exists(entryPoint))
        {
            throw new InvalidDataException("ExtensionEntryPointMissing".Tr(manifest.EntryPoint));
        }
        if (manifest.ExecutionMode.Equals("in_process", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(manifest.EntryType) || !entryPoint.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("ExtensionInProcessEntryInvalid".Tr());
        }
        if (manifest.ExecutionMode.Equals("process", StringComparison.OrdinalIgnoreCase)
            && !new[] { "executable", "powershell", "python", "node" }.Contains(manifest.Runtime, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ExtensionRuntimeUnsupported".Tr());
        }
        if (manifest.TimeoutSeconds is < 5 or > 86400)
        {
            throw new InvalidDataException("ExtensionTimeoutInvalid".Tr());
        }
        string[] settingKeys = manifest.Settings.Select(item => item.Key).ToArray();
        if (settingKeys.Any(key => !SettingKeyRegex().IsMatch(key))
            || settingKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != settingKeys.Length)
        {
            throw new InvalidDataException("ExtensionSettingKeyInvalid".Tr());
        }
        if (manifest.Settings.Any(item => !new[] { "text", "password", "boolean", "number", "choice" }.Contains(item.Type, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("ExtensionSettingTypeUnsupported".Tr());
        }
        if (manifest.Settings.Any(item => item.Column is < 0 or > 1))
        {
            throw new InvalidDataException("ExtensionSettingColumnInvalid".Tr());
        }
        if (manifest.Settings.Any(item => !string.IsNullOrWhiteSpace(item.VisibleWhenKey)
            && (!settingKeys.Contains(item.VisibleWhenKey, StringComparer.OrdinalIgnoreCase)
                || item.Key.Equals(item.VisibleWhenKey, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("ExtensionSettingDependencyInvalid".Tr());
        }
        if (manifest.Settings.Any(item => item.Type.Equals("choice", StringComparison.OrdinalIgnoreCase) && item.Options.Length == 0))
        {
            throw new InvalidDataException("ExtensionChoiceOptionsMissing".Tr());
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
                throw new InvalidDataException("ExtensionEntryTypeUnsupported".Tr(extension.Manifest.EntryType));
            }
            IEmerdeExtension module = (IEmerdeExtension)(Activator.CreateInstance(entryType)
                ?? throw new InvalidOperationException("ExtensionEntryInstanceCreationFailed".Tr()));
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
            return Failure("ExtensionTaskAlreadyRunning".Tr());
        }
        string requestId = Guid.NewGuid().ToString("N");
        try
        {
            if (!process.Start())
            {
                return Failure("ExtensionProcessStartFailed".Tr());
            }
            ExtensionProcessRequest request = new()
            {
                RequestId = requestId,
                Method = method,
                Settings = settings,
                Payload = JsonSerializer.SerializeToElement(payload, JsonOptions),
            };
            int outputLimitExceeded = 0;
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(extension.Manifest.TimeoutSeconds));
            Task<BoundedTextReadResult> stdoutTask = ReadBoundedTextAsync(
                process.StandardOutput,
                MaximumProcessStandardOutputCharacters,
                retainTail: false,
                () =>
                {
                    Interlocked.Exchange(ref outputLimitExceeded, 1);
                    TryKill(process);
                },
                CancellationToken.None);
            Task<BoundedTextReadResult> stderrTask = ReadBoundedTextAsync(
                process.StandardError,
                MaximumProcessStandardErrorCharacters,
                retainTail: true,
                null,
                CancellationToken.None);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            BoundedTextReadResult stdout = await stdoutTask;
            BoundedTextReadResult stderr = await stderrTask;
            if (stdout.ExceededLimit || Volatile.Read(ref outputLimitExceeded) != 0)
            {
                return new ExtensionExecutionResult(false, "ExtensionResponseTooLarge".Tr(), EmptyJson(), process.ExitCode);
            }
            ExtensionProcessResponse? response = ParseResponse(stdout.Text, requestId);
            if (response == null)
            {
                string message = string.IsNullOrWhiteSpace(stderr.Text) ? "ExtensionResponseInvalid".Tr(process.ExitCode) : stderr.Text.Trim();
                return new ExtensionExecutionResult(false, message, EmptyJson(), process.ExitCode);
            }
            return new ExtensionExecutionResult(response.Success, response.Message, response.Data, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Failure(cancellationToken.IsCancellationRequested ? "ExtensionTaskCanceled".Tr() : "ExtensionTaskTimedOut".Tr());
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

    internal static async Task<BoundedTextReadResult> ReadBoundedTextAsync(
        TextReader reader,
        int maximumCharacters,
        bool retainTail,
        Action? limitExceeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        char[] buffer = new char[4096];
        StringBuilder retained = new(Math.Min(maximumCharacters, buffer.Length));
        long totalCharacters = 0;
        bool exceededLimit = false;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }
            totalCharacters += read;
            if (retainTail)
            {
                retained.Append(buffer, 0, read);
                if (retained.Length > maximumCharacters)
                {
                    retained.Remove(0, retained.Length - maximumCharacters);
                }
            }
            else if (retained.Length < maximumCharacters)
            {
                retained.Append(buffer, 0, Math.Min(read, maximumCharacters - retained.Length));
            }
            if (!exceededLimit && totalCharacters > maximumCharacters)
            {
                exceededLimit = true;
                limitExceeded?.Invoke();
            }
        }
        return new BoundedTextReadResult(retained.ToString(), exceededLimit);
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
        throw new FileNotFoundException("ExtensionRuntimeMissing".Tr(string.Join(" / ", names)));
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
            throw new InvalidDataException("ExtensionPackageEntryLimitExceeded".Tr());
        }
        long expandedBytes = 0;
        string root = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumPackageExpandedBytes)
            {
                throw new InvalidDataException("ExtensionPackageExpandedSizeExceeded".Tr());
            }
            string destinationPath = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("ExtensionPackagePathTraversal".Tr());
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
            throw new InvalidDataException("ExtensionPackageManifestLocationInvalid".Tr());
        }
        return Path.GetDirectoryName(manifests[0])!;
    }

    private static string GetContainedPath(string rootDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("ExtensionEntryPathInvalid".Tr());
        }
        string root = Path.GetFullPath(rootDirectory) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ExtensionEntryOutsideDirectory".Tr());
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
                throw new InvalidDataException("ExtensionSettingRequired".Tr(definition.Label));
            }
            if (definition.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase) && !bool.TryParse(value, out _))
            {
                throw new InvalidDataException("ExtensionSettingBooleanRequired".Tr(definition.Label));
            }
            if (definition.Type.Equals("number", StringComparison.OrdinalIgnoreCase) && !double.TryParse(value, out _))
            {
                throw new InvalidDataException("ExtensionSettingNumberRequired".Tr(definition.Label));
            }
            if (definition.Type.Equals("choice", StringComparison.OrdinalIgnoreCase) && !definition.Options.Contains(value, StringComparer.Ordinal))
            {
                throw new InvalidDataException("ExtensionSettingChoiceInvalid".Tr(definition.Label));
            }
        }
    }

    private async Task<ExtensionStateDocument> LoadStateAsync(CancellationToken cancellationToken)
    {
        ExtensionStateDocument? primary = await TryLoadStateAsync(extensionStateFilePath, cancellationToken);
        if (primary != null)
        {
            return primary;
        }

        return await TryLoadStateAsync(GetStateBackupPath(), cancellationToken) ?? new ExtensionStateDocument();
    }

    private static async Task<ExtensionStateDocument?> TryLoadStateAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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
            QuarantineInvalidStateFile(path);
            return null;
        }
    }

    private static void QuarantineInvalidStateFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            string invalidPath = path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            File.Move(path, invalidPath, overwrite: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
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
        await AtomicFile.WriteJsonAsync(
            extensionStateFilePath,
            protectedState,
            JsonOptions,
            cancellationToken,
            GetStateBackupPath());
    }

    private string GetStateBackupPath() => extensionStateFilePath + ".bak";

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
