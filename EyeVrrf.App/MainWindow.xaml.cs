using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EyeVrrf.Core;
using Microsoft.Win32;

namespace EyeVrrf.App;

public partial class MainWindow : Window
{
    public ObservableCollection<RangeRow> RangeRows { get; } = [];
    public ObservableCollection<HistoryRow> HistoryRows { get; } = [];

    private readonly List<MeasurementSummary> _summaries = [];
    private CancellationTokenSource? _measureCts;
    private VrrfResult? _currentResult;
    private ProgressWindow? _progressWindow;
    private bool _isMeasuring;
    private ZoomMode _zoomMode = ZoomMode.XOnly;
    private PanMode _panMode = PanMode.HorizontalOnly;
    private Point? _dragStart;
    private double _dragXMin;
    private double _dragXMax;
    private double _dragYMin;
    private double _dragYMax;
    private double _dragTrendMin;
    private double _dragTrendMax;
    private double? _xMin;
    private double? _xMax;
    private double? _yMin;
    private double? _yMax;
    private double? _trendMin;
    private double? _trendMax;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ResetRangeRows();
        UpdateZoomMenuChecks();
        LoadSerialPorts();
        UpdateObservationFromSampleCount();
        SampleCountComboBox.SelectionChanged += (_, _) => UpdateObservationFromSampleCount();
    }

    private async void MeasureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMeasuring)
        {
            _measureCts?.Cancel();
            return;
        }

        try
        {
            var settings = ReadSettings();
            _measureCts = new CancellationTokenSource();
            SetMeasuringState(true);

            var mode = SelectedMode();
            if (mode == "Continuous")
            {
                while (!_measureCts.IsCancellationRequested)
                {
                    await MeasureOnceAsync(settings, _measureCts.Token);
                }
            }
            else if (mode == "Interval")
            {
                var count = ParsePositiveInt(IntervalCountTextBox.Text, "Interval count");
                var seconds = ParseNonNegativeDouble(IntervalSecondsTextBox.Text, "Interval seconds");
                for (var index = 0; index < count && !_measureCts.IsCancellationRequested; index++)
                {
                    await MeasureOnceAsync(settings, _measureCts.Token);
                    if (index + 1 < count && seconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(seconds), _measureCts.Token);
                    }
                }
            }
            else
            {
                await MeasureOnceAsync(settings, _measureCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Measurement stopped.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            SetMeasuringState(false);
            _measureCts?.Dispose();
            _measureCts = null;
        }
    }

    private async Task MeasureOnceAsync(MeasurementSettings settings, CancellationToken cancellationToken)
    {
        SetStatus($"Measuring {settings.SampleCount} samples on {settings.PortName}...");
        ShowProgress("Preparing measurement...");
        var progress = new Progress<MeasurementProgress>(item =>
        {
            _progressWindow?.UpdateProgress(item.Percent, item.Message);
        });
        using var client = new Eye200WaveformClient(settings.PortName, settings.BaudRate, settings.TimeoutMilliseconds);
        var waveform = await client.AcquireAsync(
            settings.SampleCount,
            settings.DecimationInterval,
            settings.BaseSampleRateHz,
            cancellationToken,
            progress);

        _progressWindow?.UpdateProgress(100, "Calculating VRRF");
        var result = VrrfCalculator.Calculate(waveform, settings.TrendWindowSeconds);
        _currentResult = result;
        var summary = MeasurementSummary.FromResult(_summaries.Count + 1, result, DateTime.Now);
        _summaries.Add(summary);
        HistoryRows.Add(HistoryRow.FromSummary(summary));
        UpdateRangeRows(result);
        DrawChart();
        SetStatus($"Measurement complete. VRRF Ave. {summary.TrendAveragePercent:F3}%");
        CloseProgress();
    }

    private MeasurementSettings ReadSettings()
    {
        var sampleCount = ParseSampleCount();
        var trendWindow = ParseNonNegativeDouble(TrendWindowTextBox.Text, "Trend window");
        if (trendWindow < 0.100 || trendWindow > 0.500)
        {
            throw new ArgumentOutOfRangeException(nameof(trendWindow), "Trend window must be between 0.100 and 0.500 seconds.");
        }

        return new MeasurementSettings
        {
            PortName = Convert.ToString(PortComboBox.SelectedItem, CultureInfo.InvariantCulture) ?? "COM37",
            SampleCount = sampleCount,
            DecimationInterval = 1,
            TrendWindowSeconds = trendWindow,
            TimeoutMilliseconds = Math.Max(3000, (int)Math.Ceiling(sampleCount / 3000.0 * 1000.0) + 5000),
        };
    }

    private int ParseSampleCount()
    {
        if (SampleCountComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(Convert.ToString(item.Content, CultureInfo.InvariantCulture), out var sampleCount))
        {
            Eye200WaveformClient.ValidateCaptureParams(sampleCount, 1);
            return sampleCount;
        }

        throw new InvalidOperationException("Select a valid sample count.");
    }

    private void UpdateObservationFromSampleCount()
    {
        if (!IsLoaded && ObservationTextBox is null)
        {
            return;
        }

        try
        {
            var sampleCount = ParseSampleCount();
            ObservationTextBox.Text = (sampleCount / 3000.0).ToString("F3", CultureInfo.InvariantCulture);
        }
        catch
        {
            ObservationTextBox.Text = "10.923";
        }
    }

    private static int ParsePositiveInt(string text, string name)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new ArgumentException($"{name} must be a positive integer.");
        }

        return value;
    }

    private static double ParseNonNegativeDouble(string text, string name)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new ArgumentException($"{name} must be a non-negative number.");
        }

        return value;
    }

    private string SelectedMode()
    {
        return ModeComboBox.SelectedItem is ComboBoxItem item
            ? Convert.ToString(item.Content, CultureInfo.InvariantCulture) ?? "Single"
            : "Single";
    }

    private void SetMeasuringState(bool measuring)
    {
        _isMeasuring = measuring;
        MeasureButton.Content = measuring ? "Stop" : "Measure";
        PortComboBox.IsEnabled = !measuring;
        SampleCountComboBox.IsEnabled = !measuring;
        TrendWindowTextBox.IsEnabled = !measuring;
        ModeComboBox.IsEnabled = !measuring;
        IntervalCountTextBox.IsEnabled = !measuring;
        IntervalSecondsTextBox.IsEnabled = !measuring;
        if (!measuring)
        {
            CloseProgress();
        }
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void LoadSerialPorts()
    {
        PortComboBox.Items.Clear();
        var ports = SerialPort.GetPortNames()
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var port in ports)
        {
            PortComboBox.Items.Add(port);
        }

        if (ports.Contains("COM37", StringComparer.OrdinalIgnoreCase))
        {
            PortComboBox.SelectedItem = "COM37";
        }
        else if (ports.Length > 0)
        {
            PortComboBox.SelectedIndex = 0;
        }
        else
        {
            PortComboBox.Items.Add("COM37");
            PortComboBox.SelectedIndex = 0;
            SetStatus("No serial ports found. Defaulting to COM37.");
        }
    }

    private void UpdateRangeRows(VrrfResult result)
    {
        ResetRangeRows();
        var row = RangeRows[0];
        row.StartData = "0.00000";
        row.EndData = result.TimesSeconds[^1].ToString("F5", CultureInfo.InvariantCulture);
        row.FlickerIndex = $"{result.TrendAveragePercent:F3}%";
        row.TrendMax = $"{result.TrendMaxPercent:F2}%";
        row.TrendMin = $"{result.TrendMinPercent:F2}%";
        row.TrendAve = $"{result.TrendAveragePercent:F2}%";
        RangeSummaryGrid.Items.Refresh();
        RangeTrendGrid.Items.Refresh();
    }

    private void ResetRangeRows()
    {
        RangeRows.Clear();
        RangeRows.Add(new RangeRow("ALL"));
        for (var index = 1; index <= 5; index++)
        {
            RangeRows.Add(new RangeRow(index.ToString("00", CultureInfo.InvariantCulture)));
        }
    }

    private void ChartOptionChanged(object sender, RoutedEventArgs e)
    {
        DrawChart();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawChart();
    }

    private void DrawChart()
    {
        if (ChartCanvas is null)
        {
            return;
        }

        ChartCanvas.Children.Clear();
        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width < 100 || height < 100)
        {
            return;
        }

        var left = 78.0;
        var right = 88.0;
        var top = 26.0;
        var bottom = 54.0;
        var plotWidth = Math.Max(10, width - left - right);
        var plotHeight = Math.Max(10, height - top - bottom);
        var plot = new Rect(left, top, plotWidth, plotHeight);
        DrawGrid(plot);

        if (_currentResult is null)
        {
            return;
        }

        var fullMaxTime = _currentResult.TimesSeconds[^1];
        var xMin = _xMin ?? 0.0;
        var xMax = _xMax ?? fullMaxTime;
        if (xMax <= xMin)
        {
            xMin = 0.0;
            xMax = fullMaxTime;
        }

        var luminanceValues = _currentResult.OriginalData
            .Concat(_currentResult.WeightedData.Where(double.IsFinite))
            .ToArray();
        var yMin = _yMin ?? luminanceValues.Min();
        var yMax = _yMax ?? luminanceValues.Max();
        if (Math.Abs(yMax - yMin) < 1e-12)
        {
            yMax += 1;
            yMin -= 1;
        }

        var trendValues = _currentResult.TrendPercent.Where(double.IsFinite).ToArray();
        var trendMin = _trendMin ?? (trendValues.Length > 0 ? trendValues.Min() : 0.0);
        var trendMax = _trendMax ?? (trendValues.Length > 0 ? trendValues.Max() : 1.0);
        if (Math.Abs(trendMax - trendMin) < 1e-12)
        {
            trendMax += 1;
            trendMin -= 1;
        }

        DrawAxisLabels(plot, yMin, yMax, trendMin, trendMax, xMin, xMax);

        if (OriginalCheckBox.IsChecked == true)
        {
            DrawSeries(_currentResult.TimesSeconds, _currentResult.OriginalData, plot, xMin, xMax, yMin, yMax, Brushes.White, 1.0, adaptiveToWidth: true);
        }

        if (WeightedCheckBox.IsChecked == true)
        {
            DrawSeries(_currentResult.TimesSeconds, _currentResult.WeightedData, plot, xMin, xMax, yMin, yMax, new SolidColorBrush(Color.FromRgb(255, 56, 219)), 1.2, adaptiveToWidth: true);
        }

        if (TrendCheckBox.IsChecked == true)
        {
            DrawSeries(_currentResult.TimesSeconds, _currentResult.TrendPercent, plot, xMin, xMax, trendMin, trendMax, new SolidColorBrush(Color.FromRgb(38, 255, 79)), 1.2, adaptiveToWidth: true);
        }
    }

    private void DrawGrid(Rect plot)
    {
        var border = new Rectangle
        {
            Width = plot.Width,
            Height = plot.Height,
            Stroke = Brushes.White,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(border, plot.Left);
        Canvas.SetTop(border, plot.Top);
        ChartCanvas.Children.Add(border);

        for (var index = 1; index < 10; index++)
        {
            var x = plot.Left + plot.Width * index / 10.0;
            ChartCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = plot.Top,
                Y2 = plot.Bottom,
                Stroke = Brushes.White,
                StrokeThickness = 0.6,
                StrokeDashArray = [1, 3],
            });
        }

        for (var index = 1; index < 4; index++)
        {
            var y = plot.Top + plot.Height * index / 4.0;
            ChartCanvas.Children.Add(new Line
            {
                X1 = plot.Left,
                X2 = plot.Right,
                Y1 = y,
                Y2 = y,
                Stroke = Brushes.White,
                StrokeThickness = 0.6,
                StrokeDashArray = [1, 3],
            });
        }
    }

    private void DrawAxisLabels(Rect plot, double yMin, double yMax, double trendMin, double trendMax, double xMin, double xMax)
    {
        AddChartText(yMax.ToString("F5", CultureInfo.InvariantCulture), plot.Left - 66, plot.Top - 9, Brushes.White);
        AddChartText(((yMin + yMax) / 2.0).ToString("F5", CultureInfo.InvariantCulture), plot.Left - 66, plot.Top + plot.Height / 2.0 - 9, Brushes.White);
        AddChartText(yMin.ToString("F5", CultureInfo.InvariantCulture), plot.Left - 66, plot.Bottom - 9, Brushes.White);
        AddChartText(trendMax.ToString("F5", CultureInfo.InvariantCulture), plot.Right + 8, plot.Top - 9, Brushes.White);
        AddChartText(((trendMin + trendMax) / 2.0).ToString("F5", CultureInfo.InvariantCulture), plot.Right + 8, plot.Top + plot.Height / 2.0 - 9, Brushes.White);
        AddChartText(trendMin.ToString("F5", CultureInfo.InvariantCulture), plot.Right + 8, plot.Bottom - 9, Brushes.White);

        for (var index = 0; index <= 4; index++)
        {
            var x = plot.Left + plot.Width * index / 4.0;
            AddChartText((xMin + (xMax - xMin) * index / 4.0).ToString("F3", CultureInfo.InvariantCulture), x - 18, plot.Bottom + 8, Brushes.White);
        }
    }

    private void AddChartText(string text, double x, double y, Brush brush)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = 13,
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        ChartCanvas.Children.Add(block);
    }

    private void DrawSeries(
        IReadOnlyList<double> times,
        IReadOnlyList<double> values,
        Rect plot,
        double xMin,
        double xMax,
        double minValue,
        double maxValue,
        Brush stroke,
        double thickness,
        bool adaptiveToWidth)
    {
        var points = new PointCollection();
        var startIndex = LowerBound(times, xMin);
        var endIndex = Math.Min(values.Count - 1, LowerBound(times, xMax));
        if (endIndex <= startIndex)
        {
            return;
        }

        var visibleCount = endIndex - startIndex + 1;
        var maxPoints = adaptiveToWidth
            ? Math.Max(400, (int)Math.Ceiling(plot.Width * 2.0))
            : visibleCount;
        var step = Math.Max(1, (int)Math.Ceiling(visibleCount / (double)maxPoints));
        for (var index = startIndex; index <= endIndex; index += step)
        {
            var value = values[index];
            var time = times[index];
            if (!double.IsFinite(value) || time < xMin || time > xMax)
            {
                continue;
            }

            var x = plot.Left + (time - xMin) / (xMax - xMin) * plot.Width;
            var y = plot.Bottom - (value - minValue) / (maxValue - minValue) * plot.Height;
            y = Math.Clamp(y, plot.Top, plot.Bottom);
            points.Add(new Point(x, y));
        }

        if (points.Count < 2)
        {
            return;
        }

        ChartCanvas.Children.Add(new Polyline
        {
            Points = points,
            Stroke = stroke,
            StrokeThickness = thickness,
        });
    }

    private static int LowerBound(IReadOnlyList<double> values, double target)
    {
        var low = 0;
        var high = values.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (values[mid] < target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return Math.Clamp(low, 0, Math.Max(0, values.Count - 1));
    }

    private void SaveCsvButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentResult is null)
        {
            SetStatus("No measurement data to save.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"eye200_vrrf_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dialog.ShowDialog(this) == true)
        {
            CsvExporter.SavePointData(_currentResult, dialog.FileName);
            SetStatus($"Saved {dialog.FileName}");
        }
    }

    private void SaveRangeButton_Click(object sender, RoutedEventArgs e)
    {
        SaveHistorySummaryCsv();
    }

    private void ExportHistoryCsvMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SaveHistorySummaryCsv();
    }

    private void SaveHistorySummaryCsv()
    {
        if (_summaries.Count == 0)
        {
            SetStatus("No range data to save.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"eye200_vrrf_summary_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dialog.ShowDialog(this) == true)
        {
            CsvExporter.SaveSummaryData(_summaries, dialog.FileName);
            SetStatus($"Saved {dialog.FileName}");
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _currentResult = null;
        ResetZoom();
        _summaries.Clear();
        HistoryRows.Clear();
        ResetRangeRows();
        DrawChart();
        SetStatus("Cleared.");
    }

    private void ChartCanvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (_currentResult is null)
        {
            return;
        }

        var zoomIn = e.Delta > 0;
        if (_zoomMode == ZoomMode.XOnly)
        {
            ZoomX(zoomIn);
        }
        else
        {
            ZoomY(zoomIn);
        }

        DrawChart();
        e.Handled = true;
    }

    private void ZoomX(bool zoomIn)
    {
        var fullMin = 0.0;
        var fullMax = _currentResult!.TimesSeconds[^1];
        var min = _xMin ?? fullMin;
        var max = _xMax ?? fullMax;
        ZoomRange(ref min, ref max, fullMin, fullMax, zoomIn);
        _xMin = min;
        _xMax = max;
    }

    private void ZoomY(bool zoomIn)
    {
        var luminanceValues = _currentResult!.OriginalData
            .Concat(_currentResult.WeightedData.Where(double.IsFinite))
            .ToArray();
        var fullYMin = luminanceValues.Min();
        var fullYMax = luminanceValues.Max();
        var yMin = _yMin ?? fullYMin;
        var yMax = _yMax ?? fullYMax;
        ZoomRange(ref yMin, ref yMax, fullYMin, fullYMax, zoomIn);
        _yMin = yMin;
        _yMax = yMax;

        var trendValues = _currentResult.TrendPercent.Where(double.IsFinite).ToArray();
        if (trendValues.Length > 0)
        {
            var fullTrendMin = trendValues.Min();
            var fullTrendMax = trendValues.Max();
            var trendMin = _trendMin ?? fullTrendMin;
            var trendMax = _trendMax ?? fullTrendMax;
            ZoomRange(ref trendMin, ref trendMax, fullTrendMin, fullTrendMax, zoomIn);
            _trendMin = trendMin;
            _trendMax = trendMax;
        }
    }

    private static void ZoomRange(ref double min, ref double max, double fullMin, double fullMax, bool zoomIn)
    {
        var fullSpan = fullMax - fullMin;
        if (fullSpan <= 0)
        {
            return;
        }

        var center = (min + max) / 2.0;
        var span = (max - min) * (zoomIn ? 0.8 : 1.25);
        var minSpan = Math.Max(fullSpan * 0.001, 1e-9);
        span = Math.Max(span, minSpan);
        if (span >= fullSpan)
        {
            min = fullMin;
            max = fullMax;
            return;
        }

        min = center - span / 2.0;
        max = center + span / 2.0;
        if (min < fullMin)
        {
            max += fullMin - min;
            min = fullMin;
        }

        if (max > fullMax)
        {
            min -= max - fullMax;
            max = fullMax;
        }
    }

    private void ZoomXMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _zoomMode = ZoomMode.XOnly;
        UpdateZoomMenuChecks();
    }

    private void ZoomYMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _zoomMode = ZoomMode.YOnly;
        UpdateZoomMenuChecks();
    }

    private void AutoFitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ResetZoom();
        DrawChart();
    }

    private void ResetZoom()
    {
        _xMin = null;
        _xMax = null;
        _yMin = null;
        _yMax = null;
        _trendMin = null;
        _trendMax = null;
    }

    private void UpdateZoomMenuChecks()
    {
        ZoomXMenuItem.IsChecked = _zoomMode == ZoomMode.XOnly;
        ZoomYMenuItem.IsChecked = _zoomMode == ZoomMode.YOnly;
        PanXMenuItem.IsChecked = _panMode == PanMode.HorizontalOnly;
        PanYMenuItem.IsChecked = _panMode == PanMode.VerticalOnly;
    }

    private void PanXMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _panMode = PanMode.HorizontalOnly;
        UpdateZoomMenuChecks();
    }

    private void PanYMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _panMode = PanMode.VerticalOnly;
        UpdateZoomMenuChecks();
    }

    private void ChartCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_currentResult is null)
        {
            return;
        }

        _dragStart = e.GetPosition(ChartCanvas);
        _dragXMin = _xMin ?? 0.0;
        _dragXMax = _xMax ?? _currentResult.TimesSeconds[^1];
        var (fullYMin, fullYMax) = GetFullLuminanceRange();
        _dragYMin = _yMin ?? fullYMin;
        _dragYMax = _yMax ?? fullYMax;
        var (fullTrendMin, fullTrendMax) = GetFullTrendRange();
        _dragTrendMin = _trendMin ?? fullTrendMin;
        _dragTrendMax = _trendMax ?? fullTrendMax;
        ChartCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ChartCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStart = null;
        ChartCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ChartCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_currentResult is null || _dragStart is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(ChartCanvas);
        var dx = current.X - _dragStart.Value.X;
        var dy = current.Y - _dragStart.Value.Y;
        var plot = GetPlotRect();
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        if (_panMode == PanMode.HorizontalOnly)
        {
            var fullMin = 0.0;
            var fullMax = _currentResult.TimesSeconds[^1];
            var span = _dragXMax - _dragXMin;
            var delta = -dx / plot.Width * span;
            var min = _dragXMin + delta;
            var max = _dragXMax + delta;
            ClampRange(ref min, ref max, fullMin, fullMax);
            _xMin = min;
            _xMax = max;
        }
        else
        {
            var (fullYMin, fullYMax) = GetFullLuminanceRange();
            var ySpan = _dragYMax - _dragYMin;
            var yDelta = dy / plot.Height * ySpan;
            var yMin = _dragYMin + yDelta;
            var yMax = _dragYMax + yDelta;
            ClampRange(ref yMin, ref yMax, fullYMin, fullYMax);
            _yMin = yMin;
            _yMax = yMax;

            var (fullTrendMin, fullTrendMax) = GetFullTrendRange();
            var trendSpan = _dragTrendMax - _dragTrendMin;
            var trendDelta = dy / plot.Height * trendSpan;
            var trendMin = _dragTrendMin + trendDelta;
            var trendMax = _dragTrendMax + trendDelta;
            ClampRange(ref trendMin, ref trendMax, fullTrendMin, fullTrendMax);
            _trendMin = trendMin;
            _trendMax = trendMax;
        }

        DrawChart();
        e.Handled = true;
    }

    private Rect GetPlotRect()
    {
        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        var left = 78.0;
        var right = 88.0;
        var top = 26.0;
        var bottom = 54.0;
        return new Rect(left, top, Math.Max(10, width - left - right), Math.Max(10, height - top - bottom));
    }

    private (double Min, double Max) GetFullLuminanceRange()
    {
        if (_currentResult is null)
        {
            return (0.0, 1.0);
        }

        var values = _currentResult.OriginalData
            .Concat(_currentResult.WeightedData.Where(double.IsFinite))
            .ToArray();
        return (values.Min(), values.Max());
    }

    private (double Min, double Max) GetFullTrendRange()
    {
        if (_currentResult is null)
        {
            return (0.0, 1.0);
        }

        var values = _currentResult.TrendPercent.Where(double.IsFinite).ToArray();
        return values.Length == 0 ? (0.0, 1.0) : (values.Min(), values.Max());
    }

    private static void ClampRange(ref double min, ref double max, double fullMin, double fullMax)
    {
        var span = max - min;
        var fullSpan = fullMax - fullMin;
        if (fullSpan <= 0 || span >= fullSpan)
        {
            min = fullMin;
            max = fullMax;
            return;
        }

        if (min < fullMin)
        {
            max += fullMin - min;
            min = fullMin;
        }

        if (max > fullMax)
        {
            min -= max - fullMax;
            max = fullMax;
        }
    }

    private void ShowProgress(string message)
    {
        if (_progressWindow is not null)
        {
            return;
        }

        _progressWindow = new ProgressWindow
        {
            Owner = this,
        };
        _progressWindow.UpdateProgress(0, message);
        _progressWindow.Show();
    }

    private void CloseProgress()
    {
        _progressWindow?.Close();
        _progressWindow = null;
    }
}

