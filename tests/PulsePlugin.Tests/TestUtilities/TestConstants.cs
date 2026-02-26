namespace PulsePlugin.Tests.TestUtilities;

/// <summary>
/// Shared test constants.
/// </summary>
internal static class TestConstants
{
    /// <summary>Standard test sample rate.</summary>
    public const int SampleRate44100 = 44100;

    /// <summary>Standard test sample rate.</summary>
    public const int SampleRate48000 = 48000;

    /// <summary>Stereo channel count.</summary>
    public const int Stereo = 2;

    /// <summary>Mono channel count.</summary>
    public const int Mono = 1;

    /// <summary>Default amplitude tracker window in ms.</summary>
    public const double DefaultWindowMs = 20.0;
}
