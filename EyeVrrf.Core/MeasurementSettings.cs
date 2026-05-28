namespace EyeVrrf.Core;

public sealed record MeasurementSettings
{
    public string PortName { get; init; } = "COM37";
    public int BaudRate { get; init; } = 115200;
    public int SampleCount { get; init; } = 32768;
    public int DecimationInterval { get; init; } = 1;
    public double BaseSampleRateHz { get; init; } = 3000.0;
    public double TrendWindowSeconds { get; init; } = 0.150;
    public int TimeoutMilliseconds { get; init; } = 3000;

    public double ObservationSeconds => SampleCount * DecimationInterval / BaseSampleRateHz;
}
