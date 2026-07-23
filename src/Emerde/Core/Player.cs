using System.Diagnostics;

namespace Emerde.Core;

public sealed class Player
{
    public static Task PlayAsync(string mediaPath, bool isSeekable = false)
    {
        if (!File.Exists(mediaPath))
        {
            return Task.CompletedTask;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = mediaPath,
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        return Task.CompletedTask;
    }
}
