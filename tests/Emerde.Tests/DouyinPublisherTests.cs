using Emerde.DouyinPublisher;
using Emerde.Plugins;
using Emerde.ViewModels;
using System.IO.Compression;
using System.Windows.Controls;

namespace Emerde.Tests;

public sealed class DouyinPublisherTests
{
    [Fact]
    public async Task ExtensionService_LoadsDetailPanelAndVideoActionFromPackage()
    {
        await RunStaAsync(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.Load.{Guid.NewGuid():N}");
            string packageSource = Path.Combine(root, "package");
            string packagePath = Path.Combine(root, "publisher.emerde-extension");
            ExtensionService? service = null;
            using IDisposable mainViewModel = ExtensionHostRuntime.RegisterHostObject(ExtensionContractNames.MainViewModel, new MainViewModel());
            using IDisposable dialogService = ExtensionHostRuntime.RegisterHostObject(ExtensionContractNames.DialogService, new TestExtensionDialogService());
            try
            {
                Directory.CreateDirectory(packageSource);
                string assemblyPath = typeof(Emerde.DouyinPublisher.ExtensionEntry).Assembly.Location;
                string configuration = typeof(Emerde.DouyinPublisher.ExtensionEntry).Assembly
                    .GetCustomAttributesData()
                    .First(attribute => attribute.AttributeType == typeof(System.Reflection.AssemblyConfigurationAttribute))
                    .ConstructorArguments[0]
                    .Value as string ?? throw new InvalidOperationException("Missing assembly configuration.");
                string dependencyFileName = Path.GetFileName(Path.ChangeExtension(assemblyPath, ".deps.json"));
                File.Copy(assemblyPath, Path.Combine(packageSource, Path.GetFileName(assemblyPath)));
                File.Copy(
                    FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "bin", configuration, "net9.0-windows10.0.26100.0", dependencyFileName),
                    Path.Combine(packageSource, dependencyFileName));
                File.Copy(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "package", "extension.json"), Path.Combine(packageSource, "extension.json"));
                ZipFile.CreateFromDirectory(packageSource, packagePath);
                service = new ExtensionService(Path.Combine(root, "extensions"));

                ExtensionInstallResult installed = service.InstallAsync(packagePath).GetAwaiter().GetResult();
                service.SetEnabledAsync(installed.Extension.Manifest.Id, true).GetAwaiter().GetResult();

                ExtensionUiContribution panel = Assert.Single(ExtensionHostRuntime.GetUiContributionsSnapshot(), item => item.ExtensionId == "emerde.douyin-publisher"
                    && item.RegionName == ExtensionContractNames.ExtensionDetail);
                Assert.False(panel.Content.Focusable);
                Assert.Null(panel.Content.FocusVisualStyle);
                Assert.Equal(0, Assert.IsAssignableFrom<Border>(panel.Content).BorderThickness.Left);
                Assert.Contains(ExtensionHostRuntime.GetOverrides<IExtensionVideoAction>(ExtensionContractNames.VideoListActions), item => item.Id == "douyin.publish");
            }
            finally
            {
                service?.ShutdownAsync().GetAwaiter().GetResult();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        });
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

    [Fact]
    public async Task ExtensionDetails_SelectsExpandedCardAndRefreshesItsPanel()
    {
        await RunStaAsync(() =>
        {
            string extensionId = $"test.detail.{Guid.NewGuid():N}";
            using ExtensionCenterViewModel viewModel = new();
            ExtensionCardViewModel card = new(new InstalledExtensionInfo
            {
                Manifest = new ExtensionManifest { Id = extensionId, Name = "Detail" },
                InstallDirectory = string.Empty,
                IsEnabled = true,
                IsLoaded = true,
                IsValid = true,
                ValidationError = string.Empty,
            });
            viewModel.Extensions.Add(card);
            using IDisposable registration = ExtensionHostRuntime.RegisterUi(
                extensionId,
                ExtensionContractNames.ExtensionDetail,
                new Border());

            viewModel.ToggleExtensionDetailsCommand.Execute(card);

            Assert.Same(card, viewModel.SelectedExtension);
            Assert.True(card.IsExpanded);
            Assert.Single(viewModel.SelectedUiContributions);
        });
    }

    [Fact]
    public void Extension_RegistersVideoContextActionInsteadOfToolbarButton()
    {
        string entry = File.ReadAllText(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "ExtensionEntry.cs"));
        string manifest = File.ReadAllText(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "package", "extension.json"));

        Assert.Contains("RegisterOverride(ExtensionContractNames.VideoListActions", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoListToolbar", entry, StringComparison.Ordinal);
        Assert.Contains("ui.video-list-actions", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("ui.video-list-toolbar", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void PublisherPanel_UsesNonBlockingDispatcherUpdates()
    {
        string source = File.ReadAllText(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "DouyinPublisherPanel.cs"));

        Assert.Contains("Dispatcher.BeginInvoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.Invoke(", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.HasShutdownStarted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoContextMenu_PopulatesActionsBeforeManualOpen()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        int populate = source.IndexOf("PopulateVideoCardContextMenu(card);", StringComparison.Ordinal);
        int open = source.IndexOf("card.ContextMenu.IsOpen = true;", StringComparison.Ordinal);

        Assert.True(populate >= 0);
        Assert.True(open > populate);
    }

    [Fact]
    public void ExtensionCenter_UsesPixelScrollingAndShowsExtensionPanelBeforeSettings()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ExtensionCenterWindow.xaml"));
        int contribution = xaml.IndexOf("SelectedUiContributions", StringComparison.Ordinal);
        int settings = xaml.IndexOf("Text=\"扩展设置\"", StringComparison.Ordinal);

        Assert.Contains("ScrollViewer.CanContentScroll\" Value=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding PrimarySettings}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SecondarySettings}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HasTemplateOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("ExtensionTextInputStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DisplayValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsVisible, Converter={x:Static c:BoolToVisibilityConverter.Instance}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Description}\"", xaml, StringComparison.Ordinal);
        Assert.True(contribution >= 0);
        Assert.True(settings > contribution);
    }

    [Fact]
    public void ExtensionSetting_TitleTemplateAcceptsTextAndVariableOptions()
    {
        ExtensionSettingViewModel setting = new(new ExtensionSettingDefinition
        {
            Key = "title_template",
            Label = "标题模板",
            Section = "基础信息",
            Column = 1,
            Type = "text",
            DefaultValue = "精彩片段",
            Options = ["{title}", "{nickname}"],
        });

        ExtensionTemplateOptionViewModel nickname = Assert.Single(setting.TemplateOptions, option => option.Value == "{nickname}");
        setting.AppendTemplateOptionCommand.Execute(nickname);

        Assert.True(setting.HasTemplateOptions);
        Assert.Equal("基础信息", setting.Section);
        Assert.Equal("主播昵称", nickname.DisplayName);
        Assert.Equal("精彩片段 {主播昵称}", setting.DisplayValue);
        Assert.Equal("精彩片段 {nickname}", setting.Value);

        setting.DisplayValue = "回放 {直播标题} {日期}";

        Assert.Equal("回放 {title} {date}", setting.Value);
        Assert.Equal("回放 {直播标题} {日期}", setting.DisplayValue);
    }

    [Fact]
    public void ExtensionCard_SplitsManifestSettingsIntoSectionedColumns()
    {
        InstalledExtensionInfo info = new()
        {
            Manifest = new ExtensionManifest
            {
                Id = "test.publisher",
                Name = "测试投稿扩展",
                Settings =
                [
                    new ExtensionSettingDefinition { Key = "title", Label = "标题", Section = "基础信息", Column = 0 },
                    new ExtensionSettingDefinition { Key = "publish_time", Label = "发布时间", Section = "发布设置", Column = 1, Type = "choice", DefaultValue = "立即发布", Options = ["立即发布", "定时发布"] },
                    new ExtensionSettingDefinition { Key = "delay", Label = "延迟", Section = "发布设置", Column = 1, VisibleWhenKey = "publish_time", VisibleWhenValue = "定时发布" },
                ],
            },
            InstallDirectory = Path.GetTempPath(),
            IsEnabled = true,
            IsLoaded = true,
            IsValid = true,
            ValidationError = string.Empty,
        };

        ExtensionCardViewModel card = new(info);

        ExtensionSettingViewModel primary = Assert.Single(card.PrimarySettings);
        Assert.Equal(2, card.SecondarySettings.Count);
        ExtensionSettingViewModel secondary = card.SecondarySettings[0];
        ExtensionSettingViewModel delay = card.SecondarySettings[1];
        Assert.True(card.HasSecondarySettings);
        Assert.False(card.HasOnlyPrimarySettings);
        Assert.True(primary.ShowSectionHeader);
        Assert.True(secondary.ShowSectionHeader);
        Assert.Equal("基础信息", primary.Section);
        Assert.Equal("发布设置", secondary.Section);
        Assert.False(delay.IsVisible);

        secondary.Value = "定时发布";

        Assert.True(delay.IsVisible);
    }

    [Fact]
    public async Task StateStore_QueuesSelectedDouyinRoomOnceAndPersistsIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.{Guid.NewGuid():N}");
        string mediaPath = Path.Combine(root, "recording.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(mediaPath, [1, 2, 3]);
        try
        {
            PublisherStateStore store = new(root);
            const string roomUrl = "https://live.douyin.com/123";
            await store.SetRoomSelectedAsync(roomUrl, true);
            ExtensionMediaFinalizedEvent payload = CreatePayload("event-1", roomUrl, mediaPath);

            await store.EnqueueAsync(payload, CancellationToken.None);
            await store.EnqueueAsync(payload, CancellationToken.None);

            PublisherState persisted = new PublisherStateStore(root).Snapshot();
            PublisherQueueItem item = Assert.Single(persisted.Queue);
            Assert.Equal(payload.EventId, item.EventId);
            Assert.Equal(payload.FilePath, item.FilePath);
            Assert.Contains(roomUrl, persisted.SelectedRoomUrls);
            Assert.Contains(payload.EventId, persisted.HandledEventIds);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_QueuesSelectedRoomFromAnyPlatform()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.Platform.{Guid.NewGuid():N}");
        string mediaPath = Path.Combine(root, "recording.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(mediaPath, [1, 2, 3]);
        try
        {
            PublisherStateStore store = new(root);
            const string roomUrl = "https://live.bilibili.com/123";
            await store.SetRoomSelectedAsync(roomUrl, true);

            await store.EnqueueAsync(CreatePayload("bilibili-event", roomUrl, mediaPath, "Bilibili"), CancellationToken.None);

            PublisherQueueItem item = Assert.Single(new PublisherStateStore(root).Snapshot().Queue);
            Assert.Equal(roomUrl, item.RoomUrl);
            Assert.Equal(mediaPath, item.FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_DoesNotQueuePreviouslyIgnoredEventAfterRoomIsSelected()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.{Guid.NewGuid():N}");
        string mediaPath = Path.Combine(root, "recording.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(mediaPath, [1]);
        try
        {
            PublisherStateStore store = new(root);
            const string roomUrl = "https://live.douyin.com/456";
            ExtensionMediaFinalizedEvent payload = CreatePayload("event-2", roomUrl, mediaPath);

            await store.EnqueueAsync(payload, CancellationToken.None);
            await store.SetRoomSelectedAsync(roomUrl, true);
            await store.EnqueueAsync(payload, CancellationToken.None);

            Assert.Empty(store.Snapshot().Queue);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_PersistsAutomaticPublishOptionsWhenMediaIsFinalized()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.{Guid.NewGuid():N}");
        string mediaPath = Path.Combine(root, "automatic.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(mediaPath, [1, 2, 3]);
        try
        {
            const string roomUrl = "https://live.douyin.com/789";
            PublisherStateStore store = new(root);
            await store.SetRoomSelectedAsync(roomUrl, true);
            PublisherTaskOptions taskOptions = new(
                "{title}",
                "简介",
                "直播",
                "C:\\covers\\default.png",
                "官方活动",
                "合集",
                "内容由我原创",
                "00:00 开始",
                "标签",
                "上海",
                "热点",
                PublisherVisibility.Friends,
                false,
                new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.FromHours(8)));

            await store.EnqueueAsync(CreatePayload("event-options", roomUrl, mediaPath), taskOptions, CancellationToken.None);

            PublisherQueueItem queued = Assert.Single(new PublisherStateStore(root).Snapshot().Queue);
            PublisherTaskOptions persisted = Assert.IsType<PublisherTaskOptions>(queued.TaskOptions);
            Assert.Equal(taskOptions, persisted);
            Assert.Equal("automatic", queued.Source);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_QueuesOldVideoWithoutRoomMetadataAndDeduplicatesManualClicks()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.{Guid.NewGuid():N}");
        string mediaPath = Path.Combine(root, "old-recording.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(mediaPath, [1, 2, 3]);
        try
        {
            PublisherStateStore store = new(root);
            FileInfo file = new(mediaPath);
            ExtensionVideoFileInfo selected = new(
                mediaPath,
                string.Empty,
                "旧视频主播",
                string.Empty,
                "旧视频",
                file.Length,
                file.CreationTime,
                file.LastWriteTimeUtc);

            ManualEnqueueResult first = await store.EnqueueManualAsync([selected]);
            ManualEnqueueResult second = await store.EnqueueManualAsync([selected]);

            Assert.Equal(1, first.Queued);
            Assert.Equal(0, second.Queued);
            Assert.Equal(1, second.Duplicate);
            PublisherQueueItem queued = Assert.Single(new PublisherStateStore(root).Snapshot().Queue);
            Assert.Equal("manual", queued.Source);
            Assert.Equal(string.Empty, queued.RoomUrl);
            Assert.Equal(file.FullName, queued.FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_ManualQueueSkipsMissingFileWithoutBlockingExistingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.{Guid.NewGuid():N}");
        string mediaPath = Path.Combine(root, "existing.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(mediaPath, [1]);
        try
        {
            FileInfo file = new(mediaPath);
            ExtensionVideoFileInfo existing = new(mediaPath, string.Empty, string.Empty, string.Empty, string.Empty, file.Length, file.CreationTime, file.LastWriteTimeUtc);
            ExtensionVideoFileInfo missing = existing with { FilePath = Path.Combine(root, "missing.mp4") };
            PublisherStateStore store = new(root);

            ManualEnqueueResult result = await store.EnqueueManualAsync([missing, existing]);

            Assert.Equal(1, result.Queued);
            Assert.Equal(1, result.Missing);
            Assert.Single(store.Snapshot().Queue);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_PersistsManualPublishOptionsAndNormalizesValues()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.{Guid.NewGuid():N}");
        string mediaPath = Path.Combine(root, "manual.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(mediaPath, [1, 2]);
        try
        {
            FileInfo file = new(mediaPath);
            ExtensionVideoFileInfo selected = new(mediaPath, string.Empty, "主播", "Douyin", "标题", file.Length, file.CreationTime, file.LastWriteTimeUtc);
            PublisherTaskOptions options = new(
                "  投稿标题  ",
                "  投稿简介  ",
                "  游戏  ",
                string.Empty,
                "  官方活动  ",
                "  合集  ",
                "  内容由我原创  ",
                "  00:00 开始  ",
                "  游戏  ",
                "  上海  ",
                "  热点  ",
                "invalid",
                false,
                new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.FromHours(8)));
            PublisherStateStore store = new(root);

            ManualEnqueueResult result = await store.EnqueueManualAsync([selected], taskOptions: options);

            Assert.Equal(1, result.Queued);
            PublisherTaskOptions persisted = Assert.IsType<PublisherTaskOptions>(Assert.Single(new PublisherStateStore(root).Snapshot().Queue).TaskOptions);
            Assert.Equal("投稿标题", persisted.TitleTemplate);
            Assert.Equal("投稿简介", persisted.DescriptionTemplate);
            Assert.Equal("官方活动", persisted.OfficialActivity);
            Assert.Equal(PublisherVisibility.Public, persisted.Visibility);
            Assert.False(persisted.AllowSave);
            Assert.NotNull(persisted.ScheduledAt);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void StateStore_LoadsQueueCreatedBeforeSourceFieldWasAdded()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "publisher-state.json"), """
                {
                  "SelectedRoomUrls": [],
                  "HandledEventIds": ["event-1"],
                  "Queue": [
                    {
                      "EventId": "event-1",
                      "RoomUrl": "https://live.douyin.com/123",
                      "NickName": "主播",
                      "Title": "标题",
                      "FilePath": "C:\\videos\\old.mp4",
                      "FileSize": 1,
                      "QueuedAt": "2026-08-02T00:00:00+00:00",
                      "Status": "pending"
                    }
                  ]
                }
                """);

            PublisherQueueItem item = Assert.Single(new PublisherStateStore(root).Snapshot().Queue);

            Assert.Equal("automatic", item.Source);
            Assert.Null(item.TaskOptions);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_RestoresInterruptedTaskAndStartsItAgain()
    {
        string root = CreateStateDirectory("""
            {
              "Queue": [
                {
                  "EventId": "interrupted",
                  "FilePath": "C:\\videos\\interrupted.mp4",
                  "QueuedAt": "2026-08-02T00:00:00+00:00",
                  "Status": "uploading",
                  "Attempts": 1
                }
              ]
            }
            """);
        try
        {
            PublisherStateStore store = new(root);
            await store.RestoreInterruptedAsync();

            PublisherQueueItem? started = await store.TryStartNextAsync(DateTimeOffset.UtcNow);

            Assert.NotNull(started);
            Assert.Equal(PublisherQueueStatus.Preparing, started.Status);
            Assert.Equal(2, started.Attempts);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_WaitingLoginBlocksFollowingTasksUntilResume()
    {
        string root = CreateStateDirectory("""
            {
              "Queue": [
                {
                  "EventId": "login",
                  "FilePath": "C:\\videos\\one.mp4",
                  "QueuedAt": "2026-08-02T00:00:00+00:00",
                  "Status": "waiting_login"
                },
                {
                  "EventId": "next",
                  "FilePath": "C:\\videos\\two.mp4",
                  "QueuedAt": "2026-08-02T00:01:00+00:00",
                  "Status": "pending"
                }
              ]
            }
            """);
        try
        {
            PublisherStateStore store = new(root);

            Assert.Null(await store.TryStartNextAsync(DateTimeOffset.UtcNow));
            Assert.Equal(1, await store.ResumeBlockedAsync());
            Assert.NotNull(await store.TryStartNextAsync(DateTimeOffset.UtcNow));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StateStore_UsesRetryDelayAndStopsAtMaximumAttempts()
    {
        string root = CreateStateDirectory("""
            {
              "Queue": [
                {
                  "EventId": "retry",
                  "FilePath": "C:\\videos\\retry.mp4",
                  "QueuedAt": "2026-08-02T00:00:00+00:00",
                  "Status": "pending"
                }
              ]
            }
            """);
        try
        {
            PublisherStateStore store = new(root);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PublisherQueueItem started = Assert.IsType<PublisherQueueItem>(await store.TryStartNextAsync(now));

            await store.MarkRetryAsync(started.EventId, "temporary", 2, now);
            PublisherQueueItem retry = Assert.Single(store.Snapshot().Queue);
            Assert.Equal(PublisherQueueStatus.Retry, retry.Status);
            Assert.Equal(now + TimeSpan.FromSeconds(15), retry.NextAttemptAt);

            PublisherQueueItem second = Assert.IsType<PublisherQueueItem>(await store.TryStartNextAsync(now.AddMinutes(1)));
            await store.MarkRetryAsync(second.EventId, "final", 2, now.AddMinutes(1));
            PublisherQueueItem failed = Assert.Single(store.Snapshot().Queue);
            Assert.Equal(PublisherQueueStatus.Failed, failed.Status);
            Assert.Null(failed.NextAttemptAt);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PublisherOptions_ParsesSettingsAndFormatsPublishText()
    {
        PublisherOptions options = PublisherOptions.From(new Dictionary<string, string>
        {
            ["auto_publish"] = "false",
            ["confirm_before_publish"] = "true",
            ["title_template"] = "{nickname} {title}",
            ["description_template"] = "来自 {filename}",
            ["topics"] = "直播, 游戏",
            ["cover_path"] = "C:\\covers\\default.png",
            ["official_activity"] = "夏日活动",
            ["collection_name"] = "直播合集",
            ["declaration"] = "内容由我原创",
            ["video_chapters"] = "00:00 开始",
            ["tags"] = "游戏",
            ["location"] = "上海",
            ["hotspot"] = "热门直播",
            ["visibility"] = "仅自己可见",
            ["allow_save"] = "false",
            ["publish_time"] = "定时发布",
            ["schedule_delay_minutes"] = "90",
            ["max_retries"] = "20",
        });
        PublisherQueueItem item = new(
            "event",
            string.Empty,
            "主播",
            "精彩片段",
            "C:\\videos\\clip.mp4",
            1,
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
            PublisherQueueStatus.Pending);

        Assert.False(options.AutoPublish);
        Assert.True(options.ConfirmBeforePublish);
        Assert.Equal(11, options.MaximumAttempts);
        PublisherTaskOptions automatic = options.CreateAutomaticTaskOptions(new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.FromHours(8)));
        Assert.Equal("C:\\covers\\default.png", automatic.CoverPath);
        Assert.Equal("夏日活动", automatic.OfficialActivity);
        Assert.Equal("直播合集", automatic.CollectionName);
        Assert.Equal("内容由我原创", automatic.Declaration);
        Assert.Equal("00:00 开始", automatic.VideoChapters);
        Assert.Equal("游戏", automatic.Tags);
        Assert.Equal("上海", automatic.Location);
        Assert.Equal("热门直播", automatic.Hotspot);
        Assert.Equal(PublisherVisibility.Private, automatic.Visibility);
        Assert.False(automatic.AllowSave);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 9, 30, 0, TimeSpan.FromHours(8)), automatic.ScheduledAt);
        ExtensionVideoFileInfo selected = new(
            "C:\\videos\\clip.mp4",
            string.Empty,
            "主播",
            "Douyin",
            "精彩片段",
            1,
            DateTime.MinValue,
            DateTime.MinValue);
        PublisherTaskOptions manualDefaults = PublisherTaskOptions.CreateDefault(options, [selected]);
        Assert.Equal("主播 精彩片段", manualDefaults.TitleTemplate);
        Assert.Equal(automatic.CoverPath, manualDefaults.CoverPath);
        Assert.Equal(automatic.OfficialActivity, manualDefaults.OfficialActivity);
        Assert.Equal(automatic.CollectionName, manualDefaults.CollectionName);
        Assert.Equal(automatic.Visibility, manualDefaults.Visibility);
        Assert.Equal(automatic.AllowSave, manualDefaults.AllowSave);
        Assert.Equal("主播 精彩片段", PublisherTextFormatter.BuildTitle(options.TitleTemplate, item));
        Assert.Equal("来自 clip #直播 #游戏", PublisherTextFormatter.BuildDescription(options.DescriptionTemplate, options.Topics, item));
    }

    [Fact]
    public void PublisherTextFormatter_TruncatesTitleByTextElements()
    {
        PublisherQueueItem item = new(
            "event",
            string.Empty,
            string.Empty,
            new string('标', 40),
            "C:\\videos\\clip.mp4",
            1,
            DateTimeOffset.UtcNow,
            PublisherQueueStatus.Pending);

        string title = PublisherTextFormatter.BuildTitle("{title}", item);

        Assert.Equal(30, title.Length);
    }

    [Fact]
    public void PublisherBrowser_UsesSharedFluentTitleBarAndHandlesTaskPublishOptions()
    {
        string source = File.ReadAllText(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "DouyinPublisherBrowser.cs"));

        Assert.DoesNotContain("Content = \"隐藏\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 52", source, StringComparison.Ordinal);
        Assert.Contains("FluentWindow browserWindow = new()", source, StringComparison.Ordinal);
        Assert.Contains("FluentTitleBar titleBar = new()", source, StringComparison.Ordinal);
        Assert.Contains("ShowMaximize = true", source, StringComparison.Ordinal);
        Assert.Contains("WindowAppearance.EnableBorderless(browserWindow);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateCaptionButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility.Collapsed", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPublishOptionsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyCoverAsync", source, StringComparison.Ordinal);
        Assert.Contains("options.ScheduledAt", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WebViewWindows_UseSharedBorderlessAppearance()
    {
        string publisher = File.ReadAllText(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "DouyinPublisherBrowser.cs"));
        string cookieLogin = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "PlatformCookieLoginWindow.xaml.cs"));
        string douyinResolver = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "DouyinWebViewResolver.cs"));

        Assert.Contains("WindowAppearance.EnableBorderless(browserWindow);", publisher, StringComparison.Ordinal);
        Assert.Contains("WindowAppearance.EnableBorderless(this);", cookieLogin, StringComparison.Ordinal);
        Assert.Contains("WindowAppearance.EnableBorderless(createdWindow);", douyinResolver, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigRestoreWindow_UsesCompactShadow()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ConfigRestoreWindow.xaml"));

        Assert.Contains("BlurRadius=\"14\"", source, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0.10\"", source, StringComparison.Ordinal);
        Assert.Contains("ShadowDepth=\"2\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublisherManifest_ExposesEveryAutomaticPublishSetting()
    {
        string manifest = File.ReadAllText(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "package", "extension.json"));
        string[] keys =
        [
            "title_template",
            "description_template",
            "topics",
            "cover_path",
            "official_activity",
            "collection_name",
            "declaration",
            "video_chapters",
            "tags",
            "location",
            "hotspot",
            "visibility",
            "allow_save",
            "publish_time",
            "schedule_delay_minutes",
        ];

        foreach (string key in keys)
        {
            Assert.Contains($"\"key\": \"{key}\"", manifest, StringComparison.Ordinal);
        }
        Assert.Contains("\"version\": \"1.3.3\"", manifest, StringComparison.Ordinal);
        Assert.Contains("events.media-finalized.read", manifest, StringComparison.Ordinal);
        Assert.Contains("\"section\": \"基础信息\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"column\": 1", manifest, StringComparison.Ordinal);
        Assert.Contains("\"{title}\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"{nickname}\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"{filename}\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"{date}\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"visible_when_key\": \"publish_time\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"visible_when_value\": \"定时发布\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void PublisherUi_OffersTooltipsTemplateOptionsAndEveryHomeRoom()
    {
        string optionsPanel = File.ReadAllText(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "DouyinPublishOptionsPanel.cs"));
        string publisherPanel = File.ReadAllText(FindRepositoryFile("extensions", "Emerde.DouyinPublisher", "DouyinPublisherPanel.cs"));
        string dialogService = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Plugins", "ExtensionDialogService.cs"));
        string resources = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Resources.xaml"));

        Assert.Contains("ToolTipService.SetToolTip(control, description)", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("CreateTitleTemplateEditor()", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("(\"{title}\", \"直播标题\")", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("(\"{nickname}\", \"主播昵称\")", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("(\"{filename}\", \"文件名\")", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("(\"{date}\", \"日期\")", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("PublisherTemplateVariables.ToDisplay(options.TitleTemplate)", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("PublisherTemplateVariables.ToStorage(titleInput.Text)", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("FontSize = 17", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("FontWeight = FontWeights.Bold", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("SystemAccentColorPrimaryBrush", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("TextFillColorPrimaryBrush", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("EmerdeCardBrush", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("ThemeShadowChrome.IsShadowEnabledProperty", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("DisablePopupShadows(comboBox)", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("TryFindResource(\"ExtensionChoiceInputStyle\")", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("new Wpf.Ui.Controls.TextBox", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("TryFindResource(\"ExtensionTextInputStyle\")", optionsPanel, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEditable = true", optionsPanel, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExtensionChoiceInputStyle\"", resources, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExtensionTextInputStyle\"", resources, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"ExtensionTextInputStyle\"", resources, StringComparison.Ordinal);
        Assert.Contains("<Style x:Key=\"ExtensionChoiceInputStyle\"", resources, StringComparison.Ordinal);
        Assert.Contains("Background\" Value=\"{DynamicResource EmerdeCardBrush}", resources, StringComparison.Ordinal);
        Assert.Contains("EmerdeExtensionInputBorderBrush", resources, StringComparison.Ordinal);
        Assert.Contains("FocusVisualStyle = null", dialogService, StringComparison.Ordinal);
        Assert.Contains("BorderBrush = System.Windows.Media.Brushes.Transparent", dialogService, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(room => room.PlatformName", publisherPanel, StringComparison.Ordinal);
        Assert.Contains("Text = \"首页还没有直播间\"", publisherPanel, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DoesNotReferenceRemovedCookieGuides()
    {
        string readme = File.ReadAllText(FindRepositoryFile("README.md"));
        string localizedReadme = File.ReadAllText(FindRepositoryFile("README.zh-Hans.md"));

        Assert.DoesNotContain("GETCOOKIE_DOUYIN.md", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("GETCOOKIE_TIKTOK.md", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("GETCOOKIE_DOUYIN.md", localizedReadme, StringComparison.Ordinal);
        Assert.DoesNotContain("GETCOOKIE_TIKTOK.md", localizedReadme, StringComparison.Ordinal);
    }

    private static string CreateStateDirectory(string json)
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.DouyinPublisher.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "publisher-state.json"), json);
        return root;
    }

    private static ExtensionMediaFinalizedEvent CreatePayload(string eventId, string roomUrl, string filePath, string platformName = "Douyin")
    {
        return new ExtensionMediaFinalizedEvent(
            eventId,
            "recording",
            roomUrl,
            "主播",
            platformName,
            "标题",
            filePath,
            new FileInfo(filePath).Length,
            "mp4",
            DateTime.Now,
            DateTimeOffset.UtcNow,
            true,
            false);
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

    private sealed class TestExtensionDialogService : IExtensionDialogService
    {
        public Task<ExtensionDialogResult> ShowAsync(ExtensionDialogRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExtensionDialogResult.Close);
        }
    }
}
