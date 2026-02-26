using PulsePlugin.Models;
using PulsePlugin.Services;
using PulsePlugin.Tests.TestUtilities;
using Xunit;

namespace PulsePlugin.Tests.Services;

/// <summary>
/// Unit tests for AudioPreAnalysisService — pipeline wiring, cancellation, progress, BeatMap output.
/// </summary>
public class AudioPreAnalysisServiceTests : IDisposable
{
    private readonly AudioPreAnalysisService _sut;
    private readonly MockAudioDecoder _decoder;

    public AudioPreAnalysisServiceTests()
    {
        _decoder = new MockAudioDecoder();
        _sut = new AudioPreAnalysisService(_decoder);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    // ──────────────────────────────────────────────
    //  Constructor validation
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_NullDecoder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AudioPreAnalysisService(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidSensitivity_Throws(double sensitivity)
    {
        Assert.Throws<ArgumentException>(() => new AudioPreAnalysisService(_decoder, sensitivity));
    }

    [Fact]
    public void Constructor_ValidParameters_DoesNotThrow()
    {
        using var svc = new AudioPreAnalysisService(_decoder, 2.0);
        Assert.NotNull(svc);
    }

    // ──────────────────────────────────────────────
    //  Initial state
    // ──────────────────────────────────────────────

    [Fact]
    public void InitialState_CurrentBeatMapIsNull()
    {
        Assert.Null(_sut.CurrentBeatMap);
    }

    [Fact]
    public void InitialState_IsAnalyzingIsFalse()
    {
        Assert.False(_sut.IsAnalyzing);
    }

    // ──────────────────────────────────────────────
    //  AnalyzeAsync validation
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeAsync_InvalidPath_Throws(string? path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.AnalyzeAsync(path!));
    }

    // ──────────────────────────────────────────────
    //  Basic analysis — click track
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ClickTrack_ProducesBeatMap()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        const double durationMs = 5000;
        var click = SyntheticAudioGenerator.ClickTrack(120, durationMs, sampleRate);

        _decoder.SetAudio(click, sampleRate, durationMs);

        await _sut.AnalyzeAsync("test.mp4");

        Assert.NotNull(_sut.CurrentBeatMap);
        Assert.True(_sut.CurrentBeatMap!.Beats.Count > 0, "Should detect beats");
        Assert.True(_sut.CurrentBeatMap.DurationMs > 0, "Duration should be set");
    }

    [Fact]
    public async Task AnalyzeAsync_ClickTrack_BpmIsApproximatelyCorrect()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click = SyntheticAudioGenerator.ClickTrack(120, 8000, sampleRate);

        _decoder.SetAudio(click, sampleRate, 8000);

        await _sut.AnalyzeAsync("test.mp4");

