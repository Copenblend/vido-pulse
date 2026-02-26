namespace PulsePlugin.Models;

/// <summary>
/// Result of one analysis frame during live playback (~20ms of audio).
/// Used for real-time amplitude tracking and waveform display updates.
/// </summary>
public sealed class PulseAnalysisResult
{
    /// <summary>Media timestamp in milliseconds.</summary>
    public double TimestampMs { get; init; }

    /// <summary>RMS amplitude 0.0–1.0 normalized.</summary>
    public double RmsAmplitude { get; init; }

    /// <summary>Downsampled waveform samples for live display.</summary>
    public float[] WaveformSamples { get; init; } = Array.Empty<float>();
}
