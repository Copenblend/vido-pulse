using PulsePlugin.Models;
using Vido.Core.Events;
using Vido.Core.Logging;
using Vido.Core.Playback;
using Vido.Haptics;

namespace PulsePlugin.Services;

/// <summary>
/// Central coordinator — manages Pulse state machine, bridges pre-analyzed beat map +
/// live amplitude to TCode output, and publishes haptic events on <see cref="IEventBus"/>.
/// </summary>
/// <remarks>
/// <para>State machine: <c>Inactive → Analyzing → Ready ⇄ Active</c>, with <c>Error</c> for failures.</para>
/// <para>Thread-safe: called from UI thread (enable/disable, video loaded), decode thread
/// (audio samples), and timer thread (position changes).</para>
/// </remarks>
internal sealed class PulseEngine : IDisposable
{
    private const string LogSource = "Pulse";

    /// <summary>Lookahead window for BeatBar beat events (ms).</summary>
    private const double BeatLookaheadMs = 5000.0;

    // ── Dependencies ──
    private readonly AudioPreAnalysisService _preAnalysisService;
    private readonly LiveAmplitudeService _liveAmplitudeService;
    private readonly PulseTCodeMapper _tCodeMapper;
    private readonly IEventBus _eventBus;
    private readonly ILogService _logger;

    // ── Internal state ──
    private readonly PulseBeatSource _beatSource = new();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _lock = new();

    private PulseState _state = PulseState.Inactive;
    private bool _enabled;
    private BeatMap? _currentBeatMap;
    private string? _currentMediaPath;
    private int _lastPublishedBeatIndex = -1;
    private bool _isPlaying;
    private bool _disposed;

    // ── Public state ──

    /// <summary>Current engine state.</summary>
    public PulseState State { get { lock (_lock) return _state; } }

    /// <summary>Most recent beat map (null until analysis completes).</summary>
    public BeatMap? CurrentBeatMap { get { lock (_lock) return _currentBeatMap; } }

    /// <summary>Whether the user has enabled Pulse.</summary>
    public bool IsEnabled { get { lock (_lock) return _enabled; } }

    /// <summary>Current live RMS amplitude (0.0–1.0).</summary>
    public double CurrentAmplitude => _liveAmplitudeService.CurrentAmplitude;

    /// <summary>Current BPM from the pre-analyzed beat map.</summary>
    public double CurrentBpm { get { lock (_lock) return _currentBeatMap?.Bpm ?? 0; } }

    /// <summary>The registered external beat source for BeatBar integration.</summary>
    internal PulseBeatSource BeatSource => _beatSource;

    // ── Events ──

    /// <summary>Fires when the engine state changes.</summary>
    public event Action<PulseState>? StateChanged;

    /// <summary>Fires when pre-analysis completes with a valid beat map.</summary>
    public event Action<BeatMap>? BeatMapReady;

    /// <summary>Fires with analysis progress (0.0–1.0).</summary>
    public event Action<double>? AnalysisProgress;

    /// <summary>Fires when an error occurs (message string).</summary>
    public event Action<string>? ErrorOccurred;

    // ── Constructor ──

    /// <param name="preAnalysisService">Pre-analysis pipeline.</param>
    /// <param name="liveAmplitudeService">Real-time amplitude tracker.</param>
    /// <param name="tCodeMapper">Beat-to-position mapper.</param>
    /// <param name="eventBus">Vido event bus for inter-plugin communication.</param>
    /// <param name="logger">Logging service.</param>
    /// <param name="currentMediaPath">File path of already-loaded media (if any).</param>
    public PulseEngine(
        AudioPreAnalysisService preAnalysisService,
        LiveAmplitudeService liveAmplitudeService,
        PulseTCodeMapper tCodeMapper,
        IEventBus eventBus,
        ILogService logger,
        string? currentMediaPath = null)
    {
        ArgumentNullException.ThrowIfNull(preAnalysisService);
        ArgumentNullException.ThrowIfNull(liveAmplitudeService);
        ArgumentNullException.ThrowIfNull(tCodeMapper);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);

        _preAnalysisService = preAnalysisService;
        _liveAmplitudeService = liveAmplitudeService;
        _tCodeMapper = tCodeMapper;
        _eventBus = eventBus;
        _logger = logger;
        _currentMediaPath = currentMediaPath;

        // Wire pre-analysis callbacks.
        _preAnalysisService.AnalysisComplete += OnAnalysisComplete;
        _preAnalysisService.AnalysisFailed += OnAnalysisFailed;
        _preAnalysisService.AnalysisProgress += OnAnalysisProgressUpdated;

