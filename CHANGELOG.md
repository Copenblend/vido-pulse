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
