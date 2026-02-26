namespace PulsePlugin.Models;

/// <summary>
/// BPM estimate with confidence and phase offset.
/// </summary>
public sealed class BpmEstimate
{
    /// <summary>Estimated BPM (0 if not yet determined).</summary>
    public double Bpm { get; init; }

    /// <summary>Confidence level 0.0–1.0.</summary>
    public double Confidence { get; init; }

    /// <summary>Phase offset in milliseconds (time of the nearest beat grid line).</summary>
    public double PhaseOffsetMs { get; init; }
}
