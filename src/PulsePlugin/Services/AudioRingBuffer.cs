using System.Runtime.CompilerServices;
using System.Threading;

namespace PulsePlugin.Services;

/// <summary>
/// Lock-free single-producer, single-consumer (SPSC) ring buffer for float audio samples.
/// Bridges the FFmpeg decode thread to analysis/amplitude tracking threads.
/// </summary>
/// <remarks>
/// <para>
/// Thread safety: exactly one thread may call <see cref="Write"/> (producer/decode thread)
/// and exactly one thread may call <see cref="Read"/> (consumer/analysis thread).
/// <see cref="Available"/> and <see cref="Clear"/> are safe to call from either thread.
/// </para>
/// <para>
/// Overflow policy: when the buffer is full, <see cref="Write"/> drops the oldest samples
/// by advancing the read position. This is acceptable for audio analysis where occasional
/// data loss is preferable to blocking the decode thread.
/// </para>
/// </remarks>
internal sealed class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _capacity;

    // Volatile positions — only producer writes _writePos, only consumer writes _readPos.
    // Both may read each other's position for Available/overflow detection.
    private volatile int _writePos;
    private volatile int _readPos;

    /// <summary>
    /// Creates a new ring buffer with the specified capacity in mono samples.
    /// </summary>
    /// <param name="capacitySamples">Maximum number of float samples the buffer can hold.</param>
    /// <exception cref="ArgumentOutOfRangeException">Capacity must be positive.</exception>
    public AudioRingBuffer(int capacitySamples)
    {
        if (capacitySamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacitySamples), "Capacity must be positive.");

        _capacity = capacitySamples;
        _buffer = new float[capacitySamples];
    }

    /// <summary>
    /// Number of samples available to read.
    /// </summary>
    public int Available
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            int write = _writePos;
            int read = _readPos;
            int available = write - read;
            if (available < 0) available += _capacity;
            return available;
        }
    }

    /// <summary>
    /// Total capacity in samples.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Write samples from the decode thread. If the buffer is full, oldest samples are
    /// dropped (read position is advanced) to make room.
    /// </summary>
    /// <param name="samples">Samples to write.</param>
    public void Write(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;

        int count = samples.Length;

        // If writing more than capacity, only keep the last _capacity samples
        if (count >= _capacity)
        {
            samples = samples.Slice(count - _capacity + 1);
            count = samples.Length;
            // Reset positions and bulk-copy
            samples.CopyTo(_buffer.AsSpan(0, count));
            _readPos = 0;
            Thread.MemoryBarrier();
            _writePos = count;
            return;
        }

        int write = _writePos;
        int read = _readPos;

        // Check if we need to drop old data to make room
        int available = write - read;
        if (available < 0) available += _capacity;
        int freeSpace = _capacity - 1 - available; // -1 because write==read means empty

        if (count > freeSpace)
        {
            // Advance read to discard oldest samples
            int discard = count - freeSpace;
            int newRead = (read + discard) % _capacity;
            _readPos = newRead;
            Thread.MemoryBarrier();
        }

        // Write in up to two segments (wrap-around)
        int firstChunk = Math.Min(count, _capacity - write);
        samples.Slice(0, firstChunk).CopyTo(_buffer.AsSpan(write, firstChunk));

        if (firstChunk < count)
        {
            int secondChunk = count - firstChunk;
            samples.Slice(firstChunk, secondChunk).CopyTo(_buffer.AsSpan(0, secondChunk));
        }

        Thread.MemoryBarrier();
        _writePos = (write + count) % _capacity;
    }

    /// <summary>
    /// Read available samples into the output buffer. Returns the number of samples actually read.
    /// </summary>
    /// <param name="output">Destination buffer.</param>
    /// <returns>Number of samples copied into <paramref name="output"/>.</returns>
    public int Read(Span<float> output)
    {
        if (output.IsEmpty) return 0;

        int read = _readPos;
        int write = _writePos;

        int available = write - read;
        if (available < 0) available += _capacity;
        if (available == 0) return 0;

        int count = Math.Min(available, output.Length);

        // Read in up to two segments (wrap-around)
        int firstChunk = Math.Min(count, _capacity - read);
        _buffer.AsSpan(read, firstChunk).CopyTo(output.Slice(0, firstChunk));

        if (firstChunk < count)
        {
            int secondChunk = count - firstChunk;
            _buffer.AsSpan(0, secondChunk).CopyTo(output.Slice(firstChunk, secondChunk));
        }

        Thread.MemoryBarrier();
        _readPos = (read + count) % _capacity;

        return count;
    }

    /// <summary>
    /// Discard all buffered samples, resetting read and write positions.
    /// </summary>
    public void Clear()
    {
        _readPos = 0;
        Thread.MemoryBarrier();
        _writePos = 0;
    }
}
