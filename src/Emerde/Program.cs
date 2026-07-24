using Emerde.Core;

namespace Emerde;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (MediaWorker.TryRun(args, out int mediaWorkerExitCode))
        {
            return mediaWorkerExitCode;
        }

        App app = new();
        return app.Run();
    }
}
