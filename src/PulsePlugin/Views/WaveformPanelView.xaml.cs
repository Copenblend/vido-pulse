using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PulsePlugin.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace PulsePlugin.Views;

/// <summary>
/// Code-behind for the Pulse Waveform bottom panel.
/// Renders full-track waveform, beat tick markers, BPM readout,
/// and scrolling playback cursor using SkiaSharp.
/// </summary>
public partial class WaveformPanelView : UserControl
{
    private WaveformViewModel? _viewModel;
    private SKElement? _skiaCanvas;

    // ── Pulse theme colors ──
    private static readonly SKColor BackgroundColor = SKColor.Parse("#1E1E1E");
    private static readonly SKColor SurfaceColor = SKColor.Parse("#252526");
    private static readonly SKColor BorderColor = SKColor.Parse("#3C3C3C");
    private static readonly SKColor TextPrimary = SKColor.Parse("#CCCCCC");
    private static readonly SKColor TextSecondary = SKColor.Parse("#808080");
    private static readonly SKColor WaveformColor = SKColor.Parse("#4EC9B0");
    private static readonly SKColor WaveformFill = SKColor.Parse("#264EC9B0"); // 15% alpha
    private static readonly SKColor CursorColor = SKColors.White;
    private static readonly SKColor GridLineColor = SKColor.Parse("#2A2A2A");
    private static readonly SKColor BpmColor = SKColor.Parse("#DCDCAA");

    /// <summary>Cursor position as fraction of the canvas width (20% from left).</summary>
    private const float CursorFraction = 0.20f;

    private readonly SKPaint _gridPaint = new()
    {
        Color = GridLineColor,
        StrokeWidth = 1,
        IsAntialias = false
    };

    private readonly SKPaint _gridQuarterPaint = new()
    {
        Color = GridLineColor.WithAlpha(80),
        StrokeWidth = 1,
        IsAntialias = false
    };

    private readonly SKPaint _waveformFillPaint = new()
    {
        Color = WaveformFill,
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };

    private readonly SKPaint _waveformLinePaint = new()
    {
        Color = WaveformColor,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        IsAntialias = true
    };

    private readonly SKPaint _cursorPaint = new()
    {
        Color = CursorColor,
        StrokeWidth = 2f,
        IsAntialias = false
    };

    private readonly SKPaint _cursorTrianglePaint = new()
    {
        Color = CursorColor,
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };

    private readonly SKPaint _timeLabelPaint = new()
    {
        Color = TextSecondary,
        TextSize = 10,
        IsAntialias = true
    };

    private readonly SKPaint _timeTickPaint = new()
    {
        Color = GridLineColor,
        StrokeWidth = 1,
        IsAntialias = false
    };

    private readonly SKPath _waveformFillPath = new();
    private readonly SKPath _waveformLinePath = new();
    private readonly SKPath _waveformBottomPath = new();
    private readonly SKPath _cursorTrianglePath = new();
    private bool _isDisposed;

    public WaveformPanelView()
    {
        InitializeComponent();

        try
        {
            _skiaCanvas = new SKElement();
            _skiaCanvas.PaintSurface += OnPaintSurface;
            CanvasHost.Content = _skiaCanvas;
        }
        catch (Exception)
        {
            EmptyStateText.Text = "Waveform unavailable (SkiaSharp failed to load)";
            EmptyStateText.Visibility = Visibility.Visible;
        }

        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    // ── DataContext wiring ──

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.RepaintRequested -= OnRepaintRequested;
        }

