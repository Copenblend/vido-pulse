using PulsePlugin.Models;
using PulsePlugin.Services;
using Xunit;

namespace PulsePlugin.Tests.Services;

/// <summary>
/// Unit tests for PulseTCodeMapper — Hybrid beat-to-position mapping.
/// </summary>
public class PulseTCodeMapperTests
{
    private readonly PulseTCodeMapper _mapper = new();

    // Helper: create a simple beat map with evenly spaced beats.
    private static BeatMap MakeBeatMap(double bpm, int beatCount, double strength = 1.0)
    {
        double intervalMs = 60000.0 / bpm;
        var beats = new List<BeatEvent>();
        for (int i = 0; i < beatCount; i++)
        {
            beats.Add(new BeatEvent
            {
                TimestampMs = i * intervalMs,
                Strength = strength,
                IsQuantized = false
            });
        }
        return new BeatMap
        {
            Beats = beats.AsReadOnly(),
            Bpm = bpm,
            BpmConfidence = 0.9,
            DurationMs = beatCount * intervalMs
        };
    }

    // ──────────────────────────────────────────────
    //  Null / empty beat map
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_NullBeatMap_ReturnsRestPosition()
    {
        double pos = _mapper.MapToPosition(null, 1000, 0.5);
        Assert.Equal(50.0, pos);
    }

    [Fact]
    public void MapToPosition_EmptyBeats_ReturnsRestPosition()
    {
        var map = new BeatMap { Beats = Array.Empty<BeatEvent>() };
        double pos = _mapper.MapToPosition(map, 1000, 0.5);
        Assert.Equal(50.0, pos);
    }

    // ──────────────────────────────────────────────
    //  Before first beat
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_BeforeFirstBeat_ReturnsRestPosition()
    {
        var map = MakeBeatMap(120, 10);
        // First beat is at 0ms, query at -100ms.
        double pos = _mapper.MapToPosition(map, -100, 0.5);
        Assert.Equal(50.0, pos);
    }

    // ──────────────────────────────────────────────
    //  Position output range
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_AlwaysWithinRange()
    {
        var map = MakeBeatMap(120, 20);

        for (double t = 0; t < 10000; t += 16.67) // ~60Hz
        {
            double pos = _mapper.MapToPosition(map, t, 0.8);
            Assert.True(pos >= 5.0 && pos <= 95.0,
                $"Position {pos:F1} at t={t:F0} out of range 5–95");
        }
    }

    [Fact]
    public void MapToPosition_FullAmplitude_UsesWiderRange()
    {
        var map = MakeBeatMap(120, 10);
        // At the upstroke peak (40% through the beat).
        double beatInterval = 500;
        double peakTime = beatInterval * 0.4; // end of upstroke

        // Use separate mapper instances to avoid smoothing interference.
        var mapperHigh = new PulseTCodeMapper();
        var mapperLow = new PulseTCodeMapper();

        double posHigh = mapperHigh.MapToPosition(map, peakTime * 0.99, 1.0);
        double posLow = mapperLow.MapToPosition(map, peakTime * 0.99, 0.0);

        // Full amplitude should produce a wider stroke than zero amplitude.
        Assert.True(posHigh > posLow,
            $"Full amplitude position ({posHigh:F1}) should be higher than zero amplitude ({posLow:F1})");
    }

    [Fact]
    public void MapToPosition_ZeroAmplitude_StillMoves()
    {
        var map = MakeBeatMap(120, 10);
        double peakTime = 500 * 0.2; // mid-upstroke

        double pos = _mapper.MapToPosition(map, peakTime, 0.0);

        // Even at zero amplitude, minimum scale means some movement.
        Assert.NotEqual(50.0, pos);
    }

    // ──────────────────────────────────────────────
    //  Stroke waveform shape
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_AtBeatStart_IsNearBottom()
    {
        var map = MakeBeatMap(120, 10);
        // At exactly the beat time, phase=0 → should be at bottom of stroke.
        double pos = _mapper.MapToPosition(map, 0, 0.8);

        // At phase=0, easeOutQuad(0)=0, position = bottom.
        Assert.True(pos < 50.0, $"At beat start, position ({pos:F1}) should be below rest (50)");
    }

    [Fact]
    public void MapToPosition_AtUpstrokePeak_IsAboveRest()
    {
        var map = MakeBeatMap(120, 10);
        double intervalMs = 500;
        // Just before end of upstroke phase (40% of interval).
        double peakTime = intervalMs * 0.39;

        double pos = _mapper.MapToPosition(map, peakTime, 0.8);

        Assert.True(pos > 50.0, $"At upstroke peak, position ({pos:F1}) should be above rest (50)");
    }

