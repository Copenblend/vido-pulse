using Moq;
using PulsePlugin.Models;
using PulsePlugin.Services;
using PulsePlugin.Tests.Services;
using PulsePlugin.Tests.TestUtilities;
using Vido.Core.Events;
using Vido.Core.Logging;
using Vido.Core.Playback;
using Vido.Haptics;
using Xunit;

namespace PulsePlugin.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the Pulse ↔ OSR2+ BeatBar flow.
/// Simulates the subscriber side (OSR2+ BeatBarViewModel, AxisControlViewModel, TCodeService)
/// by consuming published events from the real PulseEngine and verifying event data contracts.
/// </summary>
public class BeatBarIntegrationTests : IDisposable
{
    private readonly MockAudioDecoder _decoder;
    private readonly AudioPreAnalysisService _preAnalysis;
    private readonly LiveAmplitudeService _liveAmplitude;
    private readonly PulseTCodeMapper _mapper;
    private readonly TestEventBus _eventBus;
    private readonly Mock<ILogService> _logMock;
    private readonly PulseEngine _engine;

    // ── Simulated OSR2+ subscriber state ──
    private readonly List<IExternalBeatSource> _registeredBeatSources = new();
    private bool _funscriptsSuppressed;
    private readonly Dictionary<string, double> _externalPositions = new();
    private readonly List<ExternalBeatEvent> _beatEvents = new();

    public BeatBarIntegrationTests()
    {
        _decoder = new MockAudioDecoder();
        var samples = SyntheticAudioGenerator.ClickTrack(
            bpm: 120, durationMs: 3000, sampleRate: TestConstants.SampleRate44100);
        _decoder.SetAudio(samples, TestConstants.SampleRate44100, 3000.0, chunkSize: 4410);

        _preAnalysis = new AudioPreAnalysisService(_decoder);
        _liveAmplitude = new LiveAmplitudeService();
        _mapper = new PulseTCodeMapper();
        _eventBus = new TestEventBus();
        _logMock = new Mock<ILogService>();

        _engine = new PulseEngine(
            _preAnalysis, _liveAmplitude, _mapper,
            _eventBus, _logMock.Object);

        // ── Wire up simulated OSR2+ subscriber handlers ──
        // Mirrors Osr2PlusPlugin.Activate() subscriptions.
        _eventBus.Subscribe<ExternalBeatSourceRegistration>(OnBeatSourceRegistration);
        _eventBus.Subscribe<SuppressFunscriptEvent>(OnSuppressFunscript);
        _eventBus.Subscribe<ExternalAxisPositionsEvent>(OnExternalAxisPositions);
        _eventBus.Subscribe<ExternalBeatEvent>(OnExternalBeatEvent);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _preAnalysis.Dispose();
    }

    // ── Simulated OSR2+ handlers ──

    private void OnBeatSourceRegistration(ExternalBeatSourceRegistration reg)
    {
        if (reg.Source is not { } source)
            return;

        if (reg.IsRegistering)
        {
            // De-duplicate (same pattern as BeatBarViewModel).
            _registeredBeatSources.RemoveAll(s => s.Id == source.Id);
            _registeredBeatSources.Add(source);
        }
        else
        {
            _registeredBeatSources.RemoveAll(s => s.Id == source.Id);
        }
    }

    private void OnSuppressFunscript(SuppressFunscriptEvent evt)
    {
        _funscriptsSuppressed = evt.SuppressFunscripts;
    }

    private void OnExternalAxisPositions(ExternalAxisPositionsEvent evt)
    {
        foreach (var position in evt.Positions.Span)
            _externalPositions[position.AxisId] = position.Position;
    }

    private void OnExternalBeatEvent(ExternalBeatEvent evt)
    {
        _beatEvents.Add(evt);
    }

    // ── Helpers ──

    private static VideoLoadedEvent MakeVideoLoaded(string path = @"C:\Videos\test.mp4") => new()
    {
        FilePath = path,
        Metadata = new VideoMetadata
        {
            FilePath = path,
            FileName = System.IO.Path.GetFileName(path)
        }
    };

