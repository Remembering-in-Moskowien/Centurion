# Centurion v0.5.0-alpha Release Notes

**📅 Release Date:** 2026-09-26

## 💬 A Note Before We Begin

A day after v0.4.0, this release is about **engineering backbone** — the parts you rarely see but feel on every run. The pipeline is now a real **DAG** (parallel branches, conditional nodes, retries), the intermediate file is a **versioned contract** (`validate` / `migrate`), all inference flows through a **provider abstraction** with fallback chains, the CLI got a full Spectre.Console facelift, and OCR gained a **RapidOCR** local backend plus a reliable **VideoSubFinder** auto-download. We also **removed the experimental VAD pre-filter** entirely after diagnosing that it was useless on full-music material. Same DNA: *speech in, subtitles out* — now on sturdier rails. 🚂

## ✨ What's New

### 🧱 Governance & Architecture (Steps ①–⑨)

- **Centurion.Core 分层目录** — source is organized into `Workflow / Capabilities / Processing / Operators / Utils`; no more stray files piling up at the root.
- **IR Schema 版本化** — `*.centurion.json` is now a stable contract: root `CenturionDocument` carries `schemaVersion` + `generator` + `provenance` (operator / model / parameter hash per step).
  - New `validate <file>` (pre-flight schema check) and `migrate <file> --to 1.0` (upgrade old IR files) commands.
  - Serialization via **System.Text.Json source generators** (no reflection); JSON Schema at `schemas/centurion-v1.json`.
  - All IR reads/writes go through `ICenturionDocumentStore`.
- **富上下文工作流状态** — official fields persist into the IR; edge data (single-operator scratch) lives in `Extensions` and never leaks into output.
- **Provider 抽象** — six domain interfaces (`IAsrProvider / IOcrProvider / ILlmProvider / ITtsProvider / IDiarizationProvider / IVocalSeparationProvider`) with `ProviderCapabilities` (local/cloud, language, GPU, cost, latency, quality tier):
  - **Fallback chains**: cloud-first → local fallback, or local-first → cloud backup; no API key = automatic local fallback, never a crash.
  - New `Centurion models list / install / verify / remove` and `Centurion providers list / test`.
  - Built-in profiles: `offline`, `fast`, `quality`, `cheap`; unified rate-limit, retry, circuit-breaker and budget control.
  - Run summary now reports tokens, audio minutes, cache hits and estimated cost.

### 🕸️ DAG Pipeline & Quality Loop

- **DAG executor** replaces the linear pipeline: nodes are operators, edges are data dependencies.
  - **Parallel branches** — vocal separation ∥ diarization, multi-language translation in parallel.
  - **Conditional nodes** (`when`), per-node **timeout / retry / cancel / degradation** — one node failing never sinks the whole task.
  - `Centurion pipeline-graph` visualizes the DAG for any workflow.
- **Quality closed loop** — `.quality.json` now covers CPS, line width, min/max duration, overlap, ASR confidence, translation term hit-rate, length deviation, back-translation similarity, TTS alignment error, loudness & ducking.
  - **HTML quality report** with CI thresholds: `--fail-on cps>20`, `--fail-on coverage<95` — issues are located down to the specific subtitle line.
- Core commands are now **DAG-native**; the single-operator micro-commands were removed. Missing models fail fast with a pointer to `Centurion models install`.

### 🎨 Modern CLI & Configuration

- `Centurion init` — interactive wizard; detects existing subtitle files and inserts a `convert` step automatically.
- `--json` (script-consumable output), `--dry-run` (preview DAG, models & estimated cost), `--verbose`, standardized exit codes.
- `centurion.config.json` support with environment-variable overrides.
- **Spectre.Console 全面现代化** — aligned big tables, redesigned layout, gold `WARN` badges (no more clashing with PowerShell yellow), custom color palette for warnings/errors.
- **Localization** — English-first, auto-detects the environment language; all help text is hard-coded English.
- **Windows terminal compatibility** — UTF-8 output + Unicode capability detection fallback (no more unrecognizable glyphs).
- Banner shows **Build #N** (git commit count), never a version number.
- `server` merged into the CLI as **`serve`** — in-process ASP.NET Core, no separate project.

### 👁️ OCR: RapidOCR + Reliable VSF Auto-Download

- **RapidOCR local backend** (PaddleOCR ONNX, bundled engine) — fully offline OCR, no API key needed.
- **VideoSubFinder 6.10** — official zip support with a working **SourceForge direct-link** auto-download (no more manual zip hunting); extracts subtitle frames before OCR.
- OCR still supports GLM-OCR (cloud) and Ollama / llama.cpp local vision models.

## 🐛 What We've Fixed

- **Duplicate first word on every subtitle line** — token-repeat bug in the ASR post-processing is gone (dedupe rule: identical text + identical timestamps).
- Quality report **duration units** and a **downgrade-path glyph crash** on Windows consoles.
- `init` recommendation chain now correct for existing subtitle files.
- Logging/console output reworked; `WARN`/`ERR` badges use the new palette.
- Help text fully localized to English (no more hard-coded Chinese strings in the CLI).

## 🧹 What We've Removed

- **VAD pre-filter entirely** — the energy-VAD + Silero ONNX experiment was diagnosed as useless on full-music material (the whole track classifies as "non-speech", and energy VAD degenerates to a single segment on mixed audio). The original pipeline (preprocess → vocal separation → transcribe) is restored, along with its config, settings, DI registrations, DAG nodes, tests and the ONNX Runtime dependency. The codebase is clean of `VoiceActivityFilter` / `Silero` / `OnnxRuntime` references.

## ⚠️ Known Limitations

- **Alpha software**: things may still break; we fix fast and appreciate issue reports.
- `dub` remains a Phase 1 MVP — speaker profiling picks reference lines automatically; manual `--speaker-reference` and finer ducking are next.
- OCR accuracy tracks your model: RapidOCR (local) is fast and offline; GLM-OCR (cloud) is the most reliable for hard frames.
- `serve` executes commands **synchronously per request** — long pipelines block that request until done; streaming/polling is planned.
- Windows remains the best-tested platform; Linux/macOS builds exist and CI covers them, expect a few rough edges.

## 📦 Installation

Download the pre-release package from the [Releases page](https://github.com/Remembering-in-Moskowien/Centurion/releases):

- `centurion-win64.zip`

Extract and run from any directory. Tools and models download on first use. Need the API? `Centurion serve` — the REST API now lives inside the CLI binary.

## 🙏 Feedback Welcome

Architecture-heavy release — we'd love reports on **DAG pipeline edge cases**, **provider fallback behavior**, **RapidOCR extraction**, **quality-report CI thresholds** and the new **`init` wizard**. Open an issue, drop a suggestion, or just say hi. Every report makes the next release better. 💙

**Built with .NET 10 — still learning, still improving.** 🎬