    [Fact]
    public void MapToPosition_AtDownstrokeEnd_IsNearBottom()
    {
        var map = MakeBeatMap(120, 10);
        double intervalMs = 500;
        // Near end of downstroke (95% through interval).
        double endTime = intervalMs * 0.95;

        double pos = _mapper.MapToPosition(map, endTime, 0.8);

        Assert.True(pos < 50.0, $"At downstroke end, position ({pos:F1}) should be below rest (50)");
    }

    [Fact]
    public void MapToPosition_StrokeCycle_RisesAndFalls()
    {
        var map = MakeBeatMap(120, 10);
        double intervalMs = 500;

        // Sample positions through one beat cycle.
        var positions = new List<double>();
        for (double t = 0; t < intervalMs; t += 10)
            positions.Add(_mapper.MapToPosition(map, t, 0.8));

        // Find the peak position.
        double peakPos = positions.Max();
        int peakIdx = positions.IndexOf(peakPos);

        // Peak should be roughly at the upstroke fraction (~40%).
        double peakFraction = (double)peakIdx / positions.Count;
        Assert.True(peakFraction > 0.2 && peakFraction < 0.6,
            $"Peak at {peakFraction:P0} should be near upstroke fraction (40%)");

        // Position should be lower at start and end than at peak.
        Assert.True(positions[0] < peakPos, "Start should be below peak");
        Assert.True(positions[^1] < peakPos, "End should be below peak");
    }

    // ──────────────────────────────────────────────
    //  Beat strength modulation
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_StrongerBeat_ProducesLargerStroke()
    {
        var mapStrong = MakeBeatMap(120, 10, strength: 1.0);
        var mapWeak = MakeBeatMap(120, 10, strength: 0.2);

        double intervalMs = 500;
        double peakTime = intervalMs * 0.35;

        double posStrong = new PulseTCodeMapper().MapToPosition(mapStrong, peakTime, 0.8);
        double posWeak = new PulseTCodeMapper().MapToPosition(mapWeak, peakTime, 0.8);

        Assert.True(posStrong > posWeak,
            $"Strong beat ({posStrong:F1}) should produce higher peak than weak beat ({posWeak:F1})");
    }

    // ──────────────────────────────────────────────
    //  Amplitude scaling
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_HigherAmplitude_LargerStroke()
    {
        var map = MakeBeatMap(120, 10);
        double peakTime = 500 * 0.35;

        double posHigh = _mapper.MapToPosition(map, peakTime, 1.0);
        _mapper.Reset();
        double posLow = _mapper.MapToPosition(map, peakTime, 0.2);

        Assert.True(posHigh > posLow,
            $"High amplitude ({posHigh:F1}) should produce larger stroke than low ({posLow:F1})");
    }

    [Fact]
    public void MapToPosition_AmplitudeClampedAbove1()
    {
        var map = MakeBeatMap(120, 10);
        double peakTime = 500 * 0.35;

        double posAt1 = _mapper.MapToPosition(map, peakTime, 1.0);
        _mapper.Reset();
        double posAt2 = _mapper.MapToPosition(map, peakTime, 2.0); // above 1.0

        Assert.Equal(posAt1, posAt2, 3); // should be clamped
    }

    [Fact]
    public void MapToPosition_NegativeAmplitude_ClampedToZero()
    {
        var map = MakeBeatMap(120, 10);
        double peakTime = 500 * 0.35;

        double posAtZero = _mapper.MapToPosition(map, peakTime, 0.0);
        _mapper.Reset();
        double posAtNeg = _mapper.MapToPosition(map, peakTime, -0.5);

        Assert.Equal(posAtZero, posAtNeg, 3);
    }

    // ──────────────────────────────────────────────
    //  Different BPMs
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_60Bpm_StrokeCompletesWithinInterval()
    {
        var map = MakeBeatMap(60, 10);
        double intervalMs = 1000;

        // Sample the full cycle — should return to near-bottom.
        double startPos = _mapper.MapToPosition(map, 0, 0.8);
        double endPos = _mapper.MapToPosition(map, intervalMs * 0.95, 0.8);

        // Both should be below rest.
        Assert.True(startPos < 50, $"Start at 60BPM ({startPos:F1}) should be below rest");
        Assert.True(endPos < 50, $"End at 60BPM ({endPos:F1}) should be below rest");
    }

    [Fact]
    public void MapToPosition_180Bpm_StillProducesMovement()
    {
        var map = MakeBeatMap(180, 20);
        double intervalMs = 60000.0 / 180; // ~333ms

        double peakTime = intervalMs * 0.35;
        double pos = _mapper.MapToPosition(map, peakTime, 0.8);

        Assert.True(pos > 50.0, $"At 180BPM peak, position ({pos:F1}) should be above rest");
    }

    // ──────────────────────────────────────────────
    //  Past last beat
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_AfterLastBeat_PastInterval_ReturnsRest()
    {
        var map = MakeBeatMap(120, 5); // 5 beats, last at 2000ms
        double lastBeatTime = 4 * 500;
        // Way past the last beat's interval.
        double pos = _mapper.MapToPosition(map, lastBeatTime + 1000, 0.8);

        Assert.Equal(50.0, pos);
    }

