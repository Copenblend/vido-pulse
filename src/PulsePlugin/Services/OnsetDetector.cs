using System.Numerics;
using PulsePlugin.Models;

namespace PulsePlugin.Services;

/// <summary>
/// Detects beat onsets using spectral flux with adaptive thresholding.
/// Processes mono float32 PCM in streaming fashion — accumulates across calls.
/// </summary>
internal sealed class OnsetDetector
{
    private readonly int _fftSize;
    private readonly int _hopSize;
    private double _sensitivity;

    /// <summary>Number of spectral flux values in the mean window (~0.5s).</summary>
    private readonly int _meanWindowSize;

    /// <summary>Minimum inter-onset interval in seconds (100ms = max 600 BPM).</summary>
    private const double MinInterOnsetSeconds = 0.100;

    // Precomputed Hann window coefficients.
    private readonly float[] _hannWindow;

    // Accumulation buffer for incoming samples.
    private readonly float[] _accumBuffer;
    private int _accumCount;

    // FFT scratch buffers.
    private readonly Complex[] _fftBuffer;
    private readonly double[] _magnitudeSpectrum;
    private readonly double[] _previousMagnitude;
    private bool _hasPreviousMagnitude;

    // Spectral flux history for adaptive threshold.
    private readonly double[] _fluxHistory;
    private int _fluxHistoryCount;
    private int _fluxHistoryIndex;

    // Running sample position for timestamp calculation.
    private long _samplePosition;
    private double _baseTimeMs;
    private bool _baseTimeSet;

    // Last onset time to enforce minimum inter-onset interval.
    private double _lastOnsetTimeMs = double.NegativeInfinity;

    /// <param name="fftSize">FFT window size. Must be a power of 2 (default 2048).</param>
    /// <param name="hopSize">Hop size in samples (default 512).</param>
    /// <param name="sensitivity">Threshold multiplier (default 1.5). Higher = fewer detections.</param>
    public OnsetDetector(int fftSize = 2048, int hopSize = 512, double sensitivity = 1.5)
    {
        if (fftSize < 2 || (fftSize & (fftSize - 1)) != 0)
            throw new ArgumentException("fftSize must be a power of 2 and >= 2.", nameof(fftSize));
        if (hopSize < 1 || hopSize > fftSize)
            throw new ArgumentException("hopSize must be between 1 and fftSize.", nameof(hopSize));
        if (sensitivity <= 0)
            throw new ArgumentException("sensitivity must be positive.", nameof(sensitivity));

        _fftSize = fftSize;
        _hopSize = hopSize;
        _sensitivity = sensitivity;

        // ~0.5s of flux history based on hop rate at a typical 44100 Hz sample rate.
        // hopsPerSecond ≈ sampleRate / hopSize. We'll use a fixed count that gives
        // ~0.5s at 44100: ceil(0.5 * 44100 / 512) = 44. Works well for 48000 too.
        _meanWindowSize = Math.Max(3, (int)Math.Ceiling(0.5 * 44100 / hopSize));

        _hannWindow = ComputeHannWindow(fftSize);
        _accumBuffer = new float[fftSize + hopSize]; // extra room to avoid frequent shifting
        _accumCount = 0;

        _fftBuffer = new Complex[fftSize];
        _magnitudeSpectrum = new double[fftSize / 2 + 1];
        _previousMagnitude = new double[fftSize / 2 + 1];
        _hasPreviousMagnitude = false;

        _fluxHistory = new double[_meanWindowSize];
        _fluxHistoryCount = 0;
        _fluxHistoryIndex = 0;
    }

    /// <summary>
    /// Process a chunk of mono samples. Returns detected beat onsets.
    /// Call repeatedly with sequential chunks; state is accumulated across calls.
    /// </summary>
    /// <param name="monoSamples">Mono float32 PCM samples.</param>
    /// <param name="startTimeMs">Media timestamp of the first sample in this chunk.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>Detected beat events for this chunk.</returns>
    public IReadOnlyList<BeatEvent> Process(ReadOnlySpan<float> monoSamples, double startTimeMs, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentException("sampleRate must be positive.", nameof(sampleRate));

        if (!_baseTimeSet)
        {
            _baseTimeMs = startTimeMs;
            _samplePosition = 0;
            _baseTimeSet = true;
        }

        var results = new List<BeatEvent>();
        int inputOffset = 0;

        while (inputOffset < monoSamples.Length)
        {
            // Fill accumulation buffer.
            int spaceInAccum = _accumBuffer.Length - _accumCount;
            int toCopy = Math.Min(spaceInAccum, monoSamples.Length - inputOffset);
            monoSamples.Slice(inputOffset, toCopy).CopyTo(_accumBuffer.AsSpan(_accumCount));
            _accumCount += toCopy;
            inputOffset += toCopy;

            // Process all available complete frames.
            while (_accumCount >= _fftSize)
            {
                ProcessFrame(_accumBuffer.AsSpan(0, _fftSize), sampleRate, results);
                _samplePosition += _hopSize;

                // Shift buffer by hopSize.
                int remaining = _accumCount - _hopSize;
                if (remaining > 0)
                    Array.Copy(_accumBuffer, _hopSize, _accumBuffer, 0, remaining);
                _accumCount = remaining;
            }
        }

        return results;
    }

