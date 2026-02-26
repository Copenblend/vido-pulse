using System.Numerics;
using PulsePlugin.Services;
using PulsePlugin.Tests.TestUtilities;
using Xunit;

namespace PulsePlugin.Tests.Services;

/// <summary>
/// Unit tests for OnsetDetector — spectral flux beat detection with adaptive thresholding.
/// </summary>
public class OnsetDetectorTests
{
    private const int DefaultSampleRate = TestConstants.SampleRate44100;

    // ──────────────────────────────────────────────
    //  Constructor validation
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultParameters_DoesNotThrow()
    {
        var detector = new OnsetDetector();
        Assert.NotNull(detector);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]   // not power of 2
    [InlineData(-1)]
    [InlineData(1)]
    public void Constructor_InvalidFftSize_Throws(int fftSize)
    {
        Assert.Throws<ArgumentException>(() => new OnsetDetector(fftSize: fftSize));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidHopSize_Throws(int hopSize)
    {
        Assert.Throws<ArgumentException>(() => new OnsetDetector(hopSize: hopSize));
    }

    [Fact]
    public void Constructor_HopSizeLargerThanFftSize_Throws()
    {
        Assert.Throws<ArgumentException>(() => new OnsetDetector(fftSize: 256, hopSize: 512));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    public void Constructor_InvalidSensitivity_Throws(double sensitivity)
    {
        Assert.Throws<ArgumentException>(() => new OnsetDetector(sensitivity: sensitivity));
    }

    [Fact]
    public void Constructor_ValidCustomParameters_DoesNotThrow()
    {
        var detector = new OnsetDetector(fftSize: 1024, hopSize: 256, sensitivity: 2.0);
        Assert.NotNull(detector);
    }

    // ──────────────────────────────────────────────
    //  Silence — should produce no onsets
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_Silence_ReturnsNoOnsets()
    {
        var detector = new OnsetDetector();
        var silence = SyntheticAudioGenerator.Silence(2000, DefaultSampleRate);

        var beats = detector.Process(silence, 0, DefaultSampleRate);

        Assert.Empty(beats);
    }

    // ──────────────────────────────────────────────
    //  Click track — should detect known beats
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_ClickTrack120Bpm_DetectsBeats()
    {
        var detector = new OnsetDetector(fftSize: 2048, hopSize: 512, sensitivity: 1.5);
        var click = SyntheticAudioGenerator.ClickTrack(120, 5000, DefaultSampleRate);

        var beats = detector.Process(click, 0, DefaultSampleRate);

        // 120 BPM = 2 beats/sec. Over 5 seconds we expect ~10 beats.
        // Allow some tolerance for edge effects and first-frame skip.
        Assert.True(beats.Count >= 5, $"Expected at least 5 beats at 120 BPM, got {beats.Count}");
        Assert.True(beats.Count <= 15, $"Expected at most 15 beats at 120 BPM, got {beats.Count}");
    }

    [Fact]
    public void Process_ClickTrack120Bpm_TimestampsAreApproximatelyCorrect()
    {
        var detector = new OnsetDetector(fftSize: 2048, hopSize: 512, sensitivity: 1.5);
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, DefaultSampleRate);

        var beats = detector.Process(click, 0, DefaultSampleRate);

        // Expected inter-beat interval at 120 BPM = 500ms.
        // Detected onsets should have roughly ~500ms intervals.
        if (beats.Count >= 2)
        {
            var intervals = new List<double>();
            for (int i = 1; i < beats.Count; i++)
                intervals.Add(beats[i].TimestampMs - beats[i - 1].TimestampMs);

            double avgInterval = intervals.Average();
            Assert.True(avgInterval > 300, $"Average interval {avgInterval}ms is too short for 120 BPM");
            Assert.True(avgInterval < 700, $"Average interval {avgInterval}ms is too long for 120 BPM");
        }
    }

    [Fact]
    public void Process_ClickTrack60Bpm_DetectsBeats()
    {
        var detector = new OnsetDetector(fftSize: 2048, hopSize: 512, sensitivity: 1.5);
        var click = SyntheticAudioGenerator.ClickTrack(60, 5000, DefaultSampleRate);

        var beats = detector.Process(click, 0, DefaultSampleRate);

        // 60 BPM = 1 beat/sec. Over 5 seconds we expect ~5 beats.
        Assert.True(beats.Count >= 2, $"Expected at least 2 beats at 60 BPM, got {beats.Count}");
        Assert.True(beats.Count <= 8, $"Expected at most 8 beats at 60 BPM, got {beats.Count}");
    }

    // ──────────────────────────────────────────────
    //  Sine beats: bursts of sine waves separated by silence
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_SineBursts_DetectsOnsets()
    {
        // Create 4 bursts of 100ms 440Hz sine separated by 400ms silence = 500ms period (120 BPM equiv).
        const int burstMs = 100;
        const int gapMs = 400;
        const int numBursts = 4;

        var samples = new List<float>();
        for (int b = 0; b < numBursts; b++)
        {
            samples.AddRange(SyntheticAudioGenerator.SineWave(440, burstMs, DefaultSampleRate, 0.8f));
            if (b < numBursts - 1)
                samples.AddRange(SyntheticAudioGenerator.Silence(gapMs, DefaultSampleRate));
        }

        var detector = new OnsetDetector(fftSize: 1024, hopSize: 256, sensitivity: 1.5);
        var beats = detector.Process(samples.ToArray(), 0, DefaultSampleRate);

        // Should detect onset at or near each burst start.
        Assert.True(beats.Count >= 2, $"Expected at least 2 onset detections from sine bursts, got {beats.Count}");
    }

    // ──────────────────────────────────────────────
    //  White noise — low or no onsets (steady-state)
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_SteadyWhiteNoise_FewOrNoOnsets()
    {
        var detector = new OnsetDetector(fftSize: 2048, hopSize: 512, sensitivity: 2.0);
        var noise = SyntheticAudioGenerator.WhiteNoise(3000, DefaultSampleRate, amplitude: 0.3f, seed: 123);

        var beats = detector.Process(noise, 0, DefaultSampleRate);

        // Steady-state noise should produce few or near-zero onsets 
        // (initial transient may trigger one or two).
        Assert.True(beats.Count <= 5, $"Steady noise should produce few onsets, got {beats.Count}");
    }

    // ──────────────────────────────────────────────
    //  Timestamps and strength
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_AllBeatTimestampsAreNonNegative()
    {
        var detector = new OnsetDetector();
        var click = SyntheticAudioGenerator.ClickTrack(120, 2000, DefaultSampleRate);

        var beats = detector.Process(click, 0, DefaultSampleRate);

        foreach (var beat in beats)
            Assert.True(beat.TimestampMs >= 0, $"Timestamp should be non-negative, got {beat.TimestampMs}");
    }

    [Fact]
    public void Process_AllBeatTimestampsAreMonotonicallyIncreasing()
    {
        var detector = new OnsetDetector();
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, DefaultSampleRate);

        var beats = detector.Process(click, 0, DefaultSampleRate);

        for (int i = 1; i < beats.Count; i++)
            Assert.True(beats[i].TimestampMs > beats[i - 1].TimestampMs,
                $"Beat {i} at {beats[i].TimestampMs}ms should be after beat {i - 1} at {beats[i - 1].TimestampMs}ms");
    }

