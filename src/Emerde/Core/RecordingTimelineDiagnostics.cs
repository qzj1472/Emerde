namespace Emerde.Core;

internal sealed record RecordingTimelineSample(
    DateTime RecordedAt,
    long AudioPresentationTimestampMicroseconds,
    long VideoPresentationTimestampMicroseconds,
    long AudioVideoDifferenceMicroseconds,
    long BaselineDifferenceMicroseconds,
    long CumulativeDriftMicroseconds,
    double DriftRateMicrosecondsPerSecond,
    string Classification,
    int SampleCount);

internal sealed class RecordingTimelineDiagnostics
{
    private const long StableDifferenceThresholdMicroseconds = 250_000;
    private const long SuddenJumpThresholdMicroseconds = 500_000;
    private const int MaximumSamples = 30;
    private readonly object syncRoot = new();
    private readonly Queue<RecordingTimelineSample> samples = new();
    private long? baselineDifference;
    private RecordingTimelineSample? previous;

    public RecordingTimelineSample Add(
        DateTime recordedAt,
        long audioPresentationTimestampMicroseconds,
        long videoPresentationTimestampMicroseconds,
        long audioVideoDifferenceMicroseconds,
        double sampleElapsedSeconds)
    {
        lock (syncRoot)
        {
            baselineDifference ??= audioVideoDifferenceMicroseconds;
            long cumulativeDrift = audioVideoDifferenceMicroseconds - baselineDifference.Value;
            double driftRate = previous == null || sampleElapsedSeconds <= 0d
                ? 0d
                : (audioVideoDifferenceMicroseconds - previous.AudioVideoDifferenceMicroseconds) / sampleElapsedSeconds;
            long previousDelta = previous == null
                ? 0
                : audioVideoDifferenceMicroseconds - previous.AudioVideoDifferenceMicroseconds;
            string classification = GetClassification(
                audioVideoDifferenceMicroseconds,
                cumulativeDrift,
                previousDelta);
            RecordingTimelineSample sample = new(
                recordedAt,
                audioPresentationTimestampMicroseconds,
                videoPresentationTimestampMicroseconds,
                audioVideoDifferenceMicroseconds,
                baselineDifference.Value,
                cumulativeDrift,
                driftRate,
                classification,
                previous == null ? 1 : previous.SampleCount + 1);
            samples.Enqueue(sample);
            while (samples.Count > MaximumSamples)
            {
                samples.Dequeue();
            }
            previous = sample;
            return sample;
        }
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            samples.Clear();
            baselineDifference = null;
            previous = null;
        }
    }

    internal int Count
    {
        get
        {
            lock (syncRoot)
            {
                return samples.Count;
            }
        }
    }

    internal static string GetClassification(
        long differenceMicroseconds,
        long cumulativeDriftMicroseconds,
        long previousDeltaMicroseconds)
    {
        if (Math.Abs(previousDeltaMicroseconds) >= SuddenJumpThresholdMicroseconds)
        {
            return "sudden_jump";
        }
        if (Math.Abs(cumulativeDriftMicroseconds) >= StableDifferenceThresholdMicroseconds)
        {
            return "gradual_drift";
        }
        return Math.Abs(differenceMicroseconds) >= StableDifferenceThresholdMicroseconds
            ? "fixed_offset"
            : "aligned";
    }
}
