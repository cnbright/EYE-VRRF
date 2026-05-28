namespace EyeVrrf.Core;

public sealed record MeasurementSummary(
    int Number,
    double SamplingRateHz,
    double ObservationSeconds,
    double TrendWindowSeconds,
    double TrendMaxPercent,
    double TrendMinPercent,
    double TrendAveragePercent,
    double WeightedMax,
    double WeightedMin,
    double WeightedAverage,
    DateTime Timestamp)
{
    public static MeasurementSummary FromResult(int number, VrrfResult result, DateTime timestamp)
    {
        return new MeasurementSummary(
            number,
            result.SamplingRateHz,
            result.ObservationSeconds,
            result.TrendWindowSeconds,
            result.TrendMaxPercent,
            result.TrendMinPercent,
            result.TrendAveragePercent,
            result.WeightedMax,
            result.WeightedMin,
            result.WeightedAverage,
            timestamp);
    }
}
