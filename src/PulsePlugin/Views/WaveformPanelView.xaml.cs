using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PulsePlugin.Models;
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
    private static readonly SKColor BeatTickColor = SKColor.Parse("#c42b1c");
    private static readonly SKColor BeatTickFaint = SKColor.Parse("#40c42b1c"); // 25% alpha
    private static readonly SKColor CursorColor = SKColors.White;
    private static readonly SKColor GridLineColor = SKColor.Parse("#2A2A2A");
    private static readonly SKColor BpmColor = SKColor.Parse("#DCDCAA");

    /// <summary>Cursor position as fraction of the canvas width (20% from left).</summary>
    private const float CursorFraction = 0.20f;

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
        CompositionTarget.Rendering += OnRendering;
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
                UpdateEmptyState();
                break;
            case nameof(WaveformViewModel.CurrentBpm):
                UpdateBpmReadout();
                break;
        }
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
        // SkiaSharp invalidation must happen on UI thread;
        // CompositionTarget.Rendering will pick it up.
    }

    // ── Render loop (~60 fps via CompositionTarget) ──

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_viewModel?.IsActive == true && IsVisible)
            _skiaCanvas?.InvalidateVisual();
    }

    // ── Paint surface ──

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(BackgroundColor);

        if (_viewModel == null || !_viewModel.IsActive)
            return;

        var waveform = _viewModel.FullWaveform;
        var beats = _viewModel.AllBeats;
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

        // Draw order: grid → waveform → beat ticks → cursor → time labels
        DrawGridLines(canvas, width, height, windowStartTime, windowEndTime);
        DrawWaveform(canvas, waveform, waveformSampleRate, totalDuration, width, height, windowStartTime, windowEndTime);
        DrawBeatTicks(canvas, beats, width, height, windowStartTime, windowEndTime);
        DrawCursor(canvas, cursorX, height);
        DrawTimeLabels(canvas, width, height, windowStartTime, windowEndTime);
    }

    // ── Drawing methods ──

    private static void DrawGridLines(SKCanvas canvas, float width, float height,
        double windowStartTime, double windowEndTime)
    {
        using var paint = new SKPaint
        {
            Color = GridLineColor,
            StrokeWidth = 1,
            IsAntialias = false
        };

        // Horizontal center line
        float midY = height / 2;
        canvas.DrawLine(0, midY, width, midY, paint);

        // Horizontal quarter lines
        paint.Color = GridLineColor.WithAlpha(80);
        canvas.DrawLine(0, height * 0.25f, width, height * 0.25f, paint);
        canvas.DrawLine(0, height * 0.75f, width, height * 0.75f, paint);
    }

    private static void DrawWaveform(SKCanvas canvas, IReadOnlyList<float> waveform,
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

        // Build path for the waveform envelope (mirrored)
        using var pathFill = new SKPath();
        using var pathLine = new SKPath();
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
                pathLine.MoveTo(x, midY - yOffset);
                pathFill.MoveTo(x, midY - yOffset);
                pathStarted = true;
            }
            else
            {
                pathLine.LineTo(x, midY - yOffset);
                pathFill.LineTo(x, midY - yOffset);
            }
        }

        // Mirror the path back for the bottom half (for fill)
        for (int i = endSample; i >= startSample; i -= step)
        {
            double sampleTime = (double)i / sampleRate;
            float x = (float)((sampleTime - windowStartTime) * pixelsPerSecond);
            float amplitude = Math.Abs(waveform[i]);
            float yOffset = amplitude * maxAmplitude;
            pathFill.LineTo(x, midY + yOffset);
        }
        pathFill.Close();

        // Draw filled waveform
        using var fillPaint = new SKPaint
        {
            Color = WaveformFill,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawPath(pathFill, fillPaint);

        // Draw waveform outline
        using var linePaint = new SKPaint
        {
            Color = WaveformColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        canvas.DrawPath(pathLine, linePaint);

        // Draw mirrored bottom outline
        using var bottomPath = new SKPath();
        bool bottomStarted = false;
        for (int i = startSample; i <= endSample; i += step)
        {
            double sampleTime = (double)i / sampleRate;
            float x = (float)((sampleTime - windowStartTime) * pixelsPerSecond);
            float amplitude = Math.Abs(waveform[i]);
            float yOffset = amplitude * maxAmplitude;

            if (!bottomStarted)
            {
                bottomPath.MoveTo(x, midY + yOffset);
                bottomStarted = true;
            }
            else
            {
                bottomPath.LineTo(x, midY + yOffset);
            }
        }
        canvas.DrawPath(bottomPath, linePaint);
    }

    private static void DrawBeatTicks(SKCanvas canvas, IReadOnlyList<BeatEvent>? beats,
        float width, float height, double windowStartTime, double windowEndTime)
    {
        if (beats == null || beats.Count == 0) return;

        double windowDuration = windowEndTime - windowStartTime;
        double windowStartMs = windowStartTime * 1000.0;
        double windowEndMs = windowEndTime * 1000.0;

        // Binary search for first visible beat
        int lo = 0, hi = beats.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (beats[mid].TimestampMs < windowStartMs)
                lo = mid + 1;
            else
                hi = mid;
        }

        using var strongPaint = new SKPaint
        {
            Color = BeatTickColor,
            StrokeWidth = 1.5f,
            IsAntialias = false
        };

        using var faintPaint = new SKPaint
        {
            Color = BeatTickFaint,
            StrokeWidth = 1f,
            IsAntialias = false
        };

        for (int i = lo; i < beats.Count; i++)
        {
            var beat = beats[i];
            if (beat.TimestampMs > windowEndMs) break;

            float x = (float)((beat.TimestampMs / 1000.0 - windowStartTime) / windowDuration * width);

            // Strong beats get full-height prominent line; weak beats get faint
            if (beat.Strength >= 0.5)
            {
                canvas.DrawLine(x, 0, x, height, strongPaint);
            }
            else
            {
                canvas.DrawLine(x, height * 0.2f, x, height * 0.8f, faintPaint);
            }
        }
    }

    private static void DrawCursor(SKCanvas canvas, float cursorX, float height)
    {
        using var paint = new SKPaint
        {
            Color = CursorColor,
            StrokeWidth = 2f,
            IsAntialias = false
        };
        canvas.DrawLine(cursorX, 0, cursorX, height, paint);

        // Small triangle marker at top
        using var trianglePaint = new SKPaint
        {
            Color = CursorColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var triangle = new SKPath();
        triangle.MoveTo(cursorX - 4, 0);
        triangle.LineTo(cursorX + 4, 0);
        triangle.LineTo(cursorX, 6);
        triangle.Close();
        canvas.DrawPath(triangle, trianglePaint);
    }

    private static void DrawTimeLabels(SKCanvas canvas, float width, float height,
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

        using var paint = new SKPaint
        {
            Color = TextSecondary,
            TextSize = 10,
            IsAntialias = true
        };

        double firstTick = Math.Ceiling(windowStartTime / tickInterval) * tickInterval;
        for (double t = firstTick; t <= windowEndTime; t += tickInterval)
        {
            float x = (float)((t - windowStartTime) / windowDuration * width);

            // Tick mark
            using var tickPaint = new SKPaint { Color = GridLineColor, StrokeWidth = 1 };
            canvas.DrawLine(x, height - 12, x, height, tickPaint);

            // Time label
            var ts = TimeSpan.FromSeconds(Math.Max(0, t));
            string label = ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}"
                : $"{ts.Seconds}s";
            float labelWidth = paint.MeasureText(label);
            canvas.DrawText(label, x - labelWidth / 2, height - 1, paint);
        }
    }
}