    private async Task WaitForState(PulseState target, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (_engine.State != target && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.Equal(target, _engine.State);
    }

    // ════════════════════════════════════════════════
    //  1. Registration flow — ExternalBeatSourceRegistration
    // ════════════════════════════════════════════════

    [Fact]
    public void Enable_Registers_BeatSource_InSimulatedBeatBar()
    {
        _engine.SetEnabled(true);

        Assert.Single(_registeredBeatSources);
        Assert.Equal("com.vido.pulse", _registeredBeatSources[0].Id);
        Assert.Equal("Pulse", _registeredBeatSources[0].DisplayName);
        Assert.True(_registeredBeatSources[0].HidesBuiltInModes);
    }

    [Fact]
    public void Disable_Unregisters_BeatSource_FromSimulatedBeatBar()
    {
        _engine.SetEnabled(true);
        Assert.Single(_registeredBeatSources);

        _engine.SetEnabled(false);
        Assert.Empty(_registeredBeatSources);
    }

    [Fact]
    public void EnableDisableEnable_RegistersTwice()
    {
        _engine.SetEnabled(true);
        Assert.Single(_registeredBeatSources);

        _engine.SetEnabled(false);
        Assert.Empty(_registeredBeatSources);

        _engine.SetEnabled(true);
        Assert.Single(_registeredBeatSources);
    }

    [Fact]
    public async Task BeatSource_IsAvailable_TrueAfterAnalysis()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        Assert.True(_registeredBeatSources[0].IsAvailable);
    }

    [Fact]
    public void BeatSource_IsAvailable_TrueImmediatelyOnEnable()
    {
        _engine.SetEnabled(true);

        Assert.Single(_registeredBeatSources);
        Assert.True(_registeredBeatSources[0].IsAvailable);
    }

    // ════════════════════════════════════════════════
    //  2. SuppressFunscriptEvent round-trip
    // ════════════════════════════════════════════════

    [Fact]
    public void Enable_SuppressesFunscripts()
    {
        Assert.False(_funscriptsSuppressed);

        _engine.SetEnabled(true);
        Assert.True(_funscriptsSuppressed);
    }

    [Fact]
    public void Disable_UnsuppressesFunscripts()
    {
        _engine.SetEnabled(true);
        Assert.True(_funscriptsSuppressed);

        _engine.SetEnabled(false);
        Assert.False(_funscriptsSuppressed);
    }

    [Fact]
    public void SuppressRoundTrip_MultipleToggleCycles()
    {
        for (int i = 0; i < 3; i++)
        {
            _engine.SetEnabled(true);
            Assert.True(_funscriptsSuppressed, $"Cycle {i}: expected suppressed after enable");

            _engine.SetEnabled(false);
            Assert.False(_funscriptsSuppressed, $"Cycle {i}: expected unsuppressed after disable");
        }
    }

    // ════════════════════════════════════════════════
    //  3. ExternalAxisPositionsEvent — L0 position injection
    // ════════════════════════════════════════════════

    [Fact]
    public async Task Active_PositionTick_InjectsL0Position()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        Assert.Equal(PulseState.Active, _engine.State);

        _externalPositions.Clear();
        _engine.OnPositionChanged(500);

