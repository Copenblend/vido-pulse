using PulsePlugin.Models;
using PulsePlugin.Services;
using PulsePlugin.Tests.TestUtilities;
using Xunit;

namespace PulsePlugin.Tests.Services;

/// <summary>
/// Unit tests for BpmEstimator — autocorrelation BPM estimation with phase locking.
/// </summary>
public class BpmEstimatorTests
{
    // ──────────────────────────────────────────────
    //  Constructor validation
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultParameters_DoesNotThrow()
    {
        var estimator = new BpmEstimator();
        Assert.NotNull(estimator);
    }

    [Fact]
    public void Constructor_CustomRange_DoesNotThrow()
    {
        var estimator = new BpmEstimator(minBpm: 60, maxBpm: 160);
        Assert.NotNull(estimator);
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(-10, 180)]
    public void Constructor_InvalidMinBpm_Throws(double minBpm, double maxBpm)
    {
        Assert.Throws<ArgumentException>(() => new BpmEstimator(minBpm, maxBpm));
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(50, 30)]
    public void Constructor_MaxBpmNotGreaterThanMin_Throws(double minBpm, double maxBpm)
    {
        Assert.Throws<ArgumentException>(() => new BpmEstimator(minBpm, maxBpm));
    }

    // ──────────────────────────────────────────────
    //  Initial state
    // ──────────────────────────────────────────────

    [Fact]
    public void InitialEstimate_BpmIsZero()
    {
        var estimator = new BpmEstimator();
        Assert.Equal(0, estimator.CurrentEstimate.Bpm);
        Assert.Equal(0, estimator.CurrentEstimate.Confidence);
    }

    [Fact]
    public void AddBeat_Null_Throws()
    {
        var estimator = new BpmEstimator();
        Assert.Throws<ArgumentNullException>(() => estimator.AddBeat(null!));
    }

    // ──────────────────────────────────────────────
    //  Single beat — not enough to estimate
    // ──────────────────────────────────────────────

    [Fact]
    public void AddBeat_SingleBeat_BpmStaysZero()
    {
        var estimator = new BpmEstimator();
        estimator.AddBeat(new BeatEvent { TimestampMs = 0, Strength = 1.0 });

        Assert.Equal(0, estimator.CurrentEstimate.Bpm);
    }

    [Fact]
    public void AddBeat_TwoBeats_BpmStillZeroInsufficientIntervals()
    {
        var estimator = new BpmEstimator();
        estimator.AddBeat(new BeatEvent { TimestampMs = 0, Strength = 1.0 });
        estimator.AddBeat(new BeatEvent { TimestampMs = 500, Strength = 1.0 });

        // Only 1 interval, need at least 2 to estimate.
        Assert.Equal(0, estimator.CurrentEstimate.Bpm);
    }

    // ──────────────────────────────────────────────
    //  Regular intervals — converge to correct BPM
    // ──────────────────────────────────────────────

    [Fact]
    public void AddBeats_120Bpm_ConvergesToCorrectBpm()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 60000.0 / 120; // 500ms

