using System.ComponentModel;
using System.Runtime.CompilerServices;
using PulsePlugin.Models;
using PulsePlugin.Services;

namespace PulsePlugin.ViewModels;

/// <summary>
/// ViewModel for the Pulse Waveform bottom panel — provides pre-analyzed waveform data,
/// beat markers, BPM readout, current playback position, and live amplitude.
/// Drives SkiaSharp rendering via <see cref="RepaintRequested"/>.
/// </summary>
internal sealed class WaveformViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PulseEngine _engine;

    private double _currentTimeSeconds;
    private double _totalDurationSeconds;
    private double _currentBpm;
    private double _windowDurationSeconds = 30.0;
    private double _currentAmplitude;
    private bool _isActive;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the view should repaint the SkiaSharp canvas.</summary>
    public event Action? RepaintRequested;

    /// <summary>Available window duration options (in seconds), matching OSR2+ visualizer.</summary>
    public static readonly double[] WindowDurationOptions = [10, 30, 60, 120, 300];

    /// <summary>Display labels for the window duration dropdown.</summary>
    public static readonly string[] WindowDurationLabels = ["10s", "30s", "60s", "2m", "5m"];

    public WaveformViewModel(PulseEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;

        _engine.StateChanged += OnEngineStateChanged;
        _engine.BeatMapReady += OnBeatMapReady;
    }

    // ── Properties ──

    /// <summary>Current playback position in seconds.</summary>
    public double CurrentTimeSeconds
    {
        get => _currentTimeSeconds;
        private set
        {
            if (Math.Abs(_currentTimeSeconds - value) < 0.001) return;
            _currentTimeSeconds = value;
            // No PropertyChanged for high-frequency updates — repaint handles it.
        }
    }

    /// <summary>Total media duration in seconds.</summary>
    public double TotalDurationSeconds
    {
        get => _totalDurationSeconds;
        private set
        {
            if (Math.Abs(_totalDurationSeconds - value) < 0.01) return;
            _totalDurationSeconds = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Current BPM readout.</summary>
    public double CurrentBpm
    {
        get => _currentBpm;
        private set
        {
            if (Math.Abs(_currentBpm - value) < 0.01) return;
            _currentBpm = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Visible time window duration in seconds.</summary>
    public double WindowDurationSeconds
    {
        get => _windowDurationSeconds;
        set
        {
            if (Math.Abs(_windowDurationSeconds - value) < 0.01) return;
            _windowDurationSeconds = value;
            OnPropertyChanged();
            RepaintRequested?.Invoke();
        }
    }

    /// <summary>Index into <see cref="WindowDurationOptions"/> for ComboBox binding.</summary>
    public int WindowDurationIndex
    {
        get
        {
            for (int i = 0; i < WindowDurationOptions.Length; i++)
                if (Math.Abs(WindowDurationOptions[i] - _windowDurationSeconds) < 0.01)
                    return i;
            return 1; // default 30s
        }
        set
        {
            if (value >= 0 && value < WindowDurationOptions.Length)
                WindowDurationSeconds = WindowDurationOptions[value];
        }
    }

    /// <summary>Live amplitude at the cursor position (0.0–1.0).</summary>
    public double CurrentAmplitude
    {
        get => _currentAmplitude;
        private set
        {
            if (Math.Abs(_currentAmplitude - value) < 0.005) return;
            _currentAmplitude = value;
            // High-frequency — no PropertyChanged, repaint handles it.
        }
    }

    /// <summary>Whether the waveform panel has data to render.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Pre-analyzed waveform for the full track (RMS envelope).</summary>
    public IReadOnlyList<float>? FullWaveform { get; private set; }

    /// <summary>Sample rate of <see cref="FullWaveform"/> (samples per second).</summary>
    public int WaveformSampleRate { get; private set; }

    /// <summary>All beats from pre-analysis.</summary>
    public IReadOnlyList<BeatEvent>? AllBeats { get; private set; }

    // ── Public methods (called by plugin entry point) ──

    /// <summary>
    /// Update current playback position. Called at ~60 Hz during playback.
    /// </summary>
    public void UpdateTime(double seconds)
    {
        if (_disposed) return;
        CurrentTimeSeconds = seconds;
        CurrentAmplitude = _engine.CurrentAmplitude;
        RepaintRequested?.Invoke();
    }

    /// <summary>Clear all data (new video or disabled).</summary>
    public void Clear()
    {
        FullWaveform = null;
        WaveformSampleRate = 0;
        AllBeats = null;
        CurrentBpm = 0;
        CurrentAmplitude = 0;
        CurrentTimeSeconds = 0;
        TotalDurationSeconds = 0;
        IsActive = false;
        OnPropertyChanged(nameof(FullWaveform));
        OnPropertyChanged(nameof(AllBeats));
        RepaintRequested?.Invoke();
    }

    // ── Engine callbacks ──

    private void OnEngineStateChanged(PulseState newState)
    {
        if (_disposed) return;

        IsActive = newState is PulseState.Ready or PulseState.Active;

        if (newState == PulseState.Inactive || newState == PulseState.Error)
            Clear();
    }

    private void OnBeatMapReady(BeatMap beatMap)
    {
        if (_disposed) return;

        FullWaveform = beatMap.WaveformSamples;
        WaveformSampleRate = beatMap.WaveformSampleRate;
        AllBeats = beatMap.Beats;
        CurrentBpm = beatMap.Bpm;
        TotalDurationSeconds = beatMap.DurationMs / 1000.0;
        IsActive = true;

        OnPropertyChanged(nameof(FullWaveform));
        OnPropertyChanged(nameof(AllBeats));
        RepaintRequested?.Invoke();
    }

    // ── INotifyPropertyChanged ──

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ── IDisposable ──

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _engine.StateChanged -= OnEngineStateChanged;
        _engine.BeatMapReady -= OnBeatMapReady;
    }
}
