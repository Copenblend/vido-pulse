namespace PulsePlugin.Services;

/// <summary>
/// Tracks real-time amplitude during playback via submitted audio samples.
/// Uses an <see cref="AudioRingBuffer"/> for thread-safe sample transfer from
/// the decode thread and an <see cref="AmplitudeTracker"/> for RMS computation.
/// </summary>
internal sealed class LiveAmplitudeService
{
    private readonly AudioRingBuffer _ringBuffer;
    private readonly AmplitudeTracker _tracker;

    /// <summary>Processing chunk size — read from ring buffer in ~20ms chunks at 48kHz.</summary>
    private const int ProcessChunkSize = 960;

    private int _sampleRate;
    private int _channels;
    private bool _running;
    private double _positionMs;

    /// <summary>Fires with current RMS amplitude (0.0–1.0) after each processing pass.</summary>
    public event Action<double>? AmplitudeUpdated;

    /// <summary>Current smoothed amplitude (0.0–1.0).</summary>
    public double CurrentAmplitude => _tracker.CurrentAmplitude;

    /// <param name="bufferCapacity">Ring buffer capacity in mono samples (default ~1s at 48kHz).</param>
    /// <param name="windowMs">RMS window duration in ms.</param>
    public LiveAmplitudeService(int bufferCapacity = 48000, double windowMs = 20.0)
    {
        _ringBuffer = new AudioRingBuffer(bufferCapacity);
        _tracker = new AmplitudeTracker(windowMs);
    }

    /// <summary>
    /// Submit audio samples from the decode thread (called via AudioSamplesAvailable event).
    /// Thread-safe — writes into the ring buffer.
    /// </summary>
    /// <param name="buffer">Raw audio byte buffer (interleaved float32 PCM).</param>
    /// <param name="sampleCount">Number of samples per channel.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="channels">Number of channels.</param>
    public void SubmitSamples(ReadOnlyMemory<byte> buffer, int sampleCount, int sampleRate, int channels)
    {
        if (!_running) return;

        _sampleRate = sampleRate;
        _channels = channels;

        // Convert to mono float and write to ring buffer.
        var mono = AmplitudeTracker.ByteBufferToMono(buffer, sampleCount, channels);
        _ringBuffer.Write(mono);
    }

    /// <summary>
    /// Process available samples from the ring buffer and update amplitude.
    /// Call this periodically from the UI/engine tick (~50–60Hz).
    /// </summary>
    /// <param name="currentPositionMs">Current media playback position in ms.</param>
    public void ProcessAvailable(double currentPositionMs)
    {
        if (!_running || _sampleRate <= 0) return;

        _positionMs = currentPositionMs;

        var readBuffer = new float[ProcessChunkSize];
        int totalRead = 0;

        // Drain all available samples from the ring buffer.
        while (_ringBuffer.Available > 0)
        {
            int read = _ringBuffer.Read(readBuffer);
            if (read == 0) break;

            _tracker.Process(readBuffer.AsSpan(0, read), _positionMs, _sampleRate);
            totalRead += read;
        }

        if (totalRead > 0)
            AmplitudeUpdated?.Invoke(_tracker.CurrentAmplitude);
    }

    /// <summary>Start accepting and processing samples.</summary>
    public void Start()
    {
        _running = true;
    }

    /// <summary>Stop accepting samples.</summary>
    public void Stop()
    {
        _running = false;
    }

    /// <summary>Reset all state (ring buffer, tracker).</summary>
    public void Reset()
    {
        _running = false;
        _ringBuffer.Clear();
        _tracker.Reset();
        _sampleRate = 0;
        _channels = 0;
        _positionMs = 0;
    }
}
