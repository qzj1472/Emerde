using System.Security.Cryptography;
using System.Text;

namespace Emerde.Setup;

internal sealed record SetupContainerDescriptor(
    long BootstrapOffset,
    long BootstrapLength,
    long ApplicationOffset,
    long ApplicationLength,
    byte[] BootstrapSha256,
    byte[] ApplicationSha256);

internal static class SetupContainerFormat
{
    private static readonly byte[] HeaderMagic = Encoding.ASCII.GetBytes("EMERDE.SETUP.01!");
    private static readonly byte[] TrailerMagic = Encoding.ASCII.GetBytes("EMERDE.SETUP.END");
    private const int Version = 1;
    private const int FooterSize = 132;
    private const int SearchWindowSize = 1024 * 1024;

    public static void AppendContainer(
        Stream destination,
        string bootstrapPath,
        string applicationPath)
    {
        long bootstrapOffset = destination.Position;
        byte[] bootstrapHash = AppendFile(destination, bootstrapPath);
        long bootstrapLength = destination.Position - bootstrapOffset;
        long applicationOffset = destination.Position;
        byte[] applicationHash = AppendFile(destination, applicationPath);
        long applicationLength = destination.Position - applicationOffset;

        using BinaryWriter writer = new(destination, Encoding.UTF8, leaveOpen: true);
        writer.Write(HeaderMagic);
        writer.Write(Version);
        writer.Write(bootstrapOffset);
        writer.Write(bootstrapLength);
        writer.Write(applicationOffset);
        writer.Write(applicationLength);
        writer.Write(bootstrapHash);
        writer.Write(applicationHash);
        writer.Write(TrailerMagic);
    }

    public static SetupContainerDescriptor ReadDescriptor(Stream source)
    {
        if (!source.CanSeek || source.Length < FooterSize)
        {
            throw new InvalidDataException("安装器容器不完整。");
        }

        int searchLength = (int)Math.Min(source.Length, SearchWindowSize);
        byte[] tail = new byte[searchLength];
        source.Position = source.Length - searchLength;
        source.ReadExactly(tail);
        int trailerIndex = LastIndexOf(tail, TrailerMagic);
        if (trailerIndex < FooterSize - TrailerMagic.Length)
        {
            throw new InvalidDataException("安装器容器标记不存在。");
        }

        long footerOffset = source.Length - searchLength + trailerIndex - (FooterSize - TrailerMagic.Length);
        source.Position = footerOffset;
        using BinaryReader reader = new(source, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(HeaderMagic.Length).SequenceEqual(HeaderMagic)
            || reader.ReadInt32() != Version)
        {
            throw new InvalidDataException("安装器容器版本无效。");
        }

        SetupContainerDescriptor descriptor = new(
            reader.ReadInt64(),
            reader.ReadInt64(),
            reader.ReadInt64(),
            reader.ReadInt64(),
            reader.ReadBytes(SHA256.HashSizeInBytes),
            reader.ReadBytes(SHA256.HashSizeInBytes));
        if (!reader.ReadBytes(TrailerMagic.Length).SequenceEqual(TrailerMagic))
        {
            throw new InvalidDataException("安装器容器结尾无效。");
        }

        ValidateSegment(descriptor.BootstrapOffset, descriptor.BootstrapLength, footerOffset);
        ValidateSegment(descriptor.ApplicationOffset, descriptor.ApplicationLength, footerOffset);
        if (descriptor.ApplicationOffset != descriptor.BootstrapOffset + descriptor.BootstrapLength)
        {
            throw new InvalidDataException("安装器容器分段无效。");
        }
        if (descriptor.ApplicationOffset + descriptor.ApplicationLength != footerOffset)
        {
            throw new InvalidDataException("安装器容器尾段无效。");
        }

        return descriptor;
    }

    private static byte[] AppendFile(Stream destination, string path)
    {
        using FileStream source = File.OpenRead(path);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        int bytesRead;
        while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, bytesRead);
            hash.AppendData(buffer, 0, bytesRead);
        }

        return hash.GetHashAndReset();
    }

    private static void ValidateSegment(long offset, long length, long footerOffset)
    {
        if (offset < 0 || length <= 0 || offset > footerOffset - length)
        {
            throw new InvalidDataException("安装器容器分段越界。");
        }
    }

    private static int LastIndexOf(byte[] source, byte[] value)
    {
        for (int index = source.Length - value.Length; index >= 0; index--)
        {
            if (source.AsSpan(index, value.Length).SequenceEqual(value))
            {
                return index;
            }
        }

        return -1;
    }
}
