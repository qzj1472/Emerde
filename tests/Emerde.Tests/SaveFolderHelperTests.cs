using Emerde.Extensions;

namespace Emerde.Tests;

public sealed class SaveFolderHelperTests
{
    [Fact]
    public void SelectDefaultSaveFolder_UsesFirstFixedDriveAfterSystemDrive()
    {
        string result = SaveFolderHelper.SelectDefaultSaveFolder(
            @"C:\",
            [@"E:\", @"D:\", @"F:\"],
            @"C:\Users\Test\Documents");

        Assert.Equal(@"D:\EmerdeDownloads", result);
    }

    [Fact]
    public void SelectDefaultSaveFolder_FallsBackToDocumentsWithoutAnotherDrive()
    {
        string result = SaveFolderHelper.SelectDefaultSaveFolder(
            @"C:\",
            [@"C:\"],
            @"C:\Users\Test\Documents");

        Assert.Equal(@"C:\Users\Test\Documents\EmerdeDownloads", result);
    }

    [Fact]
    public void SelectDefaultSaveFolder_IgnoresDrivesBeforeSystemDrive()
    {
        string result = SaveFolderHelper.SelectDefaultSaveFolder(
            @"D:\",
            [@"C:\", @"D:\", @"F:\"],
            @"C:\Users\Test\Documents");

        Assert.Equal(@"F:\EmerdeDownloads", result);
    }
}
