using PulsePlugin.Services;
using SkiaSharp;
using Vido.Haptics;
using Xunit;

namespace PulsePlugin.Tests.Services;

/// <summary>
/// Unit tests for PulseBeatSource — IExternalBeatSource implementation.
/// Verifies contract properties, rendering callbacks, and availability toggling.
/// </summary>
public class PulseBeatSourceTests
{
    private readonly PulseBeatSource _source = new();

    // ──────────────────────────────────────────
    //  Contract properties
    // ──────────────────────────────────────────

    [Fact]
    public void Id_IsPulsePluginId()
    {
        Assert.Equal("com.vido.pulse", _source.Id);
    }

    [Fact]
    public void DisplayName_IsPulse()
    {
        Assert.Equal("Pulse", _source.DisplayName);
    }

    [Fact]
    public void HidesBuiltInModes_IsTrue()
    {
        Assert.True(_source.HidesBuiltInModes);
    }

    [Fact]
    public void IsAvailable_DefaultsFalse()
    {
        Assert.False(_source.IsAvailable);
    }

    [Fact]
    public void IsAvailable_CanBeToggled()
    {
        _source.IsAvailable = true;
        Assert.True(_source.IsAvailable);

        _source.IsAvailable = false;
        Assert.False(_source.IsAvailable);
    }

    [Fact]
    public void ImplementsIExternalBeatSource()
    {
        Assert.IsAssignableFrom<IExternalBeatSource>(_source);
    }

    // ──────────────────────────────────────────
    //  RenderBeat
    // ──────────────────────────────────────────

    [Fact]
    public void RenderBeat_WithSkCanvas_DoesNotThrow()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        var canvas = surface.Canvas;

        _source.RenderBeat(canvas, 100, 100, 40, 0f);
        _source.RenderBeat(canvas, 100, 100, 40, 0.5f);
        _source.RenderBeat(canvas, 100, 100, 40, 1.0f);
    }

    [Fact]
    public void RenderBeat_WithNonSkCanvas_DoesNotThrow()
    {
        // Should silently return when given a non-SKCanvas object
        _source.RenderBeat("not a canvas", 100, 100, 40, 0);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(1.0f)]
    public void RenderBeat_AllProgressValues_DoNotThrow(float progress)
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        _source.RenderBeat(surface.Canvas, 100, 100, 40, progress);
    }

    [Fact]
    public void RenderBeat_NullCanvas_DoesNotThrow()
    {
        _source.RenderBeat(null!, 100, 100, 40, 0);
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(20f)]
    [InlineData(50f)]
    [InlineData(100f)]
    public void RenderBeat_VariousSizes_DoNotThrow(float size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        _source.RenderBeat(surface.Canvas, 100, 100, size, 0);
    }

    // ──────────────────────────────────────────
    //  RenderIndicator
    // ──────────────────────────────────────────

    [Fact]
    public void RenderIndicator_WithSkCanvas_DoesNotThrow()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        _source.RenderIndicator(surface.Canvas, 100, 100, 40);
    }

    [Fact]
    public void RenderIndicator_WithNonSkCanvas_DoesNotThrow()
    {
        _source.RenderIndicator("not a canvas", 100, 100, 40);
    }

    [Fact]
    public void RenderIndicator_NullCanvas_DoesNotThrow()
    {
        _source.RenderIndicator(null!, 100, 100, 40);
    }

    // ──────────────────────────────────────────
    //  Rendering produces visible output
    // ──────────────────────────────────────────

    [Fact]
    public void RenderBeat_AtProgress0_ProducesVisiblePixels()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        surface.Canvas.Clear(SKColors.Transparent);

        _source.RenderBeat(surface.Canvas, 100, 100, 60, 0f);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        bool hasColor = false;

        for (int y = 0; y < 200 && !hasColor; y++)
            for (int x = 0; x < 200 && !hasColor; x++)
                if (pixmap.GetPixelColor(x, y).Alpha > 0)
                    hasColor = true;

        Assert.True(hasColor, "RenderBeat should produce visible heart pixels");
    }

    [Fact]
    public void RenderBeat_HeartColor_IsRed()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        surface.Canvas.Clear(SKColors.Transparent);

        _source.RenderBeat(surface.Canvas, 100, 100, 80, 0f);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();

        // Sample the center-ish area for the dominant color
        var centerColor = pixmap.GetPixelColor(100, 90);
        Assert.True(centerColor.Red > 150, $"Expected red heart, got R={centerColor.Red}");
        Assert.True(centerColor.Green < 100, $"Expected low green, got G={centerColor.Green}");
    }

    [Fact]
    public void RenderIndicator_ProducesVisiblePixels()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        surface.Canvas.Clear(SKColors.Transparent);

        _source.RenderIndicator(surface.Canvas, 100, 100, 60);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        bool hasColor = false;

        for (int y = 0; y < 200 && !hasColor; y++)
            for (int x = 0; x < 200 && !hasColor; x++)
                if (pixmap.GetPixelColor(x, y).Alpha > 0)
                    hasColor = true;

        Assert.True(hasColor, "RenderIndicator should produce visible hollow heart pixels");
    }

    [Fact]
    public void RenderIndicator_IsHollow_CenterIsTransparent()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        surface.Canvas.Clear(SKColors.Transparent);

        _source.RenderIndicator(surface.Canvas, 100, 100, 80);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();

        // The center of a hollow (stroke-only) heart should be transparent
        var centerColor = pixmap.GetPixelColor(100, 100);
        Assert.True(centerColor.Alpha < 50,
            $"Center of hollow heart should be transparent, got alpha={centerColor.Alpha}");
    }
}
