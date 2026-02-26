namespace PulsePlugin.Tests.TestUtilities;

/// <summary>
/// Generates synthetic PCM audio data for testing.
/// All output is mono float32 at the specified sample rate.
/// </summary>
internal static class SyntheticAudioGenerator
{
    /// <summary>
    /// Generate a click track — short impulses at a given BPM.
    /// Each click is a single-sample impulse of the specified amplitude.
    /// </summary>
    /// <param name="bpm">Beats per minute.</param>
    /// <param name="durationMs">Total duration in milliseconds.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="amplitude">Click amplitude (default 1.0).</param>
    /// <returns>Mono float32 samples.</returns>
    public static float[] ClickTrack(double bpm, double durationMs, int sampleRate, float amplitude = 1.0f)
    {
        int totalSamples = (int)(durationMs * sampleRate / 1000.0);
        var samples = new float[totalSamples];
        double beatIntervalSamples = sampleRate * 60.0 / bpm;

        double pos = 0;
        while (pos < totalSamples)
        {
            int idx = (int)pos;
            if (idx < totalSamples)
            {
                // Write a short click (a few samples) for a more realistic impulse
                int clickLen = Math.Min(8, totalSamples - idx);
                for (int i = 0; i < clickLen; i++)
                {
                    // Quick decay envelope
                    float env = amplitude * (1.0f - (float)i / clickLen);
                    samples[idx + i] = env;
                }
            }
            pos += beatIntervalSamples;
        }

        return samples;
    }

    /// <summary>
    /// Generate a sine wave at the specified frequency.
    /// </summary>
    /// <param name="frequencyHz">Frequency in Hz.</param>
    /// <param name="durationMs">Total duration in milliseconds.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="amplitude">Peak amplitude (default 0.5).</param>
    /// <returns>Mono float32 samples.</returns>
    public static float[] SineWave(double frequencyHz, double durationMs, int sampleRate, float amplitude = 0.5f)
    {
        int totalSamples = (int)(durationMs * sampleRate / 1000.0);
        var samples = new float[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            samples[i] = amplitude * (float)Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate);
        }

        return samples;
    }

    /// <summary>
    /// Generate silence.
    /// </summary>
    /// <param name="durationMs">Duration in milliseconds.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>Mono float32 samples (all zeros).</returns>
    public static float[] Silence(double durationMs, int sampleRate)
    {
        int totalSamples = (int)(durationMs * sampleRate / 1000.0);
        return new float[totalSamples];
    }

    /// <summary>
    /// Generate white noise (uniformly distributed random samples).
    /// </summary>
    /// <param name="durationMs">Duration in milliseconds.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="amplitude">Peak amplitude (default 0.3).</param>
    /// <param name="seed">Random seed for reproducibility.</param>
    /// <returns>Mono float32 samples.</returns>
    public static float[] WhiteNoise(double durationMs, int sampleRate, float amplitude = 0.3f, int seed = 42)
    {
        int totalSamples = (int)(durationMs * sampleRate / 1000.0);
        var samples = new float[totalSamples];
        var rng = new Random(seed);

        for (int i = 0; i < totalSamples; i++)
        {
            samples[i] = amplitude * (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        return samples;
    }

    /// <summary>
    /// Generate interleaved stereo from a mono source by duplicating channels.
    /// </summary>
    /// <param name="mono">Mono samples.</param>
    /// <returns>Interleaved stereo samples (2x length).</returns>
    public static float[] MonoToStereo(float[] mono)
    {
        var stereo = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++)
        {
            stereo[i * 2] = mono[i];
            stereo[i * 2 + 1] = mono[i];
        }
        return stereo;
    }

    /// <summary>
    /// Convert float samples to a byte buffer (float32 PCM format), simulating
    /// the buffer format from <see cref="Vido.Core.Playback.AudioSampleEventArgs"/>.
    /// </summary>
    /// <param name="samples">Float samples.</param>
    /// <returns>Byte buffer.</returns>
    public static byte[] ToByteBuffer(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