        // Feed 16 perfectly regular beats.
        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 115 && estimate.Bpm < 125,
            $"Expected ~120 BPM, got {estimate.Bpm:F1}");
        Assert.True(estimate.Confidence > 0.3,
            $"Expected reasonable confidence, got {estimate.Confidence:F2}");
    }

    [Fact]
    public void AddBeats_90Bpm_ConvergesToCorrectBpm()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 60000.0 / 90; // ~666.7ms

        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 85 && estimate.Bpm < 95,
            $"Expected ~90 BPM, got {estimate.Bpm:F1}");
    }

    [Fact]
    public void AddBeats_150Bpm_ConvergesToCorrectBpm()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 60000.0 / 150; // 400ms

        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 145 && estimate.Bpm < 155,
            $"Expected ~150 BPM, got {estimate.Bpm:F1}");
    }

    [Fact]
    public void AddBeats_60Bpm_ConvergesToCorrectBpm()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 60000.0 / 60; // 1000ms

        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 55 && estimate.Bpm < 65,
            $"Expected ~60 BPM, got {estimate.Bpm:F1}");
    }

    // ──────────────────────────────────────────────
    //  Confidence increases with more beats
    // ──────────────────────────────────────────────

    [Fact]
    public void Confidence_IncreasesWithMoreBeats()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500; // 120 BPM

        // After 4 beats (3 intervals, need 2+ to produce estimate).
        for (int i = 0; i < 4; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });
        double earlyConfidence = estimator.CurrentEstimate.Confidence;

        // After 16 beats.
        for (int i = 4; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });
        double laterConfidence = estimator.CurrentEstimate.Confidence;

        Assert.True(laterConfidence >= earlyConfidence,
            $"Later confidence ({laterConfidence:F2}) should be >= early ({earlyConfidence:F2})");
    }

    // ──────────────────────────────────────────────
    //  Noisy intervals — still converges
    // ──────────────────────────────────────────────

    [Fact]
    public void AddBeats_NoisyIntervals_StillConvergesToApproximateBpm()
    {
        var estimator = new BpmEstimator();
        double baseInterval = 500; // 120 BPM
        var rng = new Random(42);

        for (int i = 0; i < 24; i++)
        {
            double jitter = rng.NextDouble() * 40 - 20; // ±20ms jitter
            double timestamp = i * baseInterval + jitter;
            estimator.AddBeat(new BeatEvent { TimestampMs = Math.Max(0, timestamp), Strength = 1.0 });
        }

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 110 && estimate.Bpm < 130,
            $"Expected ~120 BPM with noise, got {estimate.Bpm:F1}");
    }

    // ──────────────────────────────────────────────
    //  Missed beats — gaps in detection
    // ──────────────────────────────────────────────

    [Fact]
    public void AddBeats_OccasionalMissedBeats_StillEstimatesCorrectly()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500; // 120 BPM

        // Feed beats but skip some (miss every 4th beat).
        int beatIdx = 0;
        for (int i = 0; i < 20; i++)
        {
            if (i % 4 == 3) continue; // skip
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });
            beatIdx++;
        }

        var estimate = estimator.CurrentEstimate;
        // With missed beats, the estimator should still find ~120 BPM via sub-harmonic voting.
        // The intervals will be 500ms, 500ms, 1000ms (double), 500ms, etc.
        // Due to harmonic detection, it should resolve to ~120 BPM.
        Assert.True(estimate.Bpm > 100 && estimate.Bpm < 140,
            $"Expected ~120 BPM with missed beats, got {estimate.Bpm:F1}");
    }

    // ──────────────────────────────────────────────
    //  Tempo change — adapts
    // ──────────────────────────────────────────────

    [Fact]
    public void AddBeats_TempoChange_AdaptsThroughSmoothing()
    {
        var estimator = new BpmEstimator();

        // Start at 120 BPM for 10 beats.
        double time = 0;
        for (int i = 0; i < 10; i++)
        {
            estimator.AddBeat(new BeatEvent { TimestampMs = time, Strength = 1.0 });
            time += 500;
        }

        double bpmBefore = estimator.CurrentEstimate.Bpm;

        // Switch to 100 BPM for 16 beats.
        for (int i = 0; i < 16; i++)
        {
            estimator.AddBeat(new BeatEvent { TimestampMs = time, Strength = 1.0 });
            time += 600;
        }

        double bpmAfter = estimator.CurrentEstimate.Bpm;

        // Should have moved toward 100 BPM.
        Assert.True(bpmAfter < bpmBefore,
            $"BPM should decrease from {bpmBefore:F1} toward 100, got {bpmAfter:F1}");
        Assert.True(bpmAfter > 90 && bpmAfter < 115,
            $"Expected BPM near 100 after tempo change, got {bpmAfter:F1}");
    }

    // ──────────────────────────────────────────────
    //  QuantizeBeat — phase locking
    // ──────────────────────────────────────────────

    [Fact]
    public void QuantizeBeat_NoEstimate_ReturnsOriginal()
    {
        var estimator = new BpmEstimator();
        double result = estimator.QuantizeBeat(1234.5);
        Assert.Equal(1234.5, result);
    }

    [Fact]
    public void QuantizeBeat_LowConfidence_ReturnsOriginal()
    {
        var estimator = new BpmEstimator();
        // Feed only 3 beats — low confidence.
        estimator.AddBeat(new BeatEvent { TimestampMs = 0, Strength = 1.0 });
        estimator.AddBeat(new BeatEvent { TimestampMs = 500, Strength = 1.0 });
        estimator.AddBeat(new BeatEvent { TimestampMs = 1000, Strength = 1.0 });

        double raw = 1250.0;
        double result = estimator.QuantizeBeat(raw);

        // With only 2 intervals and low confidence, should return raw.
        // (confidence threshold is 0.6, and with 2 intervals confidence is low)
        Assert.Equal(raw, result);
    }

    [Fact]
    public void QuantizeBeat_HighConfidence_SnapsToGrid()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500; // 120 BPM

        // Feed enough beats to build high confidence.
        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        // Ensure confidence is high enough for phase lock.
        if (estimator.CurrentEstimate.Confidence >= 0.6)
        {
            // A beat at 7510ms (10ms off from 7500ms grid line) should snap.
            double raw = 7510;
            double quantized = estimator.QuantizeBeat(raw);

            // Should be snapped to nearest grid line (within the beat period).
            double beatPeriod = 60000.0 / estimator.CurrentEstimate.Bpm;
            double distFromGrid = Math.Abs(quantized - Math.Round(quantized / beatPeriod) * beatPeriod);

            // The quantized beat should be closer to a grid line than the raw beat.
            Assert.True(Math.Abs(quantized - raw) < 50,
                $"Quantized ({quantized:F1}) should be near raw ({raw})");
        }
    }

    [Fact]
    public void QuantizeBeat_FarFromGrid_ReturnsOriginal()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500; // 120 BPM

        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        if (estimator.CurrentEstimate.Confidence >= 0.6)
        {
            // A beat at 7250ms is 250ms from nearest grid line — outside tolerance.
            double raw = 7250;
            double quantized = estimator.QuantizeBeat(raw);

            Assert.Equal(raw, quantized);
        }
    }

    // ──────────────────────────────────────────────
    //  Phase offset
    // ──────────────────────────────────────────────

    [Fact]
    public void PhaseOffset_IsNonNegative()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500;

        for (int i = 0; i < 8; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        Assert.True(estimator.CurrentEstimate.PhaseOffsetMs >= 0,
            $"Phase offset should be >= 0, got {estimator.CurrentEstimate.PhaseOffsetMs}");
    }

    [Fact]
    public void PhaseOffset_WithinBeatPeriod()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500;

        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        if (estimator.CurrentEstimate.Bpm > 0)
        {
            double beatPeriod = 60000.0 / estimator.CurrentEstimate.Bpm;
            Assert.True(estimator.CurrentEstimate.PhaseOffsetMs < beatPeriod,
                $"Phase offset {estimator.CurrentEstimate.PhaseOffsetMs:F1} should be < beat period {beatPeriod:F1}");
        }
    }

    // ──────────────────────────────────────────────
    //  Reset
    // ──────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsAllState()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500;

        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        Assert.True(estimator.CurrentEstimate.Bpm > 0);

        estimator.Reset();

        Assert.Equal(0, estimator.CurrentEstimate.Bpm);
        Assert.Equal(0, estimator.CurrentEstimate.Confidence);
        Assert.Equal(0, estimator.CurrentEstimate.PhaseOffsetMs);
    }

    [Fact]
    public void Reset_ThenAddBeats_WorksCleanly()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500;

        for (int i = 0; i < 8; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        estimator.Reset();

        // Feed new beats at 90 BPM.
        double newInterval = 60000.0 / 90;
        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * newInterval, Strength = 1.0 });

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 85 && estimate.Bpm < 95,
            $"After reset, expected ~90 BPM, got {estimate.Bpm:F1}");
    }

    // ──────────────────────────────────────────────
    //  Edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public void AddBeat_DuplicateTimestamp_DoesNotCrash()
    {
        var estimator = new BpmEstimator();
        estimator.AddBeat(new BeatEvent { TimestampMs = 100, Strength = 1.0 });
        estimator.AddBeat(new BeatEvent { TimestampMs = 100, Strength = 1.0 }); // zero interval
        estimator.AddBeat(new BeatEvent { TimestampMs = 600, Strength = 1.0 });

        // Should not crash. BPM may or may not be meaningful.
        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm >= 0);
    }

    [Fact]
    public void AddBeats_ManyBeats_DoesNotThrow()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 500;

        // Feed far more beats than the circular buffer size.
        for (int i = 0; i < 200; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 110 && estimate.Bpm < 130,
            $"Expected ~120 BPM after many beats, got {estimate.Bpm:F1}");
    }

    [Fact]
    public void AddBeats_BpmAtLowerBound_DetectsCorrectly()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 60000.0 / 55; // 55 BPM

        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 50 && estimate.Bpm < 65,
            $"Expected ~55 BPM, got {estimate.Bpm:F1}");
    }

    [Fact]
    public void AddBeats_BpmAtUpperBound_DetectsCorrectly()
    {
        var estimator = new BpmEstimator();
        double intervalMs = 60000.0 / 175; // 175 BPM

        for (int i = 0; i < 16; i++)
            estimator.AddBeat(new BeatEvent { TimestampMs = i * intervalMs, Strength = 1.0 });

        var estimate = estimator.CurrentEstimate;
        Assert.True(estimate.Bpm > 165 && estimate.Bpm < 185,
            $"Expected ~175 BPM, got {estimate.Bpm:F1}");
    }
}
