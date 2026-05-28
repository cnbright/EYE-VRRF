namespace EyeVrrf.Core;

public sealed record VrrfResult(
    int SampleCount,
    double SamplingRateHz,
    double TrendWindowSeconds,
    int TrendWindowSampleCount,
    double[] TimesSeconds,
    double[] OriginalData,
    double[] WeightedData,
    double[] TrendPercent,
    double TrendMaxPercent,
    double TrendMinPercent,
    double TrendAveragePercent)
{
    public double ObservationSeconds => SampleCount / SamplingRateHz;

    public double WeightedMax => WeightedData.Where(double.IsFinite).Max();
    public double WeightedMin => WeightedData.Where(double.IsFinite).Min();
    public double WeightedAverage => WeightedData.Where(double.IsFinite).Average();
}
