using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ZstdSharp;

namespace Emerde.Setup;

internal static class Program
{
    private const long MinimumTemporarySpace = 600L * 1024 * 1024;

    [STAThread]
    public static int Main(string[] args)
    {
        string? temporaryRoot = null;
        try
        {
            using PreparationWindow preparationWindow = new();
            preparationWindow.Show();
            string setupPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定安装器路径。");
            EnsureTemporarySpace();
            using FileStream setup = new(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            SetupContainerDescriptor descriptor = SetupContainerFormat.ReadDescriptor(setup);
            VerifySegment(setup, descriptor.BootstrapOffset, descriptor.BootstrapLength, descriptor.BootstrapSha256);
            VerifySegment(setup, descriptor.ApplicationOffset, descriptor.ApplicationLength, descriptor.ApplicationSha256);

            temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "Emerde",
                $"setup-{Guid.NewGuid():N}");
            string bootstrapRoot = Path.Combine(temporaryRoot, "bootstrap");
            string applicationArchive = Path.Combine(temporaryRoot, "application.7z");
            Directory.CreateDirectory(bootstrapRoot);

            setup.Position = descriptor.BootstrapOffset;
            using (BoundedReadStream compressed = new(setup, descriptor.BootstrapLength, leaveOpen: true))
            using (DecompressionStream decompressor = new(compressed, leaveOpen: false))
            {
                TarFile.ExtractToDirectory(decompressor, bootstrapRoot, overwriteFiles: true);
            }

            setup.Position = descriptor.ApplicationOffset;
            using (BoundedReadStream application = new(setup, descriptor.ApplicationLength, leaveOpen: true))
            using (FileStream output = new(applicationArchive, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                application.CopyTo(output, 1024 * 1024);
            }

            string installerExecutable = Path.Combine(
                bootstrapRoot,
                "installer",
                "Emerde.Installer.exe");
            string runtimeRoot = Path.Combine(bootstrapRoot, "runtime");
            if (!File.Exists(installerExecutable) || !Directory.Exists(runtimeRoot))
            {
                throw new InvalidDataException("安装器启动层不完整。");
            }

            preparationWindow.Close();

            ProcessStartInfo startInfo = new(installerExecutable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(installerExecutable)!,
            };
            startInfo.ArgumentList.Add("--payload");
            startInfo.ArgumentList.Add(applicationArchive);
            startInfo.ArgumentList.Add("--runtime-source");
            startInfo.ArgumentList.Add(runtimeRoot);
            startInfo.ArgumentList.Add("--setup-source");
            startInfo.ArgumentList.Add(setupPath);
            foreach (string argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process installer = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动安装界面。");
            installer.WaitForExit();
            return installer.ExitCode;
        }
        catch (Exception exception)
        {
            WriteDiagnosticLog(exception);
            MessageBoxW(IntPtr.Zero, exception.Message, "Emerde 安装程序", 0x10);
            return 1;
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
        }
    }

    private static void EnsureTemporarySpace()
    {
        string? driveRoot = Path.GetPathRoot(Path.GetTempPath());
        if (string.IsNullOrWhiteSpace(driveRoot)
            || new DriveInfo(driveRoot).AvailableFreeSpace < MinimumTemporarySpace)
        {
            throw new IOException("临时目录至少需要 600 MiB 可用空间。");
        }
    }

    private static void VerifySegment(
        Stream source,
        long offset,
        long length,
        byte[] expectedHash)
    {
        source.Position = offset;
        using BoundedReadStream segment = new(source, length, leaveOpen: true);
        byte[] actualHash = SHA256.HashData(segment);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw new InvalidDataException("安装器负载校验失败。");
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (path is null || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteDiagnosticLog(Exception exception)
    {
        string? logPath = Environment.GetEnvironmentVariable("EMERDE_SETUP_LOG");
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        try
        {
            File.WriteAllText(Path.GetFullPath(logPath), exception.ToString());
        }
        catch (Exception)
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr windowHandle, string text, string caption, uint type);
}

internal sealed class BoundedReadStream : Stream
{
    private readonly Stream source;
    private readonly long length;
    private readonly bool leaveOpen;
    private long remaining;

    public BoundedReadStream(Stream source, long length, bool leaveOpen)
    {
        this.source = source;
        this.length = length;
        this.leaveOpen = leaveOpen;
        remaining = length;
    }

    public override bool CanRead => source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => length - remaining;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = source.Read(buffer, offset, (int)Math.Min(count, remaining));
        remaining -= bytesRead;
        return bytesRead;
    }

    public override int Read(Span<byte> buffer)
    {
        int bytesRead = source.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
        remaining -= bytesRead;
        return bytesRead;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
        {
            source.Dispose();
        }

        base.Dispose(disposing);
    }
}
