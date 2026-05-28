using System.Globalization;
using System.IO.Ports;
using System.Text;

namespace EyeVrrf.Core;

public sealed class Eye200WaveformClient : IDisposable
{
    private readonly SerialPort _serialPort;
    private readonly TimeSpan _settleDelay;

    public Eye200WaveformClient(
        string portName = "COM37",
        int baudRate = 115200,
        int timeoutMilliseconds = 3000,
        TimeSpan? settleDelay = null)
    {
        _settleDelay = settleDelay ?? TimeSpan.FromMilliseconds(20);
        _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = timeoutMilliseconds,
            WriteTimeout = timeoutMilliseconds,
            Encoding = Encoding.ASCII,
            NewLine = "\r",
        };
    }

    public void Open()
    {
        if (_serialPort.IsOpen)
        {
            return;
        }

        _serialPort.Open();
        _serialPort.DiscardInBuffer();
        _serialPort.DiscardOutBuffer();
    }

    public async Task<WaveformResult> AcquireAsync(
        int sampleCount,
        int decimationInterval,
        double baseSampleRateHz = 3000.0,
        CancellationToken cancellationToken = default,
        IProgress<MeasurementProgress>? progress = null)
    {
        ValidateCaptureParams(sampleCount, decimationInterval);
        Open();

        var log2Count = (int)Math.Log2(sampleCount);
        var responses = new Dictionary<string, string>();
        var blockCount = (int)Math.Ceiling(sampleCount / 64.0);
        var totalSteps = 6 + blockCount;
        var completedSteps = 0;

        void Report(string message)
        {
            progress?.Report(new MeasurementProgress(completedSteps * 100.0 / totalSteps, message));
        }

        async Task<string> RunCommandAsync(string key, string command)
        {
            Report(command);
            var response = await CommandAsync(command, cancellationToken).ConfigureAwait(false);
            responses[key] = response;
            completedSteps++;
            Report(command);
            return response;
        }

        await RunCommandAsync("STR,0", "STR,0").ConfigureAwait(false);
        await RunCommandAsync("WCS", $"WCS,0,0,{log2Count},{decimationInterval},1").ConfigureAwait(false);
        await RunCommandAsync("FCS", "FCS,2,0").ConfigureAwait(false);
        await RunCommandAsync("MMS", "MMS,2").ConfigureAwait(false);
        await RunCommandAsync("MES", "MES,1").ConfigureAwait(false);
        await RunCommandAsync("WDR,0", "WDR,0").ConfigureAwait(false);

        var samples = new List<double>(sampleCount);
        for (var blockIndex = 1; blockIndex <= blockCount; blockIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = $"WDR,{blockIndex}";
            Report($"Reading block {blockIndex}/{blockCount}");
            var response = await CommandAsync(command, cancellationToken).ConfigureAwait(false);
            responses[command] = response;
            samples.AddRange(ParseWaveformBlock(response));
            completedSteps++;
            Report($"Reading block {blockIndex}/{blockCount}");
        }

        if (samples.Count < sampleCount)
        {
            throw new Eye200Error($"Expected {sampleCount} samples, received {samples.Count}.");
        }

        progress?.Report(new MeasurementProgress(100.0, "Calculating VRRF"));
        return new WaveformResult(
            samples.Take(sampleCount).ToArray(),
            sampleCount,
            decimationInterval,
            baseSampleRateHz,
            responses);
    }

    public async Task<string> CommandAsync(
        string command,
        CancellationToken cancellationToken = default,
        string expectPrefix = "OK")
    {
        if (!_serialPort.IsOpen)
        {
            throw new Eye200Error("Serial port is not open.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _serialPort.Write(command + "\r");
            var response = await Task.Run(() => _serialPort.ReadTo("\r"), cancellationToken).ConfigureAwait(false);
            response = response.Trim();
            if (!string.IsNullOrEmpty(expectPrefix) && !response.StartsWith(expectPrefix, StringComparison.Ordinal))
            {
                throw new Eye200Error($"Unexpected response to {command}: {response}");
            }

            if (_settleDelay > TimeSpan.Zero)
            {
                await Task.Delay(_settleDelay, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }
        catch (TimeoutException ex)
        {
            throw new Eye200Error($"Timeout waiting for response to {command}.", ex);
        }
        catch (IOException ex)
        {
            throw new Eye200Error($"Serial I/O error while sending {command}: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new Eye200Error($"Serial port is not available: {ex.Message}", ex);
        }
    }

    public static void ValidateCaptureParams(int sampleCount, int decimationInterval)
    {
        if (sampleCount <= 0 || (sampleCount & (sampleCount - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "sampleCount must be a positive power of two.");
        }

        if (decimationInterval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decimationInterval), "decimationInterval must be positive.");
        }
    }

    public static IReadOnlyList<double> ParseWaveformBlock(string response)
    {
        var parts = response.Trim().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            throw new Eye200Error($"Malformed WDR response: {response}");
        }

        if (parts[0] != "OK02" || parts[1] != "P1")
        {
            throw new Eye200Error($"Response is not a waveform data block: {response}");
        }

        var values = new List<double>(parts.Length - 2);
        for (var index = 2; index < parts.Length; index++)
        {
            if (parts[index].Length == 0)
            {
                continue;
            }

            values.Add(double.Parse(parts[index], CultureInfo.InvariantCulture));
        }

        return values;
    }

    public void Dispose()
    {
        _serialPort.Dispose();
    }
}
