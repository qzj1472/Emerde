using Emerde.Core;
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
            StartTime = DateTime.Now.AddSeconds(-5),
        };
        List<string?> changedProperties = [];
        room.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        room.RefreshDuration();

        Assert.Contains(nameof(RoomStatusReactive.Duration), changedProperties);
        Assert.Contains(nameof(RoomStatusReactive.RecordStatusText), changedProperties);
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
