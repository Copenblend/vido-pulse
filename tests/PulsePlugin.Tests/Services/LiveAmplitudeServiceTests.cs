using PulsePlugin.Services;
using PulsePlugin.Tests.TestUtilities;
using Xunit;

namespace PulsePlugin.Tests.Services;

/// <summary>
/// Unit tests for LiveAmplitudeService — real-time amplitude tracking via ring buffer.
/// </summary>
public class LiveAmplitudeServiceTests
{
    // ──────────────────────────────────────────────
    //  Constructor
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultParameters_DoesNotThrow()
    {
        var svc = new LiveAmplitudeService();
        Assert.NotNull(svc);
    }

    [Fact]
    public void Constructor_CustomParameters_DoesNotThrow()
    {
        var svc = new LiveAmplitudeService(bufferCapacity: 96000, windowMs: 10.0);
        Assert.NotNull(svc);
    }

    // ──────────────────────────────────────────────
    //  Initial state
    // ──────────────────────────────────────────────

    [Fact]
    public void InitialState_AmplitudeIsZero()
    {
        var svc = new LiveAmplitudeService();
        Assert.Equal(0, svc.CurrentAmplitude);
    }

    // ──────────────────────────────────────────────
    //  SubmitSamples + ProcessAvailable
    // ──────────────────────────────────────────────

    [Fact]
    public void SubmitSamples_WhenNotRunning_IsIgnored()
    {
        var svc = new LiveAmplitudeService();
        // Service not started — should silently ignore.
        var sine = SyntheticAudioGenerator.SineWave(440, 100, TestConstants.SampleRate44100, 0.8f);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(sine);

        svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate44100, TestConstants.Mono);
        svc.ProcessAvailable(0);

        Assert.Equal(0, svc.CurrentAmplitude);
    }

    [Fact]
    public void SubmitSamples_WhenRunning_UpdatesAmplitude()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        var sine = SyntheticAudioGenerator.SineWave(440, 200, TestConstants.SampleRate44100, 0.8f);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(sine);

        svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate44100, TestConstants.Mono);
        svc.ProcessAvailable(0);

        Assert.True(svc.CurrentAmplitude > 0, $"Amplitude should be > 0 after sine, got {svc.CurrentAmplitude}");
    }

    [Fact]
    public void SubmitSamples_Silence_AmplitudeStaysZero()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        var silence = SyntheticAudioGenerator.Silence(200, TestConstants.SampleRate44100);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(silence);

        svc.SubmitSamples(bytes, silence.Length, TestConstants.SampleRate44100, TestConstants.Mono);
        svc.ProcessAvailable(0);

        Assert.Equal(0, svc.CurrentAmplitude);
    }

    [Fact]
    public void SubmitSamples_Stereo_StillUpdatesAmplitude()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        var mono = SyntheticAudioGenerator.SineWave(440, 200, TestConstants.SampleRate44100, 0.8f);
        var stereo = SyntheticAudioGenerator.MonoToStereo(mono);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(stereo);

        svc.SubmitSamples(bytes, mono.Length, TestConstants.SampleRate44100, TestConstants.Stereo);
        svc.ProcessAvailable(0);

        Assert.True(svc.CurrentAmplitude > 0, "Stereo should still produce amplitude");
    }

    // ──────────────────────────────────────────────
    //  AmplitudeUpdated event
    // ──────────────────────────────────────────────

    [Fact]
    public void ProcessAvailable_FiresAmplitudeUpdatedEvent()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        var sine = SyntheticAudioGenerator.SineWave(440, 200, TestConstants.SampleRate44100, 0.8f);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(sine);
        svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate44100, TestConstants.Mono);

        double? received = null;
        svc.AmplitudeUpdated += amp => received = amp;

        svc.ProcessAvailable(0);

        Assert.NotNull(received);
        Assert.True(received!.Value > 0);
    }

    [Fact]
    public void ProcessAvailable_NoSamples_DoesNotFireEvent()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        bool fired = false;
        svc.AmplitudeUpdated += _ => fired = true;

        svc.ProcessAvailable(0);

        Assert.False(fired);
    }

    // ──────────────────────────────────────────────
    //  Start / Stop / Reset
    // ──────────────────────────────────────────────

    [Fact]
    public void Stop_PreventsSubmission()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();
        svc.Stop();

        var sine = SyntheticAudioGenerator.SineWave(440, 200, TestConstants.SampleRate44100, 0.8f);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(sine);
        svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate44100, TestConstants.Mono);
        svc.ProcessAvailable(0);

        Assert.Equal(0, svc.CurrentAmplitude);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        var sine = SyntheticAudioGenerator.SineWave(440, 200, TestConstants.SampleRate44100, 0.8f);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(sine);
        svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate44100, TestConstants.Mono);
        svc.ProcessAvailable(0);

        Assert.True(svc.CurrentAmplitude > 0);

        svc.Reset();

        Assert.Equal(0, svc.CurrentAmplitude);
    }

    [Fact]
    public void Reset_ThenRestart_Works()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        var sine = SyntheticAudioGenerator.SineWave(440, 200, TestConstants.SampleRate44100, 0.8f);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(sine);
        svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate44100, TestConstants.Mono);
        svc.ProcessAvailable(0);

        svc.Reset();
        svc.Start();

        svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate44100, TestConstants.Mono);
        svc.ProcessAvailable(100);

        Assert.True(svc.CurrentAmplitude > 0, "Should work after reset+restart");
    }

    // ──────────────────────────────────────────────
    //  Multiple chunks
    // ──────────────────────────────────────────────

    [Fact]
    public void MultipleSubmissions_AccumulatesCorrectly()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        // Submit multiple small chunks.
        for (int i = 0; i < 5; i++)
        {
            var sine = SyntheticAudioGenerator.SineWave(440, 50, TestConstants.SampleRate44100, 0.5f);
            var bytes = SyntheticAudioGenerator.ToByteBuffer(sine);
            svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate44100, TestConstants.Mono);
        }

        svc.ProcessAvailable(200);

        Assert.True(svc.CurrentAmplitude > 0, "Should accumulate multiple chunks");
    }

    // ──────────────────────────────────────────────
    //  48kHz
    // ──────────────────────────────────────────────

    [Fact]
    public void SubmitSamples_48kHz_Works()
    {
        var svc = new LiveAmplitudeService();
        svc.Start();

        var sine = SyntheticAudioGenerator.SineWave(440, 200, TestConstants.SampleRate48000, 0.8f);
        var bytes = SyntheticAudioGenerator.ToByteBuffer(sine);

        svc.SubmitSamples(bytes, sine.Length, TestConstants.SampleRate48000, TestConstants.Mono);
        svc.ProcessAvailable(0);

        Assert.True(svc.CurrentAmplitude > 0);
    }
}