        // Subscribe to event bus.
        _subscriptions.Add(_eventBus.Subscribe<VideoLoadedEvent>(OnVideoLoaded));
        _subscriptions.Add(_eventBus.Subscribe<PlaybackStateChangedEvent>(OnPlaybackStateChanged));
        _subscriptions.Add(_eventBus.Subscribe<HapticTransportStateEvent>(OnTransportStateChanged));
        _subscriptions.Add(_eventBus.Subscribe<HapticScriptsChangedEvent>(OnScriptsChanged));
        _subscriptions.Add(_eventBus.Subscribe<HapticAxisConfigEvent>(OnAxisConfigChanged));
    }

    // ── Public API ──

    /// <summary>
    /// Toggle Pulse on or off. When enabled, publishes <see cref="SuppressFunscriptEvent"/>
    /// and registers the beat source. Starts analysis if a video is loaded.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        PulseState? stateChange = null;
        string? mediaToAnalyze = null;
        bool doEnable = false;
        bool doDisable = false;

        lock (_lock)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;

            if (enabled)
            {
                doEnable = true;
                if (_currentMediaPath != null)
                {
                    mediaToAnalyze = _currentMediaPath;
                    stateChange = TransitionTo(PulseState.Analyzing);
                }
            }
            else
            {
                doDisable = true;
                _preAnalysisService.Cancel();
                _liveAmplitudeService.Stop();
                _liveAmplitudeService.Reset();
                _tCodeMapper.Reset();
                _currentBeatMap = null;
                _lastPublishedBeatIndex = -1;
                _beatSource.IsAvailable = false;
                stateChange = TransitionTo(PulseState.Inactive);
            }
        }

        // Fire state change outside lock.
        if (stateChange.HasValue)
            StateChanged?.Invoke(stateChange.Value);

        if (doEnable)
        {
            _eventBus.Publish(new SuppressFunscriptEvent { SuppressFunscripts = true });
            _eventBus.Publish(new ExternalBeatSourceRegistration { Source = _beatSource, IsRegistering = true });
            _logger.Info("Pulse enabled", LogSource);

            if (mediaToAnalyze != null)
            {
                _logger.Info($"Starting analysis: {mediaToAnalyze}", LogSource);
                _preAnalysisService.AnalyzeAsync(mediaToAnalyze);
            }
        }

        if (doDisable)
        {
            _eventBus.Publish(new SuppressFunscriptEvent { SuppressFunscripts = false });
            _eventBus.Publish(new ExternalBeatSourceRegistration { Source = _beatSource, IsRegistering = false });
            _logger.Info("Pulse disabled", LogSource);
        }
    }

    /// <summary>
    /// Feed decoded audio samples from the playback engine (decode thread).
    /// Only processes when Pulse is <see cref="PulseState.Active"/>.
    /// </summary>
    public void OnAudioSamplesAvailable(AudioSampleEventArgs args)
    {
        lock (_lock)
        {
            if (!_enabled || _state != PulseState.Active) return;
        }

        _liveAmplitudeService.SubmitSamples(args.Buffer, args.SampleCount, args.SampleRate, args.Channels);
    }

    /// <summary>
    /// Called on each position tick (~60Hz) from the playback engine.
    /// Maps beats + amplitude to L0 position and publishes haptic events.
    /// </summary>
    public void OnPositionChanged(double positionMs)
    {
        BeatMap? beatMap;

        lock (_lock)
        {
            if (!_enabled || _state != PulseState.Active) return;
            beatMap = _currentBeatMap;
        }

        if (beatMap == null) return;

        // Drain live audio and compute amplitude.
        _liveAmplitudeService.ProcessAvailable(positionMs);
        double amplitude = _liveAmplitudeService.CurrentAmplitude;

        // Map to L0 axis position.
        double l0 = _tCodeMapper.MapToPosition(beatMap, positionMs, amplitude);

        _eventBus.Publish(new ExternalAxisPositionsEvent
        {
            Positions = new Dictionary<string, double> { ["L0"] = l0 }
        });

        // Publish beats in the BeatBar lookahead window.
        PublishBeatEvents(beatMap, positionMs);
    }

    /// <summary>
    /// Called after a seek operation completes. Resets beat tracking and live amplitude.
    /// </summary>
    public void OnSeekCompleted()
    {
        lock (_lock)
        {
            _lastPublishedBeatIndex = -1;
            _tCodeMapper.Reset();

            if (_state == PulseState.Active)
            {
                _liveAmplitudeService.Reset();
                _liveAmplitudeService.Start();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _preAnalysisService.Cancel();
        _liveAmplitudeService.Stop();

        // Unsubscribe from event bus.
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();

        // Detach pre-analysis callbacks.
        _preAnalysisService.AnalysisComplete -= OnAnalysisComplete;
        _preAnalysisService.AnalysisFailed -= OnAnalysisFailed;
        _preAnalysisService.AnalysisProgress -= OnAnalysisProgressUpdated;
    }

    // ── Event bus handlers ──

    private void OnVideoLoaded(VideoLoadedEvent e)
    {
        PulseState? stateChange = null;
        bool shouldAnalyze = false;

        lock (_lock)
        {
            _currentMediaPath = e.FilePath;

            if (_enabled)
            {
                shouldAnalyze = true;
                _preAnalysisService.Cancel();
                _currentBeatMap = null;
                _beatSource.IsAvailable = false;
                _lastPublishedBeatIndex = -1;
                _tCodeMapper.Reset();
                _liveAmplitudeService.Stop();
                _liveAmplitudeService.Reset();
                stateChange = TransitionTo(PulseState.Analyzing);
            }
        }

        if (stateChange.HasValue)
            StateChanged?.Invoke(stateChange.Value);

        if (shouldAnalyze)
        {
            _logger.Info($"Starting analysis: {e.FilePath}", LogSource);
            _preAnalysisService.AnalyzeAsync(e.FilePath);
        }
    }

    private void OnPlaybackStateChanged(PlaybackStateChangedEvent e)
    {
        PulseState? stateChange = null;
        bool startLive = false;
        bool stopLive = false;

        lock (_lock)
        {
            _isPlaying = e.State == PlaybackState.Playing;

            if (!_enabled) return;

            if (_isPlaying && _state == PulseState.Ready)
            {
                stateChange = TransitionTo(PulseState.Active);
                startLive = true;
            }
            else if (!_isPlaying && _state == PulseState.Active)
            {
                stateChange = TransitionTo(PulseState.Ready);
                stopLive = true;
            }
        }

        if (startLive)
        {
            _liveAmplitudeService.Reset();
            _liveAmplitudeService.Start();
        }

        if (stopLive)
            _liveAmplitudeService.Stop();

        if (stateChange.HasValue)
            StateChanged?.Invoke(stateChange.Value);
    }

    private void OnTransportStateChanged(HapticTransportStateEvent e)
    {
        // Tracked for status display (VPP-012/VPP-015).
    }

    private void OnScriptsChanged(HapticScriptsChangedEvent e)
    {
        // Tracked for status display (VPP-012/VPP-015).
    }

    private void OnAxisConfigChanged(HapticAxisConfigEvent e)
    {
        // Tracked for axis scaling (VPP-012/VPP-015).
    }

    // ── Pre-analysis callbacks ──

    private void OnAnalysisComplete(BeatMap beatMap)
    {
        PulseState? stateChange = null;
        bool startLive = false;

        lock (_lock)
        {
            if (!_enabled) return;

            _currentBeatMap = beatMap;
            _beatSource.IsAvailable = true;
            _lastPublishedBeatIndex = -1;

            if (_isPlaying)
            {
                stateChange = TransitionTo(PulseState.Active);
                startLive = true;
            }
            else
            {
                stateChange = TransitionTo(PulseState.Ready);
            }
        }

        if (startLive)
        {
            _liveAmplitudeService.Reset();
            _liveAmplitudeService.Start();
        }

        if (stateChange.HasValue)
            StateChanged?.Invoke(stateChange.Value);

        _logger.Info($"Analysis complete: {beatMap.Beats.Count} beats, {beatMap.Bpm:F1} BPM", LogSource);
        BeatMapReady?.Invoke(beatMap);
    }

    private void OnAnalysisFailed(Exception ex)
    {
        PulseState? stateChange;

        lock (_lock)
        {
            if (!_enabled) return;
            stateChange = TransitionTo(PulseState.Error);
        }

        if (stateChange.HasValue)
            StateChanged?.Invoke(stateChange.Value);

        _logger.Error($"Analysis failed: {ex.Message}", LogSource);
        ErrorOccurred?.Invoke(ex.Message);
    }

    private void OnAnalysisProgressUpdated(double progress)
    {
        AnalysisProgress?.Invoke(progress);
    }

    // ── Helpers ──

    /// <summary>
    /// Publish upcoming beats for BeatBar overlay display.
    /// </summary>
    private void PublishBeatEvents(BeatMap beatMap, double positionMs)
    {
        if (beatMap.Beats.Count == 0) return;

        double endMs = positionMs + BeatLookaheadMs;
        var beatsInWindow = new List<double>();

        // Find first beat at or after current position via scan from last known index.
        int startIdx = Math.Max(0, _lastPublishedBeatIndex);
        for (int i = startIdx; i < beatMap.Beats.Count; i++)
        {
            double t = beatMap.Beats[i].TimestampMs;
            if (t > endMs) break;
            if (t >= positionMs)
                beatsInWindow.Add(t);
        }

        // Track the last beat we've passed for seek detection.
        int currentBeatIdx = PulseTCodeMapper.FindCurrentBeatIndex(beatMap.Beats, positionMs);
        _lastPublishedBeatIndex = currentBeatIdx;

        if (beatsInWindow.Count > 0)
        {
            _eventBus.Publish(new ExternalBeatEvent
            {
                BeatTimesMs = beatsInWindow,
                SourceId = _beatSource.Id
            });
        }
    }

    /// <summary>
    /// Transition to a new state. Returns the new state if changed, null if already in that state.
    /// Must be called inside <see cref="_lock"/>.
    /// </summary>
    private PulseState? TransitionTo(PulseState newState)
    {
        if (_state == newState) return null;
        _state = newState;
        return newState;
    }
}
