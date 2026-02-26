using PulsePlugin.Models;

namespace PulsePlugin.Services;

/// <summary>
/// Estimates BPM from detected beat intervals using autocorrelation with phase locking.
/// BPM range is hardcoded 50–180. Phase lock is core to v1.0.
/// </summary>
internal sealed class BpmEstimator
{
    private readonly double _minBpm;
    private readonly double _maxBpm;

    /// <summary>Maximum number of beat intervals to retain for analysis.</summary>
    private const int MaxIntervals = 32;

    /// <summary>Phase lock tolerance window in ms (±).</summary>
    private const double PhaseLockToleranceMs = 50.0;

    /// <summary>Minimum confidence to engage phase locking.</summary>
    private const double PhaseLockConfidenceThreshold = 0.6;

    /// <summary>Exponential smoothing factor for BPM updates (0–1, higher = faster adapt).</summary>
    private const double SmoothingAlpha = 0.15;

    /// <summary>Number of autocorrelation candidate bins.</summary>
    private const int NumCandidateBins = 130; // covers 50–180 in 1-BPM steps

    // Circular buffer of beat timestamps.
    private readonly double[] _beatTimestamps;
    private int _beatCount;
    private int _beatWriteIndex;

    // Circular buffer of inter-beat intervals in ms.
    private readonly double[] _intervals;
    private int _intervalCount;
    private int _intervalWriteIndex;

    // Current state.
    private double _currentBpm;
    private double _currentConfidence;
    private double _phaseOffsetMs;
    private double _lastBeatTimestampMs = double.NegativeInfinity;

    public BpmEstimator(double minBpm = 50, double maxBpm = 180)
    {
        if (minBpm <= 0)
            throw new ArgumentException("minBpm must be positive.", nameof(minBpm));
        if (maxBpm <= minBpm)
            throw new ArgumentException("maxBpm must be greater than minBpm.", nameof(maxBpm));

        _minBpm = minBpm;
        _maxBpm = maxBpm;

        _beatTimestamps = new double[MaxIntervals + 1];
        _intervals = new double[MaxIntervals];
    }

    /// <summary>Current BPM estimate.</summary>
    public BpmEstimate CurrentEstimate => new()
    {
        Bpm = _currentBpm,
        Confidence = _currentConfidence,
        PhaseOffsetMs = _phaseOffsetMs
    };

    /// <summary>
    /// Feed a detected beat event. Updates BPM estimate incrementally.
    /// Beats should be fed in chronological order.
    /// </summary>
    public void AddBeat(BeatEvent beat)
    {
        ArgumentNullException.ThrowIfNull(beat);

        double timestamp = beat.TimestampMs;

        // Record timestamp.
        _beatTimestamps[_beatWriteIndex] = timestamp;
        _beatWriteIndex = (_beatWriteIndex + 1) % _beatTimestamps.Length;
        if (_beatCount < _beatTimestamps.Length)
            _beatCount++;

        // Calculate interval from previous beat.
        if (_lastBeatTimestampMs > double.NegativeInfinity)
        {
            double interval = timestamp - _lastBeatTimestampMs;
            if (interval > 0)
            {
                _intervals[_intervalWriteIndex] = interval;
                _intervalWriteIndex = (_intervalWriteIndex + 1) % MaxIntervals;
                if (_intervalCount < MaxIntervals)
                    _intervalCount++;
            }
        }

        _lastBeatTimestampMs = timestamp;

        // Need at least 2 intervals to estimate BPM.
        if (_intervalCount < 2)
            return;

        // Estimate BPM via weighted interval clustering.
        var (bpm, confidence) = EstimateBpmFromIntervals();

        if (bpm > 0)
        {
            if (_currentBpm > 0)
            {
                // Exponential smoothing for stability.
                _currentBpm = _currentBpm + SmoothingAlpha * (bpm - _currentBpm);
            }
            else
            {
                _currentBpm = bpm;
            }

            _currentConfidence = confidence;

            // Update phase offset: the time of the most recent beat modulo the beat period.
            double beatPeriodMs = 60000.0 / _currentBpm;
            _phaseOffsetMs = timestamp % beatPeriodMs;
        }
    }

    /// <summary>
    /// Quantize a raw beat time to the nearest BPM grid position.
    /// Returns the original timestamp if BPM confidence is too low.
    /// </summary>
    public double QuantizeBeat(double rawTimestampMs)
    {
        if (_currentBpm <= 0 || _currentConfidence < PhaseLockConfidenceThreshold)
            return rawTimestampMs;

        double beatPeriodMs = 60000.0 / _currentBpm;

        // Find the nearest grid line.
        // Grid lines are at: phaseOffset + n * beatPeriod.
        double offsetFromPhase = rawTimestampMs - _phaseOffsetMs;
        double nearestN = Math.Round(offsetFromPhase / beatPeriodMs);
        double nearestGridMs = _phaseOffsetMs + nearestN * beatPeriodMs;

        // Only snap if within tolerance.
        if (Math.Abs(rawTimestampMs - nearestGridMs) <= PhaseLockToleranceMs)
            return nearestGridMs;

        return rawTimestampMs;
    }

