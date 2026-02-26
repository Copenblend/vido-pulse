using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;

namespace PulsePlugin.Services;

/// <summary>
/// Decodes audio from a media file using FFmpeg.AutoGen (in-process C API bindings).
/// The host application initialises <c>DynamicallyLoadedBindings</c> at startup,
/// so the native libraries are already loaded when this class is used.
/// Outputs mono float32 PCM samples at 44100 Hz.
/// </summary>
internal sealed class FfmpegAudioDecoder : IAudioDecoder
{
    /// <summary>Target sample rate for decoded audio.</summary>
    private const int TargetSampleRate = 44100;

    /// <summary>Target channels (mono).</summary>
    private const int TargetChannels = 1;

    /// <summary>Chunk size in samples (~100 ms at 44100 Hz).</summary>
    private const int ChunkSamples = 4410;

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioChunk> DecodeAsync(
        string mediaPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // All FFmpeg work is synchronous; we wrap in Task.Run to keep the
        // async enumerable pattern and avoid blocking the caller's thread.
        var channel = System.Threading.Channels.Channel.CreateBounded<AudioChunk>(
            new System.Threading.Channels.BoundedChannelOptions(4)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
            });

        var decodeTask = Task.Run(() => DecodeToChannel(mediaPath, channel.Writer, cancellationToken), cancellationToken);

