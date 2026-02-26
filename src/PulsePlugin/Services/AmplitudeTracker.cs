namespace PulsePlugin.Services;

/// <summary>
/// RMS amplitude envelope follower. Computes the root-mean-square amplitude of
/// mono audio samples over a configurable time window.
/// </summary>
/// <remarks>
/// Used during both pre-analysis (to build the waveform display data) and
/// live playback (to provide real-time amplitude for stroke intensity scaling).
/// All input is expected as mono float32 PCM samples.
/// </remarks>
internal sealed class AmplitudeTracker
{
    private readonly double _windowMs;

    // Accumulator state for partial windows
    private double _sumOfSquares;
    private int _samplesInWindow;
    private int _windowSizeSamples;
    private double _currentTimestampMs;
    private double _currentAmplitude;

    /// <summary>
    /// Creates a new amplitude tracker with the specified RMS window duration.
    /// </summary>
    /// <param name="windowMs">RMS window duration in milliseconds (default 20ms).</param>
    /// <exception cref="ArgumentOutOfRangeException">Window must be positive.</exception>
    public AmplitudeTracker(double windowMs = 20.0)
    {
        if (windowMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowMs), "Window duration must be positive.");
        _windowMs = windowMs;
    }

    /// <summary>
    /// Current smoothed amplitude (0.0–1.0).
    /// </summary>
    public double CurrentAmplitude => _currentAmplitude;

    /// <summary>
    /// Process mono samples and return per-window RMS values.
    /// </summary>
    /// <param name="monoSamples">Mono float32 PCM samples.</param>
    /// <param name="startTimeMs">Timestamp of the first sample in milliseconds.</param>
    /// <param name="sampleRate">Audio sample rate in Hz.</param>
    /// <returns>List of (timestamp, RMS) pairs for each completed window.</returns>
    public IReadOnlyList<(double TimestampMs, double Rms)> Process(
        ReadOnlySpan<float> monoSamples, double startTimeMs, int sampleRate)
    {
        if (monoSamples.IsEmpty || sampleRate <= 0)
            return Array.Empty<(double, double)>();

        int windowSize = (int)(sampleRate * _windowMs / 1000.0);
        if (windowSize <= 0) windowSize = 1;

        // When sample rate changes (e.g., first call or new media), reset window state
        if (_windowSizeSamples != windowSize)
        {
            _windowSizeSamples = windowSize;
            _sumOfSquares = 0;
            _samplesInWindow = 0;
        }

        _currentTimestampMs = startTimeMs;

        var results = new List<(double TimestampMs, double Rms)>();

        for (int i = 0; i < monoSamples.Length; i++)
        {
            float sample = monoSamples[i];
            _sumOfSquares += sample * (double)sample;
            _samplesInWindow++;

            if (_samplesInWindow >= _windowSizeSamples)
            {
                double rms = Math.Sqrt(_sumOfSquares / _samplesInWindow);
                // Clamp to 0–1 range (float PCM is nominally -1..1, so RMS max is 1.0)
                rms = Math.Min(rms, 1.0);

                double windowTimestamp = startTimeMs + (i - _samplesInWindow + 1) * 1000.0 / sampleRate;
                results.Add((windowTimestamp, rms));

                _currentAmplitude = rms;
                _sumOfSquares = 0;
                _samplesInWindow = 0;
            }
        }

        return results;
    }

    /// <summary>
    /// Downmix interleaved multi-channel audio to mono by averaging all channels.
    /// </summary>
    /// <param name="interleaved">Interleaved float32 PCM samples.</param>
    /// <param name="channels">Number of channels.</param>
    /// <returns>Mono samples array.</returns>
    public static float[] DownmixToMono(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels), "Channels must be positive.");

        if (channels == 1)
        {
            return interleaved.ToArray();
        }

        int monoLength = interleaved.Length / channels;
        var mono = new float[monoLength];
        float scale = 1f / channels;

        for (int i = 0; i < monoLength; i++)
        {
            float sum = 0;
            int baseIdx = i * channels;
            for (int ch = 0; ch < channels; ch++)
            {
                sum += interleaved[baseIdx + ch];
            }
            mono[i] = sum * scale;
        }

        return mono;
    }

    /// <summary>
    /// Convert a <see cref="ReadOnlyMemory{T}"/> byte buffer (float32 PCM) to a float span,
    /// then downmix to mono. This is the primary entry point for processing
    /// <see cref="Vido.Core.Playback.AudioSampleEventArgs"/> buffers.
    /// </summary>
    /// <param name="buffer">Raw byte buffer containing float32 interleaved PCM.</param>
    /// <param name="sampleCount">Samples per channel.</param>
    /// <param name="channels">Number of channels.</param>
    /// <returns>Mono float samples.</returns>
    public static float[] ByteBufferToMono(ReadOnlyMemory<byte> buffer, int sampleCount, int channels)
    {
        int totalFloats = sampleCount * channels;
        var floats = new float[totalFloats];
        Buffer.BlockCopy(buffer.ToArray(), 0, floats, 0, totalFloats * sizeof(float));

        return DownmixToMono(floats, channels);
    }

    /// <summary>
    /// Reset the tracker to its initial state.
    /// </summary>
    public void Reset()
    {
        _sumOfSquares = 0;
        _samplesInWindow = 0;
        _windowSizeSamples = 0;
        _currentAmplitude = 0;
        _currentTimestampMs = 0;
    }
}