    /// <summary>Update sensitivity at runtime.</summary>
    public void SetSensitivity(double sensitivity)
    {
        if (sensitivity <= 0)
            throw new ArgumentException("sensitivity must be positive.", nameof(sensitivity));
        _sensitivity = sensitivity;
    }

    /// <summary>Reset all internal state for a new audio stream.</summary>
    public void Reset()
    {
        _accumCount = 0;
        _hasPreviousMagnitude = false;
        _fluxHistoryCount = 0;
        _fluxHistoryIndex = 0;
        _samplePosition = 0;
        _baseTimeSet = false;
        _lastOnsetTimeMs = double.NegativeInfinity;
        Array.Clear(_previousMagnitude);
        Array.Clear(_fluxHistory);
    }

    private void ProcessFrame(ReadOnlySpan<float> frame, int sampleRate, List<BeatEvent> results)
    {
        // Apply Hann window and fill FFT buffer.
        for (int i = 0; i < _fftSize; i++)
            _fftBuffer[i] = new Complex(frame[i] * _hannWindow[i], 0);

        // In-place FFT.
        Fft(_fftBuffer);

        // Compute magnitude spectrum (only positive frequencies).
        int specLen = _fftSize / 2 + 1;
        for (int i = 0; i < specLen; i++)
            _magnitudeSpectrum[i] = _fftBuffer[i].Magnitude;

        if (!_hasPreviousMagnitude)
        {
            // First frame — store and skip.
            Array.Copy(_magnitudeSpectrum, _previousMagnitude, specLen);
            _hasPreviousMagnitude = true;
            return;
        }

        // Compute spectral flux: sum of positive differences.
        double flux = 0;
        for (int i = 0; i < specLen; i++)
        {
            double diff = _magnitudeSpectrum[i] - _previousMagnitude[i];
            if (diff > 0)
                flux += diff;
        }

        // Store current as previous.
        Array.Copy(_magnitudeSpectrum, _previousMagnitude, specLen);

        // Add flux to history for adaptive threshold.
        _fluxHistory[_fluxHistoryIndex] = flux;
        _fluxHistoryIndex = (_fluxHistoryIndex + 1) % _meanWindowSize;
        if (_fluxHistoryCount < _meanWindowSize)
            _fluxHistoryCount++;

        // Compute adaptive threshold = mean(flux history) * sensitivity.
        // Mean-based threshold handles sparse signals (clicks in silence) better
        // than median, which collapses to 0 when most frames are silent.
        double mean = ComputeMean(_fluxHistory, _fluxHistoryCount);
        double threshold = mean * _sensitivity;

        // Absolute minimum flux floor to filter pure numerical noise in silence.
        const double absoluteMinFlux = 0.01;

        // Check onset: flux must exceed both the adaptive threshold and the noise floor.
        if (flux > threshold && flux > absoluteMinFlux)
        {
            double timeMs = _baseTimeMs + (_samplePosition * 1000.0 / sampleRate);

            // Enforce minimum inter-onset interval.
            if (timeMs - _lastOnsetTimeMs >= MinInterOnsetSeconds * 1000.0)
            {
                // Strength: ratio of flux to threshold, clamped to 0–1.
                double strength = Math.Min(1.0, flux / (threshold * 2.0));

                results.Add(new BeatEvent
                {
                    TimestampMs = timeMs,
                    Strength = strength,
                    IsQuantized = false
                });

                _lastOnsetTimeMs = timeMs;
            }
        }
    }

    /// <summary>Compute the mean of the first <paramref name="count"/> elements.</summary>
    private static double ComputeMean(double[] values, int count)
    {
        if (count == 0) return 0;

        double sum = 0;
        for (int i = 0; i < count; i++)
            sum += values[i];
        return sum / count;
    }

    /// <summary>Precompute Hann window coefficients.</summary>
    private static float[] ComputeHannWindow(int size)
    {
        var window = new float[size];
        for (int i = 0; i < size; i++)
            window[i] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / (size - 1)));
        return window;
    }

    /// <summary>
    /// In-place radix-2 Cooley-Tukey FFT.
    /// Input length must be a power of 2.
    /// </summary>
    internal static void Fft(Complex[] buffer)
    {
        int n = buffer.Length;
        if (n < 2) return;

        // Bit-reversal permutation.
        int bits = (int)Math.Log2(n);
        for (int i = 0; i < n; i++)
        {
            int j = BitReverse(i, bits);
            if (j > i)
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        // Butterfly stages.
        for (int size = 2; size <= n; size *= 2)
        {
            int halfSize = size / 2;
            double angle = -2.0 * Math.PI / size;

            for (int start = 0; start < n; start += size)
            {
                for (int k = 0; k < halfSize; k++)
                {
                    Complex twiddle = Complex.FromPolarCoordinates(1.0, angle * k);
                    Complex even = buffer[start + k];
                    Complex odd = buffer[start + k + halfSize] * twiddle;

                    buffer[start + k] = even + odd;
                    buffer[start + k + halfSize] = even - odd;
                }
            }
        }
    }

    /// <summary>Reverse the bottom <paramref name="bits"/> bits of <paramref name="value"/>.</summary>
    private static int BitReverse(int value, int bits)
    {
        int result = 0;
        for (int i = 0; i < bits; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }
}
