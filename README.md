# 🛡️⚡ Centurion — Speech In, Subtitles Out

> **Speech → Subtitles, done properly.** 🎬
> Centurion is a **.NET 10** command-line powerhouse that turns audio/video into **polished ASS subtitles** — and beyond: speaker diarization, machine translation, vocal separation, and machine **dubbing** with Qwen3-TTS. Fully local by default, GPU-aware, zero manual tool installs.

![Pipeline](https://img.shields.io/badge/architecture-operator%2Dpipeline-8A2BE2) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-2ea44f) ![Tests](https://img.shields.io/badge/tests-382%20passing-2ea44f) ![Status](https://img.shields.io/badge/status-early%20dev%20%F0%9F%9A%A7-yellow)

---

## 🚀 Quick Start

```bash
# 1. Install the .NET 10 SDK + FFmpeg, then build
dotnet build -c Release

# 2. Transcribe a video → intermediate file
Centurion asr demo.mp4 --language en

# 3. Render subtitles
Centurion build demo.centurion.json
```

Everything else (whisper.cpp, CrispASR, Demucs, models…) is **auto-downloaded on first use** — go grab a coffee ☕

---

## ✨ Highlights

| | |
|---|---|
| 🗣️ **Speech recognition** | Whisper.cpp & CrispASR — plus **cloud ASR** (OpenAI / Groq / DashScope / Deepgram) |
| 👁️ **OCR command** | GLM-OCR (cloud) / **RapidOCR** local ONNX / Ollama / llama.cpp — reads burned-in subtitles, signs & game dialogue |
| 👥 **Speaker diarization** | Who said what, on every path; speaker labels flow into the ASS |
| 🎙️ **Dubbing** | `dub` re-voices translations with Qwen3-TTS — voice cloning, time-aligned, mixed in |
| 🌐 **Translation** | LLM (11+ providers), glossary & target-script alignment, karaoke timestamps |
| 🏗️ **IR pipeline** | One versioned rich intermediate file (`*.centurion.json`) chains every command: `convert → correct → translate → dub → build`, with `validate` / `migrate` for schema upgrades |
| 🖥️ **REST API** | `Centurion serve` runs the whole pipeline over HTTP — in-process ASP.NET Core, no separate server project |
| 🎵 **Karaoke** | Word-level `\K` timestamps, translated karaoke included |
| 🈶 **CJK & friends** | Chinese, Japanese, Korean, Cyrillic, Arabic… no space-joined gibberish |
| 📊 **Quality reports** | Every command writes a `.quality.json` — coverage, drift, CPS, warnings |
| 🔄 **Self-update** | One command, GitHub mirrors auto-selected (520-friendly 🌏) |

---

## 🎮 Command Family

| Command | What it does | Usage |
|---|---|---|
| `init` | 🧭 Interactive setup wizard — detects files, suggests the right chain | `init` |
| `convert` | 🔄 **Entry point**: any subtitle file → intermediate | `convert <INPUT_FILE>` |
| `asr` | 🎬 Media → intermediate (speech recognition) | `asr <INPUT_FILE>` |
| `ocr` | 👁️ Video/image → intermediate (visible text recognition) | `ocr <INPUT_FILE>` |
| `from-script` | 📜 Media + script → timed intermediate | `from-script <INPUT_FILE> <SCRIPT_FILE>` |
| `correct` | 🛠️ Calibrate timeline & text (+ Hunspell spell check) | `correct <CENTURION_FILE>` |
| `translate` | 🌐 Translate an intermediate file (LLM, glossary, script) | `translate <CENTURION_FILE> -t <LANG>` |
| `dub` | 🎙️ Intermediate → dubbed WAV (Qwen3-TTS) | `dub <CENTURION_FILE> -t <LANG>` |
| `build` | 🏗️ **Exit point**: intermediate → ASS / SRT / TXT | `build <CENTURION_FILE>` |
| `serve` | 🖥️ REST API over the pipeline (in-process HTTP) | `serve [--port 8080]` |
| `validate` / `migrate` | ✅/🔄 IR schema check & upgrade | `validate <FILE>` · `migrate <FILE> --to 1.0` |
| `models` | 🧠 Manage model registry — `list` / `install` / `verify` / `remove` | `models list` |
| `providers` | 🔌 Probe provider capabilities — `list` / `test` | `providers test` |
| `pipeline-graph` | 🕸️ Visualize the DAG for any workflow | `pipeline-graph <FILE>` |
| `quality` | 📊 Per-run quality report (coverage, drift, CPS, warnings) | `quality <FILE>` |
| `update` | 🔄 Self-update from GitHub releases | `update [options]` |

> 💡 `Centurion <command> --help` is always your best friend 🤝

---

## 📚 Full Documentation → [Centurion Docs](https://remembering-in-moskowien.github.io/Centurion/)

| Page | What's inside |
|---|---|
| [⚡ Quick Start](https://remembering-in-moskowien.github.io/Centurion/quickstart) | Prerequisites, install & typical workflows |
| [🎮 Commands](https://remembering-in-moskowien.github.io/Centurion/commands) | Every command & option in detail |
| [🌐 Translate](https://remembering-in-moskowien.github.io/Centurion/translate) | LLM translation, glossary, script alignment, karaoke |
| [🎙️ Dub](https://remembering-in-moskowien.github.io/Centurion/dub) | Qwen3-TTS dubbing — voice cloning, ducking, alignment |
| [🖥️ Server](https://remembering-in-moskowien.github.io/Centurion/server) | `serve` — the whole pipeline over a REST API |
| [🔬 Advanced](https://remembering-in-moskowien.github.io/Centurion/advanced) | DAG pipelines, quality reports, self-update, localization |
| [🎨 Features](https://remembering-in-moskowien.github.io/Centurion/features) | Styles, diarization, vocal separation, GPU, non-Latin scripts |

---

## 🧠 How Is It Built?

**Operator-pipeline (DAG) architecture** — every stage is an independent, swappable module, executed as a directed acyclic graph with parallel branches, retries and graceful degradation:

```
┌─ OCR branch: VideoSubFinder frame pick ─► OcrExtract ──────────┐
│                                                               ▼
input ─► FFmpegConvert ─► AudioPreprocess ─► [🎤 VocalSeparation] ─► Transcribe
   ─► [👥 Diarization] ─► SentenceSplit ─► TextCleaning ─► [📐 Alignment] ─► IR (*.centurion.json) ─► build ─► ASS / SRT / TXT
```

All inference goes through a **Provider abstraction** (local-first with cloud fallback): ASR, OCR, LLM, TTS, diarization and vocal separation each expose `IProvider` capabilities, and `models` / `providers` commands manage the registry.

| Project | Role |
|---|---|
| `Centurion.Models` | Pure data models, metadata registry, console facade |
| `Centurion.Abstractions` | Interfaces, abstract bases, DTOs |
| `Centurion.Core` | The engine: DAG executor, operators, strategies, providers, DI |
| `Centurion.Cli` | Spectre.Console CLI front-end (incl. `serve` HTTP mode) |
| `Centurion.Tests` | xUnit test suite |

Fully async & cancellation-aware; **non-fatal errors log a warning and keep going** — no half-baked bailouts 💪

---

## 🤝 Contributing & Feedback

Issues, PRs, and spicy feedback are all welcome! 🔥 Get familiar with the pipeline/operator structure first, and keep CLI options backward-compatible where possible.

## 📜 License

MIT License — see [LICENSE](LICENSE).
