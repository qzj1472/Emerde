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
    public void TrayMenuXaml_UsesStyledWindowSurface()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "TrayMenuWindow.xaml"));

        Assert.Contains("x:Name=\"MenuSurface\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayMenuSelectionIndicatorStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayMenuSelectionTextStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding IsMonitorRunning}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding IsRecordEnabled}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding IsAutoRun}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground\" Value=\"{DynamicResource SystemAccentColorPrimaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ToggleMonitorClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ToggleRecordClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Deactivated=\"TrayMenuWindowDeactivated\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ContextMenu", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsCheckable=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenVideoListClick", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSaveFolderClick", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayActionButtonStyle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"188\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"34\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CloseSafely", File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "TrayMenuWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("internal void RequestClose()", File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "TrayMenuWindow.xaml.cs")), StringComparison.Ordinal);
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
    [InlineData(1900, 1060, 1696, 820)]
    [InlineData(100, 100, 100, 100)]
    [InlineData(4, 500, 4, 260)]
    public void CalculateTrayMenuPosition_ClampsToWorkArea(
        double cursorX,
        double cursorY,
        double expectedX,
        double expectedY)
    {
        System.Windows.Point position = TrayMenuWindow.CalculateTrayMenuPosition(
            new System.Windows.Point(cursorX, cursorY),
            new System.Windows.Rect(0, 0, 1920, 1080),
            new System.Windows.Size(224, 240));

        Assert.Equal(new System.Windows.Point(expectedX, expectedY), position);
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
