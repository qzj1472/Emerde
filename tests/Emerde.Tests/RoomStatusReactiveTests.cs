using Emerde.Core;
using Emerde.Extensions;
using Emerde.ViewModels;

namespace Emerde.Tests;

public sealed class RoomStatusReactiveTests
{
    [Fact]
    public void RefreshDuration_NotifiesDurationWhileRecording()
    {
        RoomStatusReactive room = new()
        {
            RecordStatus = RecordStatus.Recording,
            IsRecordingConfirmed = true,
            StartTime = DateTime.Now.AddSeconds(-5),
        };
        List<string?> changedProperties = [];
        room.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        room.RefreshDuration();

        Assert.Contains(nameof(RoomStatusReactive.Duration), changedProperties);
        Assert.Contains(nameof(RoomStatusReactive.RecordStatusText), changedProperties);
    }

    [Fact]
    public void RefreshDuration_DoesNotNotifyBeforeMediaProgress()
    {
        RoomStatusReactive room = new()
        {
            RecordStatus = RecordStatus.Recording,
            IsRecordingConfirmed = false,
        };
        List<string?> changedProperties = [];
        room.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        room.RefreshDuration();

        Assert.Empty(changedProperties);
        Assert.Equal("RecordStatusOfStarting".Tr(), room.RecordStatusText);
    }

    [Fact]
    public void RefreshDuration_DoesNotNotifyWhenNotRecording()
    {
        RoomStatusReactive room = new()
        {
            RecordStatus = RecordStatus.NotRecording,
            StartTime = DateTime.Now.AddSeconds(-5),
        };
        List<string?> changedProperties = [];
        room.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        room.RefreshDuration();

        Assert.Empty(changedProperties);
    }

    [Fact]
    public void RecordingEngineText_ShowsNativeWorkerProcessWhileRecording()
    {
        RoomStatusReactive room = new()
        {
            RecordStatus = RecordStatus.Recording,
            MediaWorkerProcessId = 1234,
            MediaWorkerProcessName = "Emerde",
            MediaWorkerWriteBytesPerSecond = 1.3 * 1024 * 1024,
            MediaWorkerReadBytesPerSecond = 2.5 * 1024 * 1024,
        };

        Assert.Equal("RecordingEngineActive".Tr("Emerde", 1234, "2.5 MB/s", "1.3 MB/s"), room.RecordingEngineText);
    }

    [Fact]
    public void RecordingEngineText_HidesWorkerProcessWhenNotRecording()
    {
        RoomStatusReactive room = new()
        {
            RecordStatus = RecordStatus.NotRecording,
            MediaWorkerProcessId = 1234,
        };

        Assert.Equal("-", room.RecordingEngineText);
    }

    [Theory]
    [InlineData(RecordStatus.Recording, true)]
    [InlineData(RecordStatus.NotRecording, false)]
    [InlineData(RecordStatus.Disabled, false)]
    public void IsRecordingOrStarting_UsesRequestedRecordState(RecordStatus recordStatus, bool expected)
    {
        RoomStatusReactive room = new()
        {
            RecordStatus = recordStatus,
        };

        Assert.Equal(expected, room.IsRecordingOrStarting);
    }
}
