using Moq;
using PulsePlugin.Models;
using PulsePlugin.Services;
using PulsePlugin.Tests.TestUtilities;
using Vido.Core.Events;
using Vido.Core.Logging;
using Vido.Core.Playback;
using Vido.Haptics;
using Xunit;

namespace PulsePlugin.Tests.Services;

/// <summary>
/// Unit tests for PulseEngine — state machine, event wiring, error handling.
/// </summary>
public class PulseEngineTests : IDisposable
{
    private readonly MockAudioDecoder _decoder;
    private readonly AudioPreAnalysisService _preAnalysis;
    private readonly LiveAmplitudeService _liveAmplitude;
    private readonly PulseTCodeMapper _mapper;
    private readonly TestEventBus _eventBus;
    private readonly Mock<ILogService> _logMock;
    private readonly PulseEngine _engine;

    public PulseEngineTests()
    {
        _decoder = new MockAudioDecoder();
        // Set up a simple click track so analysis completes quickly.
        var samples = SyntheticAudioGenerator.ClickTrack(
            bpm: 120, durationMs: 2000, sampleRate: TestConstants.SampleRate44100);
        _decoder.SetAudio(samples, TestConstants.SampleRate44100, 2000.0, chunkSize: 4410);

        _preAnalysis = new AudioPreAnalysisService(_decoder);
        _liveAmplitude = new LiveAmplitudeService();
        _mapper = new PulseTCodeMapper();
        _eventBus = new TestEventBus();
        _logMock = new Mock<ILogService>();

        _engine = new PulseEngine(
            _preAnalysis, _liveAmplitude, _mapper,
            _eventBus, _logMock.Object);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _preAnalysis.Dispose();
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

    // ──────────────────────────────────────────────
    //  Constructor validation
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_NullPreAnalysis_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PulseEngine(null!, _liveAmplitude, _mapper, _eventBus, _logMock.Object));
    }

    [Fact]
    public void Constructor_NullLiveAmplitude_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PulseEngine(_preAnalysis, null!, _mapper, _eventBus, _logMock.Object));
    }

    [Fact]
    public void Constructor_NullMapper_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PulseEngine(_preAnalysis, _liveAmplitude, null!, _eventBus, _logMock.Object));
    }

    [Fact]
    public void Constructor_NullEventBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PulseEngine(_preAnalysis, _liveAmplitude, _mapper, null!, _logMock.Object));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PulseEngine(_preAnalysis, _liveAmplitude, _mapper, _eventBus, null!));
    }

    // ──────────────────────────────────────────────
    //  Initial state
    // ──────────────────────────────────────────────

    [Fact]
    public void InitialState_IsInactive()
    {
        Assert.Equal(PulseState.Inactive, _engine.State);
        Assert.False(_engine.IsEnabled);
        Assert.Null(_engine.CurrentBeatMap);
        Assert.Equal(0, _engine.CurrentBpm);
    }

    // ──────────────────────────────────────────────
    //  SetEnabled
    // ──────────────────────────────────────────────

    [Fact]
    public void SetEnabled_True_PublishesSuppressAndRegistration()
    {
        _engine.SetEnabled(true);

        Assert.True(_engine.IsEnabled);

        var suppress = _eventBus.GetPublished<SuppressFunscriptEvent>();
        Assert.Single(suppress);
        Assert.True(suppress[0].SuppressFunscripts);

        var reg = _eventBus.GetPublished<ExternalBeatSourceRegistration>();
        Assert.Single(reg);
        Assert.True(reg[0].IsRegistering);
        Assert.Equal("com.vido.pulse", reg[0].Source.Id);
    }

    [Fact]
    public void SetEnabled_False_PublishesUnsuppressAndUnregistration()
    {
        _engine.SetEnabled(true);
        _eventBus.ClearPublished();

        _engine.SetEnabled(false);

        Assert.False(_engine.IsEnabled);
        Assert.Equal(PulseState.Inactive, _engine.State);

        var suppress = _eventBus.GetPublished<SuppressFunscriptEvent>();
        Assert.Single(suppress);
        Assert.False(suppress[0].SuppressFunscripts);

        var reg = _eventBus.GetPublished<ExternalBeatSourceRegistration>();
        Assert.Single(reg);
        Assert.False(reg[0].IsRegistering);
    }

    [Fact]
    public void SetEnabled_TrueWhenAlreadyEnabled_NoOp()
    {
        _engine.SetEnabled(true);
        _eventBus.ClearPublished();

        _engine.SetEnabled(true);

        Assert.Empty(_eventBus.PublishedEvents);
    }

    [Fact]
    public void SetEnabled_FalseWhenAlreadyDisabled_NoOp()
    {
        _eventBus.ClearPublished();
        _engine.SetEnabled(false);

        Assert.Empty(_eventBus.PublishedEvents);
    }

    [Fact]
    public void SetEnabled_TrueWithNoVideo_StaysInactive()
    {
        _engine.SetEnabled(true);
        Assert.Equal(PulseState.Inactive, _engine.State);
    }

    // ──────────────────────────────────────────────
    //  Analysis flow
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SetEnabled_TrueWithVideoLoaded_StartsAnalysis()
    {
        // Load video first.
        _eventBus.Publish(MakeVideoLoaded());

        _engine.SetEnabled(true);

        Assert.Equal(PulseState.Analyzing, _engine.State);

        await WaitForState(PulseState.Ready);
        Assert.NotNull(_engine.CurrentBeatMap);
        Assert.True(_engine.CurrentBpm > 0);
    }

    [Fact]
    public async Task VideoLoaded_WhileEnabled_StartsAnalysis()
    {
        _engine.SetEnabled(true);
        Assert.Equal(PulseState.Inactive, _engine.State);

        _eventBus.Publish(MakeVideoLoaded());

        Assert.Equal(PulseState.Analyzing, _engine.State);
        await WaitForState(PulseState.Ready);
        Assert.NotNull(_engine.CurrentBeatMap);
    }

    [Fact]
    public void VideoLoaded_WhileDisabled_DoesNotAnalyze()
    {
        _eventBus.Publish(MakeVideoLoaded());
        Assert.Equal(PulseState.Inactive, _engine.State);
        Assert.Null(_engine.CurrentBeatMap);
    }

    [Fact]
    public async Task AnalysisComplete_FiresBeatMapReady()
    {
        BeatMap? receivedMap = null;
        _engine.BeatMapReady += map => receivedMap = map;

        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);

        await WaitForState(PulseState.Ready);
        Assert.NotNull(receivedMap);
        Assert.Same(_engine.CurrentBeatMap, receivedMap);
    }

    [Fact]
    public async Task AnalysisComplete_BeatSourceIsAvailable()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);

        await WaitForState(PulseState.Ready);
        Assert.True(_engine.BeatSource.IsAvailable);
    }

    [Fact]
    public async Task AnalysisComplete_WhilePlaying_GoesDirectlyToActive()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);

        // Simulate playback starting during analysis.
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });

        // Should go to Active when analysis completes (skip Ready).
        await WaitForState(PulseState.Active);
    }

    [Fact]
    public async Task AnalysisProgress_FiresEvent()
    {
        var progresses = new List<double>();
        _engine.AnalysisProgress += p => progresses.Add(p);

        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);

        await WaitForState(PulseState.Ready);
        Assert.NotEmpty(progresses);
        Assert.Contains(progresses, p => p >= 1.0);
    }

    [Fact]
    public async Task AnalysisFailed_SetsErrorState()
    {
        _decoder.SetException(new InvalidOperationException("decode error"));

        string? errorMsg = null;
        _engine.ErrorOccurred += msg => errorMsg = msg;

        _engine.SetEnabled(true);
        _eventBus.Publish(MakeVideoLoaded());

        await WaitForState(PulseState.Error);
        Assert.Contains("decode error", errorMsg);
    }

    [Fact]
    public async Task NewVideoWhileAnalyzing_RestartsAnalysis()
    {
        _engine.SetEnabled(true);
        _eventBus.Publish(MakeVideoLoaded(@"C:\Videos\first.mp4"));

        Assert.Equal(PulseState.Analyzing, _engine.State);

        // Load a new video — should restart analysis.
        _eventBus.Publish(MakeVideoLoaded(@"C:\Videos\second.mp4"));
        Assert.Equal(PulseState.Analyzing, _engine.State);

        await WaitForState(PulseState.Ready);
        Assert.NotNull(_engine.CurrentBeatMap);
    }

    // ──────────────────────────────────────────────
    //  SetEnabled false during Analyzing
    // ──────────────────────────────────────────────

    [Fact]
    public void SetEnabled_FalseDuringAnalyzing_CancelsAndGoesInactive()
    {
        _engine.SetEnabled(true);
        _eventBus.Publish(MakeVideoLoaded());
        Assert.Equal(PulseState.Analyzing, _engine.State);

        _engine.SetEnabled(false);
        Assert.Equal(PulseState.Inactive, _engine.State);
        Assert.Null(_engine.CurrentBeatMap);
        Assert.False(_engine.BeatSource.IsAvailable);
    }

    // ──────────────────────────────────────────────
    //  Playback state transitions
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PlaybackPlaying_DuringReady_GoesToActive()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        Assert.Equal(PulseState.Active, _engine.State);
    }

    [Fact]
    public async Task PlaybackPaused_DuringActive_GoesToReady()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        Assert.Equal(PulseState.Active, _engine.State);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Paused });
        Assert.Equal(PulseState.Ready, _engine.State);
    }

    [Fact]
    public async Task PlaybackStopped_DuringActive_GoesToReady()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        Assert.Equal(PulseState.Active, _engine.State);

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Stopped });
        Assert.Equal(PulseState.Ready, _engine.State);
    }

    [Fact]
    public async Task PlaybackPlaying_DuringAnalyzing_StaysAnalyzing()
    {
        _engine.SetEnabled(true);
        _eventBus.Publish(MakeVideoLoaded());
        Assert.Equal(PulseState.Analyzing, _engine.State);

        // Playback starts during analysis — should stay Analyzing (transitions to Active on completion).
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });

        // State is still Analyzing.
        Assert.Equal(PulseState.Analyzing, _engine.State);

        // When analysis completes, goes directly to Active.
        await WaitForState(PulseState.Active);
    }

    // ──────────────────────────────────────────────
    //  OnPositionChanged
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnPositionChanged_DuringActive_PublishesL0Position()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        Assert.Equal(PulseState.Active, _engine.State);
        _eventBus.ClearPublished();

        _engine.OnPositionChanged(500);

        var posEvents = _eventBus.GetPublished<ExternalAxisPositionsEvent>();
        Assert.NotEmpty(posEvents);
        Assert.True(posEvents[0].Positions.ContainsKey("L0"));

        double l0 = posEvents[0].Positions["L0"];
        Assert.True(l0 >= 5.0 && l0 <= 95.0, $"L0 position {l0} out of range");
    }

    [Fact]
    public void OnPositionChanged_DuringInactive_DoesNotPublish()
    {
        _eventBus.ClearPublished();
        _engine.OnPositionChanged(500);

        var posEvents = _eventBus.GetPublished<ExternalAxisPositionsEvent>();
        Assert.Empty(posEvents);
    }

    [Fact]
    public async Task OnPositionChanged_PublishesBeatEvents()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });
        _eventBus.ClearPublished();

        // Position at the start — should have beats in the lookahead window.
        _engine.OnPositionChanged(0);

        var beatEvents = _eventBus.GetPublished<ExternalBeatEvent>();
        if (beatEvents.Count > 0)
        {
            Assert.Equal("com.vido.pulse", beatEvents[0].SourceId);
            Assert.NotEmpty(beatEvents[0].BeatTimesMs);
        }
    }

    // ──────────────────────────────────────────────
    //  OnAudioSamplesAvailable
    // ──────────────────────────────────────────────

    [Fact]
    public void OnAudioSamplesAvailable_DuringNonActive_NoOp()
    {
        // Should not throw when not active.
        var args = new AudioSampleEventArgs
        {
            Buffer = new byte[256],
            SampleCount = 32,
            SampleRate = 44100,
            Channels = 2
        };

        _engine.OnAudioSamplesAvailable(args);
        // No exception is the assertion.
    }

    // ──────────────────────────────────────────────
    //  OnSeekCompleted
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OnSeekCompleted_ResetsTracking()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });

        // Tick at a position.
        _engine.OnPositionChanged(1000);

        // Seek.
        _engine.OnSeekCompleted();

        // Position at a different point — should still work.
        _eventBus.ClearPublished();
        _engine.OnPositionChanged(0);

        var posEvents = _eventBus.GetPublished<ExternalAxisPositionsEvent>();
        Assert.NotEmpty(posEvents);
    }

    // ──────────────────────────────────────────────
    //  StateChanged event
    // ──────────────────────────────────────────────

    [Fact]
    public async Task StateChanged_FiresOnEachTransition()
    {
        var states = new List<PulseState>();
        _engine.StateChanged += s => states.Add(s);

        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true); // Inactive → Analyzing
        await WaitForState(PulseState.Ready); // Analyzing → Ready

        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing }); // Ready → Active

        Assert.Contains(PulseState.Analyzing, states);
        Assert.Contains(PulseState.Ready, states);
        Assert.Contains(PulseState.Active, states);
    }

    // ──────────────────────────────────────────────
    //  Dispose
    // ──────────────────────────────────────────────

    [Fact]
    public void Dispose_CleanupDoesNotThrow()
    {
        _engine.SetEnabled(true);
        _eventBus.Publish(MakeVideoLoaded());

        _engine.Dispose();

        // Engine should still report state, but events shouldn't fire.
        // Verify no crash on second dispose.
        _engine.Dispose();
    }

    [Fact]
    public void Dispose_UnsubscribesFromEventBus()
    {
        _engine.Dispose();

        // After dispose, publishing VideoLoadedEvent should not trigger analysis.
        _eventBus.Publish(MakeVideoLoaded());
        Assert.Equal(PulseState.Inactive, _engine.State);
    }

    // ──────────────────────────────────────────────
    //  Constructor with initial media path
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Constructor_WithInitialMediaPath_AnalyzesOnEnable()
    {
        using var preAnalysis2 = new AudioPreAnalysisService(_decoder);
        using var engine2 = new PulseEngine(
            preAnalysis2, new LiveAmplitudeService(), new PulseTCodeMapper(),
            _eventBus, _logMock.Object, currentMediaPath: @"C:\Videos\test.mp4");

        engine2.SetEnabled(true);
        Assert.Equal(PulseState.Analyzing, engine2.State);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (engine2.State == PulseState.Analyzing && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Equal(PulseState.Ready, engine2.State);
    }

    // ──────────────────────────────────────────────
    //  PulseBeatSource properties
    // ──────────────────────────────────────────────

    [Fact]
    public void BeatSource_HasCorrectProperties()
    {
        var source = _engine.BeatSource;
        Assert.Equal("com.vido.pulse", source.Id);
        Assert.Equal("Pulse", source.DisplayName);
        Assert.True(source.HidesBuiltInModes);
        Assert.False(source.IsAvailable); // Not available until SetEnabled(true) is called.
    }

    // ──────────────────────────────────────────────
    //  Event bus subscription verification
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_SubscribesToRequiredEvents()
    {
        Assert.True(_eventBus.HasSubscription<VideoLoadedEvent>());
        Assert.True(_eventBus.HasSubscription<PlaybackStateChangedEvent>());
        Assert.True(_eventBus.HasSubscription<HapticTransportStateEvent>());
        Assert.True(_eventBus.HasSubscription<HapticScriptsChangedEvent>());
        Assert.True(_eventBus.HasSubscription<HapticAxisConfigEvent>());
    }

    // ──────────────────────────────────────────────
    //  BeatDivisor
    // ──────────────────────────────────────────────

    [Fact]
    public void BeatDivisor_DefaultIsOne()
    {
        Assert.Equal(1, _engine.BeatDivisor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BeatDivisor_AcceptsValidValues(int divisor)
    {
        _engine.BeatDivisor = divisor;
        Assert.Equal(divisor, _engine.BeatDivisor);
    }

    [Fact]
    public void BeatDivisor_ClampsBelow1()
    {
        _engine.BeatDivisor = 0;
        Assert.Equal(1, _engine.BeatDivisor);
    }

    [Fact]
    public void BeatDivisor_ClampsAbove4()
    {
        _engine.BeatDivisor = 10;
        Assert.Equal(4, _engine.BeatDivisor);
    }

    [Fact]
    public void BeatDivisor_FiresChangedEvent()
    {
        int? received = null;
        _engine.BeatDivisorChanged += v => received = v;

        _engine.BeatDivisor = 3;

        Assert.Equal(3, received);
    }

    [Fact]
    public void BeatDivisor_SameValue_NoEvent()
    {
        _engine.BeatDivisor = 1;
        bool fired = false;
        _engine.BeatDivisorChanged += _ => fired = true;

        _engine.BeatDivisor = 1;

        Assert.False(fired);
    }

    [Fact]
    public async Task BeatDivisor_FiltersBeatsForTCode()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });

        // Get baseline beats at divisor 1
        _eventBus.ClearPublished();
        _engine.OnPositionChanged(0);
        var baselineBeats = _eventBus.GetPublished<ExternalBeatEvent>();

        // Set divisor to 2 — should roughly halve the beat count
        _engine.BeatDivisor = 2;
        _eventBus.ClearPublished();
        _engine.OnPositionChanged(0);
        var filteredBeats = _eventBus.GetPublished<ExternalBeatEvent>();

        if (baselineBeats.Count > 0 && filteredBeats.Count > 0)
        {
            Assert.True(filteredBeats[0].BeatTimesMs.Count <= baselineBeats[0].BeatTimesMs.Count,
                "Divisor 2 should produce fewer or equal beats in lookahead window");
        }
    }

    [Fact]
    public async Task BeatDivisor_AffectsL0Position()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForState(PulseState.Ready);
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });

        // Get L0 at a specific position with divisor 1
        _eventBus.ClearPublished();
        _engine.OnPositionChanged(250); // Near a beat at 120 BPM
        var pos1 = _eventBus.GetPublished<ExternalAxisPositionsEvent>();

        // Set divisor to 4 — beat timing changes
        _engine.BeatDivisor = 4;
        _eventBus.ClearPublished();
        _engine.OnPositionChanged(250);
        var pos4 = _eventBus.GetPublished<ExternalAxisPositionsEvent>();

        // Both should produce valid L0 positions
        Assert.NotEmpty(pos1);
        Assert.NotEmpty(pos4);
        Assert.True(pos1[0].Positions.ContainsKey("L0"));
        Assert.True(pos4[0].Positions.ContainsKey("L0"));
    }
}

