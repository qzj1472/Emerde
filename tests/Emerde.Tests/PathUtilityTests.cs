using Emerde.Core;

namespace Emerde.Tests;

public sealed class PathUtilityTests
{
    [Fact]
    public void TryGetRelativePathWithinRoot_AcceptsNamesStartingWithTwoDots()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-path-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "..host", "record.mp4");

        bool result = PathUtility.TryGetRelativePathWithinRoot(path, root, out string relativePath);

        Assert.True(result);
        Assert.Equal(Path.Combine("..host", "record.mp4"), relativePath);
    }

    [Fact]
    public void IsSameOrDescendant_RejectsSiblingWithMatchingPrefix()
    {
        string root = Path.Combine(Path.GetTempPath(), "emerde-path-root");
        string sibling = Path.Combine(Path.GetTempPath(), "emerde-path-root-other", "record.mp4");

        Assert.False(PathUtility.IsSameOrDescendant(sibling, root));
    }
}
