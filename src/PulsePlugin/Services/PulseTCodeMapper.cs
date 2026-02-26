using PulsePlugin.Models;

namespace PulsePlugin.Services;

/// <summary>
/// Maps pre-analyzed beats + live amplitude to L0 axis positions (0–100 scale).
/// Hybrid mode only: beats set timing, amplitude sets intensity.
/// Applies exponential smoothing to prevent jerky jumps when the beat divisor changes.
/// </summary>
internal sealed class PulseTCodeMapper
{
    /// <summary>Minimum position (bottom of stroke). 0–100 scale.</summary>
    private const double MinPosition = 5.0;

    /// <summary>Maximum position (top of stroke). 0–100 scale.</summary>
    private const double MaxPosition = 95.0;

    /// <summary>Resting position when no beats are nearby.</summary>
    private const double RestPosition = 50.0;

    /// <summary>
    /// Fraction of the inter-beat interval used for the upstroke.
    /// The rest is the downstroke (return).
    /// </summary>
    private const double UpstrokeFraction = 0.4;

    /// <summary>Minimum amplitude scaling — even at zero amplitude, still move a bit.</summary>
    private const double MinAmplitudeScale = 0.15;

    /// <summary>
    /// Smoothing time constant in milliseconds. Controls how quickly the output
    /// converges to the raw target after a beat-grid change. Lower = more responsive,
    /// higher = smoother transitions. 40 ms ≈ 2–3 ticks at 60 Hz.
    /// </summary>
    private const double SmoothingTimeConstantMs = 40.0;

    // ── Smoothing state ──
    private double _lastPosition = RestPosition;
    private double _lastTimeMs = -1;

    /// <summary>
    /// Given the current playback position, pre-analyzed BeatMap, and live amplitude,
    /// return the desired L0 axis position on a 0–100 scale.
    /// </summary>
    /// <param name="beatMap">Pre-analyzed beat map. Null or empty beats returns rest position.</param>
    /// <param name="currentTimeMs">Current playback position in milliseconds.</param>
    /// <param name="currentAmplitude">Live RMS amplitude (0.0–1.0) from LiveAmplitudeService.</param>
    /// <returns>L0 axis position (0–100).</returns>
    public double MapToPosition(BeatMap? beatMap, double currentTimeMs, double currentAmplitude)
    {
        double rawPosition = ComputeRawPosition(beatMap, currentTimeMs, currentAmplitude);

        // Apply exponential smoothing to prevent jerky jumps when the beat
        // divisor changes (the beat grid shifts and the raw target can jump
        // to a very different phase position).
        if (_lastTimeMs < 0)
        {
            // First call — no smoothing, just adopt the position.
            _lastPosition = rawPosition;
            _lastTimeMs = currentTimeMs;
            return rawPosition;
        }

        double deltaMs = currentTimeMs - _lastTimeMs;
        _lastTimeMs = currentTimeMs;

        if (deltaMs <= 0)
            return _lastPosition;

        double alpha = 1.0 - Math.Exp(-deltaMs / SmoothingTimeConstantMs);
        double smoothed = _lastPosition + alpha * (rawPosition - _lastPosition);
        _lastPosition = smoothed;

        return Math.Clamp(smoothed, MinPosition, MaxPosition);
    }

    /// <summary>
    /// Computes the raw (unsmoothed) L0 position from the beat map and amplitude.
    /// </summary>
    private double ComputeRawPosition(BeatMap? beatMap, double currentTimeMs, double currentAmplitude)
    {
        if (beatMap is null || beatMap.Beats.Count == 0)
            return RestPosition;

        // Clamp amplitude.
        double amplitude = Math.Clamp(currentAmplitude, 0.0, 1.0);

        // Find the surrounding beats.
        int beatIndex = FindCurrentBeatIndex(beatMap.Beats, currentTimeMs);

        if (beatIndex < 0)
        {
            // Before the first beat — resting.
            return RestPosition;
        }

        var currentBeat = beatMap.Beats[beatIndex];
        double beatTimeMs = currentBeat.TimestampMs;
        double beatStrength = currentBeat.Strength;

        // Determine inter-beat interval.
        double intervalMs;
        if (beatIndex + 1 < beatMap.Beats.Count)
        {
            intervalMs = beatMap.Beats[beatIndex + 1].TimestampMs - beatTimeMs;
        }
        else if (beatMap.Bpm > 0)
        {
            // Last beat — use BPM to estimate interval.
            intervalMs = 60000.0 / beatMap.Bpm;
        }
        else
        {
            // No BPM info — use a default 500ms (120 BPM).
            intervalMs = 500.0;
        }

        // Ensure interval is sane.
        intervalMs = Math.Max(intervalMs, 50.0);

        // Time since the current beat.
        double elapsed = currentTimeMs - beatTimeMs;

        // If we've passed beyond the current beat's interval, rest.
        if (elapsed > intervalMs)
            return RestPosition;

        // Phase within the beat cycle (0.0 – 1.0).
        double phase = elapsed / intervalMs;

        // Compute stroke intensity from amplitude and beat strength.
        double amplitudeScale = MinAmplitudeScale + (1.0 - MinAmplitudeScale) * amplitude;
        double intensityScale = amplitudeScale * (0.5 + 0.5 * beatStrength);

        // Full stroke range scaled by intensity.
        double halfRange = (MaxPosition - MinPosition) / 2.0 * intensityScale;
        double top = RestPosition + halfRange;
        double bottom = RestPosition - halfRange;

        // Clamp to valid range.
        top = Math.Min(top, MaxPosition);
        bottom = Math.Max(bottom, MinPosition);

        // Stroke waveform: up during first fraction, down during remainder.
        double position;
        if (phase < UpstrokeFraction)
        {
            // Upstroke: bottom → top using smooth ease-out.
            double t = phase / UpstrokeFraction;
            double eased = EaseOutQuad(t);
            position = bottom + (top - bottom) * eased;
        }
        else
        {
            // Downstroke: top → bottom using smooth ease-in.
            double t = (phase - UpstrokeFraction) / (1.0 - UpstrokeFraction);
            double eased = EaseInQuad(t);
            position = top + (bottom - top) * eased;
        }

        return Math.Clamp(position, MinPosition, MaxPosition);
    }

    /// <summary>Reset internal tracking state (e.g., on seek, media change, or divisor change).</summary>
    public void Reset()
    {
        _lastPosition = RestPosition;
        _lastTimeMs = -1;
    }

    /// <summary>
    /// Find the index of the most recent beat at or before <paramref name="timeMs"/>.
    /// Uses binary search on the sorted beat list.
    /// Returns -1 if timeMs is before all beats.
    /// </summary>
    internal static int FindCurrentBeatIndex(IReadOnlyList<BeatEvent> beats, double timeMs)
    {
        if (beats.Count == 0) return -1;

        int lo = 0, hi = beats.Count - 1;
        int result = -1;

        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (beats[mid].TimestampMs <= timeMs)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    /// <summary>Quadratic ease-out: fast start, gradual stop.</summary>
    private static double EaseOutQuad(double t) => 1.0 - (1.0 - t) * (1.0 - t);

    /// <summary>Quadratic ease-in: gradual start, fast end.</summary>
    private static double EaseInQuad(double t) => t * t;
}
