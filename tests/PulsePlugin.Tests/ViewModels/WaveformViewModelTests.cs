using PulsePlugin.Models;
using PulsePlugin.Services;
using PulsePlugin.Tests.Services;
using PulsePlugin.Tests.TestUtilities;
using PulsePlugin.ViewModels;
using Xunit;

namespace PulsePlugin.Tests.ViewModels;

/// <summary>
/// Unit tests for WaveformViewModel — constructor validation, initial state,
/// BeatMap loading, time updates, window duration binding, clear, state transitions, disposal.
/// </summary>
public class WaveformViewModelTests : IDisposable
{
    private readonly MockAudioDecoder _decoder;
    private readonly AudioPreAnalysisService _preAnalysis;
    private readonly LiveAmplitudeService _liveAmplitude;
    private readonly PulseTCodeMapper _mapper;
    private readonly TestEventBus _eventBus;
    private readonly Moq.Mock<Vido.Core.Logging.ILogService> _logMock;
    private readonly PulseEngine _engine;
    private readonly WaveformViewModel _vm;

    public WaveformViewModelTests()
    {
        _decoder = new MockAudioDecoder();
        var samples = SyntheticAudioGenerator.ClickTrack(
            bpm: 120, durationMs: 2000, sampleRate: TestConstants.SampleRate44100);
        _decoder.SetAudio(samples, TestConstants.SampleRate44100, 2000.0, chunkSize: 4410);

        _preAnalysis = new AudioPreAnalysisService(_decoder);
        _liveAmplitude = new LiveAmplitudeService();
        _mapper = new PulseTCodeMapper();
        _eventBus = new TestEventBus();
        _logMock = new Moq.Mock<Vido.Core.Logging.ILogService>();

        _engine = new PulseEngine(
            _preAnalysis, _liveAmplitude, _mapper,
            _eventBus, _logMock.Object);

        _vm = new WaveformViewModel(_engine);
    }

    public void Dispose()
    {
        _vm.Dispose();
        _engine.Dispose();
        _preAnalysis.Dispose();
    }

    private static Vido.Core.Events.VideoLoadedEvent MakeVideoLoaded(string path = @"C:\Videos\test.mp4") => new()
    {
        FilePath = path,
        Metadata = new Vido.Core.Playback.VideoMetadata
        {
            FilePath = path,
            FileName = System.IO.Path.GetFileName(path)
        }
    };

    private async Task WaitForReady(int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!_vm.IsActive && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(_vm.IsActive);
    }

    // ──────────────────────────────────────────
    //  Constructor
    // ──────────────────────────────────────────

