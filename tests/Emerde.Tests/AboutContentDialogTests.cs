using Emerde.Views;
using System.Xml.Linq;

namespace Emerde.Tests;

public class AboutContentDialogTests
{
    [Fact]
    public void WarningCard_UsesTheSameRightSpacingAsContentCards()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        XElement warningCard = document.Descendants()
            .Single(element => element.Name.LocalName == "Border" && (string?)element.Attribute("Background") == "#14D83B01");

        Assert.Equal("0,0,12,0", (string?)warningCard.Attribute("Margin"));
    }

    [Theory]
    [InlineData(1174, 548, 268)]
    [InlineData(754, 688, 338)]
    [InlineData(500, 434, 434)]
    [InlineData(40, 0, 0)]
    public void CalculateCardWidths_UsesResponsiveColumnCounts(
        double controlWidth,
        double expectedCardWidth,
        double expectedWorkflowWidth)
    {
        (double cardWidth, double workflowWidth) = AboutContentDialog.CalculateCardWidths(controlWidth);

        Assert.Equal(expectedCardWidth, cardWidth);
        Assert.Equal(expectedWorkflowWidth, workflowWidth);
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
