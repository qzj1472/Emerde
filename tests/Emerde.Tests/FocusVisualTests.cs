using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class FocusVisualTests
{
    [Theory]
    [InlineData("StatusTrayChipButtonStyle")]
    [InlineData("StatusTrayCapacityButtonStyle")]
    [InlineData("StatusTrayCapacityRefreshButtonStyle")]
    public void HomeStatusTrayButtons_DoNotRenderWindowSwitchFocusOutline(string styleKey)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement style = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", XamlNamespace)) == styleKey);

        Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
            (string?)setter.Attribute("Value") == "{x:Null}");
    }

    [Fact]
    public void HomeRoomDetailScrollViewer_DoesNotRenderWindowSwitchFocusOutline()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement roomDetailPanel = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomDetailPanel");
        XElement scrollViewer = roomDetailPanel.Descendants()
            .First(element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal("False", (string?)scrollViewer.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)scrollViewer.Attribute("FocusVisualStyle"));
        Assert.Equal("0", (string?)scrollViewer.Attribute("BorderThickness"));
    }

    [Theory]
    [InlineData("SettingsWindow.xaml", "SettingsScrollViewer")]
    [InlineData("LocalSettingsContentDialog.xaml", "LocalSettingsScrollViewer")]
    public void SettingsScrollViewers_DoNotRenderWindowSwitchFocusOutline(string fileName, string scrollViewerName)
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", fileName));
        XElement scrollViewer = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == scrollViewerName);

        Assert.Equal("False", (string?)scrollViewer.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)scrollViewer.Attribute("FocusVisualStyle"));
        Assert.Equal("0", (string?)scrollViewer.Attribute("BorderThickness"));
    }

    [Fact]
    public void SettingsCards_DoNotRenderWindowSwitchFocusOutline()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        foreach (string targetType in new[] { "{x:Type ui:Card}", "{x:Type ui:CardExpander}" })
        {
            XElement style = document.Descendants()
                .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == targetType);
            Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
                (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
                (string?)setter.Attribute("Value") == "{x:Null}");
        }
    }

    [Fact]
    public void SaveFolderPathLevel_DefaultsToAuthorYearMonthDate()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Configurations.cs"));

        Assert.Contains("SaveFolderPathLevel), 3", source);
    }

    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

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