        var map = _sut.CurrentBeatMap;
        Assert.NotNull(map);
        // BPM should be approximately 120, allow wide tolerance for DSP pipeline.
        Assert.True(map!.Bpm > 80 && map.Bpm < 160,
            $"Expected BPM near 120, got {map.Bpm:F1}");
    }

    [Fact]
    public async Task AnalyzeAsync_ClickTrack_WaveformIsGenerated()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, sampleRate);

        _decoder.SetAudio(click, sampleRate, 3000);

        await _sut.AnalyzeAsync("test.mp4");

        var map = _sut.CurrentBeatMap;
        Assert.NotNull(map);
        Assert.True(map!.WaveformSamples.Count > 0, "Waveform should have samples");
        Assert.True(map.WaveformSampleRate > 0, "Waveform sample rate should be set");
    }

    // ──────────────────────────────────────────────
    //  Silence — should produce empty or minimal beats
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_Silence_ProducesMinimalBeats()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var silence = SyntheticAudioGenerator.Silence(3000, sampleRate);

        _decoder.SetAudio(silence, sampleRate, 3000);

        await _sut.AnalyzeAsync("test.mp4");

        var map = _sut.CurrentBeatMap;
        Assert.NotNull(map);
        // Silence should produce very few or no beats.
        Assert.True(map!.Beats.Count <= 3, $"Silence produced too many beats: {map.Beats.Count}");
    }

    // ──────────────────────────────────────────────
    //  AnalysisComplete event
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_FiresAnalysisCompleteEvent()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click = SyntheticAudioGenerator.ClickTrack(120, 2000, sampleRate);

        _decoder.SetAudio(click, sampleRate, 2000);

        BeatMap? receivedMap = null;
        _sut.AnalysisComplete += map => receivedMap = map;

        await _sut.AnalyzeAsync("test.mp4");

        Assert.NotNull(receivedMap);
        Assert.Same(_sut.CurrentBeatMap, receivedMap);
    }

    // ──────────────────────────────────────────────
    //  AnalysisProgress event
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ReportsProgress()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, sampleRate);

        _decoder.SetAudio(click, sampleRate, 3000);

        var progressValues = new List<double>();
        _sut.AnalysisProgress += p => progressValues.Add(p);

        await _sut.AnalyzeAsync("test.mp4");

        Assert.True(progressValues.Count > 0, "Should have reported progress");
        // Final progress should be 1.0.
        Assert.Equal(1.0, progressValues[^1]);
    }

    [Fact]
    public async Task AnalyzeAsync_ProgressValuesAreInRange()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, sampleRate);

        _decoder.SetAudio(click, sampleRate, 3000);

        var progressValues = new List<double>();
        _sut.AnalysisProgress += p => progressValues.Add(p);

        await _sut.AnalyzeAsync("test.mp4");

        foreach (var p in progressValues)
            Assert.True(p >= 0.0 && p <= 1.0, $"Progress {p} out of range 0–1");
    }

    // ──────────────────────────────────────────────
    //  Cancellation
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_Cancellation_StopsAnalysis()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        // Long audio to ensure cancellation has time to fire.
        var audio = SyntheticAudioGenerator.Silence(30000, sampleRate);

        _decoder.SetAudio(audio, sampleRate, 30000, chunkSize: 4410); // small chunks

        using var cts = new CancellationTokenSource();
        int progressCount = 0;
        _sut.AnalysisProgress += _ =>
        {
            progressCount++;
            if (progressCount >= 3)
                cts.Cancel();
        };

        // Should not throw — cancellation is swallowed internally.
        await _sut.AnalyzeAsync("test.mp4", cts.Token);

        // BeatMap may or may not be set depending on when cancellation hit.
        // The key is that it completed without throwing.
    }

    [Fact]
    public async Task Cancel_DuringAnalysis_StopsGracefully()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var audio = SyntheticAudioGenerator.Silence(30000, sampleRate);

        _decoder.SetAudio(audio, sampleRate, 30000, chunkSize: 4410);

        int progressCount = 0;
        _sut.AnalysisProgress += _ =>
        {
            progressCount++;
            if (progressCount >= 3)
                _sut.Cancel();
        };

        await _sut.AnalyzeAsync("test.mp4");
        // Should complete without throwing.
    }

    // ──────────────────────────────────────────────
    //  AnalysisFailed event
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DecoderThrows_FiresAnalysisFailedEvent()
    {
        _decoder.SetException(new InvalidOperationException("decode error"));

        Exception? received = null;
        _sut.AnalysisFailed += ex => received = ex;

        await _sut.AnalyzeAsync("test.mp4");

        Assert.NotNull(received);
        Assert.IsType<InvalidOperationException>(received);
        Assert.Null(_sut.CurrentBeatMap);
    }

    // ──────────────────────────────────────────────
    //  Re-analysis replaces previous result
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_SecondCall_ReplacesResult()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click1 = SyntheticAudioGenerator.ClickTrack(120, 2000, sampleRate);
        _decoder.SetAudio(click1, sampleRate, 2000);
        await _sut.AnalyzeAsync("first.mp4");

        var map1 = _sut.CurrentBeatMap;
        Assert.NotNull(map1);

        var click2 = SyntheticAudioGenerator.ClickTrack(90, 3000, sampleRate);
        _decoder.SetAudio(click2, sampleRate, 3000);
        await _sut.AnalyzeAsync("second.mp4");

        var map2 = _sut.CurrentBeatMap;
        Assert.NotNull(map2);
        Assert.NotSame(map1, map2);
    }

    // ──────────────────────────────────────────────
    //  Sensitivity
    // ──────────────────────────────────────────────

    [Fact]
    public void UpdateSensitivity_ValidValue_DoesNotThrow()
    {
        _sut.UpdateSensitivity(2.5);
        // Should not throw.
    }

    // ──────────────────────────────────────────────
    //  48kHz sample rate
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_48kHz_Works()
    {
        const int sampleRate = TestConstants.SampleRate48000;
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, sampleRate);

        _decoder.SetAudio(click, sampleRate, 3000);

        await _sut.AnalyzeAsync("test.mp4");

        Assert.NotNull(_sut.CurrentBeatMap);
        Assert.True(_sut.CurrentBeatMap!.Beats.Count > 0);
    }

    // ──────────────────────────────────────────────
    //  Beat quantization in pipeline
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_BeatsAreSortedByTimestamp()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click = SyntheticAudioGenerator.ClickTrack(120, 5000, sampleRate);

        _decoder.SetAudio(click, sampleRate, 5000);

        await _sut.AnalyzeAsync("test.mp4");

        var map = _sut.CurrentBeatMap;
        Assert.NotNull(map);

        for (int i = 1; i < map!.Beats.Count; i++)
            Assert.True(map.Beats[i].TimestampMs >= map.Beats[i - 1].TimestampMs,
                $"Beat {i} at {map.Beats[i].TimestampMs}ms should be >= beat {i - 1} at {map.Beats[i - 1].TimestampMs}ms");
    }

    [Fact]
    public async Task AnalyzeAsync_BeatStrengthsAreInRange()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click = SyntheticAudioGenerator.ClickTrack(120, 3000, sampleRate);

        _decoder.SetAudio(click, sampleRate, 3000);

        await _sut.AnalyzeAsync("test.mp4");

        foreach (var beat in _sut.CurrentBeatMap!.Beats)
            Assert.True(beat.Strength >= 0 && beat.Strength <= 1,
                $"Beat strength {beat.Strength} out of range 0–1");
    }

    // ──────────────────────────────────────────────
    //  Duration calculated from samples when not provided
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_NoDurationProvided_CalculatesFromSamples()
    {
        const int sampleRate = TestConstants.SampleRate44100;
        var click = SyntheticAudioGenerator.ClickTrack(120, 2000, sampleRate);

        // Set audio with zero totalDurationMs — service should calculate duration from samples.
        _decoder.SetAudio(click, sampleRate, totalDurationMs: 0);

        await _sut.AnalyzeAsync("test.mp4");

        var map = _sut.CurrentBeatMap;
        Assert.NotNull(map);
        Assert.True(map!.DurationMs > 1500, $"Duration should be ~2000ms, got {map.DurationMs:F0}");
    }
}

