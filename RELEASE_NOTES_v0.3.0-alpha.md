# Centurion v0.3.0-alpha Release Notes

**📅 Release Date:** 2026-09-19

## 💬 A Note Before We Begin

This is a big one. 🎉 Since v0.2.1, Centurion has grown from a single-purpose transcription tool into a proper subtitle workstation. We've added speaker diarization, vocal separation, a dedicated translation command, full CJK support, and a production-hardening pass that fixes a couple of genuinely nasty security holes. Early testers have been incredibly patient with us — this release is largely a thank-you to you. 💙

## ✨ What's New

### 🎤 Speaker Diarization (at last!)

The long-awaited feature is here. Centurion now detects **who is speaking, when**:

- **Two backends to choose from**: CrispASR's built-in methods (`foxnose` is the default — highest accuracy, no stereo requirement) or the **Pyannote segmentation + TitaNet embedding** combo for globally stable speaker IDs on long audio.
- **Speakers flow into your subtitles**: every word gets a speaker label, and the final ASS file carries it in the standard `Name` field — plus an optional `[SPEAKER_01]` prefix on the visible text. It's the industry-standard way, so players and editors can read, filter, and recolor by speaker.
- **Smarter sentence splitting**: speaker changes now act as split points, so a new speaker starts a new line (and a new label).
- New CLI flag: `--num-speakers <N>` (0 = auto), `--no-speaker-labels` to hide the text prefix.

### 🎶 Demucs Vocal Separation

Music-heavy media finally gets a fighting chance:

- **Separate vocals before transcription** with `--vocal-separation` — ideal for songs, podcasts with BGM, and noisy livestreams.
- Runs **demucs-rs** (htdemucs model), with automatic model caching and a HuggingFace → hf-mirror fallback so it actually works behind the Great Firewall. 🐉
- Non-fatal by design: if separation fails, the pipeline quietly falls back to the original audio.

### 🌐 Translation Command

A whole new subcommand, kept cleanly separate from everything else:

- **`translate`**: translate an existing subtitle file without touching your transcription workflow.
- **Multiple strategies behind one interface**: LLM-powered translation first (OpenAI or local Ollama), with batching, retries, and graceful degradation.
- **Glossary support** (`--glossary`): domain terms stay consistent, every time.
- **Script alignment**: provide a target-language script and the translator aligns it 1:1 against your source lines.
- **Karaoke-grade word timestamps**: translated lines get interpolated word-level `\K` timestamps, with long-syllable words receiving more time. 🎵
- **Bilingual mode**: source on top, translation at the bottom, styled after real-world main/subtitle layouts.

### 🌏 Non-Latin Script Support

- **Chinese and Japanese** now work end-to-end: no-space-language splitting, CJK punctuation-aware sentence breaks, and automatic language detection in Whisper.
- `correct` also gained `-l` for language-aware calibration.

### 🏗️ Architecture & Codebase

- **Split into multiple class libraries** (`Models`, `Abstractions`, `Core`, `Cli`) — cleaner boundaries, faster builds, easier testing.
- **Full OOP pass**: pipeline operators, strategies, and DI wiring now follow consistent design patterns.
- **Unified XML documentation**: every public type and member documented (0 warnings, strict mode).
- Rich **context JSON** output (`.context.json`) alongside every subtitle file — config, per-stage sentences, diagnostics — for debugging and automation.

### 🔒 Production Hardening

- **Two high-severity security fixes**: archive extraction path traversal (zip-slip) and a command-injection hole in the self-update script. Both are closed and regression-tested.
- **HTTPS-only downloads** — the downloader refuses plain-HTTP URLs.
- **Optional SHA256 verification** for tools/models via the `fileHash` field in `metadata.json`.
- **Metadata config now auto-merges** new defaults on upgrade — your custom entries are kept, and new tools appear without manual editing.
- Top-level exception handling, unified versioning (`Directory.Build.props`), and a **GitHub Actions CI** (Windows + Ubuntu) so nothing regresses silently.

### 🚀 Other Goodies

- **`update` command**: self-update from GitHub Releases, cross-platform apply scripts.
- **GPU detection & auto-download**: CUDA / Vulkan / DirectML builds are detected and fetched on demand.
- **dotnet-style progress animation** (no more confirmation prompts).
- `from-script` (script timing) and `correct` (calibration) were re-scoped to match their names — the two commands' logic was swapped to make each do exactly what it says. 🔄

## 🐛 What We've Fixed

- Overlapping subtitle blocks after alignment (karaoke accuracy included).
- Repeated-word loss in LLM sentence splitting — fuzzy-alignment fallback now preserves every word.
- Missing `-n` argument for demucs, model download failures on HuggingFace (mirror fallback added).
- Stale test coverage updated to match the refactored argument pipelines.

## ⚠️ Known Limitations

- **Alpha software**: things may still break; we fix fast and appreciate issue reports.
- GPU builds are downloaded automatically but require a compatible driver (CUDA 12.4 / Vulkan / DirectML).
- Speaker labels are auto-assigned (`SPEAKER_01`, `SPEAKER_02`, …) — no identity mapping yet.
- Translation quality depends on your model/API; Ollama works fully offline.
- Windows is the best-tested platform; Linux/macOS builds exist and CI covers them, but expect a few rough edges.

## 📦 Installation

Download the pre-release package from the [Releases page](https://github.com/Remembering-in-Moskowien/Centurion/releases):

- `Centurion-win-x64.zip` (and Linux/macOS variants where available)

Extract and run from any directory. Tools and models download on first use.

## 🙏 Feedback Welcome

This release is feature-dense, so we'd love to hear what works and what doesn't — especially around diarization quality, translation output, and CJK handling. Open an issue, drop a suggestion, or just say hi. Every report makes the next release better.

Thank you for growing Centurion with us. 🎬

**Built with .NET 10 — still learning, still improving.** 💙
