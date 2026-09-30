# Centurion

Speech in, subtitles out. Centurion is a .NET 10 command-line application that
turns audio and video into polished ASS subtitles, and goes further: speaker
diarization, machine translation, vocal separation, and machine dubbing with
Qwen3-TTS / QORA-TTS / IndexTTS. Fully local by default, GPU-aware.

![architecture](https://img.shields.io/badge/architecture-operator%2Dpipeline-8A2BE2)
![platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-2ea44f)
![tests](https://img.shields.io/badge/tests-391%20passing-2ea44f)
![status](https://img.shields.io/badge/status-active-yellow)

---

## Quick Start

```bash
# 1. Install the .NET 10 SDK + FFmpeg, then build
dotnet build -c Release

# 2. Transcribe a video to an intermediate file
Centurion asr demo.mp4 --language en

# 3. Render subtitles
Centurion build demo.centurion.json
```

Third-party tools and models (whisper.cpp, CrispASR, Demucs, QORA-TTS weights,
etc.) are downloaded automatically on first use.

---

## Highlights

| Capability | Description |
|---|---|
| Speech recognition | Whisper.cpp and CrispASR, plus cloud ASR (OpenAI / Groq / DashScope / Deepgram) |
| OCR | GLM-OCR (cloud), RapidOCR (local ONNX), Ollama, llama.cpp — burned-in subtitles, signs and game dialogue |
| Speaker diarization | Speaker labels flow into the ASS output |
| Dubbing | Qwen3-TTS / QORA-TTS / IndexTTS voice cloning, time-aligned and mixed in |
| Translation | 11+ LLM providers, glossary and target-script alignment, karaoke timestamps |
| IR pipeline | One versioned intermediate file (`*.centurion.json`) chains every command |
| REST API | `Centurion serve` exposes the pipeline over HTTP (in-process ASP.NET Core) |
| Quality reports | Every command writes a `.quality.json` with coverage, drift, CPS and warnings |

---

## Commands

The full command reference lives in the
[Commands documentation](https://remembering-in-moskowien.github.io/Centurion/commands).
Short overview:

| Command | Purpose |
|---|---|
| `init` | Interactive setup wizard |
| `convert` | Subtitle file to intermediate |
| `asr` / `ocr` / `from-script` | Media to intermediate |
| `correct` | Timeline and text calibration |
| `translate` / `dub` | Translation and dubbing |
| `build` | Intermediate to ASS / SRT / TXT |
| `quality` / `validate` / `migrate` / `pipeline-graph` | QA and IR tooling |
| `models` / `providers` | Model and provider registry |
| `serve` | REST API |
| `doctor` | Environment diagnostics and diagnostic log |

Every command accepts `--help`. `Centurion doctor` probes the environment
(runtime, toolchain, models, config, network, disk) and writes a diagnostic
log suitable for issue reports.

---

## Documentation

- [Quick Start](https://remembering-in-moskowien.github.io/Centurion/quickstart)
- [Commands](https://remembering-in-moskowien.github.io/Centurion/commands)
- [Translate](https://remembering-in-moskowien.github.io/Centurion/translate)
- [Dub](https://remembering-in-moskowien.github.io/Centurion/dub)
- [Server](https://remembering-in-moskowien.github.io/Centurion/server)
- [Advanced](https://remembering-in-moskowien.github.io/Centurion/advanced)
- [Features](https://remembering-in-moskowien.github.io/Centurion/features)

---

## Architecture

Operator-pipeline (DAG) architecture: every stage is an independent, swappable
module, executed as a directed acyclic graph with parallel branches, retries
and graceful degradation.

```
input ─► FFmpegConvert ─► AudioPreprocess ─► [VocalSeparation] ─► Transcribe
   ─► [Diarization] ─► SentenceSplit ─► TextCleaning ─► [Alignment]
   ─► IR (*.centurion.json) ─► build ─► ASS / SRT / TXT
```

OCR branch: VideoSubFinder frame pick to OcrExtract, parallel to the audio path.

All inference goes through a provider abstraction (local-first with cloud
fallback): ASR, OCR, LLM, TTS, diarization and vocal separation each expose
`IProvider` capabilities; the `models` and `providers` commands manage the
registry.

| Project | Role |
|---|---|
| `src/Centurion.Models` | Data models, metadata registry, console facade |
| `src/Centurion.Abstractions` | Interfaces, abstract bases, DTOs |
| `src/Centurion.Core` | Engine: DAG executor, operators, strategies, providers, DI |
| `src/Centurion.Cli` | Spectre.Console CLI front-end (incl. `serve` HTTP mode) |
| `src/Centurion.Tests` | xUnit test suite |

---

## Versioning & Releases

- Version identity is the build number: banners and IR provenance
  (`generator.version`) show `build-N`, where N is the git commit count.
- Every push to `master` is automatically published by GitHub Actions as a
  pre-release tagged `build-N`, with asset `centurion-win64.zip`.
- Release packages do not include third-party tool executables
  (VideoSubFinder / RapidOCR / QORA-TTS / IndexTTS / llama.cpp); they are
  downloaded on first use or placed by the user under `tools/`.
- Build dates can be pinned with `-p:BuildDate=yyyy-MM-ddTHH:mm:ssZ`.

See [CHANGELOG.md](CHANGELOG.md) for the full history.

---

## Contributing

Issues, pull requests and feedback are welcome. Familiarize yourself with the
pipeline/operator structure first, and keep CLI options backward-compatible
where possible.

## License

MIT — see [LICENSE](LICENSE).
