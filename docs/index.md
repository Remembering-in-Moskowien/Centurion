---
title: Centurion
---

# 🛡️⚡ Centurion — Speech In, Subtitles Out

> **Speech → Subtitles, done properly.** 🎬
> Centurion is a **.NET 10** command-line toolkit that turns audio/video into **polished ASS subtitles** — and beyond: speaker diarization, machine translation, vocal separation, and even **machine dubbing** with Qwen3-TTS. Fully local by default, GPU-aware, zero manual tool installs. ✨

![Pipeline](https://img.shields.io/badge/architecture-operator%2Dpipeline-8A2BE2) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-2ea44f) ![Tests](https://img.shields.io/badge/tests-261%20passing-2ea44f) ![Status](https://img.shields.io/badge/status-early%20dev%20%F0%9F%9A%A7-yellow)

## 🚀 Quick Start

```bash
# 1. Install the .NET 10 SDK and FFmpeg, then build
dotnet build -c Release

# 2. Transcribe a video → intermediate file
Centurion spawn demo.mp4 --language en

# 3. Render subtitles
Centurion build demo.centurion.json
```

Everything else (whisper.cpp, CrispASR, Demucs, models…) is **auto-downloaded on first use** ☕

## 📚 Documentation

| Page | What's inside |
|---|---|
| [⚡ Quick Start](quickstart.md) | Prerequisites, install, build & typical workflows |
| [🎮 Commands](commands.md) | The full command family — `convert`, `spawn`, `correct`, `from-script`, `build` with every option |
| [🌐 Translate](translate.md) | LLM translation, glossary, target-script alignment & karaoke timestamps |
| [🎙️ Dub](dub.md) | Media dubbing with Qwen3-TTS — voice cloning, time alignment, ducking |
| [🖥️ Server](server.md) | Centurion.Server — run the whole pipeline over a REST API |
| [🔬 Advanced](advanced.md) | Operator micro-commands, quality reports, self-update, localization |
| [🎨 Features](features.md) | Subtitle styles, speaker diarization, vocal separation, GPU, non-Latin scripts |

## 🎮 Command Family at a Glance

| Command | What it does |
|---|---|
| `convert` | 🔄 **Entry point**: any subtitle file → intermediate file |
| `spawn` | 🎬 Media → intermediate (ASR **or OCR** mode, cloud/local) |
| `from-script` | 📜 Media + script → timed intermediate file |
| `correct` | 🛠️ Calibrate timeline & text of an intermediate file |
| `translate` | 🌐 Translate an intermediate file (LLM, glossary, script) |
| `dub` | 🎙️ Intermediate → dubbed WAV audio (Qwen3-TTS) |
| `build` | 🏗️ **Exit point**: intermediate → ASS / SRT / TXT |
| `update` | 🔄 Self-update from GitHub releases |
| `transcribe` … `quality` | 🔬 Operator micro-commands — one pipeline stage at a time |

## 🧠 How Is It Built?

**Operator-pipeline architecture** — every stage is an independent, swappable module:

```
input ──► FFmpegConvert ──► AudioPreprocess ──► [🎤 VocalSeparation] ──► Transcribe
             ──► [👥 Diarization] ──► SentenceSplit ──► TextCleaning ──► Alignment ──► ASS 📦
```

Projects: `Centurion.Models` (data) → `Centurion.Abstractions` (contracts) → `Centurion.Core` (engine) → `Centurion.Cli` (CLI) → `Centurion.Server` (REST API) → `Centurion.Tests`. Details in [Features](features.md).

## 🤝 Contributing

Issues, PRs, and spicy feedback are all welcome! 🔥 Get familiar with the pipeline/operator structure first, and keep CLI options backward-compatible where possible.

## 📜 License

MIT License — see [LICENSE](https://github.com/Remembering-in-Moskowien/Centurion/blob/master/LICENSE) for details.
