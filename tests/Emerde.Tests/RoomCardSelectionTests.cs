using Emerde.Core;
using Emerde.ViewModels;
using Emerde.Views;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class RoomCardSelectionTests
{
    [Theory]
    [InlineData(System.Windows.Input.Key.Up, true)]
    [InlineData(System.Windows.Input.Key.Down, true)]
    [InlineData(System.Windows.Input.Key.Left, true)]
    [InlineData(System.Windows.Input.Key.Right, true)]
    [InlineData(System.Windows.Input.Key.W, true)]
    [InlineData(System.Windows.Input.Key.A, true)]
    [InlineData(System.Windows.Input.Key.S, true)]
    [InlineData(System.Windows.Input.Key.D, true)]
    [InlineData(System.Windows.Input.Key.Home, false)]
    public void RoomCardKeyboardNavigation_UsesArrowAndWasdKeys(System.Windows.Input.Key key, bool expected)
    {
        Assert.Equal(expected, MainWindow.IsRoomCardKeyboardNavigationKey(key));
    }

    [Theory]
    [InlineData(0, -1, 4, 3)]
    [InlineData(3, 1, 4, 0)]
    [InlineData(1, 1, 4, 2)]
    [InlineData(2, -1, 4, 1)]
    [InlineData(0, -3, 4, 1)]
    [InlineData(3, 3, 4, 2)]
    [InlineData(-1, 1, 4, 0)]
    [InlineData(-1, -1, 4, 3)]
    public void ResolveCyclicRoomIndex_WrapsAcrossBothEnds(int currentIndex, int offset, int count, int expected)
    {
        Assert.Equal(expected, MainWindow.ResolveCyclicRoomIndex(currentIndex, offset, count));
    }

    [Fact]
    public void ResolveCyclicRoomIndex_RejectsEmptyCollections()
    {
        Assert.Equal(-1, MainWindow.ResolveCyclicRoomIndex(0, 1, 0));
    }

    [Fact]
    public void CurrentRoomVisual_IsHiddenWhenRoomsAreAlreadySelected()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement[] conditions = document.Descendants()
            .Where(element => element.Name.LocalName == "Condition"
                && ((string?)element.Attribute("Binding"))?.Contains("HasSelectedRooms", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(conditions);
        Assert.All(conditions, condition => Assert.Equal("False", (string?)condition.Attribute("Value")));
    }

    [Fact]
    public void DetailRecordButton_UsesEffectiveRecordingIntent()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement button = document.Descendants()
            .Single(element => element.Name.LocalName == "Button"
                && (string?)element.Attribute("Command") == "{Binding ToggleSelectedRoomRecordCommand}");

        Assert.Equal("{Binding SelectedItem.EffectiveIsToRecord}", (string?)button.Attribute("Tag"));
    }

    [Fact]
    public void RoomHistoryLimit_IsTwoHundred()
    {
        Assert.Equal(200, MainViewModel.RoomHistoryLimit);
    }

    [Fact]
    public void CloneRoom_CreatesIndependentHistorySnapshot()
    {
        Room source = new()
        {
            NickName = "Host",
            RoomUrl = "https://example.com/room",
            PlatformName = "Douyin",
            PreferredStreamQuality = "original",
            RecordFormat = "mkv",
            IsOptimizeAudio = true,
            SaveFolder = @"D:\records",
            RoutineInterval = 60000,
        };

        Room clone = MainViewModel.CloneRoom(source);
        source.NickName = "Changed";
        source.SaveFolder = @"E:\changed";

        Assert.NotSame(source, clone);
        Assert.Equal("Host", clone.NickName);
        Assert.Equal(@"D:\records", clone.SaveFolder);
        Assert.Equal("original", clone.PreferredStreamQuality);
        Assert.Equal("mkv", clone.RecordFormat);
        Assert.True(clone.IsOptimizeAudio);
        Assert.Equal(60000, clone.RoutineInterval);
    }

    [Fact]
    public void LocalRecordingSettings_PersistAndResolveOptimizedAudio()
    {
        Room room = new()
        {
            IsFollowGlobalSettings = false,
        };
        RoomRecordingSettings.Apply(room, new RoomRecordingOptions
        {
            RecordFormat = "TS/FLV -> MP4",
            IsOptimizeAudio = true,
        });

        Assert.True(room.IsOptimizeAudio);
        Assert.True(RoomRecordingSettings.Get(room).IsOptimizeAudio);

        room.IsOptimizeAudio = null;
        Assert.Equal(RoomRecordingSettings.GetGlobal().IsOptimizeAudio, RoomRecordingSettings.Get(room).IsOptimizeAudio);
    }

    [Fact]
    public void BuildRestoredRoomConfiguration_PreservesCurrentSettingsForExistingRooms()
    {
        Room current = new()
        {
            NickName = "Current",
            RoomUrl = "https://example.com/current",
            SaveFolder = @"E:\current",
        };
        Room restored = new()
        {
            NickName = "Restored",
            RoomUrl = "https://example.com/restored",
            SaveFolder = @"D:\restored",
        };
        Room[] target =
        [
            new Room { NickName = "Old", RoomUrl = current.RoomUrl, SaveFolder = @"D:\old" },
            restored,
        ];

        Room[] result = MainViewModel.BuildRestoredRoomConfiguration([current], target);

        Assert.Equal([current.RoomUrl, restored.RoomUrl], result.Select(room => room.RoomUrl));
        Assert.Equal("Current", result[0].NickName);
        Assert.Equal(@"E:\current", result[0].SaveFolder);
        Assert.Equal("Restored", result[1].NickName);
        Assert.NotSame(current, result[0]);
        Assert.NotSame(restored, result[1]);
    }

    [Fact]
    public void BuildMovedRoomOrder_MovesSelectedRoomsAsOneBlock()
    {
        RoomStatusReactive first = CreateRoom("first", "Douyin");
        RoomStatusReactive second = CreateRoom("second", "Douyin");
        RoomStatusReactive third = CreateRoom("third", "Douyin");
        RoomStatusReactive fourth = CreateRoom("fourth", "Douyin");

        RoomStatusReactive[] result = MainViewModel.BuildMovedRoomOrder(
            [first, second, third, fourth],
            [first, second, third, fourth],
            [second, third],
            4);

        Assert.Equal([first, fourth, second, third], result);
    }

    [Fact]
    public void BuildMovedRoomOrder_PreservesHiddenRoomsDuringFilteredMove()
    {
        RoomStatusReactive first = CreateRoom("first", "Douyin");
        RoomStatusReactive hidden = CreateRoom("hidden", "Twitch");
        RoomStatusReactive second = CreateRoom("second", "Douyin");
        RoomStatusReactive third = CreateRoom("third", "Douyin");

        RoomStatusReactive[] result = MainViewModel.BuildMovedRoomOrder(
            [first, hidden, second, third],
            [first, second, third],
            [second],
            0);

        Assert.Equal([second, first, hidden, third], result);
    }

    [Fact]
    public void ResolveRoomRemovalTargets_PrefersMarqueeSelectionOverStaleClickedRoom()
    {
        RoomStatusReactive staleClicked = CreateRoom("stale", "Douyin");
        RoomStatusReactive firstSelected = CreateRoom("first", "Douyin");
        RoomStatusReactive secondSelected = CreateRoom("second", "Douyin");
        firstSelected.IsSelected = true;
        secondSelected.IsSelected = true;

        RoomStatusReactive[] targets = MainViewModel.ResolveRoomRemovalTargets(
            [staleClicked, firstSelected, secondSelected],
            staleClicked);

        Assert.Equal([firstSelected, secondSelected], targets);
    }

    [Fact]
    public void ResolveRoomRemovalTargets_PreservesMultiSelectionWhenContextRoomIsSelected()
    {
        RoomStatusReactive firstSelected = CreateRoom("first", "Douyin");
        RoomStatusReactive contextRoom = CreateRoom("second", "Douyin");
        firstSelected.IsSelected = true;
        contextRoom.IsSelected = true;

        RoomStatusReactive[] targets = MainViewModel.ResolveRoomRemovalTargets(
            [firstSelected, contextRoom],
            contextRoom,
            allowSingleSelectionFallback: true);

        Assert.Equal([firstSelected, contextRoom], targets);
    }

    [Fact]
    public void ResolveRoomRemovalTargets_FallsBackToClickedRoomWithoutMarqueeSelection()
    {
        RoomStatusReactive selectedItem = CreateRoom("selected", "Douyin");

        RoomStatusReactive[] targets = MainViewModel.ResolveRoomRemovalTargets([selectedItem], selectedItem);

        Assert.Equal([selectedItem], targets);
    }

    [Fact]
    public void ResolveMarqueeSelection_ReplacesPreviousSelectionWithoutModifier()
    {
        RoomStatusReactive previous = CreateRoom("previous", "Douyin");
        RoomStatusReactive selected = CreateRoom("selected", "Douyin");
        previous.IsSelected = true;

        RoomStatusReactive[] targets = MainViewModel.ResolveMarqueeSelection(
            [previous, selected],
            [selected],
            preserveExistingSelection: false);

        Assert.Equal([selected], targets);
    }

    [Fact]
    public void ResolveMarqueeSelection_AppendsPreviousSelectionWithModifier()
    {
        RoomStatusReactive previous = CreateRoom("previous", "Douyin");
        RoomStatusReactive selected = CreateRoom("selected", "Douyin");
        previous.IsSelected = true;

        RoomStatusReactive[] targets = MainViewModel.ResolveMarqueeSelection(
            [previous, selected],
            [selected],
            preserveExistingSelection: true);

        Assert.Equal([previous, selected], targets);
    }

    [Fact]
    public void ResolveMarqueeSelection_ClearsPreviousSelectionForEmptyPlainMarquee()
    {
        RoomStatusReactive previous = CreateRoom("previous", "Douyin");
        previous.IsSelected = true;

        RoomStatusReactive[] targets = MainViewModel.ResolveMarqueeSelection(
            [previous],
            [],
            preserveExistingSelection: false);

        Assert.Empty(targets);
    }

    [Fact]
    public void ResolveMarqueeSelection_PreservesPreviousSelectionForEmptyModifiedMarquee()
    {
        RoomStatusReactive previous = CreateRoom("previous", "Douyin");
        previous.IsSelected = true;

        RoomStatusReactive[] targets = MainViewModel.ResolveMarqueeSelection(
            [previous],
            [],
            preserveExistingSelection: true);

        Assert.Equal([previous], targets);
    }

    [Fact]
    public void ResolveRoomRemovalTargets_DoesNotFallbackAfterSelectionWasCleared()
    {
        RoomStatusReactive staleClicked = CreateRoom("stale", "Douyin");

        RoomStatusReactive[] targets = MainViewModel.ResolveRoomRemovalTargets(
            [staleClicked],
            staleClicked,
            allowSingleSelectionFallback: false);

        Assert.Empty(targets);
    }

    [Fact]
    public void BuildPlatformFilterOptions_UsesOnlyDetectedPlatforms()
    {
        RoomStatusReactive[] rooms =
        [
            CreateRoom("first", "Douyin"),
            CreateRoom("second", "Twitch"),
            CreateRoom("third", "douyin"),
            CreateRoom("unknown", string.Empty),
        ];

        string[] result = MainViewModel.BuildPlatformFilterOptions(rooms);

        Assert.Equal(MainViewModel.AllPlatformFilter, result[0]);
        Assert.Equal(3, result.Length);
        Assert.Contains(result, platform => platform.Equals("Douyin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, platform => platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizePlatformFilter_TreatsEmptyValuesAsAllPlatforms(string? value)
    {
        Assert.Equal(MainViewModel.AllPlatformFilter, MainViewModel.NormalizePlatformFilter(value));
    }

    [Fact]
    public void BuildRoomSortDescriptions_UsesSelectedSortMode()
    {
        System.ComponentModel.SortDescription[] byName = MainViewModel.BuildRoomSortDescriptions(true);
        System.ComponentModel.SortDescription[] byAddedOrder = MainViewModel.BuildRoomSortDescriptions(false);

        Assert.Equal(nameof(RoomStatusReactive.NickName), byName[0].PropertyName);
        Assert.Equal(nameof(RoomStatusReactive.RoomUrl), byName[1].PropertyName);
        Assert.Equal(nameof(RoomStatusReactive.AddedOrder), byAddedOrder[0].PropertyName);
        Assert.Equal(nameof(RoomStatusReactive.RoomUrl), byAddedOrder[1].PropertyName);
    }

    private static RoomStatusReactive CreateRoom(string name, string platform)
    {
        return new RoomStatusReactive
        {
            NickName = name,
            RoomUrl = $"https://example.com/{name}",
            PlatformName = platform,
        };
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
