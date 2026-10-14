using Emerde.Core;
using Emerde.Extensions;
using System.Diagnostics;

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

        RuntimeHelper.WaitForRestartParent(args);
        string instanceName = AppConfig.PackName + (Debugger.IsAttached ? "_DEBUG" : string.Empty);
        if (!RuntimeHelper.TryAcquireSingleInstance(instanceName))
        {
            return 0;
        }

        App app = new();
        return app.Run();
    }
}