        await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return chunk;
        }

        // Propagate any exception from the decode task.
        await decodeTask;
    }

    /// <summary>
    /// Performs the actual FFmpeg decode loop, writing <see cref="AudioChunk"/>s
    /// into the channel writer.
    /// </summary>
    private static unsafe void DecodeToChannel(
        string mediaPath,
        System.Threading.Channels.ChannelWriter<AudioChunk> writer,
        CancellationToken ct)
    {
        AVFormatContext* fmtCtx = null;
        AVCodecContext* codecCtx = null;
        SwrContext* swrCtx = null;
        AVFrame* frame = null;
        AVPacket* packet = null;

        try
        {
            // ── Open input ──
            var result = ffmpeg.avformat_open_input(&fmtCtx, mediaPath, null, null);
            if (result < 0)
                throw new InvalidOperationException($"Failed to open '{mediaPath}': {ErrorString(result)}");

            result = ffmpeg.avformat_find_stream_info(fmtCtx, null);
            if (result < 0)
                throw new InvalidOperationException($"Failed to find stream info: {ErrorString(result)}");

            // ── Find audio stream ──
            int audioIdx = ffmpeg.av_find_best_stream(fmtCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (audioIdx < 0)
                throw new InvalidOperationException("No audio stream found in media file.");

            var stream = fmtCtx->streams[audioIdx];
            var codecPar = stream->codecpar;

            // ── Open codec ──
            var codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);
            if (codec == null)
                throw new InvalidOperationException($"Unsupported audio codec: {codecPar->codec_id}");

            codecCtx = ffmpeg.avcodec_alloc_context3(codec);
            if (codecCtx == null)
                throw new InvalidOperationException("Failed to allocate audio codec context.");

            result = ffmpeg.avcodec_parameters_to_context(codecCtx, codecPar);
            if (result < 0)
                throw new InvalidOperationException($"Failed to copy codec params: {ErrorString(result)}");

            codecCtx->thread_count = Math.Min(Environment.ProcessorCount, 4);

            result = ffmpeg.avcodec_open2(codecCtx, codec, null);
            if (result < 0)
                throw new InvalidOperationException($"Failed to open audio codec: {ErrorString(result)}");

            // ── Probe total duration ──
            double totalDurationMs = 0;
            if (fmtCtx->duration > 0)
                totalDurationMs = fmtCtx->duration / (double)ffmpeg.AV_TIME_BASE * 1000.0;
            else if (stream->duration > 0)
                totalDurationMs = stream->duration * stream->time_base.num * 1000.0 / stream->time_base.den;

            // ── Init resampler: source → mono float32 @ 44100 Hz ──
            swrCtx = ffmpeg.swr_alloc();
            if (swrCtx == null)
                throw new InvalidOperationException("Failed to allocate SwrContext.");

            AVChannelLayout outLayout;
            ffmpeg.av_channel_layout_default(&outLayout, TargetChannels);
            var inLayout = codecCtx->ch_layout;

            ffmpeg.swr_alloc_set_opts2(&swrCtx,
                &outLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, TargetSampleRate,
                &inLayout, codecCtx->sample_fmt, codecCtx->sample_rate,
                0, null);

            result = ffmpeg.swr_init(swrCtx);
            if (result < 0)
                throw new InvalidOperationException($"Failed to init resampler: {ErrorString(result)}");

            // ── Decode loop ──
            frame = ffmpeg.av_frame_alloc();
            packet = ffmpeg.av_packet_alloc();
            if (frame == null || packet == null)
                throw new InvalidOperationException("Failed to allocate frame/packet.");

            // Accumulator for sub-chunk frames
            var sampleBuffer = new float[ChunkSamples];
            int bufferOffset = 0;
            int totalSamplesEmitted = 0;

            while (ffmpeg.av_read_frame(fmtCtx, packet) >= 0)
            {
                ct.ThrowIfCancellationRequested();

                if (packet->stream_index != audioIdx)
                {
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }

                result = ffmpeg.avcodec_send_packet(codecCtx, packet);
                ffmpeg.av_packet_unref(packet);
                if (result < 0) continue;

                while (ffmpeg.avcodec_receive_frame(codecCtx, frame) == 0)
                {
                    ct.ThrowIfCancellationRequested();

                    var outSamples = ffmpeg.swr_get_out_samples(swrCtx, frame->nb_samples);
                    if (outSamples <= 0)
                    {
                        ffmpeg.av_frame_unref(frame);
                        continue;
                    }

                    var tempBuf = new byte[outSamples * TargetChannels * sizeof(float)];
                    int converted;
                    fixed (byte* pOut = tempBuf)
                    {
                        var outPtr = pOut;
                        converted = ffmpeg.swr_convert(
                            swrCtx, &outPtr, outSamples,
                            frame->extended_data, frame->nb_samples);
                    }

                    ffmpeg.av_frame_unref(frame);

                    if (converted <= 0) continue;

                    // Copy converted float samples into accumulator
                    var floatSpan = MemoryMarshal.Cast<byte, float>(tempBuf.AsSpan(0, converted * TargetChannels * sizeof(float)));
                    int srcOffset = 0;

                    while (srcOffset < floatSpan.Length)
                    {
                        int remaining = ChunkSamples - bufferOffset;
                        int toCopy = Math.Min(remaining, floatSpan.Length - srcOffset);
                        floatSpan.Slice(srcOffset, toCopy).CopyTo(sampleBuffer.AsSpan(bufferOffset));
                        bufferOffset += toCopy;
                        srcOffset += toCopy;

                        if (bufferOffset >= ChunkSamples)
                        {
                            double timestampMs = totalSamplesEmitted * 1000.0 / TargetSampleRate;
                            totalSamplesEmitted += ChunkSamples;

                            var chunk = new AudioChunk
                            {
                                Samples = sampleBuffer.ToArray(),
                                SampleRate = TargetSampleRate,
                                TimestampMs = timestampMs,
                                TotalDurationMs = totalDurationMs
                            };

                            // WriteAsync would be ideal but we're on a sync thread;
                            // spin-wait if the bounded channel is full.
                            while (!writer.TryWrite(chunk))
                                Thread.Sleep(1);

                            bufferOffset = 0;
                        }
                    }
                }
            }

            // Flush remaining samples
            if (bufferOffset > 0)
            {
                double timestampMs = totalSamplesEmitted * 1000.0 / TargetSampleRate;
                var remaining = new float[bufferOffset];
                Array.Copy(sampleBuffer, remaining, bufferOffset);

                writer.TryWrite(new AudioChunk
                {
                    Samples = remaining,
                    SampleRate = TargetSampleRate,
                    TimestampMs = timestampMs,
                    TotalDurationMs = totalDurationMs
                });
            }

            writer.Complete();
        }
        catch (Exception ex)
        {
            writer.Complete(ex);
        }
        finally
        {
            if (frame != null) { var f = frame; ffmpeg.av_frame_free(&f); }
            if (packet != null) { var p = packet; ffmpeg.av_packet_free(&p); }
            if (swrCtx != null) { var s = swrCtx; ffmpeg.swr_free(&s); }
            if (codecCtx != null) { var c = codecCtx; ffmpeg.avcodec_free_context(&c); }
            if (fmtCtx != null) { var f = fmtCtx; ffmpeg.avformat_close_input(&f); }
        }
    }

    private static unsafe string ErrorString(int error)
    {
        var bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"Error {error}";
    }
}
