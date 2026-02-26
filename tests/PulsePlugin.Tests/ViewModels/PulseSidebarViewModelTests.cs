using PulsePlugin.Models;
using PulsePlugin.Services;
using PulsePlugin.Tests.Services;
using PulsePlugin.Tests.TestUtilities;
using PulsePlugin.ViewModels;
using Xunit;

namespace PulsePlugin.Tests.ViewModels;

/// <summary>
/// Unit tests for PulseSidebarViewModel — property binding, state transitions,
/// toggle behaviour, status messages, and engine wiring.
/// </summary>
public class PulseSidebarViewModelTests : IDisposable
{
    private readonly MockAudioDecoder _decoder;
    private readonly AudioPreAnalysisService _preAnalysis;
    private readonly LiveAmplitudeService _liveAmplitude;
    private readonly PulseTCodeMapper _mapper;
    private readonly TestEventBus _eventBus;
    private readonly Moq.Mock<Vido.Core.Logging.ILogService> _logMock;
    private readonly PulseEngine _engine;
    private readonly PulseSidebarViewModel _vm;

    public PulseSidebarViewModelTests()
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

        _vm = new PulseSidebarViewModel(_engine);
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

    private async Task WaitForState(PulseState target, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (_vm.State != target && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.Equal(target, _vm.State);
    }

    // ──────────────────────────────────────────
    //  Constructor
    // ──────────────────────────────────────────

    [Fact]
    public void Constructor_NullEngine_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PulseSidebarViewModel(null!));
    }

    // ──────────────────────────────────────────
    //  Initial state
    // ──────────────────────────────────────────

    [Fact]
    public void InitialState_UsePulseIsFalse()
    {
        Assert.False(_vm.UsePulse);
    }

    [Fact]
    public void InitialState_StateIsInactive()
    {
        Assert.Equal(PulseState.Inactive, _vm.State);
    }

    [Fact]
    public void InitialState_StatusMessageIsEmpty()
    {
        Assert.Equal(string.Empty, _vm.StatusMessage);
    }

    [Fact]
    public void InitialState_IsAnalyzingFalse()
    {
        Assert.False(_vm.IsAnalyzing);
    }

    [Fact]
    public void InitialState_ShowBpmFalse()
    {
        Assert.False(_vm.ShowBpm);
    }

    [Fact]
    public void InitialState_StateColorIsGrey()
    {
        Assert.Equal("Grey", _vm.StateColor);
    }

    // ──────────────────────────────────────────
    //  Description
    // ──────────────────────────────────────────

    [Fact]
    public void Description_ContainsKeyBehaviors()
    {
        Assert.Contains("pre-analyzed", _vm.Description);
        Assert.Contains("Funscript", _vm.Description);
        Assert.Contains("BeatBar", _vm.Description);
        Assert.Contains("L0 axis", _vm.Description);
        Assert.Contains("Toggle off", _vm.Description);
    }

    // ──────────────────────────────────────────
    //  UsePulse toggle → engine
    // ──────────────────────────────────────────

    [Fact]
    public void SetUsePulse_True_EnablesEngine()
    {
        _vm.UsePulse = true;
        Assert.True(_engine.IsEnabled);
    }

    [Fact]
    public void SetUsePulse_False_DisablesEngine()
    {
        _vm.UsePulse = true;
        _vm.UsePulse = false;
        Assert.False(_engine.IsEnabled);
    }

    [Fact]
    public void SetUsePulse_SameValue_NoOp()
    {
        var changes = new List<string>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        _vm.UsePulse = false; // already false
        Assert.Empty(changes);
    }

    // ──────────────────────────────────────────
    //  PropertyChanged notifications
    // ──────────────────────────────────────────

    [Fact]
    public void SetUsePulse_FiresPropertyChanged()
    {
        var props = new List<string>();
        _vm.PropertyChanged += (_, e) => props.Add(e.PropertyName!);

        _vm.UsePulse = true;
        Assert.Contains("UsePulse", props);
    }

    [Fact]
    public async Task AnalysisComplete_FiresStateAndBpmChanges()
    {
        var props = new List<string>();
        _vm.PropertyChanged += (_, e) => props.Add(e.PropertyName!);

        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;

        await WaitForState(PulseState.Ready);

        Assert.Contains("State", props);
        Assert.Contains("CurrentBpm", props);
        Assert.Contains("StatusMessage", props);
        Assert.Contains("ShowBpm", props);
        Assert.Contains("StateColor", props);
    }

    // ──────────────────────────────────────────
    //  State transitions → ViewModel properties
    // ──────────────────────────────────────────

    [Fact]
    public async Task AnalyzingState_ShowsProgressAndCorrectColor()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;

        // Should immediately go to Analyzing.
        Assert.Equal(PulseState.Analyzing, _vm.State);
        Assert.True(_vm.IsAnalyzing);
        Assert.Equal("Yellow", _vm.StateColor);
        Assert.Contains("Analyzing", _vm.StatusMessage);

        await WaitForState(PulseState.Ready);
    }

    [Fact]
    public async Task ReadyState_ShowsBpmAndCorrectColor()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;
        await WaitForState(PulseState.Ready);

        Assert.Equal("Yellow", _vm.StateColor);
        Assert.True(_vm.ShowBpm);
        Assert.False(_vm.IsAnalyzing);
        Assert.Contains("Ready", _vm.StatusMessage);
        Assert.Contains("BPM", _vm.StatusMessage);
        Assert.True(_vm.CurrentBpm > 0);
    }

    [Fact]
    public async Task ActiveState_ShowsBpmAndGreenColor()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;
        await WaitForState(PulseState.Ready);

        _eventBus.Publish(new Vido.Core.Events.PlaybackStateChangedEvent
        {
            State = Vido.Core.Playback.PlaybackState.Playing
        });

        Assert.Equal(PulseState.Active, _vm.State);
        Assert.Equal("Green", _vm.StateColor);
        Assert.True(_vm.ShowBpm);
        Assert.Contains("BPM", _vm.StatusMessage);
    }

    [Fact]
    public void InactiveState_EmptyStatusAndGreyColor()
    {
        Assert.Equal(PulseState.Inactive, _vm.State);
        Assert.Equal("Grey", _vm.StateColor);
        Assert.Equal(string.Empty, _vm.StatusMessage);
    }

    [Fact]
    public async Task ErrorState_ShowsErrorMessage()
    {
        _decoder.SetException(new InvalidOperationException("test error"));

        _vm.UsePulse = true;
        _eventBus.Publish(MakeVideoLoaded());

        await WaitForState(PulseState.Error);

        Assert.Equal("Red", _vm.StateColor);
        Assert.Contains("Error", _vm.StatusMessage);
        Assert.Contains("test error", _vm.StatusMessage);
    }

    // ──────────────────────────────────────────
    //  Analysis progress
    // ──────────────────────────────────────────

    [Fact]
    public async Task AnalysisProgress_UpdatesProperty()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;

        await WaitForState(PulseState.Ready);

        // Progress should have reached 1.0 by the time analysis completes.
        Assert.True(_vm.AnalysisProgress >= 0.9, $"Progress was {_vm.AnalysisProgress}");
    }

    // ──────────────────────────────────────────
    //  Toggle off → reset
    // ──────────────────────────────────────────

    [Fact]
    public async Task ToggleOff_ResetsToInactive()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;
        await WaitForState(PulseState.Ready);

        _vm.UsePulse = false;

        Assert.Equal(PulseState.Inactive, _vm.State);
        Assert.Equal(string.Empty, _vm.StatusMessage);
        Assert.Equal("Grey", _vm.StateColor);
        Assert.False(_vm.ShowBpm);
    }

    // ──────────────────────────────────────────
    //  StatusBarText
    // ──────────────────────────────────────────

    [Fact]
    public void StatusBarText_InitialValue_IsOff()
    {
        Assert.Equal("\u2665 Pulse: Off", _vm.StatusBarText);
    }

    [Fact]
    public async Task StatusBarText_Analyzing_ShowsAnalyzing()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;

        // Wait briefly for analysis to start.
        var deadline = DateTime.UtcNow.AddMilliseconds(3000);
        while (_vm.State == PulseState.Inactive && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        // During analysis the text should indicate analyzing.
        if (_vm.State == PulseState.Analyzing)
            Assert.Equal("\u2665 Pulse: Analyzing...", _vm.StatusBarText);
    }

    [Fact]
    public async Task StatusBarText_Ready_ShowsReady()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;
        await WaitForState(PulseState.Ready);

        Assert.Equal("\u2665 Pulse: Ready", _vm.StatusBarText);
    }

    [Fact]
    public async Task StatusBarText_Active_ShowsBpm()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;
        await WaitForState(PulseState.Ready);

        // Simulate playback to transition to Active.
        _eventBus.Publish(new Vido.Core.Events.PlaybackStateChangedEvent
        {
            State = Vido.Core.Playback.PlaybackState.Playing
        });

        await WaitForState(PulseState.Active);

        Assert.StartsWith("\u2665 Pulse: Active", _vm.StatusBarText);
        Assert.Contains("BPM", _vm.StatusBarText);
    }

    [Fact]
    public async Task StatusBarText_ToggleOff_ReturnsToOff()
    {
        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;
        await WaitForState(PulseState.Ready);

        _vm.UsePulse = false;

        Assert.Equal("\u2665 Pulse: Off", _vm.StatusBarText);
    }

    [Fact]
    public void StatusBarText_RaisesPropertyChanged()
    {
        var raised = false;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PulseSidebarViewModel.StatusBarText))
                raised = true;
        };

        _eventBus.Publish(MakeVideoLoaded());
        _vm.UsePulse = true;

        // Wait briefly for property change.
        var deadline = DateTime.UtcNow.AddMilliseconds(3000);
        while (!raised && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.True(raised, "StatusBarText PropertyChanged was never raised");
    }

    // ──────────────────────────────────────────
    //  Dispose
    // ──────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _vm.Dispose();
        // Should not throw — already disposed.
        _vm.Dispose();
    }

    [Fact]
    public async Task Dispose_DetachesFromEngine()
    {
        _vm.Dispose();

        // Engine state changes should not update the (disposed) ViewModel.
        _eventBus.Publish(MakeVideoLoaded());
        _engine.SetEnabled(true);

        // Wait a bit for potential async updates.
        await Task.Delay(100);

        // ViewModel should still show initial state.
        Assert.Equal(PulseState.Inactive, _vm.State);
    }
}
