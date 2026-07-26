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

    [Fact]
    public void HomeRoomCardPanel_DoesNotRenderWindowSwitchFocusOutline()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XElement panel = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardPanel");
        XElement content = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardPanelContent");
        XElement list = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomCardList");

        Assert.Equal("False", (string?)panel.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)panel.Attribute("FocusVisualStyle"));
        Assert.Equal("False", (string?)content.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)content.Attribute("FocusVisualStyle"));
        Assert.Equal("{x:Null}", (string?)list.Attribute("FocusVisualStyle"));
    }

    [Fact]
    public void GlobalToolTip_UsesApplicationRoundedChrome()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Resources.xaml"));
        XElement style = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "{x:Type ToolTip}");

        Assert.Contains(style.Descendants().Where(element => element.Name.LocalName == "Border"), border =>
            (string?)border.Attribute("CornerRadius") == "{StaticResource Win11ControlCornerRadius}");
    }

    [Fact]
    public void AddRoomDialog_HasNamedUrlInputForInitialFocus()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AddRoomContentDialog.xaml"));
        XElement textBox = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)) == "RoomUrlTextBox");

        Assert.Equal("{Binding Url, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", (string?)textBox.Attribute("Text"));
    }

    [Fact]
    public void ExitConfirmationDialog_UsesApplicationContentDialogTemplate()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ExitConfirmationContentDialog.xaml"));
        XElement dialog = document.Root!;

        Assert.Equal("ContentDialog", dialog.Name.LocalName);
        Assert.Equal("{StaticResource DefaultVioletaContentDialogStyle}", (string?)dialog.Attribute("Style"));
        Assert.Equal("Primary", (string?)dialog.Attribute("DefaultButton"));
        Assert.Equal("是", (string?)dialog.Attribute("PrimaryButtonText"));
        Assert.Equal("否", (string?)dialog.Attribute("CloseButtonText"));
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
