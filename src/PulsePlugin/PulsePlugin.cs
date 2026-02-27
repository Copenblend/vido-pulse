using System.Windows;
using PulsePlugin.Services;
using PulsePlugin.ViewModels;
using PulsePlugin.Views;
using Vido.Core.Playback;
using Vido.Core.Plugin;

namespace PulsePlugin;

/// <summary>
/// Pulse plugin entry point. Audio-to-haptics beat sync — pre-analyzes audio
/// to detect beats and generate TCode haptic commands.
/// </summary>
public class PulsePlugin : IVidoPlugin
{
    private IPluginContext? _context;
    private PulseEngine? _engine;
    private PulseSidebarViewModel? _sidebarViewModel;
    private WaveformViewModel? _waveformViewModel;
    private AudioPreAnalysisService? _preAnalysisService;
    private UIElement? _beatRateControl;
    private IDisposable? _suppressSubscription;

    /// <inheritdoc />
    public void Activate(IPluginContext context)
    {
        _context = context;

        // Create service graph.
        var decoder = new FfmpegAudioDecoder();
        _preAnalysisService = new AudioPreAnalysisService(decoder);
        var liveAmplitude = new LiveAmplitudeService();
        var mapper = new PulseTCodeMapper();

        // Detect already-loaded media (only if the engine actually has a video loaded,
        // not stale metadata from a previous session).
        string? currentMedia = context.VideoEngine.State != PlaybackState.None
            ? context.VideoEngine.CurrentMetadata?.FilePath
            : null;

        _engine = new PulseEngine(
            _preAnalysisService, liveAmplitude, mapper,
            context.Events, context.Logger, currentMedia);

        // Wire video engine events.
        context.VideoEngine.AudioSamplesAvailable += OnAudioSamplesAvailable;
        context.VideoEngine.PositionChanged += OnPositionChanged;
        context.VideoEngine.SeekCompleted += OnSeekCompleted;

        // Create sidebar ViewModel and register panel.
        _sidebarViewModel = new PulseSidebarViewModel(_engine);

        context.RegisterSidebarPanel("pulse-sidebar",
            () => new PulseSidebarView { DataContext = _sidebarViewModel });

        // Create waveform ViewModel and register bottom panel.
        _waveformViewModel = new WaveformViewModel(_engine);

        context.RegisterBottomPanel("pulse-waveform",
            () => new WaveformPanelView { DataContext = _waveformViewModel });

        // Register control bar beat rate selector (visibility driven programmatically).
        context.RegisterControlBarItem("pulse-beat-rate",
            () =>
            {
                var view = new BeatRateComboBox { DataContext = _sidebarViewModel };
                view.Visibility = _sidebarViewModel!.UsePulse ? Visibility.Visible : Visibility.Collapsed;
                _beatRateControl = view;
                return view;
            });

        // Register status bar item.
        context.RegisterStatusBarItem("pulse-status",
            () => _sidebarViewModel.StatusBarText);

        // Wire PropertyChanged BEFORE restoring settings so visibility and
        // persistence handlers are active when the initial values are applied.
        _sidebarViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PulseSidebarViewModel.UsePulse))
            {
                context.Settings.Set("usePulse", _sidebarViewModel.UsePulse);
                if (_beatRateControl is not null)
                    _beatRateControl.Visibility = _sidebarViewModel.UsePulse ? Visibility.Visible : Visibility.Collapsed;
            }

            if (e.PropertyName == nameof(PulseSidebarViewModel.SelectedBeatRateIndex))
                context.Settings.Set("beatRateIndex", _sidebarViewModel.SelectedBeatRateIndex);

            if (e.PropertyName == nameof(PulseSidebarViewModel.StatusBarText))
                context.UpdateStatusBarItem("pulse-status", _sidebarViewModel.StatusBarText);
        };

        // Restore persisted toggle state.
        bool savedEnabled = context.Settings.Get("usePulse", false);
        if (savedEnabled)
            _sidebarViewModel.UsePulse = true;

        // Restore persisted beat rate.
        int savedBeatRate = context.Settings.Get("beatRateIndex", 0);
        _sidebarViewModel.SelectedBeatRateIndex = Math.Clamp(savedBeatRate, 0, 3);

        // Auto-switch bottom panel when Pulse activates
        _suppressSubscription = context.Events.Subscribe<Vido.Haptics.SuppressFunscriptEvent>(evt =>
        {
            if (evt.SuppressFunscripts)
                context.RequestShowBottomPanel("pulse-waveform");
        });

        context.Logger.Info("Pulse plugin activated", "Pulse");
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        if (_context != null)
        {
            _context.VideoEngine.AudioSamplesAvailable -= OnAudioSamplesAvailable;
            _context.VideoEngine.PositionChanged -= OnPositionChanged;
            _context.VideoEngine.SeekCompleted -= OnSeekCompleted;
        }

        _suppressSubscription?.Dispose();
        _waveformViewModel?.Dispose();
        _sidebarViewModel?.Dispose();
        _engine?.Dispose();
        _preAnalysisService?.Dispose();

        _context?.Logger.Info("Pulse plugin deactivated", "Pulse");
        _context = null;
        _engine = null;
        _sidebarViewModel = null;
        _waveformViewModel = null;
        _preAnalysisService = null;
    }

    private void OnAudioSamplesAvailable(AudioSampleEventArgs args)
    {
        _engine?.OnAudioSamplesAvailable(args);
    }

    private void OnPositionChanged(TimeSpan position)
    {
        _engine?.OnPositionChanged(position.TotalMilliseconds);
        _waveformViewModel?.UpdateTime(position.TotalSeconds);
    }

    private void OnSeekCompleted()
    {
        _engine?.OnSeekCompleted();
    }
}
