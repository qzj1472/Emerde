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
}
