using Emerde.ViewModels;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void SessionLogRetentionInput_UsesStandardSettingsWidth()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XElement input = document.Descendants()
            .Single(element => element.Name.LocalName == "CompactNumberBox"
                && ((string?)element.Attribute("Value"))?.Contains("SessionLogRetentionDays", StringComparison.Ordinal) == true);

        Assert.Equal("112", (string?)input.Attribute("Width"));
    }

    [Fact]
    public void RecordFormatOptions_UseFormatSpecificVisibility()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XElement optimizeAudio = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsOptimizeAudio", StringComparison.Ordinal) == true);
        XElement removeSource = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsRemoveTs", StringComparison.Ordinal) == true);

        Assert.Contains("IsMp4RecordFormat", (string?)optimizeAudio.Attribute("Visibility"));
        Assert.Contains("IsTranscodedRecordFormat", (string?)removeSource.Attribute("Visibility"));
    }

    [Fact]
    public void LocalRecordFormatOptions_UseFormatSpecificVisibility()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "LocalSettingsContentDialog.xaml"));
        XElement optimizeAudio = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsOptimizeAudio", StringComparison.Ordinal) == true);
        XElement removeSource = document.Descendants()
            .Single(element => element.Name.LocalName == "CheckBox"
                && ((string?)element.Attribute("IsChecked"))?.Contains("IsRemoveTs", StringComparison.Ordinal) == true);

        Assert.Contains("IsMp4RecordFormat", (string?)optimizeAudio.Attribute("Visibility"));
        Assert.Contains("IsTranscodedRecordFormat", (string?)removeSource.Attribute("Visibility"));
    }

    [Theory]
    [InlineData("127.0.0.1:7890", "http://127.0.0.1:7890/")]
    [InlineData("localhost:8080", "http://localhost:8080/")]
    [InlineData("proxy.example.com:3128", "http://proxy.example.com:3128/")]
    [InlineData("http://localhost:65535", "http://localhost:65535/")]
    [InlineData("[::1]:7890", "http://[::1]:7890/")]
    public void TryCreateProxyUri_AcceptsHostAndPort(string value, string expected)
    {
        bool result = SettingsViewModel.TryCreateProxyUri(value, out Uri? proxyUri, out string errorKey);

        Assert.True(result);
        Assert.Equal(expected, proxyUri?.AbsoluteUri);
        Assert.Equal(string.Empty, errorKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:notaport")]
    [InlineData("http://localhost")]
    public void TryCreateProxyUri_RejectsInvalidEndpoint(string value)
    {
        bool result = SettingsViewModel.TryCreateProxyUri(value, out Uri? proxyUri, out string errorKey);

        Assert.False(result);
        Assert.Null(proxyUri);
        Assert.False(string.IsNullOrWhiteSpace(errorKey));
    }

    [Theory]
    [InlineData("TS/FLV -> MP4", 0, true)]
    [InlineData("TS/FLV -> MKV", 0, true)]
    [InlineData("TS/FLV", 0, false)]
    [InlineData("TS/FLV -> MP4", 2, false)]
    [InlineData("TS/FLV", 1, false)]
    [InlineData("TS/FLV -> MP4", -1, false)]
    [InlineData("TS/FLV -> MKV", 3, false)]
    public void ShouldCancelConversionsOnRecordFormatChange_OnlyCancelsWhenSwitchingToRaw(
        string previousRecordFormat,
        int nextRecordFormatIndex,
        bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.ShouldCancelConversionsOnRecordFormatChange(previousRecordFormat, nextRecordFormatIndex));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void RecordFormatIndex_AcceptsOnlyVisibleOptions(int value, bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.IsRecordFormatIndexValid(value));
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
