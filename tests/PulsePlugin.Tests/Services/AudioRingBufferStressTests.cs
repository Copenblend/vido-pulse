using PulsePlugin.Services;
using Xunit;

namespace PulsePlugin.Tests.Services;

/// <summary>
/// Stress tests for AudioRingBuffer — concurrent producer/consumer,
/// buffer overflow under load, and sustained throughput.
/// </summary>
public class AudioRingBufferStressTests
{
    [Fact]
    public async Task ConcurrentProducerConsumer_NoDataCorruption()
    {
        // Large buffer so consumer can keep up — no overflow expected
        const int capacity = 65536;
        const int totalSamples = 50_000;
        const int chunkSize = 480;

        var buffer = new AudioRingBuffer(capacity);
        var readSamples = new List<float>(totalSamples);
        int producerDone = 0;

        var producerTask = Task.Run(() =>
        {
            int produced = 0;
            var chunk = new float[chunkSize];
            while (produced < totalSamples)
            {
                int count = Math.Min(chunkSize, totalSamples - produced);
                for (int i = 0; i < count; i++)
                    chunk[i] = produced + i;
                buffer.Write(chunk.AsSpan(0, count));
                produced += count;
                if (produced % (chunkSize * 10) == 0)
                    Thread.Yield();
            }
            Interlocked.Exchange(ref producerDone, 1);
        });

        var consumerTask = Task.Run(() =>
        {
            var output = new float[chunkSize * 2];
            while (true)
            {
                int read = buffer.Read(output);
                if (read > 0)
                {
                    for (int i = 0; i < read; i++)
                        readSamples.Add(output[i]);
                }
                else if (Volatile.Read(ref producerDone) == 1 && buffer.Available == 0)
                {
                    break;
                }
                else
                {
                    Thread.Yield();
                }
            }
        });

        await Task.WhenAll(producerTask, consumerTask);

        // With a large enough buffer and no overflow, we should get all samples
        Assert.Equal(totalSamples, readSamples.Count);

        // Monotonically increasing — no corruption
        for (int i = 1; i < readSamples.Count; i++)
        {
            Assert.True(readSamples[i] > readSamples[i - 1],
                $"Sample at index {i} ({readSamples[i]}) should be > previous ({readSamples[i - 1]})");
        }
    }

    [Fact]
    public async Task ConcurrentProducerConsumer_SlowConsumer_DropsGracefully()
    {
        // Small buffer forces frequent overflow — should not crash
        const int capacity = 256;
        const int totalSamples = 10_000;
        const int chunkSize = 64;

        var buffer = new AudioRingBuffer(capacity);
        int producerDone = 0;
        int totalRead = 0;

        var producerTask = Task.Run(() =>
        {
            var chunk = new float[chunkSize];
            int produced = 0;
            while (produced < totalSamples)
            {
                int count = Math.Min(chunkSize, totalSamples - produced);
                for (int i = 0; i < count; i++)
                    chunk[i] = produced + i;
                buffer.Write(chunk.AsSpan(0, count));
                produced += count;
            }
            Interlocked.Exchange(ref producerDone, 1);
        });

        var consumerTask = Task.Run(() =>
        {
            var output = new float[32];
            while (true)
            {
                int read = buffer.Read(output);
                if (read > 0)
                {
                    Interlocked.Add(ref totalRead, read);
                }
                else if (Volatile.Read(ref producerDone) == 1 && buffer.Available == 0)
                {
                    break;
                }
                else
                {
                    Thread.Yield();
                }
            }
        });

        await Task.WhenAll(producerTask, consumerTask);

        // Some samples should have been read (not all, due to overflow)
        Assert.True(totalRead > 0, "Should have read some samples");
        Assert.True(totalRead <= totalSamples, "Should not read more than was written");
    }

    [Fact]
    public async Task SustainedHighThroughput_NoException()
    {
        const int sampleRate = 48000;
        const int totalSamples = sampleRate / 2; // 0.5 seconds
        const int chunkSize = 960;
        const int capacity = sampleRate; // 1 second buffer — no overflow

        var buffer = new AudioRingBuffer(capacity);
        int producerDone = 0;
        int totalRead = 0;

        var producerTask = Task.Run(() =>
        {
            var chunk = new float[chunkSize];
            int written = 0;
            while (written < totalSamples)
            {
                int count = Math.Min(chunkSize, totalSamples - written);
                for (int i = 0; i < count; i++)
                    chunk[i] = (float)Math.Sin(2 * Math.PI * 440 * (written + i) / sampleRate);
                buffer.Write(chunk.AsSpan(0, count));
                written += count;
            }
            Interlocked.Exchange(ref producerDone, 1);
        });

        var consumerTask = Task.Run(() =>
        {
            var output = new float[chunkSize];
            while (true)
            {
                int read = buffer.Read(output);
                if (read > 0)
                {
                    Interlocked.Add(ref totalRead, read);
                }
                else if (Volatile.Read(ref producerDone) == 1 && buffer.Available == 0)
                {
                    break;
                }
                else
                {
                    Thread.Yield();
                }
            }
        });

        await Task.WhenAll(producerTask, consumerTask);

        Assert.True(totalRead > totalSamples / 2,
            $"Should read a significant amount. Read {totalRead} out of {totalSamples}");
    }

    [Fact]
    public async Task RapidClearDuringWriteRead_NoException()
    {
        const int capacity = 512;
        var buffer = new AudioRingBuffer(capacity);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var producerTask = Task.Run(() =>
        {
            var chunk = new float[64];
            while (!cts.IsCancellationRequested)
            {
                buffer.Write(chunk);
                Thread.Yield();
            }
        });

        var consumerTask = Task.Run(() =>
        {
            var output = new float[64];
            while (!cts.IsCancellationRequested)
            {
                buffer.Read(output);
                Thread.Yield();
            }
        });

        var clearTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                Thread.Sleep(10);
                buffer.Clear();
            }
        });

        await Task.WhenAll(producerTask, consumerTask, clearTask);
    }
}
