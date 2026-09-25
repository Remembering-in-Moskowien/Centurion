# Centurion v0.4.0-alpha Release Notes

**📅 Release Date:** 2026-09-25

## 💬 A Note Before We Begin

Two weeks after v0.3.0, Centurion has grown from a subtitle workstation into a **media localization toolkit**. 🚀 This release brings machine dubbing, a REST API server, OCR transcription, cloud ASR providers, and a rebuilt intermediate-file architecture — plus a serious logging & quality pass. Same DNA: *speech in, subtitles out* — now with *translated audio out* too. 🎙️

## ✨ What's New

### 🎙️ `dub` — Media Dubbing with Qwen3-TTS

The headline act. Feed Centurion a bilingual subtitle file (or an intermediate file) plus the media, and it **re-voices the translation** into WAV audio:

- **Voice cloning from speaker profiles** — each speaker's longest/cleanest lines become the reference for their dubbed voice.
- **Reuses the vocal-separation stack** — vocals are pulled out, TTS voices are mixed back over the original accompaniment (ducking included, no more quiet BGM drowning the dub).
- **Time alignment** keeps the translation roughly in sync with the original pacing (FFmpeg atempo).
- Result is saved back into the intermediate file, ready for further tweaks.
- Phase 1 MVP by design — expect speaker profiling and emotional transfer to sharpen in v0.5. 🎯

### 🖥️ Centurion.Server — The Pipeline, Over HTTP

The CLI's old `--config` JSON flag and the embedded HttpListener `server` command are **gone**, replaced by a proper **ASP.NET Core service** in its own project:

- `GET /health` · `GET /commands` · `GET /version` (build date, not a version number 😉)
- `POST /commands/{name}` — execute any packaged command with a JSON body (full `CommandRequest` or bare parameters object)
- Same DI container, same settings binding, same operators as the CLI — a request behaves exactly like a console invocation
- Command output is routed into the server's `ILogger`, so long pipelines stay observable
- Add a command by registering it in `ServerCommandRegistry` — instant API exposure

**Why:** JSON-driven invocation now lives *only* in the Server project. One contract, one place to maintain — and a clean foundation for the JSON-RPC / REST future. 🔌

### 👁️ OCR Transcription (`spawn -m ocr`)

Subtitles burned into the video? Game dialogue? Sign text? Now Centurion can **read the screen**:

- **GLM-OCR** in the cloud (Zhipu), or **fully local** inference via Ollama / llama.cpp vision models
- Frames are extracted at an interval, OCR'd, and consecutive identical lines merge into timed sentences
- Flows into the usual `split → clean → quality` stages — and yes, you can point it at a single image too 🖼️

### ☁️ Cloud ASR Providers

Four new transcription engines behind the existing `-t` flag:

| Engine | Notes |
|---|---|
| `openai` | OpenAI Whisper API |
| `groq` | Fast & cheap |
| `dashscope` | Alibaba — great for Chinese |
| `deepgram` | Streaming-quality ASR |

Set `--asr-api-key` (and `--asr-base-url` for custom endpoints) and go. The endpoint parser is provider-aware and forgiving. ☁️

### 🏗️ Rebuilt Command Model: IR In, IR Out

- **`convert` is the entry** (any subtitle → `*.centurion.json`), **`build` is the exit** (intermediate → **ASS, SRT or TXT**)
- Every other command reads and writes only the intermediate file — chain `convert → correct → translate → dub → build` with zero information loss
- New `CenturionFileIO` owns the format; `SubtitleFormatRenderer` renders each output style consistently

### 🔬 Operator Micro-Commands

The major pipeline operators can now be invoked **directly as small commands** (`transcribe`, and friends) — while the packaged commands (`spawn`, `correct`, …) remain exactly as they were: curated multi-operator recipes. Best of both worlds. 🧩

### 🔤 One LLM Dialect for Everything

A new provider registry + endpoint parser speaks the **OpenAI-compatible dialect** everyone uses:

- `openai` · `deepseek` · `moonshot` · `zhipu` · `openrouter` · `groq` · `siliconflow` · `dashscope` · `ark` · `azure` · `ollama`
- `auto` infers the provider from the endpoint host; forgiving aliases (`ds`, `kimi`, `glm`…) all resolve
- Powers translation *and* LLM sentence splitting with the same options

### 📋 Logging & Output, Done Properly

- **File logs**: every run writes to the `logs` directory — one log per run, cleaned on startup
- **`FailLogGate`**: exactly one `fail` per execution path (at the outermost boundary); everything else is `warn`
- **Sane console**: only errors, warnings and success get color; everything else is plain white with the standard prefix
- Console output flows through the same `ILogger` pipeline — what you see is what's logged

### 🌐 JSON Localization

UI strings are now localized from JSON files (English by default, `zh-CN` included) — ready for more languages without a rebuild.

### 🛠️ Production & Quality Pass

- **Quality reports** on every path (`*.quality.json`) — translation coverage, alignment deviation, and more
- **mkvtoolnix integration**: subtitle-track inspection runs automatically before generate/calibrate/script-timing commands (auto-downloaded; warns when existing tracks are found)
- **Hunspell spell check** for `correct --spellcheck` (en_US auto-downloaded, `.spellcheck.json` report)
- `correct` now tries to extract subtitles **from the media** when no subtitle file is given
- Unified temp-directory management under the program root
- GitHub downloads route through the **520 mirror** automatically when the main CDN stalls 🌐

## 🐛 What We've Fixed

- `--config` removed cleanly from every command — no more dual sources of truth for parameters
- `convert` parsing rewritten against the IR model (previous per-format special-casing is gone)
- Update flow now compares **build dates** instead of version numbers
- Console output no longer doubles up between renderer and logger
- Long-standing inconsistency between per-command logging levels is gone — one filter, one format

## ⚠️ Known Limitations

- **Alpha software**: things may still break; we fix fast and appreciate issue reports.
- `dub` is a Phase 1 MVP — speaker profiling picks reference lines automatically; manual `--speaker-reference` and finer ducking land next.
- OCR accuracy tracks your model: GLM-OCR (cloud) is the most reliable; local vision models are convenient but humbler.
- Server executes commands **synchronously per request** — long pipelines block that request until done. Streaming/polling is planned.
- Windows remains the best-tested platform; Linux/macOS builds exist and CI covers them, expect a few rough edges.

## 📦 Installation

Download the pre-release package from the [Releases page](https://github.com/Remembering-in-Moskowien/Centurion/releases):

- `Centurion-win-x64.zip` (and Linux/macOS variants where available)

Extract and run from any directory. Tools and models download on first use. Need the API? `dotnet run --project Centurion.Server` — or just grab the published exe.

## 🙏 Feedback Welcome

Feature-dense release again — we'd love reports on **dub quality**, **OCR extraction**, **cloud ASR accuracy** (especially DashScope for Chinese) and **Server ergonomics**. Open an issue, drop a suggestion, or just say hi. Every report makes the next release better. 💙

**Built with .NET 10 — still learning, still improving.** 🎬
