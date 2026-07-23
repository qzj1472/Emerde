using Emerde.Views;

namespace Emerde.Tests;

public sealed class TrayMenuWindowTests
{
    [Theory]
    [InlineData("显示界面 (&V)", "显示界面")]
    [InlineData("Show (&V)", "Show")]
    [InlineData("显示界面", "显示界面")]
    public void StripAccessKeySuffix_RemovesNativeMenuAccelerator(string value, string expected)
    {
        Assert.Equal(expected, TrayMenuWindow.StripAccessKeySuffix(value));
    }

    [Fact]
    public void BuildStatusText_PrioritizesRecordingThenStreaming()
    {
        string recording = TrayMenuWindow.BuildStatusText(new TrayMenuState("v1", 3, 2, true, true, false));
        string streaming = TrayMenuWindow.BuildStatusText(new TrayMenuState("v1", 3, 0, true, true, false));
        string monitoring = TrayMenuWindow.BuildStatusText(new TrayMenuState("v1", 0, 0, true, true, false));
        string paused = TrayMenuWindow.BuildStatusText(new TrayMenuState("v1", 0, 0, false, true, false));

        Assert.Contains("2", recording, StringComparison.Ordinal);
        Assert.Contains("3", streaming, StringComparison.Ordinal);
        Assert.NotEqual(recording, streaming);
        Assert.NotEqual(streaming, monitoring);
        Assert.NotEqual(monitoring, paused);
    }

    [Fact]
    public void TrayMenuXaml_UsesWpfUiContextMenuStructure()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "TrayMenuWindow.xaml"));

        Assert.Contains("<ContextMenu", xaml, StringComparison.Ordinal);
        Assert.Contains("<MenuItem", xaml, StringComparison.Ordinal);
        Assert.Contains("<Separator", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCheckable=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ToggleMonitorClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ToggleRecordClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StaysOpen=\"False\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenVideoListClick", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSaveFolderClick", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayActionButtonStyle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayMenuWindow_ConstructsOnStaThread()
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                TrayMenuWindow window = new(new TrayMenuState("v1", 0, 0, true, true, false), _ => { });
                window.Close();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Theory]
    [InlineData(true, 1, -200)]
    [InlineData(false, 1, 1)]
    public void GetTrayMenuPlacementOffset_AnchorsMenuToTrayPoint(
        bool openAbove,
        double expectedX,
        double expectedY)
    {
        System.Windows.Point offset = TrayMenuWindow.GetTrayMenuPlacementOffset(
            new System.Windows.Size(160, 200),
            new System.Windows.Size(1, 1),
            openAbove);

        Assert.Equal(new System.Windows.Point(expectedX, expectedY), offset);
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
