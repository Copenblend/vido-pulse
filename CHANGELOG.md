# Changelog

All notable changes to the Pulse Plugin for Vido will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - Unreleased

### Added

- Project scaffold with `IVidoPlugin` entry point
- Plugin manifest with `com.vido.osr2-plus ≥4.0.0` dependency
- `Directory.Build.props` with centralized version management
- `package.ps1` for local build, packaging, and deployment
- GitHub Actions CI/CD workflow (`release.yml`)
- xunit + Moq test project scaffold
- `AudioRingBuffer` — lock-free SPSC ring buffer for decoded audio samples with lossy overflow
- `AmplitudeTracker` — RMS envelope follower with configurable window, mono downmix, and byte-buffer conversion
- Domain models: `BeatEvent`, `BeatMap`, `BpmEstimate`, `PulseAnalysisResult`, `PulseState`
- `SyntheticAudioGenerator` test utility for click tracks, sine waves, silence, and white noise
- Thread-safety stress tests for concurrent producer/consumer, overflow, and rapid clear scenarios
- `OnsetDetector` — spectral flux beat detection with FFT, adaptive mean-based threshold, and minimum inter-onset interval enforcement
- `BpmEstimator` — autocorrelation-based BPM estimation with weighted histogram clustering, exponential smoothing, harmonic detection, and phase-locked beat quantization
- `AudioPreAnalysisService` — pre-analyzes complete audio track via `IAudioDecoder`, runs OnsetDetector + BpmEstimator pipeline, produces `BeatMap` with beats, BPM, and downsampled waveform; cancellable with progress reporting
- `LiveAmplitudeService` — real-time amplitude tracking via ring buffer for playback-time RMS envelope
- `PulseTCodeMapper` — hybrid beat-to-position mapper: binary-search beat lookup, upstroke/downstroke waveform with quadratic easing, amplitude and beat-strength intensity scaling, configurable stroke range (5–95)
- `PulseEngine` — central state machine coordinator (Inactive/Analyzing/Ready/Active/Error); subscribes to `VideoLoadedEvent`, `PlaybackStateChangedEvent`, and OSR2+ haptic events via `IEventBus`; publishes `SuppressFunscriptEvent`, `ExternalAxisPositionsEvent`, `ExternalBeatEvent`; registers `IExternalBeatSource` for BeatBar integration; triggers pre-analysis on media load, feeds L0 positions during playback
- `PulseBeatSource` — `IExternalBeatSource` implementation with red heart rendering (`#c42b1c`), hollow heart indicator, SkiaSharp bezier heart paths
- `PulseSidebarViewModel` — sidebar panel ViewModel with `INotifyPropertyChanged`; toggle, state indicator, analysis progress, BPM readout, status messages, description text; wired to PulseEngine events
- `PulseSidebarView` — WPF sidebar panel UI: toggle switch (♥ PULSE), state dot, analysis progress bar, BPM readout, "About Pulse" description; Vido Dark Modern theme
- `PulseConverters` — WPF value converters: `BoolToVisibilityConverter`, `StateColorToBrushConverter` (Green/Yellow/Grey/Red → brush), `FractionToPercentConverter`
- `Resources/Styles.xaml` — Vido Dark Modern resource dictionary with `#c42b1c` Pulse accent, custom toggle switch, progress bar, scrollviewer styles
- `FfmpegAudioDecoder` — `IAudioDecoder` implementation using FFmpeg external process; decodes to mono float32 PCM at 44100 Hz, probes duration via ffprobe, yields 100 ms chunks
- `PulsePlugin` entry point fully wired: creates service graph, wires `IVideoEngine` events, registers sidebar panel, persists toggle state via `IPluginSettingsStore`
- `WaveformViewModel` — ViewModel for waveform bottom panel; exposes full waveform data, beat markers, BPM readout, playback position, live amplitude, configurable window duration (10s/30s/60s/2m/5m); subscribes to PulseEngine `StateChanged` and `BeatMapReady` events; `RepaintRequested` event for SkiaSharp rendering
- `WaveformPanelView` — WPF/SkiaSharp bottom panel: scrolling waveform envelope (teal `#4EC9B0` with 15% alpha fill), red `#c42b1c` beat tick markers (binary-search for visible range), white playhead cursor at 20% from left, time axis labels with adaptive tick intervals, grid lines; CompositionTarget.Rendering ~60 fps repaint; toolbar with window duration ComboBox and BPM readout
- Bottom panel registered via `RegisterBottomPanel("pulse-waveform", ...)` with `PositionChanged` → `UpdateTime` wiring
