using Emerde.Core;
using System.Text.Json;

namespace Emerde.Tests;

[Collection("MediaOperationRegistry")]
public sealed class RecordingCleanupServiceTests : IDisposable
{
    private readonly string stateDirectory = Path.Combine(
        Path.GetTempPath(),
        "emerde-cleanup-state-test-" + Guid.NewGuid().ToString("N"));
    private string StateFilePath => Path.Combine(stateDirectory, "recording-cleanup-state.json");

    public RecordingCleanupServiceTests()
    {
        Directory.CreateDirectory(stateDirectory);
        RecordingCleanupService.ResetStateForTests(StateFilePath);
    }

    public void Dispose()
    {
        RecordingCleanupService.ResetStateForTests(StateFilePath);
        if (Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Fact]
    public void StagedVideoMetadata_CommitsAndCanRollBackTheFinalSidecar()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string mediaPath = Path.Combine(directory, "record.mp4");
        string metadataPath = Path.Combine(directory, "record.mplr.json");
        try
        {
            using StagedVideoMetadata? staged = VideoRecordingMetadataStore.StageSidecarForMedia(
                mediaPath,
                new VideoRecordingMetadata { NickName = "Host" },
                "test-metadata");

            Assert.NotNull(staged);
            Assert.False(File.Exists(metadataPath));
            Assert.Equal(metadataPath, staged.Commit());
            Assert.True(File.Exists(metadataPath));
            staged.DeleteCommitted();
            Assert.False(File.Exists(metadataPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DeleteOrphanedSidecars_PreservesMetadataWithMediaAndDeletesOrphans()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "kept.mp4"), [1]);
            File.WriteAllText(Path.Combine(directory, "kept.mplr.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "orphan.mplr.json"), "{}");

            Assert.Equal(1, VideoRecordingMetadataStore.DeleteOrphanedSidecars(directory));
            Assert.True(File.Exists(Path.Combine(directory, "kept.mplr.json")));
            Assert.False(File.Exists(Path.Combine(directory, "orphan.mplr.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, DataRetentionUnitHelper.Days, 1)]
    [InlineData(10000, DataRetentionUnitHelper.Days, DataRetentionUnitHelper.MaximumValue)]
    public void RetentionDuration_ClampsImportedValues(int value, int unit, int expectedDays)
    {
        Assert.Equal(TimeSpan.FromDays(expectedDays), DataRetentionUnitHelper.ToTimeSpan(value, unit));
    }

    [Fact]
    public void RetentionCutoff_SaturatesAtDateTimeMinimum()
    {
        DateTime now = new(2026, 7, 29);

        Assert.Equal(DateTime.MinValue, RecordingCleanupService.GetRetentionCutoff(now, TimeSpan.FromDays(9999d * 365d)));
        Assert.Equal(now.AddDays(-1), RecordingCleanupService.GetRetentionCutoff(now, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void ExpirationTime_PrefersRecordingStartOverFileCompletionTime()
    {
        DateTime recordedAt = new(2026, 8, 5, 5, 53, 5, DateTimeKind.Local);
        DateTime completedAtUtc = new DateTime(2026, 8, 5, 23, 7, 28, DateTimeKind.Local).ToUniversalTime();

        DateTime expiration = RecordingCleanupService.GetExpirationTime(recordedAt, completedAtUtc, TimeSpan.FromDays(1));

        Assert.Equal(recordedAt.AddDays(1).ToUniversalTime(), expiration);
    }

    [Fact]
    public void HasEmerdeTags_RejectsGenericMediaTags()
    {
        Dictionary<string, string> genericTags = new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = "Video",
            ["artist"] = "Host",
            ["date"] = "2026-08-05",
        };
        Dictionary<string, string> emerdeTags = new(StringComparer.OrdinalIgnoreCase)
        {
            ["emerde_nick_name"] = "Host",
            ["emerde_recorded_at"] = "2026-08-05T05:53:05+08:00",
        };

        Assert.False(VideoRecordingMetadataStore.HasEmerdeTags(genericTags));
        Assert.True(VideoRecordingMetadataStore.HasEmerdeTags(emerdeTags));
    }

    [Fact]
    public async Task RunAsync_PersistsCleanupIdentityAndExpiration()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "future.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "future", new VideoRecordingMetadata
        {
            FileName = "future.mp4",
            NickName = "Host",
            RoomUrl = "https://example.test/room",
            RecordedAt = DateTime.Now,
        });

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(7);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.True(File.Exists(StateFilePath));
            using JsonDocument state = JsonDocument.Parse(await File.ReadAllTextAsync(StateFilePath));
            JsonElement root = state.RootElement;
            Assert.Equal(1, root.GetProperty("Version").GetInt32());
            Assert.True(root.GetProperty("EmbeddedMetadataMigrationCompleted").GetBoolean());
            JsonElement recording = root.GetProperty("Recordings")
                .EnumerateArray()
                .Single(item => string.Equals(item.GetProperty("Path").GetString(), mediaPath, StringComparison.OrdinalIgnoreCase));
            Assert.False(string.IsNullOrWhiteSpace(recording.GetProperty("RecordingId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(recording.GetProperty("FileIdentity").GetString()));
            Assert.True(recording.GetProperty("ExpiresAtUtc").GetDateTime() > DateTime.UtcNow);
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task PersistedCleanupState_LoadsBackupWhenPrimaryStructureIsInvalid()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "future.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        VideoRecordingMetadata metadata = new()
        {
            FileName = "future.mp4",
            NickName = "Host",
            RoomUrl = "https://example.test/room",
            RecordedAt = DateTime.Now,
        };
        _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "future", metadata);

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(7);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);
            await RecordingCleanupService.RunAsync([tempFolder]);
            RecordingCleanupService.TrackFile(mediaPath, metadata);
            Assert.True(File.Exists(StateFilePath + ".bak"));

            await File.WriteAllTextAsync(StateFilePath, "{}");
            RecordingCleanupService.ResetStateForTests(StateFilePath);

            Assert.Equal(1, RecordingCleanupService.GetScheduledEntryCountForTests());
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task PersistedCleanupState_IgnoresNullRecordingEntries()
    {
        await File.WriteAllTextAsync(StateFilePath, """
            {
              "Version": 1,
              "EmbeddedMetadataMigrationCompleted": true,
              "Recordings": [null]
            }
            """);

        Assert.Equal(0, RecordingCleanupService.GetScheduledEntryCountForTests());
    }

    [Fact]
    public async Task PersistedCleanupState_RetriesAfterTemporaryReadFailureWithoutOverwritingState()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "future.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "future", new VideoRecordingMetadata
        {
            FileName = "future.mp4",
            NickName = "Host",
            RoomUrl = "https://example.test/room",
            RecordedAt = DateTime.Now,
        });

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(7);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);
            await RecordingCleanupService.RunAsync([tempFolder]);
            string persistedState = await File.ReadAllTextAsync(StateFilePath);
            RecordingCleanupService.ResetStateForTests(StateFilePath);

            using (FileStream stateLock = new(StateFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await RecordingCleanupService.RunAsync([tempFolder]);
                Assert.Equal(0, RecordingCleanupService.GetScheduledEntryCountForTests());
            }

            Assert.Equal(persistedState, await File.ReadAllTextAsync(StateFilePath));
            Assert.Equal(1, RecordingCleanupService.GetScheduledEntryCountForTests());
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public void EmbeddedMetadataMigration_RemainsPendingAfterIncompleteScan()
    {
        Assert.False(RecordingCleanupService.ResolveEmbeddedMetadataMigrationCompleted(false, false));
        Assert.True(RecordingCleanupService.ResolveEmbeddedMetadataMigrationCompleted(false, true));
        Assert.True(RecordingCleanupService.ResolveEmbeddedMetadataMigrationCompleted(true, false));
    }

    [Fact]
    public async Task RunAsync_IncompleteScanPreservesExistingCleanupSchedule()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        string offlineFolder = tempFolder + "-offline";
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "future.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "future", new VideoRecordingMetadata
        {
            FileName = "future.mp4",
            NickName = "Host",
            RoomUrl = "https://example.test/room",
            RecordedAt = DateTime.Now,
        });

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(7);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync([tempFolder]);
            Assert.Equal(1, RecordingCleanupService.GetScheduledEntryCountForTests());

            Directory.Move(tempFolder, offlineFolder);
            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.Equal(1, RecordingCleanupService.GetScheduledEntryCountForTests());
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
            if (Directory.Exists(offlineFolder))
            {
                Directory.Delete(offlineFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_DoesNotDeleteAReplacementAtAnIndexedPath()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.mp4");
        await File.WriteAllTextAsync(mediaPath, "old");
        _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "old", new VideoRecordingMetadata
        {
            FileName = "old.mp4",
            NickName = "Host",
            RoomUrl = "https://example.test/room",
            RecordedAt = DateTime.Now.AddDays(-10),
        });

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);
            using (MediaOperationRegistry.Register(MediaOperationKind.Conversion, () => [mediaPath]))
            {
                await RecordingCleanupService.RunAsync([tempFolder]);
            }

            File.Delete(mediaPath);
            await File.WriteAllTextAsync(mediaPath, "replacement-content");
            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.True(File.Exists(mediaPath));
            Assert.Equal("replacement-content", await File.ReadAllTextAsync(mediaPath));
            Assert.True(RecordingCleanupService.IsAwaitingFreshMetadataForTests(mediaPath));

            Assert.True(VideoRecordingMetadataStore.WriteCompletedMetadata(mediaPath, new VideoRecordingMetadata
            {
                FileName = "old.mp4",
                NickName = "Replacement",
                RoomUrl = "https://example.test/replacement",
                RecordedAt = DateTime.Now,
            }));
            Assert.False(RecordingCleanupService.IsAwaitingFreshMetadataForTests(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_UsesRecordingStartForLongRecordingExpiration()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "long.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        File.SetLastWriteTimeUtc(mediaPath, DateTime.UtcNow);
        _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "long", new VideoRecordingMetadata
        {
            FileName = "long.mp4",
            NickName = "Host",
            RecordedAt = DateTime.Now.AddDays(-2),
        });

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.False(File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_WhenDataRetentionDisabled_DoesNotDeleteExpiredMedia()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        File.SetLastWriteTime(mediaPath, DateTime.Now.AddDays(-10));

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(false);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.True(File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEnabled_DoesNotDeleteUnownedMedia()
    {
        await VerifyCleanupAsync(hasMetadata: false, expectedExists: true);
    }

    [Fact]
    public async Task RunAsync_WhenEnabled_DeletesOwnedExpiredMedia()
    {
        await VerifyCleanupAsync(hasMetadata: true, expectedExists: false);
    }

    [Fact]
    public async Task RunAsync_WhenEnabled_DeletesExpiredMediaWithAttachedMetadata()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.ts");
        await File.WriteAllTextAsync(mediaPath, "media");
        Assert.True(VideoRecordingMetadataStore.WriteCompletedMetadata(mediaPath, new VideoRecordingMetadata
        {
            FileName = "old.ts",
            NickName = "Host",
            RoomUrl = "https://example.test/room",
            RecordedAt = DateTime.Now.AddDays(-10),
        }));
        File.SetLastWriteTime(mediaPath, DateTime.Now.AddDays(-10));

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.False(File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEnabled_PreservesProtectedExpiredMedia()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        File.SetLastWriteTime(mediaPath, DateTime.Now.AddDays(-10));
        _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "old", new VideoRecordingMetadata
        {
            FileName = "old.mp4",
            NickName = "Host",
        });

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);
            using IDisposable operation = MediaOperationRegistry.Register(MediaOperationKind.Conversion, () => [mediaPath]);

            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.True(File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenDeletingExpiredMediaRemovesOnlyEmptyParentDirectories()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        string nestedFolder = Path.Combine(tempFolder, "host", "session");
        Directory.CreateDirectory(nestedFolder);
        string mediaPath = Path.Combine(nestedFolder, "old.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        _ = VideoRecordingMetadataStore.WriteSidecar(nestedFolder, "old", new VideoRecordingMetadata
        {
            FileName = "old.mp4",
            NickName = "Host",
            RecordedAt = DateTime.Now.AddDays(-10),
        });

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.True(Directory.Exists(tempFolder));
            Assert.False(Directory.Exists(Path.Combine(tempFolder, "host")));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_QuarantinesCorruptSidecar()
    {
        string directory = Path.Combine(Path.GetTempPath(), "emerde-metadata-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string videoPath = Path.Combine(directory, "record.mp4");
        string metadataPath = Path.Combine(directory, "record.mplr.json");
        File.WriteAllText(videoPath, "media");
        File.WriteAllText(metadataPath, "{");

        try
        {
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(videoPath));

            Assert.False(VideoRecordingMetadataStore.HasAnyMetadata(metadata));
            Assert.False(File.Exists(metadataPath));
            Assert.True(File.Exists(metadataPath + ".invalid"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TryDeleteSidecar_RetainsMetadataForConvertedTarget()
    {
        string directory = Path.Combine(Path.GetTempPath(), "emerde-metadata-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "record.ts");
        string targetPath = Path.Combine(directory, "record.mp4");
        File.WriteAllText(targetPath, "target");
        string? metadataPath = VideoRecordingMetadataStore.WriteSidecar(directory, "record", new VideoRecordingMetadata
        {
            FileName = "record.mp4",
            RoomUrl = "https://example.test/room",
        });

        try
        {
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourcePath);

            Assert.NotNull(metadataPath);
            Assert.True(File.Exists(metadataPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task VerifyCleanupAsync(bool hasMetadata, bool expectedExists)
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        File.SetLastWriteTime(mediaPath, DateTime.Now.AddDays(-10));
        if (hasMetadata)
        {
            _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "old", new VideoRecordingMetadata
            {
                FileName = "old.mp4",
                NickName = "Host",
                RoomUrl = "https://example.test/room",
                RecordedAt = DateTime.Now.AddDays(-10),
            });
        }

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync([tempFolder]);

            Assert.Equal(expectedExists, File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }
}
