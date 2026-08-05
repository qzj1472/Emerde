using System.IO.Compression;
using Emerde.Installer;

namespace Emerde.Tests;

public sealed class InstallerPayloadTests
{
    [Fact]
    public async Task ExtractAsyncPreservesAppDirectoryTree()
    {
        byte[] archive = CreateArchive(new Dictionary<string, string>
        {
            ["bin/Emerde.exe"] = "application",
            ["native/ffmpeg/avcodec.dll"] = "library",
            ["runtime/shared/Microsoft.NETCore.App/9.0.17/System.Private.CoreLib.dll"] = "runtime",
        });
        string destination = CreateTemporaryDirectory();

        try
        {
            InstallerPayload payload = new(() => new MemoryStream(archive, writable: false));
            await payload.ExtractAsync(destination, new Progress<InstallationProgress>());

            Assert.Equal("application", await File.ReadAllTextAsync(Path.Combine(destination, "bin", "Emerde.exe")));
            Assert.Equal("library", await File.ReadAllTextAsync(Path.Combine(destination, "native", "ffmpeg", "avcodec.dll")));
            Assert.Equal(
                "runtime",
                await File.ReadAllTextAsync(Path.Combine(
                    destination,
                    "runtime",
                    "shared",
                    "Microsoft.NETCore.App",
                    "9.0.17",
                    "System.Private.CoreLib.dll")));
            Assert.False(File.Exists(Path.Combine(destination, "Emerde.exe")));
        }
        finally
        {
            Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractAsyncRejectsPathTraversal()
    {
        byte[] archive = CreateArchive(new Dictionary<string, string>
        {
            ["../outside.txt"] = "invalid",
        });
        string destination = CreateTemporaryDirectory();

        try
        {
            InstallerPayload payload = new(() => new MemoryStream(archive, writable: false));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                payload.ExtractAsync(destination, new Progress<InstallationProgress>()));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(destination)!, "outside.txt")));
        }
        finally
        {
            Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractAsyncSupportsSolidSevenZipPayload()
    {
        byte[] archive = Convert.FromBase64String(
            "N3q8ryccAAS9/iNy1wAAAAAAAAAiAAAAAAAAAAW8Q9UBABFhcHBsaWNhdGlvbmxpYnJhcnkAAAAAAAAAAAAAAAAAAAAAAACBMweubb/OG3TSiyOW7aIFZPMt2bPLWgYdcejDo60sJm86W9S534B7NlvMUNNU+zGTln2mPzov5pdccm1vh0QWN7dwPDX9cTSHsdsE6FNfU6XdR3JlSzQb3L4B/2xwYlNNo6bPOIQQIf6L1IX3DRa5FypL2aTrxaw4ONWC2Ol/S7jjmr0PHRARvf5+4QoKsWpi/wkWqm9bi1E8+51ZmlY11Ezazfmv2TbM/JnddEISDBcGJQEJgLIABwsBAAEjAwEBBV0AEAAADIEqCgEDSeTeAAA=");
        string destination = CreateTemporaryDirectory();

        try
        {
            InstallerPayload payload = new(() => new MemoryStream(archive, writable: false));
            await payload.ExtractAsync(destination, new Progress<InstallationProgress>());

            Assert.Equal("application", await File.ReadAllTextAsync(Path.Combine(destination, "bin", "Emerde.exe")));
            Assert.Equal(
                "library",
                await File.ReadAllTextAsync(Path.Combine(destination, "native", "ffmpeg", "avcodec.dll")));
        }
        finally
        {
            Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractAsyncMergesExternalSharedRuntime()
    {
        byte[] archive = CreateArchive(new Dictionary<string, string>
        {
            ["bin/Emerde.exe"] = "application",
        });
        string testRoot = CreateTemporaryDirectory();
        string runtimeSource = Path.Combine(testRoot, "source-runtime");
        string destination = Path.Combine(testRoot, "destination");
        Directory.CreateDirectory(Path.Combine(runtimeSource, "shared", "Microsoft.NETCore.App", "9.0.17"));
        await File.WriteAllTextAsync(
            Path.Combine(runtimeSource, "shared", "Microsoft.NETCore.App", "9.0.17", "System.Private.CoreLib.dll"),
            "runtime");

        try
        {
            InstallerPayload payload = new(
                () => new MemoryStream(archive, writable: false),
                runtimeSource);
            await payload.ExtractAsync(destination, new Progress<InstallationProgress>());

            Assert.Equal(
                "runtime",
                await File.ReadAllTextAsync(Path.Combine(
                    destination,
                    "runtime",
                    "shared",
                    "Microsoft.NETCore.App",
                    "9.0.17",
                    "System.Private.CoreLib.dll")));
            Assert.Equal(
                "application".Length + "runtime".Length,
                payload.GetUncompressedLength());
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static byte[] CreateArchive(IReadOnlyDictionary<string, string> files)
    {
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                using StreamWriter writer = new(entry.Open());
                writer.Write(content);
            }
        }

        return output.ToArray();
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "EmerdeInstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
