using Vido.Core.Plugin;

namespace PulsePlugin;

/// <summary>
/// Pulse plugin entry point. Audio-to-haptics beat sync — pre-analyzes audio
/// to detect beats and generate TCode haptic commands.
/// </summary>
public class PulsePlugin : IVidoPlugin
{
    private IPluginContext? _context;

    /// <inheritdoc />
    public void Activate(IPluginContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        _context = null;
    }
}
