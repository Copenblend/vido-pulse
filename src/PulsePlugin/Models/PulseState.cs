namespace PulsePlugin.Models;

/// <summary>
/// Represents the current state of the Pulse engine.
/// </summary>
public enum PulseState
{
    /// <summary>Pulse is off — user has not toggled "Use Pulse".</summary>
    Inactive,

    /// <summary>Pulse is on, pre-analyzing audio for the loaded video.</summary>
    Analyzing,

    /// <summary>Pulse is on, analysis complete, waiting for playback to start.</summary>
    Ready,

    /// <summary>Pulse is actively generating TCode from pre-analyzed BeatMap.</summary>
    Active,

    /// <summary>Pulse is in error state (e.g., OSR2+ not connected, analysis failed).</summary>
    Error
}
