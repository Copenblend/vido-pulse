using SkiaSharp;
using Vido.Haptics;

namespace PulsePlugin.Services;

/// <summary>
/// Implements <see cref="IExternalBeatSource"/> for the BeatBar overlay.
/// Renders red hearts with white stroke for beats and indicator.
/// On hit: hearts and indicator scale up dramatically with a red glow pulse.
/// </summary>
internal sealed class PulseBeatSource : IExternalBeatSource
{
    // ── Colors ──
    private static readonly SKColor HeartFill = new(196, 43, 28);       // #c42b1c — Pulse red
    private static readonly SKColor HeartStroke = new(255, 255, 255);   // white outline
    private static readonly SKColor GlowColor = new(196, 43, 28);      // red glow on hit

    // ── Scale / glow tuning ──
    /// <summary>Scale multiplier at full hit: 1 + 2.0 = 3× (matches built-in BeatBar).</summary>
    private const float ScaleGrowth = 2.0f;
    /// <summary>Glow blur sigma for the halo effect.</summary>
    private const float GlowBlurSigma = 10f;
    /// <summary>Glow ring stroke width.</summary>
    private const float GlowStrokeWidth = 8f;
    /// <summary>Base size multiplier — hearts are drawn this many times larger than the default.</summary>
    private const float BaseSizeMultiplier = 2.0f;

    // ── Pre-allocated paints (reused every frame) ──
    private readonly SKPaint _fillPaint = new()
    {
        Color = HeartFill,
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private readonly SKPaint _strokePaint = new()
    {
        Color = HeartStroke,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        StrokeJoin = SKStrokeJoin.Round
    };

    private readonly SKPaint _indicatorStrokePaint = new()
    {
        Color = HeartStroke,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2.5f,
        StrokeJoin = SKStrokeJoin.Round
    };

    private readonly SKPaint _glowPaint = new()
    {
        Color = GlowColor,
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    /// <inheritdoc />
    public string Id => "com.vido.pulse";

    /// <inheritdoc />
    public string DisplayName => "Pulse";

    /// <inheritdoc />
    public bool IsAvailable { get; internal set; }

    /// <inheritdoc />
    public bool HidesBuiltInModes => true;

    /// <summary>
    /// Tracks the highest beat progress seen in this frame so the indicator
    /// can scale/glow in sync. BeatBarOverlay calls RenderBeat for all beats
    /// first, then RenderIndicator — so this captures the peak hit intensity.
    /// </summary>
    private float _frameMaxProgress;

    /// <inheritdoc />
    public void RenderBeat(object canvas, float centerX, float centerY, float size, float progress)
    {
        if (canvas is not SKCanvas skCanvas) return;

        // Track peak progress for indicator animation
        if (progress > _frameMaxProgress)
            _frameMaxProgress = progress;

        // Scale: 2× base, then 1× idle → 3× at hit (progress = 1.0)
        float baseSize = size * BaseSizeMultiplier;
        float scale = 1.0f + ScaleGrowth * progress;
        float scaledSize = baseSize * scale;

        // Glow halo on hit
        if (progress > 0.05f)
        {
            DrawHeartGlow(skCanvas, centerX, centerY, scaledSize, progress);
        }

        // Filled heart (no stroke)
        _fillPaint.Color = HeartFill;
        DrawHeart(skCanvas, centerX, centerY, scaledSize, _fillPaint);
    }

    /// <inheritdoc />
    public void RenderIndicator(object canvas, float centerX, float centerY, float size)
    {
        if (canvas is not SKCanvas skCanvas) return;

        float progress = _frameMaxProgress;
        float baseSize = size * BaseSizeMultiplier;
        float scale = 1.0f + ScaleGrowth * progress;
        float scaledSize = baseSize * scale;

        // Glow halo on the indicator during hit
        if (progress > 0.05f)
        {
            DrawHeartGlow(skCanvas, centerX, centerY, scaledSize * 1.15f, progress);
        }

        // White-stroked hollow heart — scales with hit
        _indicatorStrokePaint.StrokeWidth = 2.5f + progress * 1.5f;
        DrawHeart(skCanvas, centerX, centerY, scaledSize, _indicatorStrokePaint);

        // Reset for next frame
        _frameMaxProgress = 0f;
    }

    // ── Drawing helpers ──

    /// <summary>Draw a glow halo behind a heart — blurred red fill with decaying alpha.</summary>
    private void DrawHeartGlow(SKCanvas canvas, float cx, float cy, float size, float intensity)
    {
        byte glowAlpha = (byte)(200 * intensity);
        _glowPaint.Color = GlowColor.WithAlpha(glowAlpha);

        var filter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, GlowBlurSigma);
        _glowPaint.MaskFilter = filter;

        // Draw slightly oversized heart as glow
        DrawHeart(canvas, cx, cy, size * 1.25f, _glowPaint);

        filter.Dispose();
        _glowPaint.MaskFilter = null;
    }

    /// <summary>Draw a heart shape centered at (cx, cy) with the given size (diameter).</summary>
    private static void DrawHeart(SKCanvas canvas, float cx, float cy, float size, SKPaint paint)
    {
        float r = size / 2f;

        using var path = new SKPath();

        // Bottom point of heart
        path.MoveTo(cx, cy + r * 0.7f);

        // Left curve: bottom → top-left → center-top
        path.CubicTo(
            cx - r * 1.0f, cy + r * 0.1f,
            cx - r * 0.8f, cy - r * 0.7f,
            cx, cy - r * 0.3f);

        // Right curve: center-top → top-right → bottom
        path.CubicTo(
            cx + r * 0.8f, cy - r * 0.7f,
            cx + r * 1.0f, cy + r * 0.1f,
            cx, cy + r * 0.7f);

        path.Close();
        canvas.DrawPath(path, paint);
    }
}
