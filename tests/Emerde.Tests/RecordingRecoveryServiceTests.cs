using Emerde.Core;
using Emerde.Plugins;
using System.Text.Json.Nodes;

namespace Emerde.Tests;

[Collection("MediaOperationRegistry")]
public sealed class RecordingRecoveryServiceTests
{
    [Fact]
    public void PendingProcessingQueueCreatesAndCancelsWorkByBatch()
    {
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "RecordingRecoveryService.cs"));

        Assert.Contains("paths.Chunk(PendingProcessingBatchSize)", code, StringComparison.Ordinal);
        Assert.Contains("QueueProcessAsync(batch, token)", code, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(token)", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("output_track_timeline_mismatch:audio=1.000,video=4.000", true)]
    [InlineData("duration_mismatch:expected=10.000,actual=1.000", true)]
    [InlineData("native_exit_code:1", false)]
    [InlineData(null, false)]
    public void IsTerminalRecoveryFailure_OnlyBlocksUnrecoverableValidationFailures(
        string? failureReason,
        bool expected)
    {
        Assert.Equal(expected, RecordingRecoveryService.IsTerminalRecoveryFailure(failureReason));
    }

    [Fact]
    public void SelectFailureReason_PreservesTerminalFailure()
    {
        string terminal = "duration_mismatch:expected=10.000,actual=1.000";

        Assert.Equal(terminal, RecordingRecoveryService.SelectFailureReason("native_exit_code:1", terminal));
        Assert.Equal(terminal, RecordingRecoveryService.SelectFailureReason(terminal, "native_exit_code:1"));
    }

    [Fact]
    public void CreateSourceStateFingerprint_ChangesWhenSourceChanges()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-recovery-fingerprint-{Guid.NewGuid():N}.ts");
        File.WriteAllBytes(path, [1]);

        try
        {
            string before = RecordingRecoveryService.CreateSourceStateFingerprint([path]);
            File.WriteAllBytes(path, [1, 2]);
            string after = RecordingRecoveryService.CreateSourceStateFingerprint([path]);

            Assert.NotEqual(before, after);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ProcessAsync_AdoptsReservedCompletedSourceAfterRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-reserved-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string markerPath = Path.Combine(directory, "pending.json");
        string sourcePath = Path.Combine(directory, "session_000.ts");
        string targetPath = Path.Combine(directory, "session.mkv");
        File.WriteAllBytes(sourcePath, [1]);
        File.WriteAllBytes(targetPath, [1]);
        _ = VideoRecordingMetadataStore.WriteSidecar(
            directory,
            "session_000",
            new VideoRecordingMetadata { RoomUrl = "https://live.example/room" });
        JsonObject marker = new()
        {
            ["SourcePattern"] = sourcePath,
            ["TargetFormat"] = ".mkv",
            ["RemoveSource"] = false,
            ["MergeSessionParts"] = false,
            ["ReservedCompletedSources"] = new JsonObject { [sourcePath] = targetPath },
        };
        File.WriteAllText(markerPath, marker.ToJsonString());

        try
        {
            await RecordingRecoveryService.ProcessAsync(markerPath);

            Assert.False(File.Exists(markerPath));
            Assert.True(File.Exists(sourcePath));
            Assert.True(File.Exists(targetPath));
            Assert.False(File.Exists(Path.Combine(directory, "session_2.mkv")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("host_2026-08-14_000.ts", "host_2026-08-14_%03d.ts")]
    [InlineData("host_2026-08-14_0123.flv", "host_2026-08-14_%03d.flv")]
    [InlineData("host_2026-08-14.ts", "host_2026-08-14.ts")]
    public void BuildRecoverySourcePattern_GroupsSessionParts(string fileName, string expectedFileName)
    {
        string directory = Path.Combine(Path.GetTempPath(), "emerde-recovery-pattern");

        Assert.Equal(
            Path.Combine(directory, expectedFileName),
            RecordingRecoveryService.BuildRecoverySourcePattern(Path.Combine(directory, fileName)));
    }

    [Fact]
    public void ResolveFinalizationInputPath_ResumesFromDiskWithoutDuplicatingCommittedRename()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-finalization-resume-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string originalPath = Path.Combine(directory, "recording.mkv");
        string targetPath = Path.Combine(directory, "final-name.mkv");
        try
        {
            File.WriteAllBytes(originalPath, [1]);
            Assert.Equal(originalPath, RecordingRecoveryService.ResolveFinalizationInputPath(originalPath, targetPath, committed: false));
            Assert.Equal(originalPath, RecordingRecoveryService.ResolveFinalizationInputPath(originalPath, targetPath, committed: true));

            File.Move(originalPath, targetPath);
            Assert.Equal(targetPath, RecordingRecoveryService.ResolveFinalizationInputPath(originalPath, targetPath, committed: false));
            Assert.Equal(targetPath, RecordingRecoveryService.ResolveFinalizationInputPath(originalPath, targetPath, committed: true));

            File.WriteAllBytes(originalPath, [2]);
            Assert.Null(RecordingRecoveryService.ResolveFinalizationInputPath(originalPath, targetPath, committed: false));
            Assert.Equal(targetPath, RecordingRecoveryService.ResolveFinalizationInputPath(originalPath, targetPath, committed: true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("host_20260815.ts", "host_20260815.ts", "host_20260815.ts")]
    [InlineData("host_20260815.ts", "host.ts", "host_%03d.ts")]
    [InlineData("host.ts", "host.ts", "host.ts")]
    public void ResolveRecoverySourcePattern_UsesMetadataToDistinguishStandaloneFiles(
        string sourceFileName,
        string metadataFileName,
        string expectedFileName)
    {
        string directory = Path.Combine(Path.GetTempPath(), "emerde-recovery-pattern");
        string sourcePath = Path.Combine(directory, sourceFileName);

        Assert.Equal(
            Path.Combine(directory, expectedFileName),
            RecordingRecoveryService.ResolveRecoverySourcePattern(
                sourcePath,
                new VideoRecordingMetadata { FileName = metadataFileName }));
    }

    [Theory]
    [InlineData("record", "record", true)]
    [InlineData("record", "record_2", true)]
    [InlineData("record", "record_27", true)]
    [InlineData("record", "record_000", false)]
    [InlineData("record", "record_001", false)]
    [InlineData("record", "record_02", false)]
    [InlineData("record", "record_1", false)]
    [InlineData("record", "record_backup", false)]
    [InlineData("record", "record_2_backup", false)]
    [InlineData("record", "recording", false)]
    public void IsCompletedOutputStem_AcceptsOnlyReservedNumericSuffixes(
        string expectedStem,
        string candidateStem,
        bool expected)
    {
        Assert.Equal(expected, RecordingRecoveryService.IsCompletedOutputStem(expectedStem, candidateStem));
    }

    [Fact]
    public async Task ProcessAsync_RetriesBlockedMarkerFromPreviousRecoveryPolicy()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string markerPath = Path.Combine(directory, "pending.json");
        string sourcePath = Path.Combine(directory, "session.ts");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);
        _ = VideoRecordingMetadataStore.WriteSidecar(
            directory,
            "session",
            new VideoRecordingMetadata { RoomUrl = "https://live.example/room" });
        JsonObject marker = new()
        {
            ["SourcePattern"] = sourcePath,
            ["TargetFormat"] = ".mkv",
            ["RemoveSource"] = false,
            ["MergeSessionParts"] = false,
            ["FailureCount"] = 3,
            ["LastFailureReason"] = "duration_mismatch:expected=10.000,actual=1.000",
            ["RetryBlocked"] = true,
            ["BlockedSourceStateFingerprint"] = RecordingRecoveryService.CreateSourceStateFingerprint([sourcePath]),
        };
        File.WriteAllText(markerPath, marker.ToJsonString());

        try
        {
            await RecordingRecoveryService.ProcessAsync(markerPath);

            JsonNode saved = JsonNode.Parse(File.ReadAllText(markerPath))!;
            Assert.Equal(1, saved["RecoveryPolicyVersion"]!.GetValue<int>());
            Assert.Equal(1, saved["FailureCount"]!.GetValue<int>());
            Assert.False(saved["RetryBlocked"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateMediaFinalizedEvents_UsesFinalFileAndStableIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.Finalized.{Guid.NewGuid():N}");
        string markerPath = Path.Combine(root, "recording-id.json");
        string sourcePath = Path.Combine(root, "recording.ts");
        string targetPath = Path.Combine(root, "recording.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(targetPath, [1, 2, 3, 4]);
        try
        {
            VideoRecordingMetadata sourceMetadata = new()
            {
                NickName = "主播",
                RoomUrl = "https://live.douyin.com/123",
                Platform = "Douyin",
                Title = "直播标题",
                RecordedAt = new DateTime(2026, 8, 2, 1, 2, 3),
            };

            ExtensionMediaFinalizedEvent first = Assert.Single(RecordingRecoveryService.CreateMediaFinalizedEvents(
                markerPath,
                sourcePath,
                ".mp4",
                string.Empty,
                false,
                string.Empty,
                new Dictionary<string, string> { [sourcePath] = targetPath },
                [sourcePath],
                sourceMetadata,
                DateTimeOffset.UtcNow));
            ExtensionMediaFinalizedEvent second = Assert.Single(RecordingRecoveryService.CreateMediaFinalizedEvents(
                markerPath,
                sourcePath,
                ".mp4",
                string.Empty,
                false,
                string.Empty,
                new Dictionary<string, string> { [sourcePath] = targetPath },
                [sourcePath],
                sourceMetadata,
                DateTimeOffset.UtcNow.AddMinutes(1)));

            Assert.Equal(first.EventId, second.EventId);
            Assert.Equal("recording-id", first.RecordingId);
            Assert.Equal(Path.GetFullPath(targetPath), first.FilePath);
            Assert.Equal(4, first.FileSize);
            Assert.Equal("mp4", first.Container);
            Assert.Equal(sourceMetadata.RoomUrl, first.RoomUrl);
            Assert.Equal(sourceMetadata.NickName, first.NickName);
            Assert.Equal(sourceMetadata.Platform, first.PlatformName);
            Assert.Equal(sourceMetadata.Title, first.Title);
            Assert.Equal(sourceMetadata.RecordedAt, first.RecordedAt);
            Assert.True(first.WasTranscoded);
            Assert.False(first.WasMerged);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task QueueProcessAsync_HandlesInvalidPathsWithoutFaulting()
    {
        await RecordingRecoveryService.QueueProcessAsync(["\0"]);
    }

    [Fact]
    public async Task ProcessAsync_IgnoresInvalidMarkerPath()
    {
        await RecordingRecoveryService.ProcessAsync("\0");
    }

    [Fact]
    public void ShouldUpdateForGlobalChange_OnlyMatchesRoomsFollowingGlobalSettings()
    {
        Room globalRoom = new() { RoomUrl = "https://example.com/global", IsFollowGlobalSettings = true };
        Room localRoom = new() { RoomUrl = "https://example.com/local", IsFollowGlobalSettings = false };

        Assert.True(RecordingRecoveryService.ShouldUpdateForGlobalChange(globalRoom.RoomUrl, [globalRoom, localRoom]));
        Assert.False(RecordingRecoveryService.ShouldUpdateForGlobalChange(localRoom.RoomUrl, [globalRoom, localRoom]));
        Assert.False(RecordingRecoveryService.ShouldUpdateForGlobalChange("https://example.com/missing", [globalRoom, localRoom]));
        Assert.False(RecordingRecoveryService.ShouldUpdateForGlobalChange(string.Empty, [globalRoom, localRoom]));
    }

    [Fact]
    public void Register_StoresRoomOwnershipForAutomaticPostProcessing()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}.ts");
        string roomUrl = "https://example.com/room";
        string? markerPath = RecordingRecoveryService.Register(sourcePattern, new RoomRecordingOptions
        {
            RecordFormat = "TS/FLV -> MKV",
            IsOptimizeAudio = true,
        }, roomUrl);

        Assert.NotNull(markerPath);
        try
        {
            Assert.Contains($"\"RoomUrl\": \"{roomUrl}\"", File.ReadAllText(markerPath!));
            Assert.Contains("\"OptimizeAudio\": true", File.ReadAllText(markerPath!));
            Assert.Contains("\"FinalizeName\": false", File.ReadAllText(markerPath!));
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
    }

    [Fact]
    public void ResolveRoomUrl_UsesEmbeddedMetadataForLegacyMarker()
    {
        string mediaPath = Path.Combine(Path.GetTempPath(), $"emerde-legacy-{Guid.NewGuid():N}.ts");
        const string roomUrl = "https://example.com/legacy-room";
        File.WriteAllBytes(mediaPath, [1, 2, 3]);
        try
        {
            Assert.True(VideoRecordingMetadataStore.WriteCompletedMetadata(mediaPath, new VideoRecordingMetadata
            {
                RoomUrl = roomUrl,
            }));

            Assert.Equal(roomUrl, RecordingRecoveryService.ResolveRoomUrl(string.Empty, [mediaPath]));
        }
        finally
        {
            File.Delete(mediaPath);
            File.Delete(mediaPath + ".mplr.json");
        }
    }

    [Fact]
    public void UpdateOptions_AppliesLatestFormatAndRemoveSourceOutsideCurrentSaveFolder()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}.ts");
        string? markerPath = RecordingRecoveryService.Register(sourcePattern, new RoomRecordingOptions
        {
            RecordFormat = "TS/FLV -> MP4",
            IsRemoveTs = false,
        });

        Assert.NotNull(markerPath);
        try
        {
            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV -> MKV",
                IsRemoveTs = true,
                IsOptimizeAudio = true,
            }));

            string marker = File.ReadAllText(markerPath!);
            Assert.Contains("\"TargetFormat\": \".mkv\"", marker);
            Assert.Contains("\"RemoveSource\": true", marker);
            Assert.Contains("\"OptimizeAudio\": true", marker);

            Assert.False(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV",
            }));
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
    }

    [Fact]
    public void UpdateOptions_KeepsSessionMarkerWhenLatestFormatIsRaw()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}_%03d.ts");
        string? markerPath = RecordingRecoveryService.RegisterSessionParts(sourcePattern, ".ts", removeSource: false);

        Assert.NotNull(markerPath);
        try
        {
            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV",
                IsRemoveTs = false,
            }));

            string marker = File.ReadAllText(markerPath!);
            Assert.Contains("\"TargetFormat\": \".ts\"", marker);
            Assert.Contains("\"RemoveSource\": false", marker);
            Assert.Contains("\"MergeSessionParts\": true", marker);
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
    }

    [Fact]
    public void StallSessionMarker_DisablesMergingAndSurvivesRawFormatUpdate()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}_%03d.ts");
        string? markerPath = RecordingRecoveryService.RegisterSessionParts(sourcePattern, ".ts", removeSource: false);

        Assert.NotNull(markerPath);
        try
        {
            Assert.True(RecordingRecoveryService.MarkSessionPartsAsStallSegments(markerPath!));
            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV",
                IsRemoveTs = false,
            }));

            JsonObject marker = JsonNode.Parse(File.ReadAllText(markerPath!))!.AsObject();
            Assert.False(marker["MergeSessionParts"]!.GetValue<bool>());
            Assert.Equal(VideoRecordingMetadataStore.TimelineStallSegmentReason, marker["SegmentReason"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
    }

    [Fact]
    public void UpdateOptions_UpgradesLegacyMarkerWithoutCompletionState()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string markerPath = Path.Combine(directory, "pending.json");
        string sourcePattern = Path.Combine(directory, "record_%03d.ts");
        JsonObject legacyMarker = new()
        {
            ["SourcePattern"] = sourcePattern,
            ["TargetFormat"] = ".mp4",
        };
        File.WriteAllText(markerPath, legacyMarker.ToJsonString());

        try
        {
            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV -> MKV",
            }));

            JsonObject marker = JsonNode.Parse(File.ReadAllText(markerPath))!.AsObject();
            Assert.Equal(".mkv", marker["TargetFormat"]!.GetValue<string>());
            Assert.NotNull(marker["CompletedSources"]);
            Assert.False(marker["OptimizeAudio"]!.GetValue<bool>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateOptions_RejectsCompletionStateOutsideSourceDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}");
        string otherDirectory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-other-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(otherDirectory);
        string markerPath = Path.Combine(directory, "pending.json");
        string sourcePattern = Path.Combine(directory, "record_%03d.ts");
        string source = Path.Combine(directory, "record_000.ts");
        string target = Path.Combine(otherDirectory, "record_000.mkv");
        JsonObject marker = new()
        {
            ["SourcePattern"] = sourcePattern,
            ["TargetFormat"] = ".mkv",
            ["CompletedSources"] = new JsonObject { [source] = target },
        };
        File.WriteAllText(markerPath, marker.ToJsonString());

        try
        {
            Assert.False(RecordingRecoveryService.UpdateOptions(markerPath, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV -> MKV",
            }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(otherDirectory, recursive: true);
        }
    }

    [Fact]
    public void UpdateOptions_AppliesLatestRemoveSourceToSessionMarker()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}_%03d.ts");
        string? markerPath = RecordingRecoveryService.RegisterSessionParts(sourcePattern, ".mkv", removeSource: true);

        Assert.NotNull(markerPath);
        try
        {
            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV -> MKV",
                IsRemoveTs = false,
            }));

            string marker = File.ReadAllText(markerPath!);
            Assert.Contains("\"TargetFormat\": \".mkv\"", marker);
            Assert.Contains("\"RemoveSource\": false", marker);
            Assert.Contains("\"MergeSessionParts\": true", marker);
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
    }

    [Fact]
    public void UpdateOptions_PreservesCompletedSessionTransaction()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}");
        string sourcePattern = Path.Combine(directory, "session_%03d.ts");
        string completedTargetPath = Path.Combine(directory, "session.mkv");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(completedTargetPath, [1]);
        string? markerPath = RecordingRecoveryService.RegisterSessionParts(sourcePattern, ".mkv", removeSource: true);

        Assert.NotNull(markerPath);
        try
        {
            JsonObject marker = JsonNode.Parse(File.ReadAllText(markerPath!))!.AsObject();
            marker["CompletedTargetPath"] = completedTargetPath;
            File.WriteAllText(markerPath!, marker.ToJsonString());

            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV -> MP4",
                IsRemoveTs = false,
            }));

            JsonObject updated = JsonNode.Parse(File.ReadAllText(markerPath!))!.AsObject();
            Assert.Equal(".mkv", updated["TargetFormat"]!.GetValue<string>());
            Assert.True(updated["RemoveSource"]!.GetValue<bool>());
            Assert.Equal(completedTargetPath, updated["CompletedTargetPath"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(markerPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateOptions_ReusesCompletedIntermediateWhenSwitchingToRawFormat()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePattern = Path.Combine(directory, "session_%03d.ts");
        string intermediatePath = Path.Combine(directory, "session.ts");
        File.WriteAllBytes(intermediatePath, [1]);
        string? markerPath = RecordingRecoveryService.RegisterSessionParts(sourcePattern, ".mkv", removeSource: false);

        Assert.NotNull(markerPath);
        try
        {
            JsonObject marker = JsonNode.Parse(File.ReadAllText(markerPath!))!.AsObject();
            marker["IntermediateTargetPath"] = intermediatePath;
            File.WriteAllText(markerPath!, marker.ToJsonString());

            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV",
                IsRemoveTs = false,
            }));

            JsonObject updated = JsonNode.Parse(File.ReadAllText(markerPath!))!.AsObject();
            Assert.Equal(intermediatePath, updated["CompletedTargetPath"]!.GetValue<string>());
            Assert.Equal(string.Empty, updated["IntermediateTargetPath"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(markerPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PendingPaths_ProtectActualSegmentsAndReservedTransactionTargets()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePattern = Path.Combine(directory, "session_%03d.ts");
        string literalSource = sourcePattern;
        string numberedSource = Path.Combine(directory, "session_000.ts");
        string intermediatePath = Path.Combine(directory, "session.ts");
        string completedPath = Path.Combine(directory, "session.mkv");
        File.WriteAllBytes(literalSource, [1]);
        File.WriteAllBytes(numberedSource, [1]);
        string? markerPath = RecordingRecoveryService.RegisterSessionParts(sourcePattern, ".mkv", removeSource: true);

        Assert.NotNull(markerPath);
        try
        {
            JsonObject marker = JsonNode.Parse(File.ReadAllText(markerPath!))!.AsObject();
            marker["IntermediateTargetPath"] = intermediatePath;
            marker["CompletedTargetPath"] = completedPath;
            marker["CompletedSources"] = new JsonObject { [numberedSource] = Path.ChangeExtension(numberedSource, ".mkv") };
            File.WriteAllText(markerPath!, marker.ToJsonString());

            string[] protectedPaths = RecordingRecoveryService.GetPendingSourcePatterns();

            Assert.Contains(literalSource, protectedPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(numberedSource, protectedPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(intermediatePath, protectedPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(completedPath, protectedPaths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(markerPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_PreservesMarkerWhenItIsTemporarilyUnreadable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{}");

        try
        {
            await using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await RecordingRecoveryService.ProcessAsync(path);
            }

            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ProcessAsync_QuarantinesPermanentlyInvalidMarker()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{");

        try
        {
            await RecordingRecoveryService.ProcessAsync(path);

            Assert.False(File.Exists(path));
            Assert.True(File.Exists(path + ".invalid"));
            Assert.Contains("JSON", await File.ReadAllTextAsync(path + ".invalid.reason.txt"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".invalid");
            File.Delete(path + ".invalid.reason.txt");
        }
    }

    [Fact]
    public void RegisterSessionParts_StoresOptimizedAudioForCrashRecovery()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-session-{Guid.NewGuid():N}_%03d.ts");
        string? markerPath = RecordingRecoveryService.RegisterSessionParts(
            sourcePattern,
            ".mp4",
            removeSource: false,
            optimizeAudio: true);

        Assert.NotNull(markerPath);
        try
        {
            Assert.Contains("\"OptimizeAudio\": true", File.ReadAllText(markerPath!));
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
    }

    [Fact]
    public void IsRecoverySourceAllowed_AcceptsExistingMediaFromPreviousSaveFolder()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-previous-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "record.ts");
        File.WriteAllBytes(source, [1]);
        _ = VideoRecordingMetadataStore.WriteSidecar(
            directory,
            "record",
            new VideoRecordingMetadata { RoomUrl = "https://live.example/room" });

        try
        {
            Assert.True(RecordingRecoveryService.IsRecoverySourceAllowed(source, source, []));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IsRecoverySourceAllowed_RejectsUnmarkedMediaOutsideConfiguredFolders()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-untrusted-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "record.ts");
        File.WriteAllBytes(source, [1]);

        try
        {
            Assert.False(RecordingRecoveryService.IsRecoverySourceAllowed(source, source, []));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("relative.ts", ".mp4")]
    [InlineData("C:\\recording.txt", ".mp4")]
    [InlineData("C:\\record_%02d.ts", ".mp4")]
    [InlineData("C:\\record_%03d_%name.ts", ".mp4")]
    [InlineData("C:\\record.ts", ".avi")]
    [InlineData("C:\\record.ts", ".ts")]
    public async Task ProcessAsync_QuarantinesSemanticallyInvalidMarker(string sourcePattern, string targetFormat)
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, $$"""{"SourcePattern":"{{sourcePattern.Replace("\\", "\\\\", StringComparison.Ordinal)}}","TargetFormat":"{{targetFormat}}"}""");

        try
        {
            await RecordingRecoveryService.ProcessAsync(path);

            Assert.False(File.Exists(path));
            Assert.True(File.Exists(path + ".invalid"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".invalid");
            File.Delete(path + ".invalid.reason.txt");
        }
    }

    [Fact]
    public async Task ProcessAsync_QuarantinesCompletedTargetOutsideSourceDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}.json");
        string sourcePattern = Path.Combine(Path.GetTempPath(), "source", "record_%03d.ts");
        string completedTargetPath = Path.Combine(Path.GetTempPath(), "other", "record.mkv");
        JsonObject marker = new()
        {
            ["SourcePattern"] = sourcePattern,
            ["TargetFormat"] = ".mkv",
            ["RemoveSource"] = true,
            ["MergeSessionParts"] = true,
            ["CompletedTargetPath"] = completedTargetPath,
        };
        await File.WriteAllTextAsync(path, marker.ToJsonString());

        try
        {
            await RecordingRecoveryService.ProcessAsync(path);

            Assert.False(File.Exists(path));
            Assert.True(File.Exists(path + ".invalid"));
            Assert.Contains("不在源录制目录", await File.ReadAllTextAsync(path + ".invalid.reason.txt"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".invalid");
            File.Delete(path + ".invalid.reason.txt");
        }
    }

    [Fact]
    public void GetSourceFiles_ReturnsCompletedSegmentsInOrder()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string pattern = Path.Combine(directory, "record_%03d.ts");
            File.WriteAllBytes(Path.Combine(directory, "record_001.ts"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "record_000.ts"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "other_000.ts"), [1]);

            string[] result = RecordingRecoveryService.GetSourceFiles(pattern);

            Assert.Equal(
                [Path.Combine(directory, "record_000.ts"), Path.Combine(directory, "record_001.ts")],
                result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetSourceFiles_RejectsMissingAndEmptySources()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string empty = Path.Combine(directory, "empty.ts");
            File.WriteAllBytes(empty, []);

            Assert.Empty(RecordingRecoveryService.GetSourceFiles(empty));
            Assert.Empty(RecordingRecoveryService.GetSourceFiles(Path.Combine(directory, "missing.ts")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetSourceFiles_IncludesLegacyLiteralSegmentPattern()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string pattern = Path.Combine(directory, "record_%03d.ts");
            File.WriteAllBytes(pattern, [1]);
            File.WriteAllBytes(Path.Combine(directory, "record_000.ts"), [2]);

            Assert.Equal([pattern, Path.Combine(directory, "record_000.ts")], RecordingRecoveryService.GetSourceFiles(pattern));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SourceProcessingKeys_CoalesceOverlappingSourcesAndSemanticKeyIncludesAllOptions()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-recovery-key-{Guid.NewGuid():N}");
        string pattern = Path.Combine(root, "record_%03d.ts");
        string first = Path.Combine(root, "record_000.ts");
        string second = Path.Combine(root, "record_001.ts");

        string[] ordered = RecordingRecoveryService.CreateSourceProcessingKeys([first, second]);
        string[] reversed = RecordingRecoveryService.CreateSourceProcessingKeys([second, first]);
        string[] overlapping = RecordingRecoveryService.CreateSourceProcessingKeys([first]);
        string orderedSemanticKey = RecordingRecoveryService.CreateRecoverySemanticKey(
            [first, second], ".mkv", removeSource: false, optimizeAudio: false, mergeSessionParts: true);
        string reversedSemanticKey = RecordingRecoveryService.CreateRecoverySemanticKey(
            [second, first], ".MKV", removeSource: false, optimizeAudio: false, mergeSessionParts: true);
        string differentRemoveSource = RecordingRecoveryService.CreateRecoverySemanticKey(
            [first, second], ".mkv", removeSource: true, optimizeAudio: false, mergeSessionParts: true);
        string differentMergeMode = RecordingRecoveryService.CreateRecoverySemanticKey(
            [first, second], ".mkv", removeSource: false, optimizeAudio: false, mergeSessionParts: false);

        Assert.Equal(ordered.Order(), reversed.Order());
        Assert.Contains(overlapping[0], ordered);
        Assert.Equal(orderedSemanticKey, reversedSemanticKey);
        Assert.NotEqual(orderedSemanticKey, differentRemoveSource);
        Assert.NotEqual(orderedSemanticKey, differentMergeMode);
    }

    [Fact]
    public void Register_FinalNameOptionSurvivesSettingsUpdate()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}.ts");
        string? markerPath = RecordingRecoveryService.Register(
            sourcePattern,
            new RoomRecordingOptions { RecordFormat = "TS/FLV -> MKV" },
            finalizeName: true);

        Assert.NotNull(markerPath);
        try
        {
            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV -> MP4",
                SaveFileNameCustomRule = "{主播名}_{录制结束时间}",
            }));

            JsonObject marker = JsonNode.Parse(File.ReadAllText(markerPath!))!.AsObject();
            Assert.True(marker["FinalizeName"]!.GetValue<bool>());
            Assert.Equal("{主播名}_{录制结束时间}", marker["FileNameRule"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
    }

    [Fact]
    public void IsCompletedMediaOutput_RejectsNonemptyCorruptFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-invalid-output-{Guid.NewGuid():N}.mkv");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        try
        {
            Assert.False(RecordingRecoveryService.IsCompletedMediaOutput(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public void CompletedOutputProbeTimeout_RequiresCompletedMetadata(
        bool timedOut,
        bool hasCompletedMetadata,
        bool expected)
    {
        Assert.Equal(expected, RecordingRecoveryService.ShouldAcceptCompletedOutputProbeTimeout(
            timedOut,
            hasCompletedMetadata));
    }

    [Fact]
    public async Task ProcessSourcePatternAsync_DoesNotCreateTargetsForInvalidSessionParts()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePattern = Path.Combine(directory, "session_%03d.ts");
        string firstSource = Path.Combine(directory, "session_000.ts");
        string secondSource = Path.Combine(directory, "session_001.ts");

        try
        {
            await File.WriteAllBytesAsync(firstSource, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(secondSource, [5, 6, 7, 8]);

            Assert.False(await RecordingRecoveryService.ProcessSourcePatternAsync(sourcePattern, ".mkv", removeSource: false));
            Assert.False(File.Exists(Path.Combine(directory, "session.mkv")));
            Assert.False(File.Exists(Path.Combine(directory, "session_000.mkv")));
            Assert.False(File.Exists(Path.Combine(directory, "session_001.mkv")));

            Assert.False(await RecordingRecoveryService.ProcessSourcePatternAsync(sourcePattern, ".mp4", removeSource: false, mergeSessionParts: true));
            Assert.False(File.Exists(Path.Combine(directory, "session.mp4")));
            Assert.False(File.Exists(Path.Combine(directory, "session_000.mp4")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IsPathWithinRoot_RejectsSiblingDirectoriesWithTheSamePrefix()
    {
        string root = Path.Combine(Path.GetTempPath(), "emerde-recordings");
        string inside = Path.Combine(root, "room", "record.ts");
        string sibling = Path.Combine(Path.GetTempPath(), "emerde-recordings-other", "record.ts");

        Assert.True(RecordingRecoveryService.IsPathWithinRoot(inside, root));
        Assert.False(RecordingRecoveryService.IsPathWithinRoot(sibling, root));
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
}
