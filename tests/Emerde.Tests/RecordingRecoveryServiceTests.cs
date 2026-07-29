using Emerde.Core;
using System.Text.Json.Nodes;

namespace Emerde.Tests;

[Collection("MediaOperationRegistry")]
public sealed class RecordingRecoveryServiceTests
{
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
        }, roomUrl);

        Assert.NotNull(markerPath);
        try
        {
            Assert.Contains($"\"RoomUrl\": \"{roomUrl}\"", File.ReadAllText(markerPath!));
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
            }));

            string marker = File.ReadAllText(markerPath!);
            Assert.Contains("\"TargetFormat\": \".mkv\"", marker);
            Assert.Contains("\"RemoveSource\": true", marker);

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
}
