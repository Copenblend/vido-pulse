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
    private AudioPreAnalysisService? _preAnalysisService;

    /// <inheritdoc />
    public void Activate(IPluginContext context)
    {
        _context = context;

        // Create service graph.
        var decoder = new FfmpegAudioDecoder();
        _preAnalysisService = new AudioPreAnalysisService(decoder);
        var liveAmplitude = new LiveAmplitudeService();
        var mapper = new PulseTCodeMapper();

        // Detect already-loaded media.
        string? currentMedia = context.VideoEngine.CurrentMetadata?.FilePath;

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

        // Restore persisted toggle state.
        bool savedEnabled = context.Settings.Get("usePulse", false);
        if (savedEnabled)
            _sidebarViewModel.UsePulse = true;

        // Persist toggle changes.
        _sidebarViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PulseSidebarViewModel.UsePulse))
                context.Settings.Set("usePulse", _sidebarViewModel.UsePulse);
        };

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

        _sidebarViewModel?.Dispose();
        _engine?.Dispose();
        _preAnalysisService?.Dispose();

        _context?.Logger.Info("Pulse plugin deactivated", "Pulse");
        _context = null;
        _engine = null;
        _sidebarViewModel = null;
        _preAnalysisService = null;
    }

    private void OnAudioSamplesAvailable(AudioSampleEventArgs args)
    {
        _engine?.OnAudioSamplesAvailable(args);
    }

    private void OnPositionChanged(TimeSpan position)
    {
        _engine?.OnPositionChanged(position.TotalMilliseconds);
    }

    private void OnSeekCompleted()
    {
        _engine?.OnSeekCompleted();
    }
}
