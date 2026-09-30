# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses a build-number release model: each release is tagged
`build-N`, where N is the git commit count. Since build-81, every push to
`master` is automatically published as a pre-release by GitHub Actions.

## [Unreleased]

### Added

- `Centurion doctor` — environment & diagnostics probe (runtime, toolchain,
  models, config, network, disk); writes a Markdown diagnostic log for issue
  reporting; `--json` emits a machine-readable report.
- QORA-TTS 1.7B engine (`dub --tts-engine qora`) — pure-Rust Qwen3-TTS
  inference; `tools/qora-tts` auto-downloads the 1.56GB model on first use.
- IndexTTS-Rust engine (`dub --tts-engine indextts`) and the Qwen3-TTS 1.7B
  model registry entry (`qwen3tts|1.7b-base-q4`).

### Changed

- Command help, descriptions and provider catalogs fixed to English by default
  (zh-CN auto-detection removed).

### Removed

- `update` command (registration and documentation) until the new release
  flow settles.

## [build-83] - 2026-09-30

- Ignore local `.cache/` and `publish/` directories.

## [build-82] - 2026-09-30

- Publish workflow fetches full history so `build-N` equals the real commit
  count (fixes shallow-checkout reporting `build-1`).

## [build-81] - 2026-09-30

### Added

- GitHub Actions `publish.yml`: every push to `master` builds, tests and
  publishes a `build-N` pre-release with `centurion-win64.zip` (without
  third-party tool executables; those auto-download on first use).

### Removed

- `update` command registration and README mentions.
- All legacy pre-releases/tags (`v0.1.0-alpha` .. `v0.6.0-alpha`).

## [v0.6.0-alpha (build-78)] - 2026-09-27

### Added

- Self-update supporting pre-release chains; version identity unified to the
  build number.
- VideoSubFinder bundled under `tools/videosubfinder` (no download at release).

### Fixed

- RapidOCR validation allows auto-downloading models; invalid `gold` color
  styling.

## [v0.5.0-alpha] - 2026-09-26

- ROI-based OCR selection (`--ocr-roi-*`); source projects reorganized under
  `src/`; README and architecture diagram updated to the v0.5.0 state.

## [v0.4.0-alpha] - 2026-09-25

### Added

- Layered project structure, versioned IR schema (`validate` / `migrate`),
  provider abstraction with fallback chains, DAG executor with parallel
  translation and `pipeline-graph`, quality reports with auto-repair,
  modernized Spectre CLI, `init` wizard + config system, localization.

### Changed

- Core commands unified as DAG; single-operator subcommands removed.

## [v0.3.0-alpha] - 2026-09-19

- GitHub Pages site, restructured README, release notes.

## [v0.2.1-alpha] - 2026-08-31

- Mixed-language aware tokenization for code-switching text.

## [v0.2.0-alpha] - 2026-08-30

- Speaker diarization, vocal separation, translation, `dub` command, IR
  rework, command decomposition, server mode.

## [v0.1.0-alpha] - 2026-08-19

- Initial release: CLI/Core split, whisper-based transcription pipeline,
  sentence splitting, alignment (WIP), ASS output.