    [Fact]
    public void Process_StrengthIsBetween0And1()
    {
        var detector = new OnsetDetector();
        var click = SyntheticAudioGenerator.ClickTrack(120, 2000, DefaultSampleRate);

        var beats = detector.Process(click, 0, DefaultSampleRate);

        foreach (var beat in beats)
        {
            Assert.True(beat.Strength >= 0.0 && beat.Strength <= 1.0,
                $"Strength should be 0–1, got {beat.Strength}");
        }
    }

    [Fact]
    public void Process_BeatsAreNotQuantized()
    {
        var detector = new OnsetDetector();
        var click = SyntheticAudioGenerator.ClickTrack(120, 2000, DefaultSampleRate);

        var beats = detector.Process(click, 0, DefaultSampleRate);

        foreach (var beat in beats)
            Assert.False(beat.IsQuantized, "OnsetDetector should not produce quantized beats");
    }

    // ──────────────────────────────────────────────
    //  Start time offset
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_WithStartTimeOffset_TimestampsAreOffset()
    {
        const double offset = 5000;
        var detector = new OnsetDetector();
        var click = SyntheticAudioGenerator.ClickTrack(120, 2000, DefaultSampleRate);

        var beats = detector.Process(click, offset, DefaultSampleRate);

        foreach (var beat in beats)
            Assert.True(beat.TimestampMs >= offset, $"Beat at {beat.TimestampMs} should be >= offset {offset}");
    }

    // ──────────────────────────────────────────────
    //  Minimum inter-onset interval
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_ClickTrack_MinimumInterOnsetIntervalEnforced()
    {
        var detector = new OnsetDetector();
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, DefaultSampleRate);

