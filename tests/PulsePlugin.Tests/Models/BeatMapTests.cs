using PulsePlugin.Models;
using Xunit;

namespace PulsePlugin.Tests.Models;

public class BeatMapTests
{
    [Fact]
    public void DefaultValues_HaveEmptyCollections()
    {
        var map = new BeatMap();

        Assert.Empty(map.Beats);
        Assert.Equal(0, map.Bpm);
        Assert.Equal(0, map.BpmConfidence);
        Assert.Equal(0, map.DurationMs);
        Assert.Empty(map.WaveformSamples);
        Assert.Equal(0, map.WaveformSampleRate);
    }

    [Fact]
    public void InitProperties_SetCorrectly()
    {
        var beats = new List<BeatEvent>
        {
            new() { TimestampMs = 500, Strength = 0.8 },
            new() { TimestampMs = 1000, Strength = 0.9 }
        };

        var waveform = new float[] { 0.1f, 0.3f, 0.5f };

        var map = new BeatMap
        {
            Beats = beats,
            Bpm = 120,
            BpmConfidence = 0.95,
            DurationMs = 60000,
            WaveformSamples = waveform,
            WaveformSampleRate = 100
        };

        Assert.Equal(2, map.Beats.Count);
        Assert.Equal(120, map.Bpm);
        Assert.Equal(0.95, map.BpmConfidence);
        Assert.Equal(60000, map.DurationMs);
        Assert.Equal(3, map.WaveformSamples.Count);
        Assert.Equal(100, map.WaveformSampleRate);
    }

    [Fact]
    public void Beats_AreReadOnly()
    {
        var beats = new List<BeatEvent>
        {
            new() { TimestampMs = 100 }
        };

        var map = new BeatMap { Beats = beats };

        // IReadOnlyList doesn't have Add
        Assert.IsAssignableFrom<IReadOnlyList<BeatEvent>>(map.Beats);
        Assert.Single(map.Beats);
    }

    [Fact]
    public void WaveformSamples_AreReadOnly()
    {
        var map = new BeatMap { WaveformSamples = new float[] { 0.5f } };
        Assert.IsAssignableFrom<IReadOnlyList<float>>(map.WaveformSamples);
    }
}
