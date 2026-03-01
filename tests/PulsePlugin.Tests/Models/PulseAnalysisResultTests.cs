using PulsePlugin.Models;
using Xunit;

namespace PulsePlugin.Tests.Models;

public class PulseAnalysisResultTests
{
    [Fact]
    public void Defaults_AreInitializedAsExpected()
    {
        var result = new PulseAnalysisResult();

        Assert.Equal(0, result.TimestampMs);
        Assert.Equal(0, result.RmsAmplitude);
        Assert.NotNull(result.WaveformSamples);
        Assert.Empty(result.WaveformSamples);
    }

    [Fact]
    public void InitProperties_AssignExpectedValues()
    {
        var waveform = new[] { 0.1f, 0.2f };

        var result = new PulseAnalysisResult
        {
            TimestampMs = 1234.5,
            RmsAmplitude = 0.75,
            WaveformSamples = waveform
        };

        Assert.Equal(1234.5, result.TimestampMs);
        Assert.Equal(0.75, result.RmsAmplitude);
        Assert.Same(waveform, result.WaveformSamples);
    }
}
