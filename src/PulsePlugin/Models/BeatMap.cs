namespace PulsePlugin.Models;

/// <summary>
/// Complete pre-analyzed beat map for a media file.
/// </summary>
public sealed class BeatMap
{
    /// <summary>Ordered list of detected beat timestamps in milliseconds.</summary>
    public IReadOnlyList<BeatEvent> Beats { get; init; } = Array.Empty<BeatEvent>();

    /// <summary>Estimated global BPM for the track.</summary>
    public double Bpm { get; init; }

    /// <summary>BPM confidence level 0.0–1.0.</summary>
    public double BpmConfidence { get; init; }

    /// <summary>Duration of the analyzed audio in milliseconds.</summary>
    public double DurationMs { get; init; }

    /// <summary>Pre-computed downsampled waveform for the full track (for waveform panel).</summary>
    public IReadOnlyList<float> WaveformSamples { get; init; } = Array.Empty<float>();

    /// <summary>Sample rate of the waveform samples (samples per second for display).</summary>
    public int WaveformSampleRate { get; init; }
}
