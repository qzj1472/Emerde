using System.Globalization;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class LayoutRoundingTests
{
    private static readonly HashSet<string> SizeProperties = new(StringComparer.Ordinal)
    {
        "Width",
        "Height",
        "MinWidth",
        "MaxWidth",
        "MinHeight",
        "MaxHeight",
        "FontSize",
        "IconSize",
        "LineHeight",
        "Margin",
        "Padding",
        "ContentPadding",
        "CornerRadius",
        "BorderThickness",
        "StrokeThickness",
        "Spacing",
        "HorizontalSpacing",
        "VerticalSpacing",
        "ColumnSpacing",
        "RowSpacing",
    };

    [Fact]
    public void ViewRoots_EnableLayoutRoundingAndPixelSnapping()
    {
        foreach (string path in EnumerateViewXamlFiles())
        {
            XElement root = XDocument.Load(path).Root ?? throw new InvalidDataException(path);

            Assert.Equal("True", (string?)root.Attribute("UseLayoutRounding"));
            Assert.Equal("True", (string?)root.Attribute("SnapsToDevicePixels"));
        }
    }

    [Fact]
    public void FixedXamlSizes_UseIntegerConstants()
    {
        foreach (string path in EnumerateXamlFiles())
        {
            XDocument document = XDocument.Load(path);
            foreach (XElement element in (document.Root ?? throw new InvalidDataException(path)).DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes().Where(attribute => SizeProperties.Contains(attribute.Name.LocalName)))
                {
                    AssertIntegerComponents(path, element, attribute.Name.LocalName, attribute.Value);
                }

                if (element.Name.LocalName == "Setter"
                    && element.Attribute("Property") is { Value: var property }
                    && element.Attribute("Value") is { Value: var value }
                    && SizeProperties.Contains(property.Split('.').Last()))
                {
                    AssertIntegerComponents(path, element, property, value);
                }
            }
        }
    }

    [Fact]
    public void SpacedWpfUiStackPanels_DoNotContainOnlyConditionalChildren()
    {
        XNamespace wpfUi = "http://schemas.lepo.co/wpfui/2022/xaml";
        foreach (string path in EnumerateXamlFiles())
        {
            XDocument document = XDocument.Load(path);
            IEnumerable<XElement> panels = document.Descendants(wpfUi + "StackPanel")
                .Where(element => element.Attribute("Spacing") != null);
            foreach (XElement panel in panels)
            {
                XElement[] children = panel.Elements().ToArray();
                bool allChildrenAreConditional = children.Length > 0
                    && children.All(child => child.Attribute("Visibility") != null);

                Assert.False(allChildrenAreConditional, $"{path}: spaced StackPanel can measure a negative size when all children collapse");
            }
        }
    }

    private static void AssertIntegerComponents(string path, XElement element, string property, string value)
    {
        foreach (string component in value.Split(','))
        {
            if (!double.TryParse(component.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                || !double.IsFinite(number))
            {
                continue;
            }

            Assert.True(number == Math.Truncate(number), $"{path}: {element.Name.LocalName}.{property}={value}");
        }
    }

    private static IEnumerable<string> EnumerateViewXamlFiles()
    {
        return Directory.EnumerateFiles(FindRepositoryDirectory("src", "Emerde", "Views"), "*.xaml", SearchOption.TopDirectoryOnly);
    }

    private static IEnumerable<string> EnumerateXamlFiles()
    {
        return Directory.EnumerateFiles(FindRepositoryDirectory("src", "Emerde"), "*.xaml", SearchOption.AllDirectories);
    }

    private static string FindRepositoryDirectory(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (Directory.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
