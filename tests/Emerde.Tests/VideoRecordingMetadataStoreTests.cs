using Emerde.Core;

namespace Emerde.Tests;

public sealed class VideoRecordingMetadataStoreTests
{
    [Fact]
    public void FromTags_ReadsEmbeddedMetadataDictionary()
    {
        Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase)
        {
            ["emerde_nick_name"] = "Host",
            ["emerde_room_url"] = "https://example.test/room",
            ["emerde_title"] = "Live title",
            ["emerde_recorded_at"] = "2026-07-27 22:30:00",
            ["emerde_recording_session_id"] = "session-id",
            ["emerde_segment_group_id"] = "segment-group",
            ["emerde_segment_index"] = "1",
            ["emerde_segment_count"] = "3",
            ["emerde_segment_kind"] = "stall",
            ["emerde_media_issue"] = "timeline_mismatch",
            ["emerde_issue_intervals"] = "[{\"Kind\":\"timeline_mismatch\",\"StartSeconds\":12.5,\"EndSeconds\":19.2,\"Detail\":\"audio_stalled\"}]",
            ["emerde_was_repaired"] = "true",
            ["emerde_cover_composition_version"] = "2",
        };

        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.FromTags(tags, "record.mkv");

        Assert.Equal("record.mkv", metadata.FileName);
        Assert.Equal("Host", metadata.NickName);
        Assert.Equal("https://example.test/room", metadata.RoomUrl);
        Assert.Equal("Live title", metadata.Title);
        Assert.Equal(new DateTime(2026, 7, 27, 22, 30, 0), metadata.RecordedAt);
        Assert.Equal("session-id", metadata.RecordingSessionId);
        Assert.Equal("segment-group", metadata.SegmentGroupId);
        Assert.Equal(1, metadata.SegmentIndex);
        Assert.Equal(3, metadata.SegmentCount);
        Assert.Equal("stall", metadata.SegmentKind);
        Assert.Equal("timeline_mismatch", metadata.MediaIssue);
        Assert.Single(metadata.IssueIntervals);
        Assert.Equal("timeline_mismatch", metadata.IssueIntervals[0].Kind);
        Assert.Equal(12.5d, metadata.IssueIntervals[0].StartSeconds);
        Assert.Equal(19.2d, metadata.IssueIntervals[0].EndSeconds);
        Assert.Equal("audio_stalled", metadata.IssueIntervals[0].Detail);
        Assert.True(metadata.WasRepaired);
        Assert.Equal(2, metadata.CoverCompositionVersion);
    }

    [Theory]
    [InlineData(".ts")]
    [InlineData(".flv")]
    [InlineData(".mp4")]
    [InlineData(".mkv")]
    [InlineData(".webm")]
    [InlineData(".avi")]
    public void CompletedMetadata_IsAttachedToEverySupportedContainer(string extension)
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string mediaPath = Path.Combine(root, "recording" + extension);
        try
        {
            File.WriteAllBytes(mediaPath, [1, 2, 3]);
            VideoRecordingMetadata metadata = CreateMetadata();

            Assert.True(VideoRecordingMetadataStore.WriteCompletedMetadata(mediaPath, metadata));
            Assert.True(VideoRecordingMetadataStore.HasAttachedMetadata(mediaPath));
            Assert.False(File.Exists(Path.Combine(root, "recording.mplr.json")));

            VideoRecordingMetadata loaded = VideoRecordingMetadataStore.Load(new FileInfo(mediaPath));
            Assert.Equal(Path.GetFileName(mediaPath), loaded.FileName);
            Assert.Equal(metadata.NickName, loaded.NickName);
            Assert.Equal(metadata.RoomUrl, loaded.RoomUrl);
            Assert.Equal(metadata.RoomId, loaded.RoomId);
            Assert.Equal(metadata.Platform, loaded.Platform);
            Assert.Equal(metadata.Title, loaded.Title);
            Assert.Equal(metadata.Resolution, loaded.Resolution);
            Assert.Equal(metadata.Bitrate, loaded.Bitrate);
            Assert.Equal(metadata.Quality, loaded.Quality);
            Assert.Equal(metadata.FrameRate, loaded.FrameRate);
            Assert.Equal(metadata.VideoCodec, loaded.VideoCodec);
            Assert.Equal(metadata.AudioCodec, loaded.AudioCodec);
            Assert.Equal(metadata.HasOptimizedAudio, loaded.HasOptimizedAudio);
            Assert.Equal(metadata.RecordedAt, loaded.RecordedAt);
            Assert.Equal(metadata.EndedAt, loaded.EndedAt);
            Assert.Equal(metadata.DurationSeconds, loaded.DurationSeconds);
            Assert.Equal(metadata.FileNameRule, loaded.FileNameRule);
            Assert.Equal(metadata.SegmentReason, loaded.SegmentReason);
            Assert.Equal(metadata.RecordingAvatar, loaded.RecordingAvatar);
            Assert.Equal(metadata.CoverCompositionVersion, loaded.CoverCompositionVersion);
            Assert.Equal(metadata.MediaIssue, loaded.MediaIssue);
            Assert.Single(loaded.IssueIntervals);
            Assert.Equal(metadata.IssueIntervals[0].Kind, loaded.IssueIntervals[0].Kind);
            Assert.Equal(metadata.IssueIntervals[0].StartSeconds, loaded.IssueIntervals[0].StartSeconds);
            Assert.Equal(metadata.IssueIntervals[0].EndSeconds, loaded.IssueIntervals[0].EndSeconds);
            Assert.Equal(metadata.IssueIntervals[0].Detail, loaded.IssueIntervals[0].Detail);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecordingSidecar_IsRemovedAfterMetadataIsAttached()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string mediaPath = Path.Combine(root, "recording.ts");
        try
        {
            File.WriteAllBytes(mediaPath, [1, 2, 3]);
            string? sidecar = VideoRecordingMetadataStore.WriteSidecar(root, "recording", CreateMetadata());
            Assert.NotNull(sidecar);

            Assert.True(VideoRecordingMetadataStore.FinalizeSidecarForMedia([mediaPath], sidecar));
            Assert.False(File.Exists(sidecar));
            Assert.True(VideoRecordingMetadataStore.HasAttachedMetadata(mediaPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static VideoRecordingMetadata CreateMetadata()
    {
        return new VideoRecordingMetadata
        {
            FileName = "recording.ts",
            NickName = "主播",
            RoomUrl = "https://example.test/live",
            RoomId = "room-42",
            Platform = "Test",
            Title = "直播标题",
            Resolution = "1920x1080",
            Bitrate = "8 Mbps",
            Quality = "Original",
            FrameRate = 60,
            VideoCodec = "h264",
            AudioCodec = "aac",
            HasOptimizedAudio = true,
            SegmentReason = VideoRecordingMetadataStore.TimelineStallSegmentReason,
            RecordingAvatar = [1, 2, 3, 4],
            CoverCompositionVersion = RecordingCoverStore.CurrentCompositionVersion,
            RecordedAt = new DateTime(2026, 7, 23, 12, 34, 56),
            EndedAt = new DateTime(2026, 7, 23, 13, 34, 56),
            DurationSeconds = 3600,
            FileNameRule = "{主播名}_{录制开始时间}",
            MediaIssue = "timeline_mismatch",
            IssueIntervals =
            [
                new VideoRecordingIssueInterval
                {
                    Kind = "timeline_mismatch",
                    StartSeconds = 8.12d,
                    EndSeconds = 8.19d,
                    Detail = "audio_stalled",
                },
            ],
        };
    }

    [Fact]
    public void OffsetIssueIntervals_ShiftsPlaybackTimes()
    {
        List<VideoRecordingIssueInterval> shifted = VideoRecordingMetadataStore.OffsetIssueIntervals(
            [
                new VideoRecordingIssueInterval
                {
                    Kind = "timeline_mismatch",
                    StartSeconds = 10d,
                    EndSeconds = 12d,
                    Detail = "video_stalled",
                },
                new VideoRecordingIssueInterval
                {
                    Kind = "optimized_audio_failed",
                    StartSeconds = -1d,
                    EndSeconds = -1d,
                    Detail = "original_audio_preserved",
                },
            ],
            60d);

        Assert.Equal(70d, shifted[0].StartSeconds);
        Assert.Equal(72d, shifted[0].EndSeconds);
        Assert.Equal(-1d, shifted[1].StartSeconds);
        Assert.Equal(-1d, shifted[1].EndSeconds);
    }
}
