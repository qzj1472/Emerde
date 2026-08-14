using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Resources;

namespace Emerde.Views;

internal static class RoomLoadingAtlas
{
    internal const int FrameCount = 40;
    internal const int Columns = 8;
    internal const int FrameSize = 328;
    internal const double FrameRate = 60000d / 1001d;
    private static readonly Lazy<BitmapSource[]> frames = new(LoadFrames, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static BitmapSource[] Frames => frames.Value;

    private static BitmapSource[] LoadFrames()
    {
        Uri resourceUri = new("/Emerde;component/Assets/RoomLoadingAtlas.png", UriKind.Relative);
        StreamResourceInfo? resource = Application.GetResourceStream(resourceUri);
        if (resource == null)
        {
            throw new InvalidOperationException("RoomLoadingAtlas.png resource is unavailable.");
        }

        using (resource.Stream)
        {
            BitmapFrame atlas = BitmapFrame.Create(
                resource.Stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            atlas.Freeze();

            int expectedRows = (FrameCount + Columns - 1) / Columns;
            if (atlas.PixelWidth != Columns * FrameSize || atlas.PixelHeight != expectedRows * FrameSize)
            {
                throw new InvalidOperationException(
                    $"RoomLoadingAtlas.png has an invalid size: {atlas.PixelWidth}x{atlas.PixelHeight}.");
            }

            BitmapSource[] result = new BitmapSource[FrameCount];
            for (int index = 0; index < result.Length; index++)
            {
                int column = index % Columns;
                int row = index / Columns;
                CroppedBitmap frame = new(
                    atlas,
                    new Int32Rect(
                        column * FrameSize,
                        row * FrameSize,
                        FrameSize,
                        FrameSize));
                frame.Freeze();
                result[index] = frame;
            }

            return result;
        }
    }
}
