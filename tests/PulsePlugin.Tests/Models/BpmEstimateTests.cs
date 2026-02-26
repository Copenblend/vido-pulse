using PulsePlugin.Models;
using Xunit;

namespace PulsePlugin.Tests.Models;

public class BpmEstimateTests
{
    [Fact]
    public void DefaultValues_AreZero()
    {
        var estimate = new BpmEstimate();

        Assert.Equal(0, estimate.Bpm);
        Assert.Equal(0, estimate.Confidence);
        Assert.Equal(0, estimate.PhaseOffsetMs);
    }

    [Fact]
    public void InitProperties_SetCorrectly()
    {
        var estimate = new BpmEstimate
        {
            Bpm = 128,
            Confidence = 0.92,
            PhaseOffsetMs = 15.5
        };

        Assert.Equal(128, estimate.Bpm);
        Assert.Equal(0.92, estimate.Confidence);
        Assert.Equal(15.5, estimate.PhaseOffsetMs);
    }

    [Fact]
    public void ZeroBpm_IndicatesNotYetDetermined()
    {
        var estimate = new BpmEstimate { Bpm = 0, Confidence = 0 };
        Assert.Equal(0, estimate.Bpm);
    }
}
