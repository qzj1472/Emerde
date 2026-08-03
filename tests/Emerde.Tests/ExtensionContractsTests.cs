using Emerde.Plugins;
using System.IO.Compression;
using System.Text;

namespace Emerde.Tests;

public sealed class ExtensionContractsTests
{
    [Fact]
    public void RemoveExtension_UsesSharedDialogBlurScope()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "ExtensionCenterViewModel.cs"));
        int methodStart = source.IndexOf("private async Task RemoveExtension", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("[RelayCommand]", methodStart + 1, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        Assert.Contains("DialogBlurScope.ForLightDismiss", method);
        Assert.Contains("WindowSizing.ShowContentDialogAsync", method);
    }

    [Fact]
    public void MainWindow_StartsRecoveryAfterExtensionsInitialize()
    {
        string mainWindow = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        string mainViewModel = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        int extensionInitialization = mainWindow.IndexOf("await ExtensionService.Default.InitializeAsync()", StringComparison.Ordinal);
        int recoveryStartup = mainWindow.IndexOf("RecordingRecoveryService.QueueRun()", StringComparison.Ordinal);

        Assert.True(extensionInitialization >= 0);
        Assert.True(recoveryStartup > extensionInitialization);
        Assert.DoesNotContain("RecordingRecoveryService.QueueRun()", mainViewModel, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    [Fact]
    public void ExtensionContractNames_AreDistinct()
    {
        string[] contracts =
        [
            ExtensionContractNames.StreamResolver,
            ExtensionContractNames.Monitor,
            ExtensionContractNames.Recorder,
            ExtensionContractNames.MainWindow,
            ExtensionContractNames.MainViewModel,
            ExtensionContractNames.Application,
            ExtensionContractNames.MainContentOverlay,
            ExtensionContractNames.ExtensionDetail,
            ExtensionContractNames.VideoListToolbar,
            ExtensionContractNames.VideoListActions,
            ExtensionContractNames.PlatformCookies,
            ExtensionContractNames.VideoSelection,
            ExtensionContractNames.DialogService,
        ];

        Assert.Equal(contracts.Length, contracts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task ExtensionHostRuntime_PublishesEventsAndRemovesDisposedSubscriptions()
    {
        string eventName = $"test.event.{Guid.NewGuid():N}";
        int received = 0;
        ExtensionContext context = new("test.event-extension", string.Empty, string.Empty, new Dictionary<string, string>());
        IDisposable subscription = context.Subscribe<string>(eventName, (payload, _) =>
        {
            received += payload.Length;
            return ValueTask.CompletedTask;
        });

        await ExtensionHostRuntime.PublishAsync(eventName, "first");
        subscription.Dispose();
        await ExtensionHostRuntime.PublishAsync(eventName, "second");
        await context.DisposeAsync();

        Assert.Equal(5, received);
    }

    [Fact]
    public void ExtensionHostRuntime_ReturnsEveryVideoActionByPriority()
    {
        string contract = $"test.video-actions.{Guid.NewGuid():N}";
        TestVideoAction first = new("first", 10);
        TestVideoAction second = new("second", 20);
        using IDisposable firstRegistration = ExtensionHostRuntime.RegisterOverride("test.first", contract, first, 1);
        using IDisposable secondRegistration = ExtensionHostRuntime.RegisterOverride("test.second", contract, second, 2);

        IReadOnlyList<IExtensionVideoAction> actions = ExtensionHostRuntime.GetOverrides<IExtensionVideoAction>(contract);

        Assert.Equal([second, first], actions);
    }

    [Fact]
    public async Task ExtensionHostRuntime_IsolatesFailingEventHandlers()
    {
        string eventName = $"test.event.{Guid.NewGuid():N}";
        bool completed = false;
        using IDisposable failing = ExtensionHostRuntime.Subscribe<string>(
            "test.failing-extension",
            eventName,
            (_, _) => ValueTask.FromException(new InvalidOperationException("failure")));
        using IDisposable succeeding = ExtensionHostRuntime.Subscribe<string>(
            "test.succeeding-extension",
            eventName,
            (_, _) =>
            {
                completed = true;
                return ValueTask.CompletedTask;
            });

        await ExtensionHostRuntime.PublishAsync(eventName, "payload");

        Assert.True(completed);
    }

    [Fact]
    public async Task ExtensionHostRuntime_IsolatesFailingUiObservers()
    {
        await RunStaAsync(() =>
        {
            bool completed = false;
            EventHandler failing = (_, _) => throw new InvalidOperationException("failure");
            EventHandler succeeding = (_, _) => completed = true;
            ExtensionHostRuntime.UiContributionsChanged += failing;
            ExtensionHostRuntime.UiContributionsChanged += succeeding;
            try
            {
                using IDisposable registration = ExtensionHostRuntime.RegisterUi(
                    $"test.ui-extension.{Guid.NewGuid():N}",
                    $"test.ui-region.{Guid.NewGuid():N}",
                    new System.Windows.Controls.Border());

                Assert.True(completed);
            }
            finally
            {
                ExtensionHostRuntime.UiContributionsChanged -= failing;
                ExtensionHostRuntime.UiContributionsChanged -= succeeding;
            }
        });
    }

    [Fact]
    public async Task ExtensionContext_RequiresPermissionForPlatformCookies()
    {
        object provider = new();
        using IDisposable registration = ExtensionHostRuntime.RegisterHostObject(ExtensionContractNames.PlatformCookies, provider);
        ExtensionContext denied = new("test.denied", string.Empty, string.Empty, new Dictionary<string, string>());
        ExtensionContext allowed = new(
            "test.allowed",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(),
            [ExtensionPermissionNames.PlatformCookieRead]);

        Assert.Null(denied.GetHostObject(ExtensionContractNames.PlatformCookies));
        Assert.Same(provider, allowed.GetHostObject(ExtensionContractNames.PlatformCookies));

        await denied.DisposeAsync();
        await allowed.DisposeAsync();
    }

    [Fact]
    public void ExtensionHostRuntime_RemovesOverrideWhenRegistrationIsDisposed()
    {
        const string contract = "test.extension.override";
        object implementation = new();

        using IDisposable registration = ExtensionHostRuntime.RegisterOverride("test.extension", contract, implementation);

        Assert.True(ExtensionHostRuntime.TryGetOverride(contract, out object? resolved));
        Assert.Same(implementation, resolved);

        registration.Dispose();

        Assert.False(ExtensionHostRuntime.TryGetOverride<object>(contract, out _));
    }

    [Theory]
    [InlineData(".emerde-extension")]
    [InlineData(".zip")]
    [InlineData(".dll")]
    public void IsSupportedPackage_RequiresExistingSupportedFile(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.{Guid.NewGuid():N}{extension}");
        try
        {
            Assert.False(ExtensionService.IsSupportedPackage(path));
            File.WriteAllText(path, string.Empty);
            Assert.Equal(extension is ".emerde-extension" or ".zip", ExtensionService.IsSupportedPackage(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtensionService_InstallsPersistsAndUninstallsProcessExtension()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packageSource = Path.Combine(root, "package");
        string packagePath = Path.Combine(root, "sample.emerde-extension");
        string extensionsDirectory = Path.Combine(root, "extensions");
        try
        {
            Directory.CreateDirectory(packageSource);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "runner.exe"), string.Empty);
            await File.WriteAllBytesAsync(Path.Combine(packageSource, "icon.png"), [1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.process-extension",
                  "name": "Sample",
                  "version": "1.0.0",
                  "icon": "icon.png",
                  "execution_mode": "process",
                  "entry_point": "runner.exe",
                  "runtime": "executable",
                  "timeout_seconds": 30,
                  "settings": [
                    {
                      "key": "token",
                      "label": "Token",
                      "type": "password",
                      "sensitive": true
                    }
                  ]
                }
                """, Encoding.UTF8);
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            ExtensionService service = new(extensionsDirectory);

            ExtensionInstallResult installed = await service.InstallAsync(packagePath);
            await service.SetEnabledAsync(installed.Extension.Manifest.Id, true);
            await service.SaveSettingsAsync(installed.Extension.Manifest.Id, new Dictionary<string, string> { ["token"] = "secret" });

            InstalledExtensionInfo extension = Assert.Single(await service.GetInstalledExtensionsAsync());
            Assert.True(extension.IsEnabled);
            Assert.Equal("icon.png", extension.Manifest.Icon);
            Assert.Equal("secret", (await service.GetSettingsAsync(extension.Manifest.Id))["token"]);
            Assert.DoesNotContain("secret", await File.ReadAllTextAsync(Path.Combine(extensionsDirectory, "extensions-state.json")));

            await service.UninstallAsync(extension.Manifest.Id);

            Assert.Empty(await service.GetInstalledExtensionsAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_RejectsPackagePathTraversal()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packagePath = Path.Combine(root, "unsafe.emerde-extension");
        try
        {
            Directory.CreateDirectory(root);
            using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("../outside.txt");
                await using Stream stream = entry.Open();
                await stream.WriteAsync("unsafe"u8.ToArray());
            }
            ExtensionService service = new(Path.Combine(root, "extensions"));

            await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(packagePath));

            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_RejectsInvalidMinimumHostVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packageSource = Path.Combine(root, "package");
        string packagePath = Path.Combine(root, "invalid-version.emerde-extension");
        try
        {
            Directory.CreateDirectory(packageSource);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "runner.exe"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.invalid-version",
                  "name": "Sample",
                  "version": "1.0.0",
                  "execution_mode": "process",
                  "entry_point": "runner.exe",
                  "runtime": "executable",
                  "minimum_host_version": "invalid",
                  "timeout_seconds": 30
                }
                """, Encoding.UTF8);
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            ExtensionService service = new(Path.Combine(root, "extensions"));

            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(packagePath));

            Assert.Contains("minimum_host_version", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_TreatsNullManifestCollectionsAsEmpty()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packageSource = Path.Combine(root, "package");
        string packagePath = Path.Combine(root, "null-collections.emerde-extension");
        try
        {
            Directory.CreateDirectory(packageSource);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "runner.exe"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(packageSource, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.null-collections",
                  "name": "Sample",
                  "version": "1.0.0",
                  "execution_mode": "process",
                  "entry_point": "runner.exe",
                  "runtime": "executable",
                  "arguments": null,
                  "capabilities": null,
                  "permissions": null,
                  "settings": null,
                  "timeout_seconds": 30
                }
                """, Encoding.UTF8);
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            ExtensionService service = new(Path.Combine(root, "extensions"));

            ExtensionInstallResult installed = await service.InstallAsync(packagePath);

            Assert.Empty(installed.Extension.Manifest.Arguments);
            Assert.Empty(installed.Extension.Manifest.Capabilities);
            Assert.Empty(installed.Extension.Manifest.Permissions);
            Assert.Empty(installed.Extension.Manifest.Settings);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_RestoresLoadedExtensionWhenNewSettingsFail()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string packageSource = Path.Combine(root, "package");
        string packagePath = Path.Combine(root, "settings-rollback.emerde-extension");
        ExtensionService? service = null;
        try
        {
            Directory.CreateDirectory(packageSource);
            File.Copy(typeof(SettingsRejectingExtension).Assembly.Location, Path.Combine(packageSource, "TestExtension.dll"));
            await File.WriteAllTextAsync(Path.Combine(packageSource, "extension.json"), """
                {
                  "schema_version": 1,
                  "id": "sample.settings-rollback",
                  "name": "Sample",
                  "version": "1.0.0",
                  "execution_mode": "in_process",
                  "entry_point": "TestExtension.dll",
                  "entry_type": "Emerde.Tests.SettingsRejectingExtension",
                  "timeout_seconds": 30,
                  "settings": [
                    {
                      "key": "mode",
                      "label": "Mode",
                      "type": "choice",
                      "default": "working",
                      "options": ["working", "fail"]
                    }
                  ]
                }
                """, Encoding.UTF8);
            ZipFile.CreateFromDirectory(packageSource, packagePath);
            service = new ExtensionService(Path.Combine(root, "extensions"));
            ExtensionInstallResult installed = await service.InstallAsync(packagePath);
            await service.SetEnabledAsync(installed.Extension.Manifest.Id, true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
                installed.Extension.Manifest.Id,
                new Dictionary<string, string> { ["mode"] = "fail" }));

            InstalledExtensionInfo restored = Assert.Single(await service.GetInstalledExtensionsAsync());
            Assert.True(restored.IsEnabled);
            Assert.True(restored.IsLoaded);
            Assert.Equal("working", (await service.GetSettingsAsync(restored.Manifest.Id))["mode"]);
        }
        finally
        {
            if (service != null)
            {
                await service.ShutdownAsync();
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExtensionService_NormalizesCaseVariantStateKeys()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Extension.Tests.{Guid.NewGuid():N}");
        string extensionsDirectory = Path.Combine(root, "extensions");
        try
        {
            Directory.CreateDirectory(extensionsDirectory);
            await File.WriteAllTextAsync(Path.Combine(extensionsDirectory, "extensions-state.json"), """
                {
                  "extensions": {
                    "sample.extension": {
                      "enabled": false,
                      "settings": {
                        "Token": "first",
                        "token": "second"
                      }
                    },
                    "SAMPLE.EXTENSION": {
                      "enabled": true,
                      "settings": null
                    }
                  }
                }
                """, Encoding.UTF8);
            ExtensionService service = new(extensionsDirectory);

            await service.InitializeAsync();

            Assert.Empty(await service.GetInstalledExtensionsAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static Task RunStaAsync(Action action)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

internal sealed class TestVideoAction(string id, int order) : IExtensionVideoAction
{
    public string Id { get; } = id;

    public string Label => Id;

    public int Order { get; } = order;

    public bool CanExecute(IReadOnlyList<ExtensionVideoFileInfo> files) => true;

    public Task ExecuteAsync(IReadOnlyList<ExtensionVideoFileInfo> files, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SettingsRejectingExtension : IEmerdeExtension
{
    public ValueTask InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        if (context.Settings.TryGetValue("mode", out string? mode) && mode == "fail")
        {
            throw new InvalidOperationException("Rejected settings");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
