using System.Globalization;
using EyeVrrf.Core;

namespace EyeVrrf.Tests;

public sealed class VrrfCalculatorTests
{
    [Fact]
    public void Calculate_MatchesPythonGeneratedReferenceCsv()
    {
        var rows = ReadReferenceRows();

        var result = VrrfCalculator.Calculate(
            rows.Select(row => row.TimeSeconds).ToArray(),
            rows.Select(row => row.Original).ToArray(),
            trendWindowSeconds: 0.150);

        Assert.Equal(2048, result.SampleCount);
        Assert.Equal(3000.0, result.SamplingRateHz, 9);
        Assert.Equal(450, result.TrendWindowSampleCount);
        Assert.All(result.WeightedData.Take(900), AssertIsNaN);
        Assert.True(double.IsFinite(result.WeightedData[900]));
        Assert.All(result.TrendPercent.Take(1349), AssertIsNaN);
        Assert.True(double.IsFinite(result.TrendPercent[1349]));

        var referenceWeighted = rows.Select(row => row.Weighted).ToArray();
        var referenceTrend = rows.Select(row => row.Trend).ToArray();

        AssertClose(referenceWeighted[900], result.WeightedData[900], 1e-9);
        AssertClose(referenceWeighted[^1], result.WeightedData[^1], 1e-9);
        AssertClose(referenceTrend[1349], result.TrendPercent[1349], 1e-9);
        AssertClose(referenceTrend[^1], result.TrendPercent[^1], 1e-9);

        var finiteTrend = referenceTrend.Where(double.IsFinite).ToArray();
        AssertClose(finiteTrend.Max(), result.TrendMaxPercent, 1e-9);
        AssertClose(finiteTrend.Min(), result.TrendMinPercent, 1e-9);
        AssertClose(finiteTrend.Average(), result.TrendAveragePercent, 1e-9);
    }

    [Fact]
    public void Calculate_DerivesSamplingRateFromDecimationInterval()
    {
        const int sampleCount = 2048;
        var waveform = new WaveformResult(
            Enumerable.Range(0, sampleCount).Select(index => 100.0 + Math.Sin(index * 0.03)).ToArray(),
            sampleCount,
            DecimationInterval: 3);

        var result = VrrfCalculator.Calculate(waveform, 0.150);

        Assert.Equal(1000.0, result.SamplingRateHz, 9);
        Assert.Equal(150, result.TrendWindowSampleCount);
    }

    [Fact]
    public void SavePointData_WritesExpectedHeader()
    {
        var rows = ReadReferenceRows();
        var result = VrrfCalculator.Calculate(
            rows.Select(row => row.TimeSeconds).ToArray(),
            rows.Select(row => row.Original).ToArray(),
            0.150);
        var path = Path.Combine(Path.GetTempPath(), $"eye-vrrf-{Guid.NewGuid():N}.csv");

        try
        {
            CsvExporter.SavePointData(result, path);
            var lines = File.ReadLines(path).Take(2).ToArray();

            Assert.Equal("NO.,Time [sec],Original Data,Weighted Data,VRRF Trend", lines[0].TrimStart('\uFEFF'));
            Assert.StartsWith("1,0.00033333333333333332,118.407127", lines[1]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ReferenceRow[] ReadReferenceRows()
    {
        var path = Path.Combine(
            "C:\\",
            "sorce",
            "document_now",
            "TFT-LCD",
            "\u8bbe\u5907\u8bf4\u660e",
            "\u7cbe\u6d4b\u63a2\u5934",
            "eye200_vrrf_2048_interval_1.csv");
        Assert.True(File.Exists(path), $"Reference CSV not found: {path}");

        return File.ReadLines(path)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                var parts = line.Split(',');
                return new ReferenceRow(
                    Parse(parts[1]),
                    Parse(parts[2]),
                    Parse(parts[3]),
                    Parse(parts[4]));
            })
            .ToArray();
    }

    private static double Parse(string value)
    {
        return value.Equals("nan", StringComparison.OrdinalIgnoreCase)
            ? double.NaN
            : double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static void AssertClose(double expected, double actual, double tolerance)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:G17}, actual {actual:G17}");
    }

    private static void AssertIsNaN(double value)
    {
        Assert.True(double.IsNaN(value));
    }

    private sealed record ReferenceRow(double TimeSeconds, double Original, double Weighted, double Trend);
}
