namespace PulsePlugin.Models;

/// <summary>
/// A single detected beat with timing and strength.
/// </summary>
public sealed class BeatEvent
{
    /// <summary>Media timestamp of the beat in milliseconds.</summary>
    public double TimestampMs { get; init; }

    /// <summary>Beat strength/confidence 0.0–1.0.</summary>
    public double Strength { get; init; }

    /// <summary>Whether this beat was quantized to the BPM grid.</summary>
    public bool IsQuantized { get; init; }
}