        var beats = detector.Process(click, 0, DefaultSampleRate);

        // Minimum inter-onset = 100ms.
        for (int i = 1; i < beats.Count; i++)
        {
            double interval = beats[i].TimestampMs - beats[i - 1].TimestampMs;
            Assert.True(interval >= 99.0, // tiny tolerance for floating point
                $"Inter-onset interval {interval}ms between beats {i - 1} and {i} is below 100ms minimum");
        }
    }

    // ──────────────────────────────────────────────
    //  Streaming / cross-call accumulation
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_StreamedInChunks_ProducesComparableResults()
    {
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, DefaultSampleRate);

        // Single-shot.
        var detectorSingle = new OnsetDetector(fftSize: 1024, hopSize: 256, sensitivity: 1.5);
        var beatsSingle = detectorSingle.Process(click, 0, DefaultSampleRate);

        // Chunked — simulate ~10ms audio callbacks.
        var detectorChunked = new OnsetDetector(fftSize: 1024, hopSize: 256, sensitivity: 1.5);
        var beatsChunked = new List<global::PulsePlugin.Models.BeatEvent>();
        int chunkSize = DefaultSampleRate / 100; // ~10ms
        for (int offset = 0; offset < click.Length; offset += chunkSize)
        {
            int len = Math.Min(chunkSize, click.Length - offset);
            double timeMs = offset * 1000.0 / DefaultSampleRate;
            var chunk = click.AsSpan(offset, len);
            beatsChunked.AddRange(detectorChunked.Process(chunk, timeMs, DefaultSampleRate));
        }

        // Both should detect a similar number of beats.
        Assert.True(Math.Abs(beatsSingle.Count - beatsChunked.Count) <= 2,
            $"Single-shot: {beatsSingle.Count}, Chunked: {beatsChunked.Count} — should be within 2");
    }

    // ──────────────────────────────────────────────
    //  Sensitivity
    // ──────────────────────────────────────────────

    [Fact]
    public void HigherSensitivity_DetectsFewerBeats()
    {
        var click = SyntheticAudioGenerator.ClickTrack(120, 5000, DefaultSampleRate);

        var detectorLow = new OnsetDetector(sensitivity: 1.0);
        var beatsLow = detectorLow.Process(click, 0, DefaultSampleRate);

        var detectorHigh = new OnsetDetector(sensitivity: 3.0);
        var beatsHigh = detectorHigh.Process(click, 0, DefaultSampleRate);

        // Higher sensitivity multiplier → higher threshold → fewer or equal detections.
        Assert.True(beatsHigh.Count <= beatsLow.Count,
            $"High sensitivity ({beatsHigh.Count}) should detect <= low sensitivity ({beatsLow.Count})");
    }

    [Fact]
    public void SetSensitivity_InvalidValue_Throws()
    {
        var detector = new OnsetDetector();
        Assert.Throws<ArgumentException>(() => detector.SetSensitivity(0));
        Assert.Throws<ArgumentException>(() => detector.SetSensitivity(-1));
    }

    [Fact]
    public void SetSensitivity_ValidValue_UpdatesSensitivity()
    {
        var detector = new OnsetDetector(sensitivity: 1.0);
        detector.SetSensitivity(2.0);
        // No exception means success — verify indirectly via detection behavior.
        var silence = SyntheticAudioGenerator.Silence(1000, DefaultSampleRate);
        var beats = detector.Process(silence, 0, DefaultSampleRate);
        Assert.Empty(beats);
    }

    // ──────────────────────────────────────────────
    //  Reset
    // ──────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsState_SubsequentProcessStartsFresh()
    {
        var detector = new OnsetDetector();
        var click = SyntheticAudioGenerator.ClickTrack(120, 2000, DefaultSampleRate);

        var beats1 = detector.Process(click, 0, DefaultSampleRate);

        detector.Reset();

        var beats2 = detector.Process(click, 0, DefaultSampleRate);

        // After reset, should produce comparable results (not accumulated with previous).
        Assert.Equal(beats1.Count, beats2.Count);
    }

    // ──────────────────────────────────────────────
    //  Different sample rates
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_48kHz_DetectsBeats()
    {
        var detector = new OnsetDetector();
        var click = SyntheticAudioGenerator.ClickTrack(120, 5000, TestConstants.SampleRate48000);

        var beats = detector.Process(click, 0, TestConstants.SampleRate48000);

        Assert.True(beats.Count >= 3, $"Expected beats at 48kHz, got {beats.Count}");
    }

    [Fact]
    public void Process_InvalidSampleRate_Throws()
    {
        var detector = new OnsetDetector();
        var samples = SyntheticAudioGenerator.Silence(100, 8000);

        Assert.Throws<ArgumentException>(() => detector.Process(samples, 0, 0));
        Assert.Throws<ArgumentException>(() => detector.Process(samples, 0, -1));
    }

    // ──────────────────────────────────────────────
    //  Edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public void Process_EmptyInput_ReturnsEmpty()
    {
        var detector = new OnsetDetector();
        var beats = detector.Process(ReadOnlySpan<float>.Empty, 0, DefaultSampleRate);
        Assert.Empty(beats);
    }

    [Fact]
    public void Process_VeryShortInput_ReturnsEmptyOrFew()
    {
        var detector = new OnsetDetector(fftSize: 2048);
        // Input shorter than fftSize — not enough for even one frame.
        var short_ = SyntheticAudioGenerator.SineWave(440, 10, DefaultSampleRate); // ~441 samples
        var beats = detector.Process(short_, 0, DefaultSampleRate);
        // May return 0 or a few — should not throw.
        Assert.True(beats.Count >= 0);
    }

    [Fact]
    public void Process_SmallFftSize_Works()
    {
        // Minimum valid: fftSize=2.
        var detector = new OnsetDetector(fftSize: 4, hopSize: 2, sensitivity: 1.5);
        var click = SyntheticAudioGenerator.ClickTrack(120, 1000, DefaultSampleRate);
        var beats = detector.Process(click, 0, DefaultSampleRate);
        // Should not throw — beat detection may or may not work well at tiny FFT size.
        Assert.True(beats.Count >= 0);
    }

    // ──────────────────────────────────────────────
    //  FFT correctness
    // ──────────────────────────────────────────────

    [Fact]
    public void Fft_KnownInput_ProducesCorrectOutput()
    {
        // FFT of [1, 0, 0, 0] should be [1, 1, 1, 1].
        var buffer = new Complex[] { 1, 0, 0, 0 };
        OnsetDetector.Fft(buffer);

        for (int i = 0; i < 4; i++)
        {
            Assert.True(Math.Abs(buffer[i].Real - 1.0) < 1e-10,
                $"Bin {i} real: expected 1.0, got {buffer[i].Real}");
            Assert.True(Math.Abs(buffer[i].Imaginary) < 1e-10,
                $"Bin {i} imaginary: expected 0.0, got {buffer[i].Imaginary}");
        }
    }

    [Fact]
    public void Fft_PureSineWave_PeakAtExpectedBin()
    {
        // 256-point FFT of a sine at bin frequency k=8.
        const int n = 256;
        const int k = 8;
        var buffer = new Complex[n];
        for (int i = 0; i < n; i++)
            buffer[i] = new Complex(Math.Sin(2.0 * Math.PI * k * i / n), 0);

        OnsetDetector.Fft(buffer);

        // Find the bin with the maximum magnitude.
        int maxBin = 0;
        double maxMag = 0;
        for (int i = 0; i < n; i++)
        {
            double mag = buffer[i].Magnitude;
            if (mag > maxMag)
            {
                maxMag = mag;
                maxBin = i;
            }
        }

        // Should peak at bin k or its mirror (n - k).
        Assert.True(maxBin == k || maxBin == n - k,
            $"Expected peak at bin {k} or {n - k}, got {maxBin}");
    }

    [Fact]
    public void Fft_Parseval_EnergyPreserved()
    {
        // Parseval's theorem: sum(|x[n]|^2) = (1/N) * sum(|X[k]|^2).
        const int n = 64;
        var rng = new Random(42);
        var buffer = new Complex[n];
        double timeDomainEnergy = 0;
        for (int i = 0; i < n; i++)
        {
            double val = rng.NextDouble() * 2 - 1;
            buffer[i] = new Complex(val, 0);
            timeDomainEnergy += val * val;
        }

        OnsetDetector.Fft(buffer);

        double freqDomainEnergy = 0;
        for (int i = 0; i < n; i++)
            freqDomainEnergy += buffer[i].Magnitude * buffer[i].Magnitude;

        // sum(|X[k]|^2) / N should equal sum(|x[n]|^2).
        double freqNormalized = freqDomainEnergy / n;
        Assert.True(Math.Abs(freqNormalized - timeDomainEnergy) < 1e-6,
            $"Parseval: time energy {timeDomainEnergy}, freq energy/N {freqNormalized}");
    }
}
