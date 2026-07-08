using System.Globalization;

namespace Emerde.Core;

internal static class SegmentTimeUnitHelper
{
    private const int MillisecondsPerSecond = 1000;
    private const int MillisecondsPerMinute = 60 * MillisecondsPerSecond;
    private const int MillisecondsPerHour = 60 * MillisecondsPerMinute;
    private const double BytesPerMegabyte = 1000d * 1000d;
    private const double BytesPerGigabyte = 1000d * 1000d * 1000d;

    public const int Seconds = 0;
    public const int Minutes = 1;
    public const int Hours = 2;
    public const int Megabytes = 3;
    public const int Gigabytes = 4;
    public const int Milliseconds = 5;

    public static double ToDisplayValue(int rawValue, int unitIndex)
    {
        return unitIndex switch
        {
            Gigabytes => rawValue / BytesPerGigabyte,
            Megabytes => rawValue / BytesPerMegabyte,
            Milliseconds => rawValue,
            Hours => rawValue / (double)(MillisecondsPerHour / MillisecondsPerSecond),
            Minutes => rawValue / (double)(MillisecondsPerMinute / MillisecondsPerSecond),
            Seconds or _ => rawValue,
        };
    }

    public static double ConvertDisplayValue(int rawValue, int sourceUnitIndex, int targetUnitIndex)
    {
        if (IsSizeUnit(sourceUnitIndex) && IsSizeUnit(targetUnitIndex))
        {
            return ToDisplayValue(rawValue, targetUnitIndex);
        }

        if (!IsTimeUnit(sourceUnitIndex) || !IsTimeUnit(targetUnitIndex))
        {
            return ToDisplayValue(rawValue, targetUnitIndex);
        }

        double milliseconds = sourceUnitIndex == Milliseconds
            ? rawValue
            : rawValue * (double)MillisecondsPerSecond;

        return targetUnitIndex switch
        {
            Milliseconds => milliseconds,
            Hours => milliseconds / MillisecondsPerHour,
            Minutes => milliseconds / MillisecondsPerMinute,
            Seconds or _ => milliseconds / MillisecondsPerSecond,
        };
    }

    public static int ToConfigValue(double value, int unitIndex)
    {
        if (IsSizeUnit(unitIndex))
        {
            double sizeMultiplier = unitIndex == Gigabytes ? BytesPerGigabyte : BytesPerMegabyte;
            return (int)Math.Clamp(Math.Round(value * sizeMultiplier, MidpointRounding.AwayFromZero), BytesPerMegabyte, int.MaxValue);
        }

        if (unitIndex == Milliseconds)
        {
            return (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 1, int.MaxValue);
        }

        double milliseconds = unitIndex switch
        {
            Hours => value * MillisecondsPerHour,
            Minutes => value * MillisecondsPerMinute,
            Seconds or _ => value * MillisecondsPerSecond,
        };

        return (int)Math.Max(1, Math.Round(milliseconds / MillisecondsPerSecond, MidpointRounding.AwayFromZero));
    }

    public static string ToSegmentArgument(int rawValue, int unitIndex)
    {
        if (unitIndex == Milliseconds)
        {
            double seconds = Math.Max(0.001d, rawValue / 1000d);
            return seconds.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return Math.Max(1, rawValue).ToString(CultureInfo.InvariantCulture);
    }

    public static bool IsSizeUnit(int unitIndex)
    {
        return unitIndex is Megabytes or Gigabytes;
    }

    public static bool IsTimeUnit(int unitIndex)
    {
        return unitIndex is Milliseconds or Seconds or Minutes or Hours;
    }

    public static int NormalizeUnit(int unitIndex)
    {
        return unitIndex is Seconds or Minutes or Hours or Megabytes or Gigabytes or Milliseconds ? unitIndex : Seconds;
    }

    public static string FormatNumber(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.0001d)
        {
            return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