/// <summary>
/// Mock audio decoder that yields synthetic PCM chunks for testing.
/// </summary>
internal class MockAudioDecoder : IAudioDecoder
{
    private float[] _samples = Array.Empty<float>();
    private int _sampleRate = TestConstants.SampleRate44100;
    private double _totalDurationMs;
    private int _chunkSize = 44100; // 1 second default
    private Exception? _exception;

    public void SetAudio(float[] samples, int sampleRate, double totalDurationMs, int chunkSize = 0)
    {
        _samples = samples;
        _sampleRate = sampleRate;
        _totalDurationMs = totalDurationMs;
        _chunkSize = chunkSize > 0 ? chunkSize : sampleRate; // default 1s chunks
        _exception = null;
    }

    public void SetException(Exception exception)
    {
        _exception = exception;
    }

    public async IAsyncEnumerable<AudioChunk> DecodeAsync(
        string mediaPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_exception != null)
            throw _exception;

        int offset = 0;
        while (offset < _samples.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int count = Math.Min(_chunkSize, _samples.Length - offset);
            var chunk = new float[count];
            Array.Copy(_samples, offset, chunk, 0, count);

            double timestampMs = offset * 1000.0 / _sampleRate;

            yield return new AudioChunk
            {
                Samples = chunk,
                SampleRate = _sampleRate,
                TimestampMs = timestampMs,
                TotalDurationMs = _totalDurationMs
            };

            offset += count;
            await Task.Yield(); // simulate async
        }
    }
}
