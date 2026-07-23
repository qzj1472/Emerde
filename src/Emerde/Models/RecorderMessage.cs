namespace Emerde.Models;

internal sealed class RecorderMessage
{
    public object? Sender { get; set; }
    public string? Data { get; set; }
    public StandardData DataType { get; set; }
}

internal sealed record RoomRecordingStateChangedMessage(string RoomUrl);

internal sealed record RuntimeConfigurationChangedMessage(bool RecheckRooms = false);

public enum StandardData
{
    None,
    StandardError,
    StandardOutput,
}
