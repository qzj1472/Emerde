using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class UiXDialogTests
{
    [Fact]
    public void DialogPalette_UsesDedicatedOpaqueSurfaces()
    {
        XDocument theme = XDocument.Load(FindRepositoryFile("src", "Emerde", "Themes", "UiXTheme.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] keys =
        [
            "UiXDialogSurfaceBrush",
            "UiXDialogSectionBrush",
            "UiXDialogElevatedBrush",
            "UiXDialogSubtleBrush",
            "UiXDialogSelectionBrush",
            "UiXDialogWarningBrush",
            "UiXDialogDangerBrush",
            "UiXDialogLiveBrush",
            "UiXDialogRecordingBrush",
        ];

        foreach (string key in keys)
        {
            XElement brush = theme.Descendants()
                .Single(element => element.Name.LocalName == "SolidColorBrush"
                    && (string?)element.Attribute(x + "Key") == key);
            Assert.StartsWith("#FF", (string?)brush.Attribute("Color"), StringComparison.OrdinalIgnoreCase);
        }

        string themeCode = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "AppThemeBrushes.cs"));
        foreach (string key in keys)
        {
            Assert.Contains($"SetBrush(\"{key}\"", themeCode, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SharedUiXDialogContent_UsesOpaqueDialogSurfaces()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXDialogContent.cs"));

        Assert.Contains("UiXDialogSectionBrush", source, StringComparison.Ordinal);
        Assert.Contains("UiXDialogWarningBrush", source, StringComparison.Ordinal);
        Assert.Contains("UiXDialogDangerBrush", source, StringComparison.Ordinal);
        Assert.Contains("UiXDialogSelectionBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UiXCardBrush", source, StringComparison.Ordinal);
        Assert.Contains("DialogBlurScope.ForLightDismiss(owner, dialog)", source, StringComparison.Ordinal);
        Assert.Contains("WindowSizing.ShowContentDialogAsync(dialog, owner)", source, StringComparison.Ordinal);
        Assert.Contains("new System.Windows.Media.FontFamily(\"Segoe Fluent Icons\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LogExport_UsesUiXRangeCardsAndKeepsLegacyActions()
    {
        string viewModel = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "SettingsViewModel.cs"));
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXExportLogsContent.xaml"));

        Assert.Contains("new UiXExportLogsContent()", viewModel, StringComparison.Ordinal);
        Assert.Contains("uiXContent!.TodayOnly", viewModel, StringComparison.Ordinal);
        Assert.Contains("isUiXEnabled ? string.Empty : \"ExportToday\".Tr()", viewModel, StringComparison.Ordinal);
        Assert.Contains("isUiXEnabled ? \"Export\".Tr() : \"ExportAll\".Tr()", viewModel, StringComparison.Ordinal);
        Assert.Contains("UiXDialogSectionBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXDialogSelectionBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UiXCardBrush", xaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(xaml, "Text=\"{I18N ExportLogsPrompt}\""));
    }

    [Fact]
    public void RoomInformation_UsesUiXSectionsAndPreservesLegacyContent()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("return CreateUiXRoomInformationContent", source, StringComparison.Ordinal);
        Assert.Contains("CreateUiXRoomInformationHeader", source, StringComparison.Ordinal);
        Assert.Contains("UiXDialogContent.CreateSection", source, StringComparison.Ordinal);
        Assert.Contains("CreateRoomInformationStatusValue(room.StreamStatusText)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateRoomInformationStatusValue(room.StreamStatusText,", source, StringComparison.Ordinal);
        Assert.Contains("return scrollViewer;", source, StringComparison.Ordinal);
        Assert.Contains("nameof(RoomStatusReactive.AvatarDisplaySource)", source, StringComparison.Ordinal);
        Assert.Contains("UpdateRoomRecordingSummaryAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UiXConfirmations_UseSharedDialogsWhileLegacyKeepsMessageBoxes()
    {
        string dialog = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXDialogContent.cs"));
        string settings = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "SettingsViewModel.cs"));
        string main = File.ReadAllText(FindRepositoryFile("src", "Emerde", "ViewModels", "MainViewModel.cs"));
        string videos = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));

        Assert.Contains("DefaultButton = ContentDialogButton.Close", dialog, StringComparison.Ordinal);
        Assert.Contains("UiXDialogContent.ConfirmAsync", settings, StringComparison.Ordinal);
        Assert.Contains("MessageBox.Question", settings, StringComparison.Ordinal);
        Assert.Contains("UiXDialogContent.ConfirmAsync", main, StringComparison.Ordinal);
        Assert.Contains("MessageBox.QuestionAsync", main, StringComparison.Ordinal);
        Assert.Contains("UiXDialogContent.ConfirmAsync", videos, StringComparison.Ordinal);
        Assert.Contains("MessageBox.QuestionAsync", videos, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscodeDialog_DoesNotRepeatTheTargetFormatLabelInUiX()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));

        Assert.Contains("content.Children.Remove(targetFormatLabel)", source, StringComparison.Ordinal);
        Assert.Contains("UiXDialogContent.CreateForm", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RoomWorkspace_UsesOneCompactIdentityHeader()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml.cs"));
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml"));

        Assert.Contains("public string TaskTitle", source, StringComparison.Ordinal);
        Assert.Contains("\"SingleSettings\".Tr()", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TaskTitle}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding WorkspaceTitle}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"{DynamicResource UiXDividerBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RoomWorkspace_DefaultModeUsesCompactContentHeight()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        string workspaceSource = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "UiXRoomWorkspace.xaml.cs"));

        Assert.Contains("UiXWorkspaceHost.Content = workspace", source, StringComparison.Ordinal);
        Assert.Contains("IsCustomMode ? 832d : 680d", workspaceSource, StringComparison.Ordinal);
        Assert.Contains("WorkspaceSurface.Width = Math.Max", workspaceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformCookieLogin_UsesUiXSemanticSurfacesWithoutReplacingLegacyDefaults()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "PlatformCookieLoginWindow.xaml"));

        Assert.Contains("CookieLoginShellStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("CookieLoginAddressStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("CookieLoginBrowserSurfaceStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("StatusOfIsUiXEnabled", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXWindowFallbackBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXDialogSectionBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXDialogSurfaceBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXTextSecondaryBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("EmerdeShellBackgroundBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("EmerdeSurfaceBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoMultiSelectToolbar_PreservesUiXHeaderRightInset()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.DoesNotContain("<Setter Property=\"Margin\" Value=\"0,0,-20,0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0\" />", xaml, StringComparison.Ordinal);
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

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
