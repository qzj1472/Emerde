using Emerde.Core;
using Emerde.Views;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class AddRoomContentDialogTests
{
    [Fact]
    public void GetRoomInfoErrorMessage_AppendsResolverDetail()
    {
        string result = AddRoomContentDialog.GetRoomInfoErrorMessage(null, "resolver failed");

        Assert.Contains("resolver failed", result);
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
        Assert.Contains("ExpandedDialogHeightRatio,", source);
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