// ══════════════════════════════════════════════════
//  Test Infrastructure
// ══════════════════════════════════════════════════

/// <summary>
/// In-memory event bus for testing. Captures subscriptions and published events.
/// </summary>
internal class TestEventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly List<object> _published = new();
    private readonly object _lock = new();

    /// <summary>All events published to this bus.</summary>
    public IReadOnlyList<object> PublishedEvents
    {
        get { lock (_lock) return _published.ToList(); }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        var type = typeof(TEvent);
        lock (_lock)
        {
            if (!_handlers.ContainsKey(type))
                _handlers[type] = new List<Delegate>();
            _handlers[type].Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(type, out var list))
                    list.Remove(handler);
            }
        });
    }

    public void Publish<TEvent>(TEvent eventData) where TEvent : class
    {
        List<Delegate> snapshot;
        lock (_lock)
        {
            _published.Add(eventData!);
            snapshot = _handlers.TryGetValue(typeof(TEvent), out var list)
                ? list.ToList()
                : new List<Delegate>();
        }

        foreach (var h in snapshot)
            ((Action<TEvent>)h)(eventData);
    }

    /// <summary>Get all published events of a specific type.</summary>
    public IReadOnlyList<T> GetPublished<T>() where T : class
    {
        lock (_lock) return _published.OfType<T>().ToList();
    }

    /// <summary>Clear all recorded published events.</summary>
    public void ClearPublished()
    {
        lock (_lock) _published.Clear();
    }

    /// <summary>Check whether any handler is registered for the given event type.</summary>
    public bool HasSubscription<TEvent>() where TEvent : class
    {
        lock (_lock)
            return _handlers.TryGetValue(typeof(TEvent), out var list) && list.Count > 0;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _onDispose;
        public Subscription(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
