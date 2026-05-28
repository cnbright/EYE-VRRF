using EyeVrrf.Core;

namespace EyeVrrf.Tests;

public sealed class Eye200WaveformClientTests
{
    [Fact]
    public void ParseWaveformBlock_ReturnsSamples()
    {
        var samples = Eye200WaveformClient.ParseWaveformBlock("OK02,P1,118.25,119.5,120");

        Assert.Equal([118.25, 119.5, 120.0], samples);
    }

    [Theory]
    [InlineData("NG02,P1,1,2")]
    [InlineData("OK02,P2,1,2")]
    [InlineData("OK02,P1")]
    public void ParseWaveformBlock_RejectsMalformedResponses(string response)
    {
        Assert.Throws<Eye200Error>(() => Eye200WaveformClient.ParseWaveformBlock(response));
    }

    [Fact]
    public void ParseWaveformBlock_RejectsNonNumericSamples()
    {
        Assert.Throws<FormatException>(() => Eye200WaveformClient.ParseWaveformBlock("OK02,P1,not-a-number"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1000, 1)]
    [InlineData(2048, 0)]
    public void ValidateCaptureParams_RejectsInvalidValues(int sampleCount, int interval)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Eye200WaveformClient.ValidateCaptureParams(sampleCount, interval));
    }
}
