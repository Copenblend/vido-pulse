using System.ComponentModel;
using System.Runtime.CompilerServices;
using PulsePlugin.Models;
using PulsePlugin.Services;

namespace PulsePlugin.ViewModels;

/// <summary>
/// ViewModel for the Pulse sidebar panel — toggle switch, analysis progress,
/// BPM readout, state indicator, and description text.
/// </summary>
internal sealed class PulseSidebarViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PulseEngine _engine;

    private bool _usePulse;
    private PulseState _state;
    private double _currentBpm;
    private double _analysisProgress;
    private string _statusMessage = string.Empty;
    private string? _errorMessage;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PulseSidebarViewModel(PulseEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;

        _state = _engine.State;
        _usePulse = _engine.IsEnabled;

        _engine.StateChanged += OnEngineStateChanged;
        _engine.AnalysisProgress += OnAnalysisProgress;
        _engine.BeatMapReady += OnBeatMapReady;
        _engine.ErrorOccurred += OnErrorOccurred;

        UpdateStatusMessage();
    }

    // ── Properties ──

    /// <summary>Main toggle — enables/disables Pulse.</summary>
    public bool UsePulse
    {
        get => _usePulse;
        set
        {
            if (_usePulse == value) return;
            _usePulse = value;
            OnPropertyChanged();
            _engine.SetEnabled(value);
        }
    }

    /// <summary>Current engine state.</summary>
    public PulseState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAnalyzing));
            OnPropertyChanged(nameof(ShowBpm));
            OnPropertyChanged(nameof(StateColor));
        }
    }

    /// <summary>Current detected BPM.</summary>
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

    /// <summary>Analysis progress 0.0–1.0.</summary>
    public double AnalysisProgress
    {
        get => _analysisProgress;
        private set
        {
            if (Math.Abs(_analysisProgress - value) < 0.005) return;
            _analysisProgress = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Human-readable status message.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the engine is currently analyzing (progress bar visible).</summary>
    public bool IsAnalyzing => _state == PulseState.Analyzing;

    /// <summary>Whether to show the BPM readout (Ready or Active).</summary>
    public bool ShowBpm => _state is PulseState.Ready or PulseState.Active;

    /// <summary>State indicator color key: "Green", "Yellow", "Grey", "Red".</summary>
    public string StateColor => _state switch
    {
        PulseState.Active => "Green",
        PulseState.Ready or PulseState.Analyzing => "Yellow",
        PulseState.Error => "Red",
        _ => "Grey"
    };

    /// <summary>Description text explaining Pulse behaviour.</summary>
    public string Description =>
        "When Use Pulse is enabled:\n" +
        "\u2022 Audio is pre-analyzed for beat detection on load\n" +
        "\u2022 Funscript auto-loading is suppressed\n" +
        "\u2022 A \u2018Pulse\u2019 BeatBar mode appears (with red hearts)\n" +
        "\u2022 L0 axis is driven by beat-synchronized strokes\n" +
        "\u2022 Other axes (R0/R1/R2) continue with fill modes\n" +
        "\u2022 OSR2+ axis Min/Max/Enabled settings still apply\n\n" +
        "Toggle off to restore normal funscript behavior.";

    // ── Engine callbacks ──

    private void OnEngineStateChanged(PulseState newState)
    {
        State = newState;
        _errorMessage = null;
        UpdateStatusMessage();
    }

    private void OnAnalysisProgress(double progress)
    {
        AnalysisProgress = progress;
        UpdateStatusMessage();
    }

    private void OnBeatMapReady(BeatMap beatMap)
    {
        CurrentBpm = beatMap.Bpm;
        UpdateStatusMessage();
    }

    private void OnErrorOccurred(string message)
    {
        _errorMessage = message;
        UpdateStatusMessage();
    }

    // ── Helpers ──

    private void UpdateStatusMessage()
    {
        StatusMessage = _state switch
        {
            PulseState.Inactive => string.Empty,
            PulseState.Analyzing => $"Analyzing audio... {_analysisProgress:P0}",
            PulseState.Ready => $"Ready \u2014 \u2665 {_currentBpm:F0} BPM detected",
            PulseState.Active => $"\u2665 {_currentBpm:F0} BPM",
            PulseState.Error => $"Error: {_errorMessage ?? "Unknown"}",
            _ => string.Empty
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _engine.StateChanged -= OnEngineStateChanged;
        _engine.AnalysisProgress -= OnAnalysisProgress;
        _engine.BeatMapReady -= OnBeatMapReady;
        _engine.ErrorOccurred -= OnErrorOccurred;
    }
}