    [Fact]
    public void MapToPosition_AfterLastBeat_WithinInterval_StillStrokes()
    {
        var map = MakeBeatMap(120, 5);
        double lastBeatTime = 4 * 500;
        // Within the interval of the last beat (uses BPM-derived interval).
        double pos = _mapper.MapToPosition(map, lastBeatTime + 200, 0.8);

        Assert.NotEqual(50.0, pos);
    }

    // ──────────────────────────────────────────────
    //  FindCurrentBeatIndex
    // ──────────────────────────────────────────────

    [Fact]
    public void FindCurrentBeatIndex_EmptyList_ReturnsNeg1()
    {
        Assert.Equal(-1, PulseTCodeMapper.FindCurrentBeatIndex(Array.Empty<BeatEvent>(), 100));
    }

    [Fact]
    public void FindCurrentBeatIndex_BeforeAllBeats_ReturnsNeg1()
    {
        var beats = new[] { new BeatEvent { TimestampMs = 500 } };
        Assert.Equal(-1, PulseTCodeMapper.FindCurrentBeatIndex(beats, 100));
    }

    [Fact]
    public void FindCurrentBeatIndex_ExactlyOnBeat_ReturnsThatIndex()
    {
        var beats = new[]
        {
            new BeatEvent { TimestampMs = 0 },
            new BeatEvent { TimestampMs = 500 },
            new BeatEvent { TimestampMs = 1000 }
        };
        Assert.Equal(1, PulseTCodeMapper.FindCurrentBeatIndex(beats, 500));
    }

    [Fact]
    public void FindCurrentBeatIndex_BetweenBeats_ReturnsPrevious()
    {
        var beats = new[]
        {
            new BeatEvent { TimestampMs = 0 },
            new BeatEvent { TimestampMs = 500 },
            new BeatEvent { TimestampMs = 1000 }
        };
        Assert.Equal(1, PulseTCodeMapper.FindCurrentBeatIndex(beats, 750));
    }

    [Fact]
    public void FindCurrentBeatIndex_AfterAllBeats_ReturnsLast()
    {
        var beats = new[]
        {
            new BeatEvent { TimestampMs = 0 },
            new BeatEvent { TimestampMs = 500 }
        };
        Assert.Equal(1, PulseTCodeMapper.FindCurrentBeatIndex(beats, 9999));
    }

    [Fact]
    public void FindCurrentBeatIndex_SingleBeat_AtBeat()
    {
        var beats = new[] { new BeatEvent { TimestampMs = 100 } };
        Assert.Equal(0, PulseTCodeMapper.FindCurrentBeatIndex(beats, 100));
    }

    // ──────────────────────────────────────────────
    //  Reset
    // ──────────────────────────────────────────────

    [Fact]
    public void Reset_DoesNotThrow()
    {
        _mapper.Reset();
        // Should not throw and mapper should still work.
        var map = MakeBeatMap(120, 10);
        double pos = _mapper.MapToPosition(map, 100, 0.5);
        Assert.True(pos >= 5.0 && pos <= 95.0);
    }

    // ──────────────────────────────────────────────
    //  Edge: very fast BPM
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_VeryFastBpm_StillInRange()
    {
        // 300 BPM — technically outside the 50-180 range, but mapper should handle it.
        var map = MakeBeatMap(300, 30);

        for (double t = 0; t < 3000; t += 10)
        {
            double pos = _mapper.MapToPosition(map, t, 0.5);
            Assert.True(pos >= 5.0 && pos <= 95.0,
                $"At 300BPM, t={t:F0}, position {pos:F1} out of range");
        }
    }

    // ──────────────────────────────────────────────
    //  Edge: single beat
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_SingleBeat_StillProducesStroke()
    {
        var map = new BeatMap
        {
            Beats = new[] { new BeatEvent { TimestampMs = 0, Strength = 1.0 } },
            Bpm = 120,
            BpmConfidence = 0.9,
            DurationMs = 500
        };

        double peakTime = 500 * 0.35;
        double pos = _mapper.MapToPosition(map, peakTime, 0.8);

        Assert.True(pos > 50.0, $"Single beat should produce stroke, got {pos:F1}");
    }

    // ──────────────────────────────────────────────
    //  Edge: no BPM information
    // ──────────────────────────────────────────────

    [Fact]
    public void MapToPosition_ZeroBpm_UsesDefaultInterval()
    {
        var map = new BeatMap
        {
            Beats = new[] { new BeatEvent { TimestampMs = 0, Strength = 1.0 } },
            Bpm = 0, // no BPM info
            DurationMs = 1000
        };

        // Should use default 500ms interval and still produce a stroke.
        double pos = _mapper.MapToPosition(map, 200, 0.8);
        Assert.NotEqual(50.0, pos);
    }
}
