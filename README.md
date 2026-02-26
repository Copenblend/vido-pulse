# Pulse Plugin for Vido

Audio-to-haptics beat sync — pre-analyzes audio to detect beats and generate TCode haptic commands. Works on any video with audio, no funscript required.

## Features

- Automatic audio analysis on video load — detects beats and estimates BPM
- Beat-synchronized haptic output on the L0 (stroke) axis
- Integrates with the OSR2+ BeatBar for visual beat feedback
- Scrolling waveform visualizer in the bottom panel with playback cursor
- Status bar indicator showing current Pulse state and BPM
- Configurable beat sensitivity and BPM phase-lock
- Works alongside funscript-based playback — toggle Pulse on/off at any time

## Requirements

- **Vido** 0.10.0 or later
- **OSR2+ Plugin** 4.1.0 or later (installed automatically when installing from registry)

## Installation

1. Open Vido and go to **Settings → Plugins**
2. Click **Install from Registry** and select the **Pulse** plugin
3. The OSR2+ plugin dependency is installed automatically if not already present
4. Restart Vido when prompted

For manual installation, download the plugin zip and extract it to:
```
%APPDATA%\Vido\plugins\com.vido.pulse\
```
Then restart Vido.

## Getting Started

1. Open any video with audio in Vido
2. Open the **Pulse sidebar** panel
3. Toggle **Use Pulse** on — audio analysis begins automatically
4. Wait for analysis to complete (progress is shown in the sidebar and status bar)
5. Press play — haptic commands are generated in sync with detected beats

When Pulse is enabled, funscript auto-loading is suppressed. Toggle Pulse off to restore normal funscript behavior.

## Sidebar Panel

The sidebar shows:

- **Use Pulse** toggle — enables or disables the plugin
- **State indicator** — color-coded: grey (off), yellow (analyzing/ready), green (active), red (error)
- **BPM readout** — detected tempo, shown when analysis is complete
- **Analysis progress** — progress bar during audio analysis
- **Status message** — human-readable description of current state

## Waveform Panel

The bottom panel displays a scrolling waveform visualization:

- Full-track waveform rendered in teal
- White playback cursor at 20% from the left edge
- Time labels along the bottom
- Automatically scrolls to follow playback position

## Status Bar

The status bar shows a compact Pulse status indicator on the right side:

- **♥ Pulse: Off** — plugin is disabled
- **♥ Pulse: Analyzing...** — audio analysis in progress
- **♥ Pulse: Ready** — analysis complete, waiting for playback
- **♥ Pulse: Active 120 BPM** — actively generating haptic commands
- **♥ Pulse: Error** — an error occurred during analysis

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Beat Sensitivity | 1.5 | Onset detection threshold multiplier. Higher values detect fewer beats. Range: 0.5–5.0 |
| BPM Phase Lock | On | Quantizes beats to the estimated BPM grid for more consistent rhythm. Disable for non-rhythmic content. |
| Waveform Window | 30s | Duration of the visible waveform window. Options: 30s, 60s, 120s, 300s |

## How It Works

1. **Audio extraction** — When a video loads and Pulse is enabled, FFmpeg decodes the audio track into PCM samples
2. **Beat detection** — An onset detector analyzes the audio for transient peaks using spectral flux analysis
3. **BPM estimation** — A tempo estimator calculates the dominant BPM from onset intervals
4. **Phase locking** — Optionally quantizes detected beats to a regular BPM grid
5. **Haptic mapping** — During playback, the PulseTCodeMapper converts beats near the current position into L0 axis TCode commands
6. **BeatBar integration** — A "Pulse" mode appears in the OSR2+ BeatBar, displaying beat markers as red hearts

## License

MIT
