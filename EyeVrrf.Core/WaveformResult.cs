namespace EyeVrrf.Core;

public sealed record WaveformResult(
    IReadOnlyList<double> Samples,
    int SampleCount,
    int DecimationInterval,
    double BaseSampleRateHz = 3000.0,
    IReadOnlyDictionary<string, string>? Responses = null)
{
    public double SamplePeriodSeconds => DecimationInterval / BaseSampleRateHz;

    public double SamplingRateHz => BaseSampleRateHz / DecimationInterval;

    public double ObservationSeconds => SampleCount * SamplePeriodSeconds;

    public double[] TimesSeconds()
    {
        var times = new double[Samples.Count];
        for (var index = 0; index < times.Length; index++)
        {
            times[index] = (index + 1) * SamplePeriodSeconds;
        }

        return times;
    }
}
