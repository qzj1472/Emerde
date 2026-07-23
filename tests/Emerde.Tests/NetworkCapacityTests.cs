using Emerde.ViewModels;
using Emerde.Properties;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class NetworkCapacityTests
{
    [Fact]
    public void NetworkCapacityResources_AreAvailableToTheLocalizationGenerator()
    {
        Assert.False(string.IsNullOrWhiteSpace(Resources.NetworkCapacityTesting));
        Assert.False(string.IsNullOrWhiteSpace(Resources.NetworkCapacityIdle));
        Assert.NotEqual("NetworkCapacityTesting", Resources.NetworkCapacityTesting);
        Assert.NotEqual("NetworkCapacityIdle", Resources.NetworkCapacityIdle);
    }

    [Fact]
    public void NetworkCapacityTestingState_KeepsIconAndTextVisible()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement capacityStyle = document.Descendants(presentation + "Style")
            .Single(element => (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "StatusTrayCapacityButtonStyle");
        XElement displayText = document.Descendants(presentation + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding NetworkCapacityDisplayText}");

        Assert.Equal("68", (string?)capacityStyle.Elements(presentation + "Setter")
            .Single(element => (string?)element.Attribute("Property") == "MinWidth")
            .Attribute("Value"));
        Assert.Contains(displayText.Descendants(presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsNetworkCapacityTesting}" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.DoesNotContain(capacityStyle.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsEnabled" &&
            (string?)trigger.Attribute("Value") == "False");
    }

    [Theory]
    [InlineData(0d, 5d, null)]
    [InlineData(100d, 0d, null)]
    [InlineData(double.NaN, 5d, null)]
    [InlineData(double.PositiveInfinity, 5d, null)]
    [InlineData(100d, 5d, 14)]
    [InlineData(1d, 10d, 1)]
    public void CalculateNetworkCapacity_OnlyReturnsValidMeasuredResults(double measuredMbps, double perRoomMbps, int? expected)
    {
        Assert.Equal(expected, MainViewModel.CalculateNetworkCapacity(measuredMbps, perRoomMbps));
    }

    [Fact]
    public void CalculateNetworkCapacity_DoesNotEstimateMissingMeasurement()
    {
        Assert.Null(MainViewModel.CalculateNetworkCapacity(null, 5d));
    }

    [Theory]
    [InlineData("Douyin", false)]
    [InlineData("Bilibili", false)]
    [InlineData("Direct", false)]
    [InlineData("TikTok", true)]
    [InlineData("Twitch", true)]
    [InlineData("YouTube", true)]
    [InlineData("", false)]
    public void IsOverseasPlatform_ClassifiesCapacityRoute(string platformName, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsOverseasPlatform(platformName));
    }

    [Fact]
    public void StableThroughput_UsesMedianForEvenMeasurements()
    {
        Assert.Equal(60d, MainViewModel.CalculateStableNetworkThroughput([56d, 64d]));
    }

    [Fact]
    public void StableThroughput_UsesMiddleOfThreeRounds()
    {
        Assert.Equal(62d, MainViewModel.CalculateStableNetworkThroughput([30d, 62d, 150d]));
    }

    [Theory]
    [InlineData(75_000_000L, 2d, 300d)]
    [InlineData(75_000_000L, 4d, 150d)]
    [InlineData(65_535L, 2d, null)]
    [InlineData(75_000_000L, 0.1d, null)]
    public void NetworkThroughput_UsesOneSharedRoundDuration(long totalBytes, double elapsedSeconds, double? expected)
    {
        Assert.Equal(expected, MainViewModel.CalculateNetworkThroughputMbps(totalBytes, elapsedSeconds));
    }

    [Fact]
    public void StableThroughput_RejectsRepeatedOutliers()
    {
        double[] measurements = [62d, 56d, 62d, 64d, 70d, 47d, 68d];
        double? result = MainViewModel.CalculateStableNetworkThroughput(measurements);

        Assert.NotNull(result);
        Assert.Equal(62d, result.Value, 6);
    }

    [Fact]
    public void StableThroughput_IgnoresInvalidMeasurements()
    {
        Assert.Equal(62d, MainViewModel.CalculateStableNetworkThroughput([double.NaN, -1d, 62d, double.PositiveInfinity]));
        Assert.Null(MainViewModel.CalculateStableNetworkThroughput([double.NaN, 0d]));
    }

    [Theory]
    [InlineData(6, 6, 2, 2)]
    [InlineData(3, 6, 1, 1)]
    [InlineData(2, 6, 2, 0)]
    [InlineData(0, 0, 0, 0)]
    public void NetworkMeasurementConfidence_ReflectsSamplesAndEndpointDiversity(
        int successfulSamples,
        int attemptedSamples,
        int successfulEndpoints,
        int expected)
    {
        Assert.Equal(expected, (int)MainViewModel.GetNetworkMeasurementConfidence(
            successfulSamples,
            attemptedSamples,
            successfulEndpoints));
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
