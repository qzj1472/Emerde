namespace Emerde.Controls;

internal static class MarqueeAutoScroll
{
    internal const double EdgeSize = 44d;
    internal const double MinimumDelta = 4d;
    internal const double MaximumDelta = 20d;

    internal static double GetDelta(double pointerY, double viewportHeight)
    {
        if (viewportHeight <= 0d)
        {
            return 0d;
        }

        if (pointerY < EdgeSize)
        {
            double ratio = Math.Clamp((EdgeSize - pointerY) / EdgeSize, 0d, 1d);
            return -(MinimumDelta + (MaximumDelta - MinimumDelta) * ratio);
        }

        double bottomEdge = viewportHeight - EdgeSize;
        if (pointerY > bottomEdge)
        {
            double ratio = Math.Clamp((pointerY - bottomEdge) / EdgeSize, 0d, 1d);
            return MinimumDelta + (MaximumDelta - MinimumDelta) * ratio;
        }

        return 0d;
    }
}
