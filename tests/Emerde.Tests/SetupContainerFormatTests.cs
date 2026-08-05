using System.Security.Cryptography;
using Emerde.Setup;

namespace Emerde.Tests;

public sealed class SetupContainerFormatTests
{
    [Fact]
    public void AppendAndReadPreserveSegmentMetadata()
    {
        string testRoot = CreateTemporaryDirectory();
        string bootstrapPath = Path.Combine(testRoot, "bootstrap.zst");
        string applicationPath = Path.Combine(testRoot, "application.7z");
        byte[] bootstrap = [1, 2, 3, 4];
        byte[] application = [5, 6, 7, 8, 9];
        File.WriteAllBytes(bootstrapPath, bootstrap);
        File.WriteAllBytes(applicationPath, application);

        try
        {
            using MemoryStream container = new();
            container.Write([10, 11, 12]);
            SetupContainerFormat.AppendContainer(container, bootstrapPath, applicationPath);
            container.Position = 0;

            SetupContainerDescriptor descriptor = SetupContainerFormat.ReadDescriptor(container);

            Assert.Equal(3, descriptor.BootstrapOffset);
            Assert.Equal(bootstrap.Length, descriptor.BootstrapLength);
            Assert.Equal(3 + bootstrap.Length, descriptor.ApplicationOffset);
            Assert.Equal(application.Length, descriptor.ApplicationLength);
            Assert.Equal(SHA256.HashData(bootstrap), descriptor.BootstrapSha256);
            Assert.Equal(SHA256.HashData(application), descriptor.ApplicationSha256);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void ReadDescriptorRejectsSegmentOutsideContainer()
    {
        string testRoot = CreateTemporaryDirectory();
        string bootstrapPath = Path.Combine(testRoot, "bootstrap.zst");
        string applicationPath = Path.Combine(testRoot, "application.7z");
        File.WriteAllBytes(bootstrapPath, [1]);
        File.WriteAllBytes(applicationPath, [2]);

        try
        {
            using MemoryStream container = new();
            container.Write([10]);
            SetupContainerFormat.AppendContainer(container, bootstrapPath, applicationPath);
            byte[] bytes = container.ToArray();
            BitConverter.GetBytes(long.MaxValue).CopyTo(bytes, bytes.Length - 132 + 20);

            using MemoryStream corrupted = new(bytes);
            Assert.Throws<InvalidDataException>(() => SetupContainerFormat.ReadDescriptor(corrupted));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "EmerdeSetupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
