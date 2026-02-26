using PulsePlugin.Services;
using Xunit;

namespace PulsePlugin.Tests.Services;

public class AudioRingBufferTests
{
    [Fact]
    public void Constructor_ThrowsOnZeroCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioRingBuffer(0));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioRingBuffer(-1));
    }

    [Fact]
    public void NewBuffer_HasZeroAvailable()
    {
        var buffer = new AudioRingBuffer(1024);
        Assert.Equal(0, buffer.Available);
    }

    [Fact]
    public void Capacity_ReturnsConstructorValue()
    {
        var buffer = new AudioRingBuffer(2048);
        Assert.Equal(2048, buffer.Capacity);
    }

    [Fact]
    public void Write_ThenRead_ReturnsExactSamples()
    {
        var buffer = new AudioRingBuffer(1024);
        var input = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };
        buffer.Write(input);

        Assert.Equal(5, buffer.Available);

        var output = new float[5];
        int read = buffer.Read(output);

        Assert.Equal(5, read);
        Assert.Equal(input, output);
        Assert.Equal(0, buffer.Available);
    }

    [Fact]
    public void Write_EmptySpan_DoesNothing()
    {
        var buffer = new AudioRingBuffer(1024);
        buffer.Write(ReadOnlySpan<float>.Empty);
        Assert.Equal(0, buffer.Available);
    }

    [Fact]
    public void Read_EmptySpan_ReturnsZero()
    {
        var buffer = new AudioRingBuffer(1024);
        buffer.Write(new float[] { 1.0f });
        int read = buffer.Read(Span<float>.Empty);
        Assert.Equal(0, read);
        Assert.Equal(1, buffer.Available); // sample still there
    }

    [Fact]
    public void Read_EmptyBuffer_ReturnsZero()
    {
        var buffer = new AudioRingBuffer(1024);
        var output = new float[10];
        int read = buffer.Read(output);
        Assert.Equal(0, read);
    }

    [Fact]
    public void Read_LargerOutputThanAvailable_ReturnsOnlyAvailable()
    {
        var buffer = new AudioRingBuffer(1024);
        buffer.Write(new float[] { 1.0f, 2.0f, 3.0f });

        var output = new float[100];
        int read = buffer.Read(output);

        Assert.Equal(3, read);
        Assert.Equal(1.0f, output[0]);
        Assert.Equal(2.0f, output[1]);
        Assert.Equal(3.0f, output[2]);
    }

    [Fact]
    public void PartialRead_LeavesRemainder()
    {
        var buffer = new AudioRingBuffer(1024);
        buffer.Write(new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f });

        var output = new float[3];
        int read = buffer.Read(output);
        Assert.Equal(3, read);
        Assert.Equal(new float[] { 1.0f, 2.0f, 3.0f }, output);
        Assert.Equal(2, buffer.Available);

        // Read the rest
        output = new float[2];
        read = buffer.Read(output);
        Assert.Equal(2, read);
        Assert.Equal(new float[] { 4.0f, 5.0f }, output);
        Assert.Equal(0, buffer.Available);
    }

    [Fact]
    public void WrapAround_WriteAndRead()
    {
        // Small buffer to force wrap-around
        var buffer = new AudioRingBuffer(8);

        // Fill most of the buffer
        buffer.Write(new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f });
        Assert.Equal(5, buffer.Available);

        // Read some to advance read pointer
        var output = new float[3];
        buffer.Read(output);
        Assert.Equal(new float[] { 1.0f, 2.0f, 3.0f }, output);
        Assert.Equal(2, buffer.Available);

        // Write more — should wrap around
        buffer.Write(new float[] { 6.0f, 7.0f, 8.0f, 9.0f });
        Assert.Equal(6, buffer.Available);

        // Read all
        output = new float[6];
        int read = buffer.Read(output);
        Assert.Equal(6, read);
        Assert.Equal(new float[] { 4.0f, 5.0f, 6.0f, 7.0f, 8.0f, 9.0f }, output);
    }

    [Fact]
    public void Overflow_DropsOldestSamples()
    {
        var buffer = new AudioRingBuffer(4);

        // Write 3 samples (available capacity is 3 since one slot is sentinel)
        buffer.Write(new float[] { 1.0f, 2.0f, 3.0f });
        Assert.Equal(3, buffer.Available);

        // Write 2 more — should overflow, dropping oldest
        buffer.Write(new float[] { 4.0f, 5.0f });

        // Should have the most recent data, oldest dropped
        var output = new float[10];
        int read = buffer.Read(output);
        Assert.True(read > 0);

        // The buffer should contain some of the most recent samples
        // The exact behavior depends on implementation; key property:
        // most recent data should be readable
        var readData = output.AsSpan(0, read).ToArray();
        Assert.Contains(5.0f, readData);
    }

    [Fact]
    public void WriteExceedingCapacity_KeepsLastSamples()
    {
        var buffer = new AudioRingBuffer(4);

        // Write way more than capacity
        var huge = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f };
        buffer.Write(huge);

        // Should have the tail end of the data
        var output = new float[10];
        int read = buffer.Read(output);
        Assert.True(read > 0);
        Assert.True(read <= 4);

        // Last samples should be present
        var readData = output.AsSpan(0, read).ToArray();
        Assert.Contains(8.0f, readData);
    }

    [Fact]
    public void Clear_ResetsBuffer()
    {
        var buffer = new AudioRingBuffer(1024);
        buffer.Write(new float[] { 1.0f, 2.0f, 3.0f });
        Assert.Equal(3, buffer.Available);

        buffer.Clear();
        Assert.Equal(0, buffer.Available);

        // Read should return nothing
        var output = new float[10];
        int read = buffer.Read(output);
        Assert.Equal(0, read);
    }

    [Fact]
    public void Clear_ThenWriteRead_Works()
    {
        var buffer = new AudioRingBuffer(1024);
        buffer.Write(new float[] { 1.0f, 2.0f });
        buffer.Clear();
        
        buffer.Write(new float[] { 10.0f, 20.0f });
        var output = new float[2];
        int read = buffer.Read(output);
        
        Assert.Equal(2, read);
        Assert.Equal(new float[] { 10.0f, 20.0f }, output);
    }

    [Fact]
    public void MultipleWritesSingleRead()
    {
        var buffer = new AudioRingBuffer(1024);
        buffer.Write(new float[] { 1.0f, 2.0f });
        buffer.Write(new float[] { 3.0f, 4.0f });
        buffer.Write(new float[] { 5.0f });

        Assert.Equal(5, buffer.Available);

        var output = new float[5];
        int read = buffer.Read(output);
        Assert.Equal(5, read);
        Assert.Equal(new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f }, output);
    }

    [Fact]
    public void SingleWriteMultipleReads()
    {
        var buffer = new AudioRingBuffer(1024);
        buffer.Write(new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f });

        var out1 = new float[2];
        var out2 = new float[2];
        var out3 = new float[2];

        Assert.Equal(2, buffer.Read(out1));
        Assert.Equal(2, buffer.Read(out2));
        Assert.Equal(2, buffer.Read(out3));

        Assert.Equal(new float[] { 1.0f, 2.0f }, out1);
        Assert.Equal(new float[] { 3.0f, 4.0f }, out2);
        Assert.Equal(new float[] { 5.0f, 6.0f }, out3);
        Assert.Equal(0, buffer.Available);
    }

    [Fact]
    public void Available_UpdatesCorrectly()
    {
        var buffer = new AudioRingBuffer(100);

        Assert.Equal(0, buffer.Available);
        buffer.Write(new float[] { 1, 2, 3, 4, 5 });
        Assert.Equal(5, buffer.Available);

        var tmp = new float[2];
        buffer.Read(tmp);
        Assert.Equal(3, buffer.Available);

        buffer.Write(new float[] { 6, 7 });
        Assert.Equal(5, buffer.Available);

        tmp = new float[5];
        buffer.Read(tmp);
        Assert.Equal(0, buffer.Available);
    }

    [Fact]
    public void SingleSampleCapacity_WriteThenRead()
    {
        // Edge case: smallest possible buffer
        var buffer = new AudioRingBuffer(2); // need at least 2 for 1 usable slot

        buffer.Write(new float[] { 42.0f });
        Assert.Equal(1, buffer.Available);

        var output = new float[1];
        int read = buffer.Read(output);
        Assert.Equal(1, read);
        Assert.Equal(42.0f, output[0]);
    }
}
