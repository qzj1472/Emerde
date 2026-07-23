using Emerde.Core;

namespace Emerde.Models;

public sealed class RoomStatus
{
    public string NickName { get; set; } = string.Empty;

    public string AvatarThumbUrl { get; set; } = string.Empty;

    public string AvatarLocalPath { get; set; } = string.Empty;

    public string RoomUrl { get; set; } = string.Empty;

    public string PlatformName { get; set; } = string.Empty;

    public string LiveTitle { get; set; } = string.Empty;

    public string Uid { get; set; } = string.Empty;

    public string Quality { get; set; } = string.Empty;

    public string Resolution { get; set; } = string.Empty;

    public string Bitrate { get; set; } = string.Empty;

    public string Headers { get; set; } = string.Empty;

    public string FlvUrl { get; set; } = string.Empty;

    public string HlsUrl { get; set; } = string.Empty;

    public string RecordUrl { get; set; } = string.Empty;

    internal long? FixedMetadataRefreshTimestamp { get; set; }

    internal bool IsLiveSessionMetadataInitialized { get; set; }

    internal bool IsLiveTitleLoaded { get; set; }

    internal bool IsQualityLoaded { get; set; }

    internal bool IsResolutionLoaded { get; set; }

    public StreamStatus StreamStatus { get; set; } = default;

    public bool IsStreamCheckFailed { get; set; }

    public RecordStatus RecordStatus
    {
        get => Recorder.RecordStatus;
        internal set => Recorder.RecordStatus = value;
    }

    public Recorder Recorder { get; } = new();

    public Player Player { get; } = new();
}

public enum StreamStatus
{
    Initialized,
    Disabled,
    NotStreaming,
    Streaming,
}
