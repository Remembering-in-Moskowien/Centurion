# 🛡️⚡ Centurion — Speech In, Subtitles Out, One Pipeline

> **Speech → Subtitles, done properly.** 🎬
> Centurion is a **.NET 10** command-line powerhouse that turns audio/video into **polished ASS subtitles**: transcribe → diarize → split → clean → force-align, all in one automatic pipeline. Sit back, relax, let it cook. ✨

![Pipeline](https://img.shields.io/badge/architecture-operator%2Dpipeline-8A2BE2) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-2ea44f) ![Status](https://img.shields.io/badge/status-early%20dev%20%F0%9F%9A%A7-yellow)

---

## 🚦 Project Status

> ⚠️ **Early development (pre-release)** — commands and flags may shift as we go. If something surprises you, don't panic: `--help` is always your best friend 🤝

- 🎯 Primary output: **ASS subtitle files**
- 🧠 Inference: **CPU by default**, GPU auto-detected & auto-equipped (see [🖥️ GPU Detection & Auto-Download](#🖥️-gpu-detection--auto-download))
- 📦 Dependencies: **FFmpeg + .NET 10 SDK** — everything else is **downloaded on demand** (see [🗂️ Metadata Registry](#🗂️-metadata-registry))

---

## ✨ What Can It Do?

| Capability | What it means | Status |
|---|---|---|
| 🎬 Full transcription | Audio/video → ASS (mp3, mp4, mkv, flac…) | ✅ Out of the box |
| 🗣️ Two transcription engines | **Whisper.cpp** and **CrispASR** (Qwen3 / Whisper backends) | ✅ Pick your poison |
| 👥 Speaker diarization | Every pipeline path labels "who said what" — two backends | ✅ On by default |
| 🎤 Vocal separation | Demucs-rs pulls out the vocals, so BGM can't drown you | 🎛️ Opt-in |
| 🖥️ GPU smart adaptation | Auto-detects CUDA/Vulkan/DirectML, auto-downloads GPU tool builds | ✅ Fully automatic |
| ✂️ Smart sentence splitting | Rule / NLP / LLM strategies | ✅ |
| 📏 Forced alignment | Word-level timestamps + text cleaning, subtitles hit the beat | ✅ On by default |
| 🎵 Karaoke mode | `\K` tags for word-by-word highlighting | ✅ |
| 📜 Script timing | Have a script + media? `from-script` aligns them instantly | ✅ |
| 🛠️ Subtitle calibration | Existing subs slightly off? `correct` straightens them out | ✅ |
| 🔄 Format conversion | SRT/VTT/… → ASS, no fuss | ✅ |
| 🔄 Self-update | One command pulls the latest release from GitHub | ✅ |

---

## 🧠 How Is It Built?

**Operator-pipeline architecture**: every stage is an independent module — swappable, extendable, reorderable. No monoliths, no tears. 🧩

```
 input ──► FFmpegConvert ──► AudioPreprocess ──► [🎤 VocalSeparation] ──► Transcribe
              ──► [👥 Diarization] ──► SentenceSplit ──► TextCleaning ──► Alignment ──► ASS 📦
```

### 📦 Project Layout (clean layering, zero circular deps)

| Project | Role | Depends on |
|---|---|---|
| `Centurion.Models` | Pure data models (`Sentence`/`Word`/`WorkflowConfig`/ASS…), metadata JSON registry, console facade, `InferenceDevice` | nothing 🧱 |
| `Centurion.Abstractions` | Interfaces & abstract bases (operators, strategies, factories), exceptions, request DTOs | `Centurion.Models` |
| `Centurion.Core` | The engine: managers, operators, pipeline executor, strategies, DI wiring | `Centurion.Models` + `Centurion.Abstractions` |
| `Centurion.Cli` | Spectre.Console command-line front-end | Core + Models + Abstractions |
| `Centurion.Tests` | xUnit test suite | Core + Models + Abstractions |

- Each step is a `PipelineOperator` running on a shared `SubtitleWorkflowContext` (immutable `WorkflowConfig` + mutable `WorkflowState`)
- Pipelines are **assembled dynamically per command** and executed by `PipelineExecutor`
- Fully async & cancellation-aware; **non-fatal errors just log a warning and keep going** — no half-baked bailouts 💪

---

## ⚙️ Prerequisites (Just Two Steps)

### 1️⃣ Install the .NET 10 SDK
👉 https://dotnet.microsoft.com/download

### 2️⃣ Install FFmpeg
👉 https://ffmpeg.org/download.html

Windows users: add the FFmpeg `bin` directory to your `PATH` (or drop `ffmpeg.exe` / `ffprobe.exe` into the project's `tools/ffmpeg` folder). 🔧

> 🎁 Everything else (whisper.cpp, CrispASR, Demucs-rs, models…) is **auto-downloaded on first use** — zero manual installs. On the first run, go grab a coffee ☕

---

## 🔨 Build the Project

```bash
dotnet build -c Release
```

Output lands in:

```text
Centurion.Cli/bin/Release/net10.0/
```

Run it directly:

```bash
./Centurion.Cli/bin/Release/net10.0/Centurion.Cli
```

---

## 🎮 Command Family

| Command | What it does | Usage |
|---|---|---|
| `spawn` | 🎬 Standard transcription: media → ASS | `spawn <INPUT_FILE>` |
| `correct` | 🛠️ Calibrate existing subtitles | `correct <SUBTITLE_FILE>` |
| `from-script` | 📜 Script timing: media + script → ASS | `from-script <INPUT_FILE> <SCRIPT_FILE>` |
| `convert` | 🔄 Subtitle format conversion | `convert <INPUT_FILE>` |
| `update` | 🔄 Self-update from GitHub releases | `update [options]` |

---

## 1️⃣ `spawn` — Standard Transcription 🎬

The bread and butter: media file in, ASS subtitles out. Easy peasy.

```bash
Centurion spawn <INPUT_FILE> [options]
```

### Common Options

- `<INPUT_FILE>` — input audio/video file (required) 🎞️
- `-o, --output <OUTPUT_FILE>` — output ASS path (defaults to `<input>.ass`)
- `-l, --language <LANG>` — audio language, default `en`
- `--num-speakers <NUM>` — speaker count for diarization, `0` = auto-detect (default)
- `-k, --karaoke` — karaoke mode with `\K` tags 🎵
- `-t, --transcriber <ENGINE>` — engine: `crispasr` (default) / `whisper`
- `--tm, --transcriber-model <MODEL>` — model, e.g. `qwen3-asr-1.7b`, `base`, `large`
- `--tp, --transcriber-prompt <PROMPT>` — initial prompt 🧠
- `--vocal-separation` — separate vocals with Demucs first (great for BGM-heavy media) 🎤
- `--vocal-separation-model <MODEL>` — Demucs model, default `htdemucs`
- `--device <DEVICE>` — inference device: `auto` (default) / `cpu` / `cuda` / `vulkan` / `directml` 🖥️
- `--audio-noise-reduction` — conditional noise reduction
- `--audio-snr-threshold <DB>` — SNR threshold for noise reduction, default `15`
- `--disable-audio-resampling` / `--disable-audio-highpass` / `--disable-audio-loudness` — preprocess toggles
- `-s, --splitter <STRATEGY>` — `rule` (default) / `llm`
- `--splitter-*` — sentence-splitting knobs (length, granularity, spread…)
- `-a, --align` — forced alignment, **on by default** 📏
- `--am, --alignment-model <MODEL>` — aligner model, default `qwen3-forced-aligner-0.6b-f16`

### Examples 🧪

```bash
# The classic
Centurion spawn demo.mp4 --language en

# Chinese lecture, custom output
Centurion spawn lecture.wav -o lecture.ass --language zh

# Meeting + karaoke + speaker labels
Centurion spawn meeting.mp4 --karaoke --num-speakers 2
```

---

## 2️⃣ `correct` — Calibrate Existing Subtitles 🛠️

Subtitles that are close-but-not-quite? This command corrects an existing subtitle file against the source audio and/or a reference script.

```bash
Centurion correct <SUBTITLE_FILE> [options]
```

### Common Options

- `<SUBTITLE_FILE>` — input subtitle file (SRT/VTT/ASS…) 📄
- `-o, --output <OUTPUT_FILE>` — output ASS file
- `--audio <AUDIO_FILE>` — audio for **timeline correction** ⏱️
- `--script <SCRIPT_FILE>` — script for **text correction** ✏️
- `-s, --strategy <MODE>` — `both` (default) / `timeline-only` / `text-only`
- `--max-drift <MS>` — max drift for reporting, default `1500`
- `--fuzzy-threshold <T>` — minimum text similarity 0..1, default `0.72`
- `--vocal-separation` / `--vocal-separation-model <MODEL>` — Demucs before alignment 🎤
- `--device <DEVICE>` — inference device 🖥️
- `--disable-audio-*` — preprocess toggles
- `-k, --karaoke` — karaoke mode 🎵

### Example 🧪

```bash
Centurion correct subtitles.srt --audio podcast.mp3 --script transcript.txt
```

---

## 3️⃣ `from-script` — Script Timing 📜

Got a transcript/script and the matching media? This command aligns the script to the audio and produces subtitles — perfect for podcasts, interviews, and any "we already know what was said" scenario.

```bash
Centurion from-script <INPUT_FILE> <SCRIPT_FILE> [options]
```

### Common Options

- `<INPUT_FILE>` — input media file 🎞️
- `<SCRIPT_FILE>` — plain-text script file 📄
- `-o, --output <OUTPUT_FILE>` — output ASS file
- `-l, --language <LANG>` — audio language, default `en`
- `-t, --transcriber <ENGINE>` — transcription engine, default `whisper`
- `--tm, --transcriber-model <MODEL>` — model, default `base`
- `--vocal-separation` / `--vocal-separation-model <MODEL>` — Demucs vocal separation 🎤
- `--device <DEVICE>` — inference device 🖥️
- `-a, --align` — forced alignment (default on) 📏
- `--am, --alignment-model <MODEL>` — aligner model
- `--max-cps <CPS>` — max characters per second, default `5.0`
- `--max-chars-per-line <CHARS>` — max characters per line, default `18`
- `--coverage-threshold <RATIO>` — script-coverage warning threshold, default `0.92`
- `--fill-gap` — render missing script words as ellipsis

### Example 🧪

```bash
Centurion from-script podcast.mp3 transcript.txt -o podcast.ass --language en --max-chars-per-line 20
```

---

## 4️⃣ `convert` — Format Conversion 🔄

Convert any supported subtitle format to ASS. That's it, don't overthink it.

```bash
Centurion convert <INPUT_FILE> [options]
```

### Options

- `<INPUT_FILE>` — input subtitle file
- `-o, --output <OUTPUT_FILE>` — output ASS path (defaults to `<input>.ass`)

### Examples 🧪

```bash
Centurion convert subtitles.srt -o subtitles.ass
Centurion convert subtitles.vtt --output subtitles.ass
```

---

## 5️⃣ `update` — Self-Update 🔄

Never touch GitHub by hand again. `update` checks `Remembering-in-Moskowien/Centurion` releases, downloads the matching build, and swaps itself out.

```bash
Centurion update [options]
```

### Options

- `--check` — only check for a new version; download nothing
- `--apply` — download **and** apply right away (closes & restarts the program)
- `--asset <NAME>` — manually pick a release asset name (default: auto-match by platform, e.g. `Centurion-win-x64.zip`)

### How it works 🧠

1. Queries the GitHub **latest release** (semantic version compare against your local version)
2. Auto-matches the asset for your platform (`win-x64` / `linux-x64` / `osx-arm64`…)
3. Downloads it to a temp staging area and safely extracts it (zip-slip guarded 🛡️)
4. Generates an **apply script** that waits for the old process to exit, swaps the files, and restarts

```bash
# Just check
Centurion update --check

# Check + download + apply in one go (program restarts itself)
Centurion update --apply
```

---

## 👥 Speaker Diarization (Who's Talking?)

Every pipeline path can label **who said what** — each word gets a `Speaker` attribute. 🗣️

**Two backends**, both implemented natively through the CrispASR CLI (**no Python required** 🐍❌):

| Backend | `DiarizationBackend` | Method | Notes |
|---|---|---|---|
| CrispASR built-in | `crispasr` | `foxnose` (default), `energy`, `xcorr`, `vad-turns` | Zero extra deps, auto speaker count |
| Pyannote + TitaNet | `pyannote` | pyannote segmentation + TitaNet embeddings | Rock-solid on long audio; models auto-downloaded |

Configure via `WorkflowConfig`:

```csharp
DiarizationBackend = "crispasr",   // "none" to disable
DiarizationMethod  = "foxnose",    // crispasr methods
DiarizationModel   = "pyannote-seg-3.0",
NumSpeakers        = 0,            // 0 = auto
```

> 💡 Diarization runs **before sentence splitting** and is **non-fatal**: if it fails, subtitles still get generated (just without speaker labels). No drama. 😌

---

## 🎤 Vocal Separation (BGM, Step Aside!)

Background music drowning out the speech? Separate the vocals first, then transcribe — clean input, better subtitles. 🧼

- Powered by **demucs-rs** (native Rust, no Python); models auto-downloaded on first run
- 🌏 **Mirror-safe model downloads**: Demucs-rs itself only knows HuggingFace, so Centurion pre-downloads the model into its cache — and if the official source times out (we feel you, China networks 🇨🇳), it automatically falls back to the **hf-mirror.com** mirror. Zero manual steps, one working vocal track. ✨
- **Off by default** — it's slow (deep learning is patient work) and pointless for clean speech
- Turn it on only for music / MV / BGM-heavy media:

```bash
Centurion spawn song.mp4 --vocal-separation
Centurion spawn song.mp4 --vocal-separation --vocal-separation-model htdemucs_ft
```

---

## 🖥️ GPU Detection & Auto-Download

Centurion **detects your hardware** at startup and downloads the right tool builds automatically. No more "please manually install the CUDA version" emails. 📬➡️🗑️

- 🔍 Detects NVIDIA (via `nvidia-smi` / `CUDA_PATH`), other GPUs (AMD/Intel → Vulkan), system RAM, and platform
- 📦 Tools can declare **per-device variants** (e.g. whisper.cpp CPU vs CUDA builds); the matching one is auto-selected & downloaded
- 🎯 Override detection anytime with `--device`:

```bash
Centurion spawn audio.mp3 --device cuda     # force CUDA builds
Centurion spawn audio.mp3 --device cpu      # stay cozy on CPU
```

Startup banner example:

```
🖥️ Device: Platform: win-x64; GPU: Intel(R) Arc(TM) 130T GPU (16GB) (non-NVIDIA); RAM: 15.7 GB; Recommended: Vulkan
```

---

## 🗂️ Metadata Registry

Tools & models live in an **external JSON registry**, loaded at startup — edit it without touching code. 🎛️

- **Default location**: `config/metadata.json` next to the executable (seeded automatically on first run)
- **Override**: `CENTURION_METADATA_PATH` env var, or pass a path to `AddCenturionCore(metadataPath)`
- Tools support `variants` for per-device builds:

```jsonc
{
  "tools": {
    "whispercpp": {
      "downloadUrl": ".../whisper-bin-x64.zip",
      "variants": {
        "cuda": { "downloadUrl": ".../whisper-cublas-12.4.0-bin-x64.zip" }
      }
    }
  }
}
```

---

## 📋 Typical Workflows (Copy-Paste Ready)

```bash
# 🎬 Just make the subtitles
Centurion spawn video.mp4 --language en --karaoke

# 📜 Script → timed subtitles
Centurion from-script podcast.wav transcript.txt --language en

# 🛠️ Fix the timing of an existing subtitle file
Centurion correct subs.srt --audio episode.mp4 --strategy timeline-only

# 🎤 Music video with vocal separation + speakers
Centurion spawn concert.mp4 --vocal-separation --num-speakers 2

# 🖥️ Big GPU? Take off
Centurion spawn long_lecture.wav --device cuda

# 🔄 Keep yourself fresh
Centurion update --check
```

---

## ⚠️ Notes & Limitations

- 🚧 Active development: flags may change between versions — `Centurion <command> --help` is your lifesaver 🤝
- 🐢 First runs download tools/models (whisper.cpp, CrispASR, Demucs, GGUF files…) — coffee recommended ☕
- 🎵 Diarization & vocal-separation models live on HuggingFace — Centurion auto-falls back to the hf-mirror.com mirror when the official source is unreachable, so the first run just works 🌏
- ⏱️ Long audio + vocal separation on CPU = patience required (deep learning is worth it, we promise)

---

## 🤝 Contributing & Feedback

Issues, PRs, and spicy feedback are all welcome! 🔥

Before contributing, get familiar with the pipeline/operator structure, and keep CLI options & output format backward-compatible where possible.

---

## 📜 License

MIT License — see [LICENSE](LICENSE) for details.
