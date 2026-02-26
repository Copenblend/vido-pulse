using SkiaSharp;
using Vido.Haptics;

namespace PulsePlugin.Services;

/// <summary>
/// Implements <see cref="IExternalBeatSource"/> for the BeatBar overlay.
/// Renders solid red hearts for beats and a hollow heart indicator.
/// Registered/unregistered via <see cref="ExternalBeatSourceRegistration"/> on the event bus.
/// </summary>
internal sealed class PulseBeatSource : IExternalBeatSource
{
    private static readonly SKColor HeartColor = new(196, 43, 28); // #c42b1c

    /// <inheritdoc />
    public string Id => "com.vido.pulse";

    /// <inheritdoc />
    public string DisplayName => "Pulse";

    /// <inheritdoc />
    public bool IsAvailable { get; internal set; }

    /// <inheritdoc />
    public bool HidesBuiltInModes => true;

    /// <inheritdoc />
    public void RenderBeat(object canvas, float centerX, float centerY, float size, float progress)
    {
        if (canvas is not SKCanvas skCanvas) return;

        byte alpha = (byte)(255 * Math.Clamp(1.0f - 0.5f * progress, 0f, 1f));
        float scale = 1.0f + 0.15f * progress;

        using var paint = new SKPaint
        {
            Color = HeartColor.WithAlpha(alpha),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        DrawHeart(skCanvas, centerX, centerY, size * scale, paint);
    }

    /// <inheritdoc />
    public void RenderIndicator(object canvas, float centerX, float centerY, float size)
    {
        if (canvas is not SKCanvas skCanvas) return;

        using var paint = new SKPaint
        {
            Color = HeartColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        DrawHeart(skCanvas, centerX, centerY, size, paint);
    }

    /// <summary>Draw a heart shape centered at (cx, cy) with the given size.</summary>
    private static void DrawHeart(SKCanvas canvas, float cx, float cy, float size, SKPaint paint)
    {
        float r = size / 2f;

        using var path = new SKPath();

        // Bottom point of heart.
        path.MoveTo(cx, cy + r * 0.7f);

        // Left curve: bottom → top-left → center-top.
        path.CubicTo(
            cx - r * 1.0f, cy + r * 0.1f,
            cx - r * 0.8f, cy - r * 0.7f,
            cx, cy - r * 0.3f);

        // Right curve: center-top → top-right → bottom.
        path.CubicTo(
            cx + r * 0.8f, cy - r * 0.7f,
            cx + r * 1.0f, cy + r * 0.1f,
            cx, cy + r * 0.7f);

        path.Close();
        canvas.DrawPath(path, paint);
    }
}