        _viewModel = e.NewValue as WaveformViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.RepaintRequested += OnRepaintRequested;
            UpdateEmptyState();
            UpdateBpmReadout();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WaveformViewModel.IsActive):
            case nameof(WaveformViewModel.FullWaveform):
                DispatchIfNeeded(UpdateEmptyState);
                break;
            case nameof(WaveformViewModel.CurrentBpm):
                DispatchIfNeeded(UpdateBpmReadout);
                break;
        }
    }

    /// <summary>Run an action on the UI thread. If already on the UI thread, run directly.</summary>
    private void DispatchIfNeeded(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }

    private void UpdateEmptyState()
    {
        if (_viewModel == null || !_viewModel.IsActive || _viewModel.FullWaveform == null)
        {
            EmptyStateText.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyStateText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateBpmReadout()
    {
        if (_viewModel != null && _viewModel.CurrentBpm > 0)
            BpmReadout.Text = $"\u2665 {_viewModel.CurrentBpm:F0} BPM";
        else
            BpmReadout.Text = string.Empty;
    }

    private void OnRepaintRequested()
    {
        DispatchIfNeeded(() => _skiaCanvas?.InvalidateVisual());
    }

    // ── Paint surface ──

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_isDisposed)
            return;

        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(BackgroundColor);

        if (_viewModel == null || !_viewModel.IsActive)
            return;

        var waveform = _viewModel.FullWaveform;
        var currentTime = _viewModel.CurrentTimeSeconds;
        var totalDuration = _viewModel.TotalDurationSeconds;
        var windowDuration = _viewModel.WindowDurationSeconds;
        var waveformSampleRate = _viewModel.WaveformSampleRate;

        if (waveform == null || waveform.Count == 0 || totalDuration <= 0)
            return;

        float width = info.Width;
        float height = info.Height;
        float cursorX = width * CursorFraction;

        // Time window: cursor is at 20% from left
        double windowStartTime = currentTime - (windowDuration * CursorFraction);
        double windowEndTime = windowStartTime + windowDuration;

        // Draw order: grid → waveform → cursor → time labels
        DrawGridLines(canvas, width, height, windowStartTime, windowEndTime);
        DrawWaveform(canvas, waveform, waveformSampleRate, totalDuration, width, height, windowStartTime, windowEndTime);
        DrawCursor(canvas, cursorX, height);
        DrawTimeLabels(canvas, width, height, windowStartTime, windowEndTime);
    }

    // ── Drawing methods ──

    private void DrawGridLines(SKCanvas canvas, float width, float height,
        double windowStartTime, double windowEndTime)
    {
        // Horizontal center line
        float midY = height / 2;
        canvas.DrawLine(0, midY, width, midY, _gridPaint);

        // Horizontal quarter lines
        canvas.DrawLine(0, height * 0.25f, width, height * 0.25f, _gridQuarterPaint);
        canvas.DrawLine(0, height * 0.75f, width, height * 0.75f, _gridQuarterPaint);
    }

    private void DrawWaveform(SKCanvas canvas, IReadOnlyList<float> waveform,
        int sampleRate, double totalDuration, float width, float height,
        double windowStartTime, double windowEndTime)
    {
        if (sampleRate <= 0) return;

        float midY = height / 2;
        float maxAmplitude = height * 0.45f; // leave margin

        double windowDuration = windowEndTime - windowStartTime;
        double pixelsPerSecond = width / windowDuration;

        // Calculate waveform sample range for the visible window
        int startSample = Math.Max(0, (int)(windowStartTime * sampleRate));
        int endSample = Math.Min(waveform.Count - 1, (int)(windowEndTime * sampleRate));

        if (startSample >= endSample) return;

        _waveformFillPath.Reset();
        _waveformLinePath.Reset();
        _waveformBottomPath.Reset();

        bool pathStarted = false;

        // Downsample to at most ~2 points per pixel for performance
        int samplesInRange = endSample - startSample + 1;
        int step = Math.Max(1, samplesInRange / ((int)width * 2));

        for (int i = startSample; i <= endSample; i += step)
        {
            double sampleTime = (double)i / sampleRate;
            float x = (float)((sampleTime - windowStartTime) * pixelsPerSecond);
            float amplitude = Math.Abs(waveform[i]);
            float yOffset = amplitude * maxAmplitude;

            if (!pathStarted)
            {
                _waveformLinePath.MoveTo(x, midY - yOffset);
                _waveformFillPath.MoveTo(x, midY - yOffset);
                pathStarted = true;
            }
            else
            {
                _waveformLinePath.LineTo(x, midY - yOffset);
                _waveformFillPath.LineTo(x, midY - yOffset);
            }
        }

        // Mirror the path back for the bottom half (for fill)
        for (int i = endSample; i >= startSample; i -= step)
        {
            double sampleTime = (double)i / sampleRate;
            float x = (float)((sampleTime - windowStartTime) * pixelsPerSecond);
            float amplitude = Math.Abs(waveform[i]);
            float yOffset = amplitude * maxAmplitude;
            _waveformFillPath.LineTo(x, midY + yOffset);
        }
        _waveformFillPath.Close();

        // Draw mirrored bottom outline
        bool bottomStarted = false;
        for (int i = startSample; i <= endSample; i += step)
        {
            double sampleTime = (double)i / sampleRate;
            float x = (float)((sampleTime - windowStartTime) * pixelsPerSecond);
            float amplitude = Math.Abs(waveform[i]);
            float yOffset = amplitude * maxAmplitude;

            if (!bottomStarted)
            {
                _waveformBottomPath.MoveTo(x, midY + yOffset);
                bottomStarted = true;
            }
            else
            {
                _waveformBottomPath.LineTo(x, midY + yOffset);
            }
        }

        canvas.DrawPath(_waveformFillPath, _waveformFillPaint);
        canvas.DrawPath(_waveformLinePath, _waveformLinePaint);
        canvas.DrawPath(_waveformBottomPath, _waveformLinePaint);
    }

    private void DrawCursor(SKCanvas canvas, float cursorX, float height)
    {
        canvas.DrawLine(cursorX, 0, cursorX, height, _cursorPaint);

        // Small triangle marker at top
        _cursorTrianglePath.Reset();
        _cursorTrianglePath.MoveTo(cursorX - 4, 0);
        _cursorTrianglePath.LineTo(cursorX + 4, 0);
        _cursorTrianglePath.LineTo(cursorX, 6);
        _cursorTrianglePath.Close();
        canvas.DrawPath(_cursorTrianglePath, _cursorTrianglePaint);
    }

    private void DrawTimeLabels(SKCanvas canvas, float width, float height,
        double windowStartTime, double windowEndTime)
    {
        double windowDuration = windowEndTime - windowStartTime;

        // Choose tick interval based on window duration
        double tickInterval = windowDuration switch
        {
            <= 15 => 2,
            <= 45 => 5,
            <= 120 => 10,
            <= 300 => 30,
            _ => 60
        };

        double firstTick = Math.Ceiling(windowStartTime / tickInterval) * tickInterval;
        for (double t = firstTick; t <= windowEndTime; t += tickInterval)
        {
            float x = (float)((t - windowStartTime) / windowDuration * width);

            // Tick mark
            canvas.DrawLine(x, height - 12, x, height, _timeTickPaint);

            // Time label
            var ts = TimeSpan.FromSeconds(Math.Max(0, t));
            string label = ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}"
                : $"{ts.Seconds}s";
            float labelWidth = _timeLabelPaint.MeasureText(label);
            canvas.DrawText(label, x - labelWidth / 2, height - 1, _timeLabelPaint);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.RepaintRequested -= OnRepaintRequested;
            _viewModel = null;
        }

        DataContextChanged -= OnDataContextChanged;
        Unloaded -= OnUnloaded;

        if (_skiaCanvas != null)
        {
            _skiaCanvas.PaintSurface -= OnPaintSurface;
        }

        _gridPaint.Dispose();
        _gridQuarterPaint.Dispose();
        _waveformFillPaint.Dispose();
        _waveformLinePaint.Dispose();
        _cursorPaint.Dispose();
        _cursorTrianglePaint.Dispose();
        _timeLabelPaint.Dispose();
        _timeTickPaint.Dispose();
        _waveformFillPath.Dispose();
        _waveformLinePath.Dispose();
        _waveformBottomPath.Dispose();
        _cursorTrianglePath.Dispose();
    }
}