    [Fact]
    public void Constructor_NullEngine_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WaveformViewModel(null!));
    }

    // ──────────────────────────────────────────
    //  Initial state
    // ──────────────────────────────────────────

    [Fact]
    public void InitialState_IsActiveFalse()
    {
        Assert.False(_vm.IsActive);
    }

    [Fact]
    public void InitialState_CurrentTimeIsZero()
    {
        Assert.Equal(0, _vm.CurrentTimeSeconds);
    }

    [Fact]
    public void InitialState_TotalDurationIsZero()
    {
        Assert.Equal(0, _vm.TotalDurationSeconds);
    }

    [Fact]
    public void InitialState_CurrentBpmIsZero()
    {
        Assert.Equal(0, _vm.CurrentBpm);
    }

    [Fact]
    public void InitialState_FullWaveformIsNull()
    {
        Assert.Null(_vm.FullWaveform);
    }

    [Fact]
    public void InitialState_AllBeatsIsNull()
    {
        Assert.Null(_vm.AllBeats);
    }

    [Fact]
    public void InitialState_WindowDurationDefault30()
    {
        Assert.Equal(30.0, _vm.WindowDurationSeconds);
    }

    [Fact]
    public void InitialState_WindowDurationIndexIsOne()
    {
        Assert.Equal(1, _vm.WindowDurationIndex);
    }

    [Fact]
    public void InitialState_CurrentAmplitudeIsZero()
    {
        Assert.Equal(0, _vm.CurrentAmplitude);
    }

    // ──────────────────────────────────────────
    //  Window duration binding
    // ──────────────────────────────────────────

    [Fact]
    public void WindowDurationIndex_SetValid_UpdatesWindowDuration()
    {
        _vm.WindowDurationIndex = 0; // 10s
        Assert.Equal(10.0, _vm.WindowDurationSeconds);
    }

    [Fact]
    public void WindowDurationIndex_Set4_Updates300Seconds()
    {
        _vm.WindowDurationIndex = 4; // 300s
        Assert.Equal(300.0, _vm.WindowDurationSeconds);
    }

    [Fact]
    public void WindowDurationIndex_NegativeValue_NoOp()
    {
        _vm.WindowDurationIndex = -1;
        Assert.Equal(30.0, _vm.WindowDurationSeconds); // unchanged
    }

    [Fact]
    public void WindowDurationIndex_OutOfRange_NoOp()
    {
        _vm.WindowDurationIndex = 99;
        Assert.Equal(30.0, _vm.WindowDurationSeconds); // unchanged
    }

    [Fact]
    public void WindowDurationSeconds_Set_FiresPropertyChanged()
    {
        var props = new List<string>();
        _vm.PropertyChanged += (_, e) => props.Add(e.PropertyName!);

        _vm.WindowDurationSeconds = 60.0;
        Assert.Contains("WindowDurationSeconds", props);
    }

    [Fact]
    public void WindowDurationSeconds_Set_FiresRepaintRequested()
    {
        int repaintCount = 0;
        _vm.RepaintRequested += () => repaintCount++;

        _vm.WindowDurationSeconds = 60.0;
        Assert.Equal(1, repaintCount);
    }

    [Fact]
    public void WindowDurationSeconds_SameValue_NoEvent()
    {
        var props = new List<string>();
        _vm.PropertyChanged += (_, e) => props.Add(e.PropertyName!);

        _vm.WindowDurationSeconds = 30.0; // already default
        Assert.DoesNotContain("WindowDurationSeconds", props);
    }

    // ──────────────────────────────────────────
    //  Static arrays
    // ──────────────────────────────────────────

    [Fact]
    public void WindowDurationOptions_HasFiveEntries()
    {
        Assert.Equal(5, WaveformViewModel.WindowDurationOptions.Length);
    }

    [Fact]
    public void WindowDurationLabels_HasFiveEntries()
    {
        Assert.Equal(5, WaveformViewModel.WindowDurationLabels.Length);
    }

    [Fact]
    public void WindowDurationLabels_MatchOptions()
    {
        Assert.Equal("10s", WaveformViewModel.WindowDurationLabels[0]);
        Assert.Equal("30s", WaveformViewModel.WindowDurationLabels[1]);
        Assert.Equal("60s", WaveformViewModel.WindowDurationLabels[2]);
        Assert.Equal("2m", WaveformViewModel.WindowDurationLabels[3]);
        Assert.Equal("5m", WaveformViewModel.WindowDurationLabels[4]);
    }

    // ──────────────────────────────────────────
    //  BeatMap loading (via engine analysis)
    // ──────────────────────────────────────────

    [Fact]
    public async Task BeatMapReady_SetsWaveformData()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForReady();

        Assert.NotNull(_vm.FullWaveform);
        Assert.True(_vm.FullWaveform!.Count > 0);
        Assert.True(_vm.WaveformSampleRate > 0);
    }

    [Fact]
    public async Task BeatMapReady_SetsBeats()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForReady();

        Assert.NotNull(_vm.AllBeats);
        Assert.True(_vm.AllBeats!.Count > 0);
    }

    [Fact]
    public async Task BeatMapReady_SetsBpmAndDuration()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForReady();

        Assert.True(_vm.CurrentBpm > 0);
        Assert.True(_vm.TotalDurationSeconds > 0);
    }

    [Fact]
    public async Task BeatMapReady_IsActiveTrue()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForReady();

        Assert.True(_vm.IsActive);
    }

    [Fact]
    public async Task BeatMapReady_FiresPropertyChanged()
    {
        var props = new List<string>();
        _vm.PropertyChanged += (_, e) => props.Add(e.PropertyName!);

        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForReady();

        Assert.Contains("FullWaveform", props);
        Assert.Contains("AllBeats", props);
        Assert.Contains("CurrentBpm", props);
        Assert.Contains("TotalDurationSeconds", props);
        Assert.Contains("IsActive", props);
    }

    // ──────────────────────────────────────────
    //  UpdateTime
    // ──────────────────────────────────────────

    [Fact]
    public void UpdateTime_SetsCurrentTimeSeconds()
    {
        _vm.UpdateTime(5.5);
        Assert.Equal(5.5, _vm.CurrentTimeSeconds, 3);
    }

    [Fact]
    public void UpdateTime_FiresRepaint()
    {
        int repaintCount = 0;
        _vm.RepaintRequested += () => repaintCount++;

        _vm.UpdateTime(1.0);
        Assert.Equal(1, repaintCount);
    }

    [Fact]
    public void UpdateTime_AfterDispose_NoOp()
    {
        _vm.Dispose();
        _vm.UpdateTime(10.0);
        Assert.Equal(0, _vm.CurrentTimeSeconds);
    }

    // ──────────────────────────────────────────
    //  Clear
    // ──────────────────────────────────────────

    [Fact]
    public async Task Clear_ResetsAllData()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForReady();

        _vm.Clear();

        Assert.Null(_vm.FullWaveform);
        Assert.Null(_vm.AllBeats);
        Assert.Equal(0, _vm.WaveformSampleRate);
        Assert.Equal(0, _vm.CurrentBpm);
        Assert.Equal(0, _vm.CurrentAmplitude);
        Assert.Equal(0, _vm.CurrentTimeSeconds);
        Assert.Equal(0, _vm.TotalDurationSeconds);
        Assert.False(_vm.IsActive);
    }

    [Fact]
    public async Task Clear_FiresRepaint()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForReady();

        int repaintCount = 0;
        _vm.RepaintRequested += () => repaintCount++;

        _vm.Clear();
        Assert.True(repaintCount >= 1);
    }

    // ──────────────────────────────────────────
    //  State transitions
    // ──────────────────────────────────────────

    [Fact]
    public async Task EngineDisabled_ClearsViewModel()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);
        await WaitForReady();

        _engine.SetEnabled(false);

        // Give state change a moment to propagate.
        await Task.Delay(50);

        Assert.False(_vm.IsActive);
        Assert.Null(_vm.FullWaveform);
    }

    [Fact]
    public void EngineError_ClearsViewModel()
    {
        _decoder.SetException(new InvalidOperationException("boom"));
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);

        // Error state should cause clear.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (_vm.IsActive && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.False(_vm.IsActive);
    }

    // ──────────────────────────────────────────
    //  Dispose
    // ──────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _vm.Dispose();
        _vm.Dispose(); // double dispose safe
    }

    [Fact]
    public async Task Dispose_DetachesFromEngine()
    {
        _vm.Dispose();

        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);

        await Task.Delay(200);

        // Should still be in initial state.
        Assert.False(_vm.IsActive);
        Assert.Null(_vm.FullWaveform);
    }
}