    /// <summary>Reset all internal state.</summary>
    public void Reset()
    {
        _beatCount = 0;
        _beatWriteIndex = 0;
        _intervalCount = 0;
        _intervalWriteIndex = 0;
        _currentBpm = 0;
        _currentConfidence = 0;
        _phaseOffsetMs = 0;
        _lastBeatTimestampMs = double.NegativeInfinity;
        Array.Clear(_beatTimestamps);
        Array.Clear(_intervals);
    }

    /// <summary>
    /// Estimate BPM from stored intervals using weighted histogram clustering.
    /// Returns (bpm, confidence) where confidence is 0–1.
    /// </summary>
    private (double Bpm, double Confidence) EstimateBpmFromIntervals()
    {
        // Convert BPM range to interval range in ms.
        double maxIntervalMs = 60000.0 / _minBpm; // slow BPM → long interval
        double minIntervalMs = 60000.0 / _maxBpm; // fast BPM → short interval

        // Build a weighted histogram of candidate BPMs.
        // Each stored interval votes for its corresponding BPM, weighted by recency.
        var bpmVotes = new double[NumCandidateBins];
        double totalWeight = 0;

        for (int i = 0; i < _intervalCount; i++)
        {
            // Read from circular buffer — most recent has highest index relative to write pos.
            int idx = ((_intervalWriteIndex - _intervalCount + i) % MaxIntervals + MaxIntervals) % MaxIntervals;
            double interval = _intervals[idx];

            if (interval <= 0) continue;

            // Recency weight: more recent intervals weigh more.
            double recencyWeight = (double)(i + 1) / _intervalCount;

            // The interval itself, plus sub-harmonics (interval/2, interval/3) and
            // super-harmonics (interval*2) to handle double-time/half-time detection.
            VoteForInterval(interval, recencyWeight, bpmVotes, minIntervalMs, maxIntervalMs);
            VoteForInterval(interval * 2.0, recencyWeight * 0.5, bpmVotes, minIntervalMs, maxIntervalMs);
            VoteForInterval(interval / 2.0, recencyWeight * 0.5, bpmVotes, minIntervalMs, maxIntervalMs);
            VoteForInterval(interval / 3.0, recencyWeight * 0.2, bpmVotes, minIntervalMs, maxIntervalMs);

            totalWeight += recencyWeight;
        }

        if (totalWeight <= 0)
            return (0, 0);

        // Find the peak bin.
        int peakBin = 0;
        double peakVote = 0;
        for (int i = 0; i < NumCandidateBins; i++)
        {
            if (bpmVotes[i] > peakVote)
            {
                peakVote = bpmVotes[i];
                peakBin = i;
            }
        }

        // Refine BPM using parabolic interpolation around the peak.
        double refinedBpm = BinToBpm(peakBin);
        if (peakBin > 0 && peakBin < NumCandidateBins - 1)
        {
            double left = bpmVotes[peakBin - 1];
            double center = bpmVotes[peakBin];
            double right = bpmVotes[peakBin + 1];
            double denom = 2.0 * (2.0 * center - left - right);
            if (Math.Abs(denom) > 1e-10)
            {
                double shift = (left - right) / denom;
                refinedBpm = BinToBpm(peakBin + shift);
            }
        }

        // Confidence: ratio of peak vote to total weight, clamped to 0–1.
        // Also factor in consistency — sum of votes in the ±2 bin neighborhood.
        double neighborhoodVote = 0;
        for (int i = Math.Max(0, peakBin - 2); i <= Math.Min(NumCandidateBins - 1, peakBin + 2); i++)
            neighborhoodVote += bpmVotes[i];

        double confidence = Math.Min(1.0, neighborhoodVote / (totalWeight * 1.5));

        // Boost confidence as we accumulate more intervals.
        double intervalFactor = Math.Min(1.0, _intervalCount / 8.0);
        confidence *= intervalFactor;

        return (refinedBpm, confidence);
    }

    /// <summary>Cast a vote for a given interval duration into the BPM histogram.</summary>
    private void VoteForInterval(double intervalMs, double weight,
        double[] bpmVotes, double minIntervalMs, double maxIntervalMs)
    {
        if (intervalMs < minIntervalMs || intervalMs > maxIntervalMs)
            return;

        double bpm = 60000.0 / intervalMs;
        double bin = BpmToBin(bpm);
        int binIdx = (int)Math.Round(bin);

        if (binIdx >= 0 && binIdx < NumCandidateBins)
        {
            // Spread vote with Gaussian kernel to adjacent bins (σ ≈ 1 bin).
            for (int offset = -2; offset <= 2; offset++)
            {
                int target = binIdx + offset;
                if (target >= 0 && target < NumCandidateBins)
                {
                    double gaussian = Math.Exp(-0.5 * offset * offset);
                    bpmVotes[target] += weight * gaussian;
                }
            }
        }
    }

    /// <summary>Convert a BPM value to a histogram bin index.</summary>
    private double BpmToBin(double bpm) => (bpm - _minBpm) / (_maxBpm - _minBpm) * (NumCandidateBins - 1);

    /// <summary>Convert a histogram bin index to BPM.</summary>
    private double BinToBpm(double bin) => _minBpm + bin / (NumCandidateBins - 1) * (_maxBpm - _minBpm);
}
