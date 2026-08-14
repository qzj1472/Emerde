using Emerde.Core;
using Emerde.Views;
using System.Buffers.Binary;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class AddRoomContentDialogTests
{
    [Fact]
    public void GetRoomInfoErrorMessage_DoesNotExposeInternalResolverDetail()
    {
        string result = AddRoomContentDialog.GetRoomInfoErrorMessage(null, "resolver failed");

        Assert.Equal("GetRoomInfoError".Tr(), result);
        Assert.DoesNotContain("resolver failed", result);
    }

    [Fact]
    public void HasAddableRoomInfo_AllowsOfflineValidRoom()
    {
        StreamResolverResult result = new()
        {
            RoomUrl = "https://live.douyin.com/123456",
            PlatformName = "Douyin",
            IsLiveStreaming = false,
            Nickname = "anchor",
        };

        Assert.True(AddRoomContentDialog.HasAddableRoomInfo(result, result.RoomUrl));
        Assert.Equal("anchor", AddRoomContentDialog.GetConfirmedNickName(result));
    }

    [Fact]
    public void HasAddableRoomInfo_RejectsOfflineStatusWithoutRoomIdentity()
    {
        StreamResolverResult result = new()
        {
            RoomUrl = "https://live.douyin.com/123456",
            PlatformName = "Douyin",
            IsLiveStreaming = false,
        };

        Assert.False(AddRoomContentDialog.HasAddableRoomInfo(result, result.RoomUrl));
    }

    [Fact]
    public void CanDeferRoomInfoResolution_AllowsOnlyTransientDouyinBlock()
    {
        Assert.True(AddRoomContentDialog.CanDeferRoomInfoResolution(
            "https://live.douyin.com/72024000076",
            StreamResolver.DouyinTransientBlockError));
        Assert.False(AddRoomContentDialog.CanDeferRoomInfoResolution(
            "https://live.douyin.com/72024000076",
            "invalid response"));
        Assert.False(AddRoomContentDialog.CanDeferRoomInfoResolution(
            "https://www.twitch.tv/example",
            StreamResolver.DouyinTransientBlockError));
        Assert.False(AddRoomContentDialog.CanDeferRoomInfoResolution(
            "https://webcast.amemv.com/douyin/webcast/reflow/7670549959499664180?sec_user_id=MS4w.LONG-ID",
            StreamResolver.DouyinTransientBlockError));
    }

    [Fact]
    public void AddRoomDialog_DefaultsToGlobalSettingsAndReusesTheLocalEditor()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml"));
        XElement followGlobal = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsFollowGlobalSettings", StringComparison.Ordinal) == true);
        XElement editorHost = document.Descendants()
            .Single(element => element.Name.LocalName == "ContentControl"
                && ((string?)element.Attribute("Content"))?.Contains("SettingsEditor", StringComparison.Ordinal) == true);
        XElement trigger = editorHost.Descendants()
            .Single(element => element.Name.LocalName == "DataTrigger");
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml.cs"));

        Assert.Contains("IsFollowGlobalSettings", (string?)followGlobal.Attribute("IsChecked"));
        Assert.Contains("IsFollowGlobalSettings", (string?)trigger.Attribute("Binding"));
        Assert.Equal("False", (string?)trigger.Attribute("Value"));
        Assert.Contains("private bool isFollowGlobalSettings = true", source);
        Assert.Contains("new LocalSettingsContentDialog", source);
        Assert.Contains("}, false, false, true)", source);
        Assert.Contains("ClearWideDialogVisualSize(this)", source);
        Assert.Contains("ApplyWideDialogVisualSize(this, targetWidth, targetHeight)", source);
        Assert.Contains("ExpandedDialogHeightRatio = 0.95d", source);
        Assert.Contains(": ExpandedDialogHeightRatio", source);
        Assert.Contains("? IsFollowGlobalSettings ? 0.62d : 0.78d", source);
        Assert.Contains("? IsFollowGlobalSettings ? 0.58d : 0.84d", source);
        Assert.Contains("StreamQualityCatalog.GlobalOptions", File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml.cs")));
    }

    [Fact]
    public void AddRoomFlow_PersistsTheSelectedLocalRecordingOptions()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("dialog.RecordingOptions", source);
        Assert.Contains("RoomRecordingSettings.Apply(room, recordingOptions)", source);
        Assert.Contains("IsFollowGlobalSettings = isFollowGlobalSettings", source);
    }

    [Fact]
    public void AddRoomDialog_UsesSemanticPlatformStateAndSharedDialogStyles()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml"));
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml.cs"));

        Assert.Contains("EmerdeContentDialogStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("EmerdeDialogSectionStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDetectedPlatformSupported", xaml, StringComparison.Ordinal);
        Assert.Contains("HasRoomUrl", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXDangerForegroundBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDetectedPlatformSupported = !string.IsNullOrWhiteSpace(platformName)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRoomDialog_KeepsLegacyAndUiXLayoutsSeparate()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml.cs"));

        Assert.Contains(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "RoomUrlTextBox");
        Assert.Contains(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "UiXRoomUrlTextBox");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "DataTrigger"
            && ((string?)element.Attribute("Binding"))?.Contains("IsUiXEnabled", StringComparison.Ordinal) == true);
        Assert.Contains("FrameworkElement input = IsUiXEnabled ? UiXRoomUrlTextBox : RoomUrlTextBox", source, StringComparison.Ordinal);
        Assert.Contains("!IsUiXEnabled && IsFollowGlobalSettings", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingAtlas_UsesTheExpectedFrameGrid()
    {
        string path = FindRepositoryFile("src", "Emerde", "Assets", "RoomLoadingAtlas.png");
        byte[] png = File.ReadAllBytes(path);

        Assert.Equal(0x89504E470D0A1A0AUL, BinaryPrimitives.ReadUInt64BigEndian(png.AsSpan(0, 8)));
        Assert.Equal(2624, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(1640, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [Fact]
    public void UiXWorkspace_ReusesTheGemLoadingAnimation()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml.cs"));

        Assert.Contains("x:Name=\"RoomLoadingOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LoadingFrame\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RoomLoadingAtlas.Frames", source, StringComparison.Ordinal);
        Assert.Contains("CompositionTarget.Rendering += LoadingAnimationRendering", source, StringComparison.Ordinal);
        Assert.Contains("CompositionTarget.Rendering -= LoadingAnimationRendering", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProgressBar Width=\"180\" Height=\"4\" IsIndeterminate=\"True\"", xaml, StringComparison.Ordinal);
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
