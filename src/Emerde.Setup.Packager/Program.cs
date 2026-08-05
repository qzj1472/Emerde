using System.Formats.Tar;
using System.Security.Cryptography;
using ZstdSharp;

namespace Emerde.Setup;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault()?.ToLowerInvariant() switch
            {
                "pack-zstd" => PackZstandard(args),
                "assemble" => Assemble(args),
                "verify" => Verify(args),
                _ => throw new ArgumentException("Usage: pack-zstd <source> <output> | assemble <stub> <bootstrap> <application> <output> | verify <setup>"),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int PackZstandard(string[] args)
    {
        if (args.Length != 3)
        {
            throw new ArgumentException("pack-zstd requires a source directory and output file.");
        }

        string sourceDirectory = Path.GetFullPath(args[1]);
        string outputPath = Path.GetFullPath(args[2]);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(sourceDirectory);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        string temporaryTar = Path.Combine(Path.GetTempPath(), $"emerde-bootstrap-{Guid.NewGuid():N}.tar");
        try
        {
            TarFile.CreateFromDirectory(sourceDirectory, temporaryTar, includeBaseDirectory: false);
            using FileStream input = File.OpenRead(temporaryTar);
            using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using CompressionStream compressor = new(output, level: 19, leaveOpen: false);
            input.CopyTo(compressor, 1024 * 1024);
        }
        finally
        {
            File.Delete(temporaryTar);
        }

        return 0;
    }

    private static int Assemble(string[] args)
    {
        if (args.Length != 5)
        {
            throw new ArgumentException("assemble requires stub, bootstrap, application and output paths.");
        }

        string stubPath = RequireFile(args[1]);
        string bootstrapPath = RequireFile(args[2]);
        string applicationPath = RequireFile(args[3]);
        string outputPath = Path.GetFullPath(args[4]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        string temporaryOutput = outputPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            using (FileStream output = new(temporaryOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (FileStream stub = File.OpenRead(stubPath))
            {
                stub.CopyTo(output, 1024 * 1024);
                SetupContainerFormat.AppendContainer(output, bootstrapPath, applicationPath);
            }

            File.Move(temporaryOutput, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryOutput);
        }

        return 0;
    }

    private static string RequireFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath) ? fullPath : throw new FileNotFoundException(fullPath);
    }

    private static int Verify(string[] args)
    {
        if (args.Length != 2)
        {
            throw new ArgumentException("verify requires a setup path.");
        }

        string setupPath = RequireFile(args[1]);
        using FileStream setup = new(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        SetupContainerDescriptor descriptor = SetupContainerFormat.ReadDescriptor(setup);
        VerifySegment(setup, descriptor.BootstrapOffset, descriptor.BootstrapLength, descriptor.BootstrapSha256);
        VerifySegment(setup, descriptor.ApplicationOffset, descriptor.ApplicationLength, descriptor.ApplicationSha256);
        Console.WriteLine($"bootstrap={descriptor.BootstrapLength} application={descriptor.ApplicationLength}");
        return 0;
    }

    private static void VerifySegment(Stream source, long offset, long length, byte[] expectedHash)
    {
        source.Position = offset;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        long remaining = length;
        while (remaining > 0)
        {
            int bytesRead = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Setup segment ended unexpectedly.");
            }

            hash.AppendData(buffer, 0, bytesRead);
            remaining -= bytesRead;
        }

        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expectedHash))
        {
            throw new InvalidDataException("Setup segment hash mismatch.");
        }
    }
}
