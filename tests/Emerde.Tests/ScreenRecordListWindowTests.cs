using Emerde.Views;

namespace Emerde.Tests;

public sealed class ScreenRecordListWindowTests
{
    [Theory]
    [InlineData(@"主播A\2026-07\03\record.mp4", "主播A")]
    [InlineData(@"主播A\2026-07\record.mp4", "主播A")]
    [InlineData(@"Imported\Nested\record.mp4", "Nested")]
    public void InferNickName_UsesRecordedAuthorFolder(string relativePath, string expected)
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-videos-{Guid.NewGuid():N}");
        string filePath = Path.Combine(relativePath.Split('\\').Prepend(root).ToArray());

        string nickName = ScreenRecordListViewModel.InferNickName(filePath, root);

        Assert.Equal(expected, nickName);
    }

    [Fact]
    public void InferNickName_UsesParentFolderForRootVideos()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-videos-{Guid.NewGuid():N}");
        string filePath = Path.Combine(root, "record.mp4");

        string nickName = ScreenRecordListViewModel.InferNickName(filePath, root);

        Assert.Equal(Path.GetFileName(root), nickName);
    }
}