        Assert.True(_externalPositions.ContainsKey("L0"), "Expected L0 axis position");
        Assert.InRange(_externalPositions["L0"], 5.0, 95.0);
    }

    [Fact]
    public async Task Active_MultiplePositionTicks_AllInValidRange()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        Assert.Equal(PulseState.Active, _engine.State);

        // Simulate 60 position ticks over the first 1 second
        for (int i = 0; i < 60; i++)
        {
            _engine.OnPositionChanged(i * (1000.0 / 60));
        }

        // All published positions should be in range
        var posEvents = _eventBus.GetPublished<ExternalAxisPositionsEvent>();
        foreach (var evt in posEvents)
        {
            var positions = evt.Positions.ToArray();
            Assert.Contains(positions, p => p.AxisId == "L0");
            var l0 = positions.First(p => p.AxisId == "L0").Position;
            Assert.InRange(l0, 5.0, 95.0);
        }
    }

    [Fact]
    public void Inactive_PositionTick_NoL0Published()
    {
        _externalPositions.Clear();
        _engine.OnPositionChanged(500);

        Assert.Empty(_externalPositions);
    }

    [Fact]
    public async Task Ready_PositionTick_NoL0Published()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _externalPositions.Clear();
        _eventBus.ClearPublished();
        _engine.OnPositionChanged(500);

        var posEvents = _eventBus.GetPublished<ExternalAxisPositionsEvent>();
        Assert.Empty(posEvents);
    }

    // ════════════════════════════════════════════════
    //  4. ExternalBeatEvent — beat delivery to BeatBar
    // ════════════════════════════════════════════════

    [Fact]
    public async Task Active_Beats_PublishedWithCorrectSourceId()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        _beatEvents.Clear();

        // Tick from the start — should include beats in lookahead window
        _engine.OnPositionChanged(0);

        if (_beatEvents.Count > 0)
        {
            Assert.All(_beatEvents, evt => Assert.Equal("com.vido.pulse", evt.SourceId));
        }
    }

    [Fact]
    public async Task Active_Beats_TimesAreInLookaheadWindow()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        _beatEvents.Clear();

        double positionMs = 500;
        _engine.OnPositionChanged(positionMs);

        foreach (var evt in _beatEvents)
        {
            foreach (double beatTimeMs in evt.BeatTimesMs.ToArray())
            {
                Assert.True(beatTimeMs >= positionMs,
                    $"Beat {beatTimeMs}ms should be at or after cursor at {positionMs}ms");
                Assert.True(beatTimeMs <= positionMs + 5500,
                    $"Beat {beatTimeMs}ms should be within 5.5s lookahead of cursor at {positionMs}ms");
            }
        }
    }

    [Fact]
    public async Task Active_NoBeats_WhenPositionBeyondTrack()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        _beatEvents.Clear();

        // Position well beyond the 3-second track
        _engine.OnPositionChanged(100000);

        if (_beatEvents.Count > 0)
        {
            // Any events should have empty beat lists
            Assert.All(_beatEvents, evt => Assert.Equal(0, evt.BeatTimesMs.Length));
        }
    }

    // ════════════════════════════════════════════════
    //  5. Full lifecycle: enable → analyze → active → disable
    // ════════════════════════════════════════════════

    [Fact]
    public async Task FullLifecycle_EnableAnalyzeActiveDisable()
    {
        // Step 1: Enable with video loaded
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);

        Assert.True(_funscriptsSuppressed);
        Assert.Single(_registeredBeatSources);
        Assert.Equal(PulseState.Analyzing, _engine.State);

        // Step 2: Wait for analysis
        await WaitForState(PulseState.Ready);
        Assert.True(_registeredBeatSources[0].IsAvailable);

        // Step 3: Start playback → Active
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        Assert.Equal(PulseState.Active, _engine.State);

        // Step 4: Position tick → events published
        _externalPositions.Clear();
        _beatEvents.Clear();
        _engine.OnPositionChanged(500);

        Assert.True(_externalPositions.ContainsKey("L0"));

        // Step 5: Pause → Ready
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Paused });
        Assert.Equal(PulseState.Ready, _engine.State);

        // Step 6: Disable → Inactive, funscripts unsuppressed, beat source removed
        _engine.SetEnabled(false);

        Assert.Equal(PulseState.Inactive, _engine.State);
        Assert.False(_funscriptsSuppressed);
        Assert.Empty(_registeredBeatSources);
    }

    [Fact]
    public async Task FullLifecycle_DisableReenableWithNewVideo()
    {
        // First cycle
        _eventBus.Publish(MakeVideoLoaded(@"C:\Videos\first.mp4"));
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _engine.SetEnabled(false);
        Assert.False(_funscriptsSuppressed);
        Assert.Empty(_registeredBeatSources);

        // Second cycle with new video
        _eventBus.Publish(MakeVideoLoaded(@"C:\Videos\second.mp4"));
        _engine.SetEnabled(true);

        Assert.True(_funscriptsSuppressed);
        Assert.Single(_registeredBeatSources);
        Assert.Equal(PulseState.Analyzing, _engine.State);

        await WaitForState(PulseState.Ready);
        Assert.True(_registeredBeatSources[0].IsAvailable);
    }

    [Fact]
    public async Task FullLifecycle_SeekResets_ThenResumes()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });

        // Tick some position
        _engine.OnPositionChanged(1000);

        // Seek
        _engine.OnSeekCompleted();
        _externalPositions.Clear();

        // Resume ticking from new position
        _engine.OnPositionChanged(0);

        Assert.True(_externalPositions.ContainsKey("L0"));
        Assert.InRange(_externalPositions["L0"], 5.0, 95.0);
    }

    // ════════════════════════════════════════════════
    //  6. Event ordering verification
    // ════════════════════════════════════════════════

    [Fact]
    public void Enable_SuppressBeforeRegister()
    {
        _eventBus.ClearPublished();
        _engine.SetEnabled(true);

        var events = _eventBus.PublishedEvents;
        int suppressIdx = -1, regIdx = -1;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is SuppressFunscriptEvent) suppressIdx = i;
            if (events[i] is ExternalBeatSourceRegistration) regIdx = i;
        }

        Assert.True(suppressIdx >= 0, "SuppressFunscriptEvent not published");
        Assert.True(regIdx >= 0, "ExternalBeatSourceRegistration not published");
        Assert.True(suppressIdx < regIdx,
            "SuppressFunscriptEvent should be published before ExternalBeatSourceRegistration");
    }

    [Fact]
    public void Disable_UnsuppressBeforeUnregister()
    {
        _engine.SetEnabled(true);
        _eventBus.ClearPublished();

        _engine.SetEnabled(false);

        var events = _eventBus.PublishedEvents;
        int suppressIdx = -1, regIdx = -1;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is SuppressFunscriptEvent) suppressIdx = i;
            if (events[i] is ExternalBeatSourceRegistration) regIdx = i;
        }

        Assert.True(suppressIdx >= 0, "SuppressFunscriptEvent(false) not published on disable");
        Assert.True(regIdx >= 0, "ExternalBeatSourceRegistration(false) not published on disable");
        Assert.True(suppressIdx < regIdx,
            "SuppressFunscriptEvent(false) should precede ExternalBeatSourceRegistration(false)");
    }

    // ════════════════════════════════════════════════
    //  7. Backward compatibility — no events when disabled
    // ════════════════════════════════════════════════

    [Fact]
    public void NeverEnabled_NoHapticEventsPublished()
    {
        _eventBus.ClearPublished();

        // Load a video but never enable Pulse
        _eventBus.Publish(MakeVideoLoaded());

        // Simulate playback and position changes
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        _engine.OnPositionChanged(500);
        _engine.OnPositionChanged(1000);

        // No haptic events should have been published
        Assert.Empty(_eventBus.GetPublished<SuppressFunscriptEvent>());
        Assert.Empty(_eventBus.GetPublished<ExternalBeatSourceRegistration>());
        Assert.Empty(_eventBus.GetPublished<ExternalAxisPositionsEvent>());
        Assert.Empty(_eventBus.GetPublished<ExternalBeatEvent>());
    }

    [Fact]
    public void DisabledEngine_PlaybackEvents_DoNotTriggerHaptics()
    {
        // Enable then disable
        _engine.SetEnabled(true);
        _engine.SetEnabled(false);
        _eventBus.ClearPublished();

        // Load video and play — should generate no haptic events
        _eventBus.Publish(MakeVideoLoaded());
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        _engine.OnPositionChanged(500);

        Assert.Empty(_eventBus.GetPublished<ExternalAxisPositionsEvent>());
        Assert.Empty(_eventBus.GetPublished<ExternalBeatEvent>());
    }

    // ════════════════════════════════════════════════
    //  8. Dispose cleanup
    // ════════════════════════════════════════════════

    [Fact]
    public async Task Dispose_DuringActive_EventBusUnsubscribed()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        Assert.Equal(PulseState.Active, _engine.State);

        _engine.Dispose();

        // After dispose, event bus subscriptions are removed.
        // Publishing VideoLoadedEvent should no longer trigger analysis.
        _eventBus.ClearPublished();
        _eventBus.Publish(MakeVideoLoaded(@"C:\Videos\new.mp4"));

        // Engine should not start analyzing (state stays unchanged, no new events published).
        await Task.Delay(100);
        Assert.Empty(_eventBus.GetPublished<SuppressFunscriptEvent>());
    }
}
