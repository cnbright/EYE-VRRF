namespace EyeVrrf.Core;

public static class VrrfCalculator
{
    public static VrrfResult Calculate(WaveformResult waveform, double trendWindowSeconds = 0.150)
    {
        return Calculate(waveform.TimesSeconds(), waveform.Samples, trendWindowSeconds);
    }

    public static VrrfResult Calculate(
        IReadOnlyList<double> timesSeconds,
        IReadOnlyList<double> originalData,
        double trendWindowSeconds = 0.150)
    {
        if (timesSeconds.Count != originalData.Count)
        {
            throw new ArgumentException("times and originalData must contain the same number of samples.");
        }

        if (originalData.Count < 2)
        {
            throw new ArgumentException("At least two luminance samples are required.");
        }

        var samplingRateHz = ValidateAndGetSamplingRate(timesSeconds);
        var original = originalData.ToArray();
        var times = timesSeconds.ToArray();
        var weighted = ApplyTrainedFir(original);
        var (trend, trendWindowSampleCount) = CalculateTrend(weighted, samplingRateHz, trendWindowSeconds);
        var finiteTrend = trend.Where(double.IsFinite).ToArray();
        if (finiteTrend.Length == 0)
        {
            throw new InvalidOperationException("No finite VRRF trend values could be calculated.");
        }

        return new VrrfResult(
            original.Length,
            samplingRateHz,
            trendWindowSeconds,
            trendWindowSampleCount,
            times,
            original,
            weighted,
            trend,
            finiteTrend.Max(),
            finiteTrend.Min(),
            finiteTrend.Average());
    }

    public static double ValidateAndGetSamplingRate(IReadOnlyList<double> timesSeconds)
    {
        if (timesSeconds.Count < 2)
        {
            throw new ArgumentException("At least two time samples are required.");
        }

        var intervals = new double[timesSeconds.Count - 1];
        for (var index = 0; index < intervals.Length; index++)
        {
            intervals[index] = timesSeconds[index + 1] - timesSeconds[index];
            if (intervals[index] <= 0)
            {
                throw new ArgumentException("Time values must be strictly increasing.");
            }
        }

        Array.Sort(intervals);
        var median = intervals[intervals.Length / 2];
        if (intervals.Length % 2 == 0)
        {
            median = (intervals[intervals.Length / 2 - 1] + intervals[intervals.Length / 2]) / 2.0;
        }

        var tolerance = Math.Max(1e-9, median * 0.01);
        foreach (var interval in intervals)
        {
            if (Math.Abs(interval - median) > tolerance)
            {
                throw new ArgumentException("Time values must be evenly spaced.");
            }
        }

        var duration = timesSeconds[^1] - timesSeconds[0] + median;
        if (duration <= 0)
        {
            throw new ArgumentException("Measurement duration must be positive.");
        }

        return timesSeconds.Count / duration;
    }

    public static double[] ApplyTrainedFir(IReadOnlyList<double> originalData)
    {
        if (originalData.Count <= VrrfFirModelData.Length)
        {
            throw new ArgumentException("Input is too short for the supplied FIR model.");
        }

        var original = originalData.ToArray();
        var sampleMean = original.Average();
        var centered = new double[original.Length];
        for (var index = 0; index < original.Length; index++)
        {
            centered[index] = original[index] - sampleMean;
        }

        var weightedCentered = Enumerable.Repeat(double.NaN, original.Length).ToArray();
        var start = VrrfFirModelData.FirstOffset + VrrfFirModelData.Length - 1;
        var stop = original.Length;
        var validValues = new double[stop - start];

        for (var outputIndex = start; outputIndex < stop; outputIndex++)
        {
            var sum = 0.0;
            for (var offset = 0; offset < VrrfFirModelData.Coefficients.Length; offset++)
            {
                sum += VrrfFirModelData.Coefficients[offset] * centered[outputIndex - offset];
            }

            validValues[outputIndex - start] = sum;
        }

        if (VrrfFirModelData.RecenterOutput)
        {
            var validMean = validValues.Average();
            for (var index = 0; index < validValues.Length; index++)
            {
                validValues[index] -= validMean;
            }
        }

        for (var index = 0; index < validValues.Length; index++)
        {
            weightedCentered[start + index] = sampleMean + VrrfFirModelData.MeanOffset + validValues[index];
        }

        return weightedCentered;
    }

    public static (double[] Trend, int WindowSampleCount) CalculateTrend(
        IReadOnlyList<double> weightedData,
        double samplingRateHz,
        double trendWindowSeconds)
    {
        if (trendWindowSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trendWindowSeconds), "Trend window must be positive.");
        }

        if (samplingRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingRateHz), "Sampling rate must be positive.");
        }

        var windowSampleCount = (int)Math.Round(trendWindowSeconds * samplingRateHz);
        if (windowSampleCount < 2)
        {
            throw new ArgumentException("Trend window must contain at least two samples.");
        }

        if (weightedData.Count < windowSampleCount)
        {
            throw new ArgumentException("Weighted data is shorter than the trend window.");
        }

        var trend = Enumerable.Repeat(double.NaN, weightedData.Count).ToArray();
        for (var index = windowSampleCount - 1; index < weightedData.Count; index++)
        {
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            var sum = 0.0;
            var valid = true;

            for (var cursor = index - windowSampleCount + 1; cursor <= index; cursor++)
            {
                var value = weightedData[cursor];
                if (!double.IsFinite(value))
                {
                    valid = false;
                    break;
                }

                min = Math.Min(min, value);
                max = Math.Max(max, value);
                sum += value;
            }

            if (!valid)
            {
                continue;
            }

            var average = sum / windowSampleCount;
            if (average != 0)
            {
                trend[index] = (max - min) / average * 100.0;
            }
        }

        return (trend, windowSampleCount);
    }
}
