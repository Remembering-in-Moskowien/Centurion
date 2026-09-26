---
title: Centurion
---

# 🛡️⚡ Centurion — Speech In, Subtitles Out

> **Speech → Subtitles, done properly.** 🎬
> Centurion is a **.NET 10** command-line toolkit that turns audio/video into **polished ASS subtitles** — and beyond: speaker diarization, machine translation, vocal separation, and even **machine dubbing** with Qwen3-TTS. Fully local by default, GPU-aware, zero manual tool installs. ✨

![Pipeline](https://img.shields.io/badge/architecture-operator%2Dpipeline-8A2BE2) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-2ea44f) ![Tests](https://img.shields.io/badge/tests-382%20passing-2ea44f) ![Status](https://img.shields.io/badge/status-early%20dev%20%F0%9F%9A%A7-yellow)

## 🚀 Quick Start

```bash
# 1. Install the .NET 10 SDK and FFmpeg, then build
dotnet build -c Release

# 2. Transcribe a video → intermediate file
Centurion asr demo.mp4 --language en

# 3. Render subtitles
Centurion build demo.centurion.json
```

Everything else (whisper.cpp, CrispASR, Demucs, models…) is **auto-downloaded on first use** ☕

## 📚 Documentation

| Page | What's inside |
|---|---|
| [⚡ Quick Start](quickstart.md) | Prerequisites, install, build & typical workflows |
| [🎮 Commands](commands.md) | The full command family — `convert`, `asr`, `ocr`, `correct`, `from-script`, `build` with every option |
| [🌐 Translate](translate.md) | LLM translation, glossary, target-script alignment & karaoke timestamps |
| [🎙️ Dub](dub.md) | Media dubbing with Qwen3-TTS — voice cloning, time alignment, ducking |
| [🖥️ Server](server.md) | Centurion.Server — run the whole pipeline over a REST API |
| [🔬 Advanced](advanced.md) | Operator micro-commands, quality reports, self-update, localization |
| [🎨 Features](features.md) | Subtitle styles, speaker diarization, vocal separation, GPU, non-Latin scripts |

## 🎮 Command Family at a Glance

| Command | What it does |
|---|---|
| `init` | 🧭 Interactive setup wizard — detects files, suggests the right chain |
| `convert` | 🔄 **Entry point**: any subtitle file → intermediate file |
| `asr` | 🎬 Media → intermediate through speech recognition |
| `ocr` | 👁️ Video/image → intermediate through visible text recognition |
| `from-script` | 📜 Media + script → timed intermediate file |
| `correct` | 🛠️ Calibrate timeline & text of an intermediate file |
| `translate` | 🌐 Translate an intermediate file (LLM, glossary, script) |
| `dub` | 🎙️ Intermediate → dubbed WAV audio (Qwen3-TTS) |
| `build` | 🏗️ **Exit point**: intermediate → ASS / SRT / TXT |
| `serve` | 🖥️ REST API over the pipeline (in-process HTTP) |
| `validate` / `migrate` | ✅/🔄 IR schema check & upgrade |
| `models` / `providers` | 🧠 Model registry & provider capability probes |
| `pipeline-graph` / `quality` | 🕸️📊 DAG visualization & per-run quality reports |
| `update` | 🔄 Self-update from GitHub releases |

## 🧠 How Is It Built?

**Operator-pipeline (DAG) architecture** — every stage is an independent, swappable module, executed as a directed acyclic graph with parallel branches, retries and graceful degradation:

```
┌─ OCR branch: VideoSubFinder frame pick ─► OcrExtract ──────────┐
│                                                               ▼
input ─► FFmpegConvert ─► AudioPreprocess ─► [🎤 VocalSeparation] ─► Transcribe
   ─► [👥 Diarization] ─► SentenceSplit ─► TextCleaning ─► [📐 Alignment] ─► IR (*.centurion.json) ─► build ─► ASS / SRT / TXT
```

All inference goes through a **Provider abstraction** (local-first with cloud fallback) — `models` / `providers` manage the registry. Projects: `Centurion.Models` (data) → `Centurion.Abstractions` (contracts) → `Centurion.Core` (engine) → `Centurion.Cli` (CLI, incl. `serve` HTTP mode) → `Centurion.Tests`. Details in [Features](features.md).

## 🤝 Contributing

Issues, PRs, and spicy feedback are all welcome! 🔥 Get familiar with the pipeline/operator structure first, and keep CLI options backward-compatible where possible.

## 📜 License

MIT License — see [LICENSE](https://github.com/Remembering-in-Moskowien/Centurion/blob/master/LICENSE) for details.
