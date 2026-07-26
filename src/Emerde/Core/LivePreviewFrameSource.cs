using LibVLCSharp.Shared;

namespace Emerde.Core;

public sealed class LivePreviewFrameSource : IDisposable
{
    private const int BytesPerPixel = 4;
    private static readonly long MinimumFrameInterval = System.Diagnostics.Stopwatch.Frequency / 30;
    private readonly object syncRoot = new();
    private readonly System.Windows.Threading.Dispatcher dispatcher;
    private nint allocatedBuffer;
    private nint alignedBuffer;
    private byte[]? framePixels;
    private System.Windows.Media.Imaging.WriteableBitmap? source;
    private int pixelHeight;
    private int pitch;
    private int framePending;
    private int generation;
    private long lastFrameTimestamp;
    private bool disposed;

    public System.Windows.Media.Imaging.BitmapSource? Source => source;

    public event EventHandler? SourceChanged;

    public LivePreviewFrameSource(MediaPlayer mediaPlayer)
    {
        dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        mediaPlayer.SetVideoCallbacks(LockVideo, UnlockVideo, null);
        mediaPlayer.SetVideoFormatCallbacks(ConfigureVideo, CleanupVideo);
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            generation++;
            framePixels = null;
            ReleaseBuffer();
        }

        source = null;
        SourceChanged = null;
    }

    internal static (int Pitch, int BufferLength) CalculateBufferLayout(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            return (0, 0);
        }

        long alignedPitch = ((long)width * BytesPerPixel + 31L) & ~31L;
        long bufferLength = alignedPitch * height;
        if (alignedPitch > int.MaxValue || bufferLength > int.MaxValue)
        {
            return (0, 0);
        }

        return ((int)alignedPitch, (int)bufferLength);
    }

    private uint ConfigureVideo(
        ref nint opaque,
        nint chroma,
        ref uint width,
        ref uint height,
        ref uint pitches,
        ref uint lines)
    {
        try
        {
            (int configuredPitch, int bufferLength) = CalculateBufferLayout(width, height);
            if (configuredPitch == 0 || bufferLength == 0)
            {
                return 0;
            }

            byte[] chromaBytes = [(byte)'R', (byte)'V', (byte)'3', (byte)'2'];
            System.Runtime.InteropServices.Marshal.Copy(chromaBytes, 0, chroma, chromaBytes.Length);
            pitches = (uint)configuredPitch;
            lines = height;
            int configuredWidth = (int)width;
            int configuredHeight = (int)height;

            int currentGeneration;
            lock (syncRoot)
            {
                if (disposed)
                {
                    return 0;
                }

                ReleaseBuffer();
                allocatedBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(bufferLength + 31);
                alignedBuffer = (allocatedBuffer + 31) & ~31;
                framePixels = new byte[bufferLength];
                pixelHeight = configuredHeight;
                pitch = configuredPitch;
                framePending = 0;
                lastFrameTimestamp = 0;
                currentGeneration = ++generation;
            }

            _ = dispatcher.BeginInvoke(
                () => EnsureSource(currentGeneration, configuredWidth, configuredHeight),
                System.Windows.Threading.DispatcherPriority.Render);
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private void CleanupVideo(ref nint opaque)
    {
        lock (syncRoot)
        {
            generation++;
            framePixels = null;
            framePending = 0;
            ReleaseBuffer();
        }
    }

    private nint LockVideo(nint opaque, nint planes)
    {
        lock (syncRoot)
        {
            if (disposed || alignedBuffer == 0)
            {
                System.Runtime.InteropServices.Marshal.WriteIntPtr(planes, nint.Zero);
                return nint.Zero;
            }

            System.Runtime.InteropServices.Marshal.WriteIntPtr(planes, alignedBuffer);
            return alignedBuffer;
        }
    }

    private void UnlockVideo(nint opaque, nint picture, nint planes)
    {
        long timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (timestamp - Interlocked.Read(ref lastFrameTimestamp) < MinimumFrameInterval
            || Interlocked.CompareExchange(ref framePending, 1, 0) != 0)
        {
            return;
        }

        byte[]? pixels;
        int currentPitch;
        int currentHeight;
        int currentGeneration;
        lock (syncRoot)
        {
            if (disposed || picture == 0 || framePixels == null)
            {
                Interlocked.Exchange(ref framePending, 0);
                return;
            }

            pixels = framePixels;
            currentPitch = pitch;
            currentHeight = pixelHeight;
            currentGeneration = generation;
            System.Runtime.InteropServices.Marshal.Copy(picture, pixels, 0, currentPitch * currentHeight);
        }

        Interlocked.Exchange(ref lastFrameTimestamp, timestamp);
        try
        {
            _ = dispatcher.BeginInvoke(
                () => PresentFrame(currentGeneration, pixels, currentPitch, currentHeight),
                System.Windows.Threading.DispatcherPriority.Render);
        }
        catch
        {
            ClearPendingFrame(currentGeneration);
        }
    }

    private void EnsureSource(int expectedGeneration, int width, int height)
    {
        if (disposed || expectedGeneration != generation)
        {
            return;
        }

        if (source is { PixelWidth: var sourceWidth, PixelHeight: var sourceHeight }
            && sourceWidth == width
            && sourceHeight == height)
        {
            return;
        }

        source = new System.Windows.Media.Imaging.WriteableBitmap(
            width,
            height,
            96d,
            96d,
            System.Windows.Media.PixelFormats.Bgr32,
            null);
        SourceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PresentFrame(int expectedGeneration, byte[] pixels, int currentPitch, int currentHeight)
    {
        try
        {
            if (disposed || expectedGeneration != generation || source == null)
            {
                return;
            }

            source.WritePixels(
                new System.Windows.Int32Rect(0, 0, source.PixelWidth, source.PixelHeight),
                pixels,
                currentPitch,
                0);
        }
        finally
        {
            ClearPendingFrame(expectedGeneration);
        }
    }

    private void ClearPendingFrame(int expectedGeneration)
    {
        if (expectedGeneration == Volatile.Read(ref generation))
        {
            Interlocked.Exchange(ref framePending, 0);
        }
    }

    private void ReleaseBuffer()
    {
        alignedBuffer = 0;
        if (allocatedBuffer == 0)
        {
            return;
        }

        System.Runtime.InteropServices.Marshal.FreeHGlobal(allocatedBuffer);
        allocatedBuffer = 0;
    }
}
