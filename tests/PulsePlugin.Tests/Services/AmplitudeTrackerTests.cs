using PulsePlugin.Services;
using PulsePlugin.Tests.TestUtilities;
using Xunit;

namespace PulsePlugin.Tests.Services;

public class AmplitudeTrackerTests
{
    // ─── Constructor ──────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsOnZeroWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmplitudeTracker(0));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmplitudeTracker(-5));
    }

    [Fact]
    public void Constructor_DefaultWindow_DoesNotThrow()
    {
        var tracker = new AmplitudeTracker();
        Assert.NotNull(tracker);
    }

    // ─── Process ──────────────────────────────────────────────

    [Fact]
    public void Process_EmptySamples_ReturnsEmpty()
    {
        var tracker = new AmplitudeTracker();
        var result = tracker.Process(ReadOnlySpan<float>.Empty, 0, TestConstants.SampleRate48000);
        Assert.Empty(result);
    }

    [Fact]
    public void Process_ZeroSampleRate_ReturnsEmpty()
    {
        var tracker = new AmplitudeTracker();
        var samples = new float[] { 0.5f, 0.5f };
        var result = tracker.Process(samples, 0, 0);
        Assert.Empty(result);
    }

    [Fact]
    public void Process_Silence_ReturnsZeroRms()
    {
        var tracker = new AmplitudeTracker(windowMs: 10);
        var silence = SyntheticAudioGenerator.Silence(100, TestConstants.SampleRate48000);

        var results = tracker.Process(silence, 0, TestConstants.SampleRate48000);

        Assert.NotEmpty(results);
        foreach (var (_, rms) in results)
        {
            Assert.Equal(0.0, rms, 6);
        }
    }

    [Fact]
    public void Process_FullScaleSine_ReturnsExpectedRms()
    {
        // RMS of a full-scale sine wave = 1/sqrt(2) ≈ 0.7071
        var tracker = new AmplitudeTracker(windowMs: 100);
        var sine = SyntheticAudioGenerator.SineWave(440, 500, TestConstants.SampleRate48000, amplitude: 1.0f);

        var results = tracker.Process(sine, 0, TestConstants.SampleRate48000);

        Assert.NotEmpty(results);

        // Skip first window (may be partial). Check that RMS is close to 1/sqrt(2)
        double expectedRms = 1.0 / Math.Sqrt(2);
        foreach (var (_, rms) in results)
        {
            Assert.InRange(rms, expectedRms - 0.05, expectedRms + 0.05);
        }
    }

    [Fact]
    public void Process_HalfAmplitudeSine_ReturnsHalfRms()
    {
        var tracker = new AmplitudeTracker(windowMs: 100);
        var sine = SyntheticAudioGenerator.SineWave(440, 500, TestConstants.SampleRate48000, amplitude: 0.5f);

        var results = tracker.Process(sine, 0, TestConstants.SampleRate48000);
        Assert.NotEmpty(results);

        double expectedRms = 0.5 / Math.Sqrt(2);
        foreach (var (_, rms) in results)
        {
            Assert.InRange(rms, expectedRms - 0.03, expectedRms + 0.03);
        }
    }

    [Fact]
    public void Process_WindowCount_MatchesExpected()
    {
        double windowMs = 20;
        double durationMs = 200;
        int sampleRate = TestConstants.SampleRate48000;
        int windowSizeSamples = (int)(sampleRate * windowMs / 1000.0);
        int totalSamples = (int)(durationMs * sampleRate / 1000.0);
        int expectedWindows = totalSamples / windowSizeSamples;

        var tracker = new AmplitudeTracker(windowMs);
        var samples = SyntheticAudioGenerator.SineWave(440, durationMs, sampleRate);

        var results = tracker.Process(samples, 0, sampleRate);

        Assert.Equal(expectedWindows, results.Count);
    }

    [Fact]
    public void Process_TimestampsAreMonotonicallyIncreasing()
    {
        var tracker = new AmplitudeTracker(windowMs: 10);
        var samples = SyntheticAudioGenerator.SineWave(440, 200, TestConstants.SampleRate48000);

        var results = tracker.Process(samples, 0, TestConstants.SampleRate48000);

        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i].TimestampMs > results[i - 1].TimestampMs,
                $"Timestamp at index {i} ({results[i].TimestampMs}) should be greater than previous ({results[i - 1].TimestampMs})");
        }
    }

    [Fact]
    public void Process_WithStartTimeOffset_TimestampsAreOffset()
    {
        var tracker = new AmplitudeTracker(windowMs: 20);
        double startTimeMs = 5000;
        var samples = SyntheticAudioGenerator.SineWave(440, 100, TestConstants.SampleRate48000);

        var results = tracker.Process(samples, startTimeMs, TestConstants.SampleRate48000);

        Assert.NotEmpty(results);
        Assert.True(results[0].TimestampMs >= startTimeMs,
            $"First timestamp ({results[0].TimestampMs}) should be >= start time ({startTimeMs})");
    }

    [Fact]
    public void Process_AccumulatesAcrossCalls()
    {
        // If we send a partial window, then more data, it should complete the window
        var tracker = new AmplitudeTracker(windowMs: 20);
        int sampleRate = TestConstants.SampleRate48000;
        int windowSamples = (int)(sampleRate * 20.0 / 1000.0); // 960 at 48kHz

        // Send half a window
        var halfWindow = new float[windowSamples / 2];
        Array.Fill(halfWindow, 0.5f);
        var results1 = tracker.Process(halfWindow, 0, sampleRate);
        Assert.Empty(results1); // Not enough for a complete window

        // Send the other half
        var otherHalf = new float[windowSamples / 2];
        Array.Fill(otherHalf, 0.5f);
        double nextTimeMs = (windowSamples / 2) * 1000.0 / sampleRate;
        var results2 = tracker.Process(otherHalf, nextTimeMs, sampleRate);
        Assert.Single(results2); // Now we have a complete window

        // RMS of constant 0.5 = 0.5
        Assert.InRange(results2[0].Rms, 0.49, 0.51);
    }

    [Fact]
    public void CurrentAmplitude_UpdatesAfterProcess()
    {
        var tracker = new AmplitudeTracker(windowMs: 10);
        Assert.Equal(0.0, tracker.CurrentAmplitude);

        var sine = SyntheticAudioGenerator.SineWave(440, 100, TestConstants.SampleRate48000, amplitude: 1.0f);
        tracker.Process(sine, 0, TestConstants.SampleRate48000);

        Assert.True(tracker.CurrentAmplitude > 0, "CurrentAmplitude should be > 0 after processing audio");
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var tracker = new AmplitudeTracker(windowMs: 10);
        var sine = SyntheticAudioGenerator.SineWave(440, 100, TestConstants.SampleRate48000);
        tracker.Process(sine, 0, TestConstants.SampleRate48000);

        tracker.Reset();

        Assert.Equal(0.0, tracker.CurrentAmplitude);
    }

    [Fact]
    public void Process_RmsValues_ClampedToOne()
    {
        // Even with very loud samples, RMS should not exceed 1.0
        var tracker = new AmplitudeTracker(windowMs: 10);
        int sampleRate = TestConstants.SampleRate48000;
        int windowSamples = (int)(sampleRate * 10.0 / 1000.0);
        var loud = new float[windowSamples * 2];
        Array.Fill(loud, 2.0f); // Clipping — values > 1.0

        var results = tracker.Process(loud, 0, sampleRate);

        foreach (var (_, rms) in results)
        {
            Assert.InRange(rms, 0, 1.0);
        }
    }

    [Fact]
    public void Process_At44100_ProducesCorrectWindowCount()
    {
        double windowMs = 20;
        int sampleRate = TestConstants.SampleRate44100;
        int windowSamples = (int)(sampleRate * windowMs / 1000.0); // 882 at 44.1kHz
        int totalSamples = windowSamples * 5;
        double durationMs = totalSamples * 1000.0 / sampleRate;

        var tracker = new AmplitudeTracker(windowMs);
        var samples = SyntheticAudioGenerator.SineWave(440, durationMs, sampleRate);

        var results = tracker.Process(samples.AsSpan(0, totalSamples), 0, sampleRate);
        Assert.Equal(5, results.Count);
    }

    // ─── DownmixToMono ────────────────────────────────────────

    [Fact]
    public void DownmixToMono_MonoInput_ReturnsIdentical()
    {
        var mono = new float[] { 1.0f, 2.0f, 3.0f };
        var result = AmplitudeTracker.DownmixToMono(mono, 1);
        Assert.Equal(mono, result);
    }

    [Fact]
    public void DownmixToMono_StereoInput_AveragesChannels()
    {
        // L=1, R=0, L=0, R=1
        var stereo = new float[] { 1.0f, 0.0f, 0.0f, 1.0f };
        var result = AmplitudeTracker.DownmixToMono(stereo, 2);

        Assert.Equal(2, result.Length);
        Assert.Equal(0.5f, result[0]);
        Assert.Equal(0.5f, result[1]);
    }

    [Fact]
    public void DownmixToMono_IdenticalChannels_PreservesValues()
    {
        var mono = new float[] { 0.5f, -0.3f, 0.8f };
        var stereo = SyntheticAudioGenerator.MonoToStereo(mono);
        var result = AmplitudeTracker.DownmixToMono(stereo, 2);

        Assert.Equal(mono.Length, result.Length);
        for (int i = 0; i < mono.Length; i++)
        {
            Assert.Equal(mono[i], result[i], 6);
        }
    }

    [Fact]
    public void DownmixToMono_ThrowsOnZeroChannels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AmplitudeTracker.DownmixToMono(new float[] { 1.0f }, 0));
    }

    [Fact]
    public void DownmixToMono_MultiChannel_AveragesAll()
    {
        // 4 channels: all 1.0 → mono should be 1.0
        var samples = new float[] { 1.0f, 1.0f, 1.0f, 1.0f, -1.0f, -1.0f, -1.0f, -1.0f };
        var result = AmplitudeTracker.DownmixToMono(samples, 4);

        Assert.Equal(2, result.Length);
        Assert.Equal(1.0f, result[0], 6);
        Assert.Equal(-1.0f, result[1], 6);
    }

    // ─── ByteBufferToMono ─────────────────────────────────────

    [Fact]
    public void ByteBufferToMono_ConvertsCorrectly()
    {
        var mono = new float[] { 0.25f, 0.5f, 0.75f };
        var stereo = SyntheticAudioGenerator.MonoToStereo(mono);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(stereo);

        var result = AmplitudeTracker.ByteBufferToMono(bytes, 3, 2);

        Assert.Equal(3, result.Length);
        for (int i = 0; i < mono.Length; i++)
        {
            Assert.Equal(mono[i], result[i], 5);
        }
    }

    [Fact]
    public void ByteBufferToMono_MonoInput_PreservesValues()
    {
        var mono = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var bytes = SyntheticAudioGenerator.ToByteBuffer(mono);

        var result = AmplitudeTracker.ByteBufferToMono(bytes, 4, 1);

        Assert.Equal(mono.Length, result.Length);
        for (int i = 0; i < mono.Length; i++)
        {
            Assert.Equal(mono[i], result[i], 5);
        }
    }
}
