namespace Emerde.Core;

internal static class RecordingAssociatedAssets
{
    internal static bool Copy(string sourcePath, string targetPath)
    {
        if (!RecordingCoverStore.TryCopyAssociatedCover(sourcePath, targetPath))
        {
            return false;
        }
        _ = VideoRepairService.TryCopyRepairReport(sourcePath, targetPath);
        return true;
    }

    internal static bool Move(string sourcePath, string targetPath)
    {
        if (!RecordingCoverStore.TryMoveAssociatedCover(sourcePath, targetPath))
        {
            return false;
        }
        _ = VideoRepairService.TryMoveRepairReport(sourcePath, targetPath);
        return true;
    }

    internal static void Delete(string mediaPath)
    {
        RecordingCoverStore.DeleteAssociatedAssets(mediaPath);
        VideoRepairService.TryDeleteRepairReport(mediaPath);
    }
}
