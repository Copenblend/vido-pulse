using PulsePlugin.Models;

namespace PulsePlugin.Services;

/// <summary>
/// Pre-analyzes the complete audio track on media load, producing a <see cref="BeatMap"/>
/// with all detected beats, global BPM, and a downsampled waveform.
/// Runs on a background thread; cancellable and reports progress.
/// </summary>
internal sealed class AudioPreAnalysisService : IDisposable
{
    private readonly IAudioDecoder _decoder;
    private readonly OnsetDetector _onsetDetector;
    private readonly AmplitudeTracker _amplitudeTracker;
    private readonly BpmEstimator _bpmEstimator;

    /// <summary>Target waveform sample rate for the overview display (samples/sec).</summary>
    private const int WaveformTargetRate = 200;

    private CancellationTokenSource? _cts;
    private Task? _analysisTask;

    /// <summary>Fires with progress updates during analysis (0.0–1.0).</summary>
    public event Action<double>? AnalysisProgress;

    /// <summary>Fires when analysis is complete with the full beat map.</summary>
    public event Action<BeatMap>? AnalysisComplete;

    /// <summary>Fires on analysis failure.</summary>
    public event Action<Exception>? AnalysisFailed;

    /// <summary>Most recent analysis result (null if not yet analyzed).</summary>
    public BeatMap? CurrentBeatMap { get; private set; }

    /// <summary>Whether analysis is currently in progress.</summary>
    public bool IsAnalyzing => _analysisTask is { IsCompleted: false };

    public AudioPreAnalysisService(IAudioDecoder decoder, double sensitivity = 1.5)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        if (sensitivity <= 0)
            throw new ArgumentException("sensitivity must be positive.", nameof(sensitivity));

        _decoder = decoder;
        _onsetDetector = new OnsetDetector(sensitivity: sensitivity);
        _amplitudeTracker = new AmplitudeTracker(windowMs: 20.0);
        _bpmEstimator = new BpmEstimator();
    }

    /// <summary>
    /// Analyze the audio track from the given media path. Runs on a background thread.
    /// Cancels any in-progress analysis first.
    /// </summary>
    public Task AnalyzeAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            throw new ArgumentException("mediaPath must not be null or empty.", nameof(mediaPath));

        // Cancel any previous analysis.
        Cancel();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;

        _analysisTask = Task.Run(() => RunAnalysisAsync(mediaPath, token), token);
        return _analysisTask;
    }

    /// <summary>Cancel any in-progress analysis.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>Update onset detection sensitivity.</summary>
    public void UpdateSensitivity(double sensitivity)
    {
        _onsetDetector.SetSensitivity(sensitivity);
    }

    public void Dispose()
    {
        Cancel();
    }

    private async Task RunAnalysisAsync(string mediaPath, CancellationToken token)
    {
        try
        {
            _onsetDetector.Reset();
            _amplitudeTracker.Reset();
            _bpmEstimator.Reset();

            var allBeats = new List<BeatEvent>();
            var waveformSamples = new List<float>();
            double totalDurationMs = 0;
            long totalSamplesProcessed = 0;
            int sampleRate = 0;

            // Waveform downsampling state.
            double waveformAccum = 0;
            int waveformAccumCount = 0;
            int waveformDownsampleFactor = 1;

            await foreach (var chunk in _decoder.DecodeAsync(mediaPath, token))
            {
                token.ThrowIfCancellationRequested();

                sampleRate = chunk.SampleRate;
                if (chunk.TotalDurationMs > 0)
                    totalDurationMs = chunk.TotalDurationMs;

                // Calculate waveform downsample factor on first chunk.
                if (totalSamplesProcessed == 0 && sampleRate > 0)
                    waveformDownsampleFactor = Math.Max(1, sampleRate / WaveformTargetRate);

                // Run onset detection.
                var beats = _onsetDetector.Process(chunk.Samples, chunk.TimestampMs, sampleRate);
                foreach (var beat in beats)
                {
                    // Feed beat to BPM estimator for quantization.
                    _bpmEstimator.AddBeat(beat);

                    // Quantize the beat if BPM confidence is high enough.
                    double quantizedTime = _bpmEstimator.QuantizeBeat(beat.TimestampMs);
                    bool isQuantized = Math.Abs(quantizedTime - beat.TimestampMs) > 0.01;

                    allBeats.Add(new BeatEvent
                    {
                        TimestampMs = quantizedTime,
                        Strength = beat.Strength,
                        IsQuantized = isQuantized
                    });
                }

                // Compute amplitude for waveform overview.
                _amplitudeTracker.Process(chunk.Samples, chunk.TimestampMs, sampleRate);

                // Downsample for waveform display.
                for (int i = 0; i < chunk.Samples.Length; i++)
                {
                    waveformAccum += Math.Abs(chunk.Samples[i]);
                    waveformAccumCount++;

                    if (waveformAccumCount >= waveformDownsampleFactor)
                    {
                        waveformSamples.Add((float)(waveformAccum / waveformAccumCount));
                        waveformAccum = 0;
                        waveformAccumCount = 0;
                    }
                }

                totalSamplesProcessed += chunk.Samples.Length;

                // Report progress.
                if (totalDurationMs > 0)
                {
                    double progress = Math.Min(1.0, chunk.TimestampMs / totalDurationMs);
                    AnalysisProgress?.Invoke(progress);
                }
            }

            // Flush any remaining waveform accumulation.
            if (waveformAccumCount > 0)
                waveformSamples.Add((float)(waveformAccum / waveformAccumCount));

            // Calculate total duration from processed samples if not provided.
            if (totalDurationMs <= 0 && sampleRate > 0)
                totalDurationMs = totalSamplesProcessed * 1000.0 / sampleRate;

            // Sort beats by timestamp (quantization may have shifted ordering slightly).
            allBeats.Sort((a, b) => a.TimestampMs.CompareTo(b.TimestampMs));

            var bpmEstimate = _bpmEstimator.CurrentEstimate;

            var beatMap = new BeatMap
            {
                Beats = allBeats.AsReadOnly(),
                Bpm = bpmEstimate.Bpm,
                BpmConfidence = bpmEstimate.Confidence,
                DurationMs = totalDurationMs,
                WaveformSamples = waveformSamples.AsReadOnly(),
                WaveformSampleRate = sampleRate > 0 ? Math.Max(1, sampleRate / waveformDownsampleFactor) : WaveformTargetRate
            };

            CurrentBeatMap = beatMap;
            AnalysisProgress?.Invoke(1.0);
            AnalysisComplete?.Invoke(beatMap);
        }
        catch (OperationCanceledException)
        {
            // Expected — swallow.
        }
        catch (Exception ex)
        {
            AnalysisFailed?.Invoke(ex);
        }
    }
}
