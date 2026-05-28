using System.Globalization;
using System.Text;

namespace EyeVrrf.Core;

public static class CsvExporter
{
    public static void SavePointData(VrrfResult result, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("NO.,Time [sec],Original Data,Weighted Data,VRRF Trend");
        for (var index = 0; index < result.SampleCount; index++)
        {
            writer.Write(index + 1);
            writer.Write(',');
            writer.Write(Format(result.TimesSeconds[index]));
            writer.Write(',');
            writer.Write(Format(result.OriginalData[index]));
            writer.Write(',');
            writer.Write(Format(result.WeightedData[index]));
            writer.Write(',');
            writer.WriteLine(Format(result.TrendPercent[index]));
        }
    }

    public static void SaveSummaryData(IEnumerable<MeasurementSummary> summaries, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("No.,Sampling Freq.,Observation Time,Time Window for Trend,VRRF Trend Max.,VRRF Trend Min.,VRRF Trend Ave.,Weighted Data Max.,Weighted Data Min.,Weighted Data Ave.,Date,Time");
        foreach (var summary in summaries)
        {
            writer.WriteLine(string.Join(
                ',',
                summary.Number.ToString(CultureInfo.InvariantCulture),
                Format(summary.SamplingRateHz),
                Format(summary.ObservationSeconds),
                Format(summary.TrendWindowSeconds),
                Format(summary.TrendMaxPercent),
                Format(summary.TrendMinPercent),
                Format(summary.TrendAveragePercent),
                Format(summary.WeightedMax),
                Format(summary.WeightedMin),
                Format(summary.WeightedAverage),
                summary.Timestamp.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                summary.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
        }
    }

    private static string Format(double value)
    {
        return double.IsFinite(value) ? value.ToString("G17", CultureInfo.InvariantCulture) : "nan";
    }
}
