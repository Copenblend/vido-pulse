namespace PulsePlugin.Services;

/// <summary>
/// Abstraction for decoding audio from a media file to PCM float32 mono samples.
/// The real implementation wraps FFmpeg process decoding; tests use a mock.
/// </summary>
internal interface IAudioDecoder
{
    /// <summary>
    /// Decode the audio track from the given media file path.
    /// Yields chunks of mono float32 PCM samples along with metadata.
    /// </summary>
    /// <param name="mediaPath">Path to the media file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of decoded audio chunks.</returns>
    IAsyncEnumerable<AudioChunk> DecodeAsync(string mediaPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// A chunk of decoded mono PCM audio.
/// </summary>
internal sealed class AudioChunk
{
    /// <summary>Mono float32 PCM samples.</summary>
    public required float[] Samples { get; init; }

    /// <summary>Sample rate in Hz.</summary>
    public required int SampleRate { get; init; }

    /// <summary>Timestamp of the first sample in this chunk, in milliseconds.</summary>
    public required double TimestampMs { get; init; }

    /// <summary>Total duration of the source audio in milliseconds (0 if unknown).</summary>
    public double TotalDurationMs { get; init; }
}
