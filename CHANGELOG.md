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
