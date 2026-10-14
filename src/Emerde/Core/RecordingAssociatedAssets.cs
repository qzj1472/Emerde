namespace Emerde.Core;

internal static class RecordingAssociatedAssets
{
    internal static bool Copy(string sourcePath, string targetPath)
    {
        bool coverCopied = RecordingCoverStore.TryCopyAssociatedCover(sourcePath, targetPath);
        bool reportCopied = VideoRepairService.TryCopyRepairReport(sourcePath, targetPath);
        return coverCopied && reportCopied;
    }

    internal static bool Move(string sourcePath, string targetPath)
    {
        bool coverMoved = RecordingCoverStore.TryMoveAssociatedCover(sourcePath, targetPath);
        bool reportMoved = VideoRepairService.TryMoveRepairReport(sourcePath, targetPath);
        return coverMoved && reportMoved;
    }

    internal static void Delete(string mediaPath)
    {
        RecordingCoverStore.DeleteAssociatedAssets(mediaPath);
        VideoRepairService.TryDeleteRepairReport(mediaPath);
    }
}