public enum ZoomMode
{
    XOnly,
    YOnly,
}

public enum PanMode
{
    HorizontalOnly,
    VerticalOnly,
}

public sealed class RangeRow
{
    public RangeRow(string rangeNo)
    {
        RangeNo = rangeNo;
    }

    public string RangeNo { get; }
    public string StartData { get; set; } = "----";
    public string EndData { get; set; } = "----";
    public string FlickerIndex { get; set; } = "----";
    public string TrendMax { get; set; } = "----";
    public string TrendMin { get; set; } = "----";
    public string TrendAve { get; set; } = "----";
}

public sealed record HistoryRow(
    int Number,
    string SamplingRate,
    string Observation,
    string TrendWindow,
    string TrendMax,
    string TrendMin,
    string TrendAve,
    string WeightedMax,
    string WeightedMin,
    string WeightedAve,
    string Date,
    string Time)
{
    public static HistoryRow FromSummary(MeasurementSummary summary)
    {
        return new HistoryRow(
            summary.Number,
            summary.SamplingRateHz.ToString("F1", CultureInfo.InvariantCulture),
            summary.ObservationSeconds.ToString("F3", CultureInfo.InvariantCulture),
            summary.TrendWindowSeconds.ToString("F3", CultureInfo.InvariantCulture),
            $"{summary.TrendMaxPercent:F2}%",
            $"{summary.TrendMinPercent:F2}%",
            $"{summary.TrendAveragePercent:F2}%",
            summary.WeightedMax.ToString("F5", CultureInfo.InvariantCulture),
            summary.WeightedMin.ToString("F5", CultureInfo.InvariantCulture),
            summary.WeightedAverage.ToString("F5", CultureInfo.InvariantCulture),
            summary.Timestamp.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
            summary.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }
}
