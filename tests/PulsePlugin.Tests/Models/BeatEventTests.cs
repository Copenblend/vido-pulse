using PulsePlugin.Models;
using Xunit;

namespace PulsePlugin.Tests.Models;

public class BeatEventTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var beat = new BeatEvent();

        Assert.Equal(0, beat.TimestampMs);
        Assert.Equal(0, beat.Strength);
        Assert.False(beat.IsQuantized);
    }

    [Fact]
    public void InitProperties_SetCorrectly()
    {
        var beat = new BeatEvent
        {
            TimestampMs = 1500.5,
            Strength = 0.85,
            IsQuantized = true
        };

        Assert.Equal(1500.5, beat.TimestampMs);
        Assert.Equal(0.85, beat.Strength);
        Assert.True(beat.IsQuantized);
    }

    [Fact]
    public void ZeroStrength_IsValid()
    {
        var beat = new BeatEvent { Strength = 0.0 };
        Assert.Equal(0.0, beat.Strength);
    }

    [Fact]
    public void MaxStrength_IsValid()
    {
        var beat = new BeatEvent { Strength = 1.0 };
        Assert.Equal(1.0, beat.Strength);
    }
}
