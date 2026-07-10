using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputedConverters;
using Emerde.Core;
using Emerde.Models;
using Windows.System;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.ViewModels;

[ObservableObject]
public partial class RoomStatusReactive : ReactiveObject
{
    [ObservableProperty]
    private string nickName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarDisplaySource))]
    private string avatarThumbUrl = string.Empty;

    public string AvatarDisplaySource => string.IsNullOrWhiteSpace(AvatarThumbUrl)
        ? "pack://application:,,,/Assets/Favicon.png"
        : AvatarThumbUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoomCodeText))]
    private string roomUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformDisplayName))]
    [NotifyPropertyChangedFor(nameof(QualityText))]
    private string platformName = string.Empty;

    public string PlatformDisplayName => global::Emerde.Core.PlatformDisplayName.Get(PlatformName);

    [ObservableProperty]
    private int addedOrder = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveTitleText))]
    private string liveTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityText))]
    private string quality = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolutionText))]
    [NotifyPropertyChangedFor(nameof(QualityText))]
    private string resolution = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateText))]
    private string bitrate = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewUrl))]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    [NotifyPropertyChangedFor(nameof(PreviewSourceText))]
    [NotifyPropertyChangedFor(nameof(LiveStreamText))]
    [NotifyPropertyChangedFor(nameof(PreviewSupportText))]
    private string flvUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewUrl))]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    [NotifyPropertyChangedFor(nameof(PreviewSourceText))]
    [NotifyPropertyChangedFor(nameof(LiveStreamText))]
    [NotifyPropertyChangedFor(nameof(PreviewSupportText))]
    private string hlsUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveIsToNotify))]
    private bool isToNotify = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveIsToRecord))]
    private bool isToRecord = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveIsToMonitor))]
    private bool isToMonitor = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveIsToNotify))]
    [NotifyPropertyChangedFor(nameof(EffectiveIsToRecord))]
    [NotifyPropertyChangedFor(nameof(EffectiveIsToMonitor))]
    [NotifyPropertyChangedFor(nameof(CanEditRoomSettings))]
    private bool isFollowGlobalSettings = true;

    public bool EffectiveIsToNotify => Configurations.IsToNotify.Get() && IsToNotify;

    public bool EffectiveIsToRecord => IsFollowGlobalSettings ? Configurations.IsToRecord.Get() : IsToRecord;

    public bool EffectiveIsToMonitor => IsFollowGlobalSettings ? Configurations.IsToMonitor.Get() : IsToMonitor;

    public bool CanEditRoomSettings => !IsFollowGlobalSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreamStatusText))]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    [NotifyPropertyChangedFor(nameof(IsStreaming))]
    private StreamStatus streamStatus = default;

    [ObservableProperty]
    private bool isRefreshFlashActive;

    public string StreamStatusText => StreamStatus switch
    {
        StreamStatus.Initialized => "StreamStatusOfInitialized".Tr(),
        StreamStatus.Disabled => "StreamStatusOfDisabled".Tr(),
        StreamStatus.NotStreaming => "StreamStatusOfNotStreaming".Tr(),
        StreamStatus.Streaming => "StreamStatusOfStreaming".Tr(),
        _ => "StreamStatusOfUnknown".Tr(),
    };

    public bool IsStreaming => StreamStatus == StreamStatus.Streaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordStatusText))]
    [NotifyPropertyChangedFor(nameof(IsRecording))]
    private RecordStatus recordStatus = default;

    public string RecordStatusText => RecordStatus switch
    {
        RecordStatus.Initialized => "RecordStatusOfInitialized".Tr(),
        RecordStatus.Disabled => "RecordStatusOfDisabled".Tr(),
        RecordStatus.NotRecording => "RecordStatusOfNotRecording".Tr(),
        RecordStatus.Recording => "RecordStatusOfRecording".Tr() + " " + Duration,
#pragma warning disable CS0618 // Type or member is obsolete
        RecordStatus.Error => "RecordStatusOfError".Tr(),
#pragma warning restore CS0618 // Type or member is obsolete
        _ => "RecordStatusOfUnknown".Tr(),
    };

    public bool IsRecording => RecordStatus == RecordStatus.Recording;

    public string PreviewUrl => !string.IsNullOrWhiteSpace(FlvUrl) ? FlvUrl : HlsUrl;

    public bool CanPreview => StreamStatus == StreamStatus.Streaming && !string.IsNullOrWhiteSpace(PreviewUrl);

    public string RoomCodeText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RoomUrl))
            {
                return "-";
            }

            if (Uri.TryCreate(RoomUrl, UriKind.Absolute, out Uri? uri))
            {
                string lastSegment = uri.Segments
                    .Select(segment => segment.Trim('/'))
                    .LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? string.Empty;

                return string.IsNullOrWhiteSpace(lastSegment) ? uri.Host : lastSegment;
            }

            string trimmed = RoomUrl.Trim().TrimEnd('/');
            int splitIndex = trimmed.LastIndexOf('/');
            return splitIndex >= 0 && splitIndex < trimmed.Length - 1 ? trimmed[(splitIndex + 1)..] : trimmed;
        }
    }

    public string LiveStreamText => string.IsNullOrWhiteSpace(PreviewUrl) ? "-" : PreviewUrl;

    public string DanmakuSupportText => "-";

    public string LiveTitleText => string.IsNullOrWhiteSpace(LiveTitle) ? string.Empty : LiveTitle;

    public string QualityText => StreamQualityCatalog.GetDisplayName(PlatformName, Quality, Resolution);

    public string ResolutionText => string.IsNullOrWhiteSpace(Resolution) ? "-" : Resolution;

    public string BitrateText => string.IsNullOrWhiteSpace(Bitrate) ? "-" : Bitrate;

    public string PreviewSupportText => PreviewSourceText == "-" ? "HLS / FLV" : PreviewSourceText;

    public string PreviewSourceText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FlvUrl))
            {
                return "FLV";
            }

            if (!string.IsNullOrWhiteSpace(HlsUrl))
            {
                return "HLS";
            }

            return "-";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public DateTime startTime = DateTime.MinValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duration))]
    public DateTime endTime = DateTime.MinValue;

    public string Duration
    {
        get
        {
            if (StartTime != DateTime.MinValue)
            {
                if (EndTime != DateTime.MinValue)
                {
                    return (EndTime - StartTime).ToTimeCodeString();
                }
                return (DateTime.Now - StartTime).ToTimeCodeString();
            }
            return string.Empty;
        }
    }

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(PlatformDisplayName));
        OnPropertyChanged(nameof(StreamStatusText));
        OnPropertyChanged(nameof(RecordStatusText));
        OnPropertyChanged(nameof(EffectiveIsToNotify));
        OnPropertyChanged(nameof(EffectiveIsToRecord));
        OnPropertyChanged(nameof(EffectiveIsToMonitor));
        OnPropertyChanged(nameof(CanEditRoomSettings));
    }

    public void RefreshDuration()
    {
        if (RecordStatus == RecordStatus.Recording)
        {
            OnPropertyChanged(nameof(RecordStatusText));
            OnPropertyChanged(nameof(Duration));
        }
    }

    public async void FlashRefresh()
    {
        IsRefreshFlashActive = false;
        await Task.Delay(1);
        IsRefreshFlashActive = true;
        await Task.Delay(360);
        IsRefreshFlashActive = false;
    }

    [RelayCommand]
    private async Task PlayRecordAsync()
    {
        if (GlobalMonitor.RoomStatus.TryGetValue(RoomUrl, out RoomStatus? roomStatus)
         && File.Exists(roomStatus.Recorder.FileName))
        {
            await Player.PlayAsync(roomStatus.Recorder.FileName, isSeekable: roomStatus.RecordStatus == RecordStatus.Recording);
        }
        else
        {
            Toast.Warning("PlayerErrorOfNoFile".Tr());
        }
    }

    [RelayCommand]
    private async Task GotoRoomUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri(RoomUrl));
    }
}

public sealed class CommandEventArgs(string command) : EventArgs
{
    public string Command { get; } = command;
}

file static class TimeSpanExtension
{
    public static string ToTimeCodeString(this TimeSpan timeSpan)
    {
        timeSpan = new TimeSpan(timeSpan.Days, timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);

        if (timeSpan.TotalHours < 1)
        {
            return timeSpan.ToString(@"mm\:ss");
        }
        else
        {
            return timeSpan.ToString(@"h\:mm\:ss");
        }
    }
}
