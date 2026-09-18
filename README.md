# 🛡️ Centurion

**Speech → Subtitles, done properly.** Centurion is a .NET 10 CLI that turns audio/video into polished ASS subtitles — transcription, sentence splitting, script alignment, speaker diarization, vocal separation, and export, all in one pipeline. 🎬✨

It's built as a **operator-pipeline architecture**: every stage (transcribe → diarize → split → clean → align) is an independent module that you can swap, extend, or reorder. No monoliths, no tears. 🧩

---

## 🚦 Project Status

> ⚠️ Still in early development (pre-release). Commands and flags may shift as we go — check `--help` if something surprises you.

- 🎯 Primary output: **ASS subtitle files**
- 🧠 Inference: **CPU by default, GPU auto-detected & auto-downloaded** when available (see [🖥️ GPU section](#-gpu-detection--auto-download))
- 🔧 Dependencies: **FFmpeg**, **.NET 10 SDK** — everything else is downloaded on demand (see [🗂️ Metadata Registry](#-metadata-registry))

---

## ✨ Feature Overview

- 🎬 Generate ASS subtitles from audio/video files (mp3, mp4, mkv, flac, ...)
- 🗣️ Multiple transcription engines: **Whisper.cpp** and **CrispASR** (Qwen3 / Whisper backends)
- 👥 **Speaker diarization** on every pipeline path — two backends: CrispASR built-in & Pyannote + TitaNet
- 🎤 **Vocal separation** with Demucs-rs (opt-in, great for music/BGM-heavy media)
- 🖥️ **GPU detection** (NVIDIA CUDA / Vulkan / DirectML) with automatic download of GPU tool builds
- ✂️ Rule-based / NLP / LLM sentence splitting
- 📏 Forced alignment (word-level timestamps) + text cleaning
- 🎵 Karaoke mode with `\K` tags
- 📜 Script-based subtitle generation (台本打轴)
- 🛠️ Subtitle calibration (correct existing subs against audio/script)
- 🔄 Subtitle format conversion (SRT/VTT/... → ASS)

---

## 🧠 Architecture

```
 input ──► FFmpegConvert ──► AudioPreprocess ──► [VocalSeparation] ──► Transcribe
              ──► Diarization ──► SentenceSplit ──► TextCleaning ──► Alignment ──► ASS 📦
```

- Each step is a `PipelineOperator` operating on a shared `SubtitleWorkflowContext` (immutable `WorkflowConfig` + mutable `WorkflowState`)
- Pipelines are **assembled dynamically per command** and executed by `PipelineExecutor`
- Everything is **async** and **cancellation-aware**; non-fatal failures log a warning and keep going 💪

---

## ⚙️ Requirements

### 1️⃣ Install .NET 10 SDK

👉 https://dotnet.microsoft.com/download

### 2️⃣ Install FFmpeg

Centurion uses FFmpeg for audio/video processing.

👉 https://ffmpeg.org/download.html

On Windows, add the FFmpeg `bin` directory to your `PATH` (or drop `ffmpeg.exe` / `ffprobe.exe` into the project's `tools/ffmpeg` folder). 🔧

> 🎁 Everything else (whisper.cpp, CrispASR, Demucs-rs, models) is downloaded **automatically on first use** — no manual installs.

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

## 🎮 Command List

| Command | What it does | Signature |
|---|---|---|
| `spawn` | 🎬 Standard transcription: media → ASS | `spawn <INPUT_FILE>` |
| `correct` | 📜 Script timing (打轴): media + script → ASS | `correct <INPUT_FILE> <SCRIPT_FILE>` |
| `from-script` | 🛠️ Calibrate existing subtitles against audio/script | `from-script <SUBTITLE_FILE>` |
| `convert` | 🔄 Subtitle format conversion | `convert <INPUT_FILE>` |

---

## 1️⃣ `spawn` — Standard Transcription 🎬

The bread-and-butter command: media file in, ASS subtitles out.

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
- `--vocal-separation` — separate vocals with Demucs first (for music/BGM-heavy media) 🎤
- `--vocal-separation-model <MODEL>` — Demucs model, default `htdemucs`
- `--device <DEVICE>` — `auto` (default) / `cpu` / `cuda` / `vulkan` / `directml` 🖥️
- `--audio-noise-reduction` — conditional FFmpeg noise reduction
- `--audio-snr-threshold <DB>` — SNR threshold for noise reduction, default `15`
- `--disable-audio-resampling` / `--disable-audio-highpass` / `--disable-audio-loudness` — preprocess toggles
- `-s, --splitter <STRATEGY>` — `rule` (default) / `llm`
- `--splitter-*` — sentence splitting knobs (length, granularity, spread...)
- `-a, --align` — forced alignment, **enabled by default** 📏
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

## 2️⃣ `correct` — Script Timing (打轴) 📜

Got a transcript/script and the matching media? This command aligns the script to the audio and produces subtitles — perfect for podcasts, interviews, and any "we already know what was said" scenario.

```bash
Centurion correct <INPUT_FILE> <SCRIPT_FILE> [options]
```

### Common Options

- `<INPUT_FILE>` — input media file 🎞️
- `<SCRIPT_FILE>` — plain-text script file 📄
- `-o, --output <OUTPUT_FILE>` — output ASS file
- `-l, --language <LANG>` — audio language, default `en`
- `-t, --transcriber <ENGINE>` — transcription engine, default `whisper`
- `--tm, --transcriber-model <MODEL>` — transcription model, default `base`
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
Centurion correct podcast.mp3 transcript.txt -o podcast.ass --language en --max-chars-per-line 20
```

---

## 3️⃣ `from-script` — Calibrate Existing Subtitles 🛠️

Have subtitles that are close-but-not-quite? This command corrects an existing subtitle file against the source audio and/or a reference script.

```bash
Centurion from-script <SUBTITLE_FILE> [options]
```

### Common Options

- `<SUBTITLE_FILE>` — input subtitle file (SRT/VTT/ASS...) 📄
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
Centurion from-script subtitles.srt --audio podcast.mp3 --script transcript.txt
```

---

## 4️⃣ `convert` — Format Conversion 🔄

Convert any supported subtitle format to ASS. Simple as that.

```bash
Centurion convert <INPUT_FILE> [options]
```

### Options

- `<INPUT_FILE>` — input subtitle file
- `-o, --output <OUTPUT_FILE>` — output ASS path (defaults to `<input>.ass`)

### Example 🧪

```bash
Centurion convert subtitles.srt -o subtitles.ass
Centurion convert subtitles.vtt --output subtitles.ass
```

---

## 👥 Speaker Diarization (说话人分割)

Every pipeline path can label **who said what** — each word gets a `Speaker` attribute. 🗣️

**Two backends**, both implemented natively through the CrispASR CLI (no Python required 🐍❌):

| Backend | `DiarizationBackend` | Method | Notes |
|---|---|---|---|
| CrispASR built-in | `crispasr` | `foxnose` (default), `energy`, `xcorr`, `vad-turns` | Zero extra deps, auto speaker count |
| Pyannote + TitaNet | `pyannote` | pyannote segmentation + TitaNet embeddings | Best long-audio stability; models auto-downloaded |

Configure via `WorkflowConfig`:

```csharp
DiarizationBackend = "crispasr",   // "none" to disable
DiarizationMethod  = "foxnose",    // crispasr methods
DiarizationModel   = "pyannote-seg-3.0",
NumSpeakers        = 0,            // 0 = auto
```

> 💡 Diarization runs **before sentence splitting** and is **non-fatal**: if it fails, subtitles still get generated (just without speaker labels). No drama. 😌

---

## 🎤 Vocal Separation (Demucs)

BGM drowning out the speech? Separate the vocals first, then transcribe — clean input, better subtitles. 🧼

- Powered by **demucs-rs** (native Rust, no Python), models auto-downloaded on first run
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

## 📋 Typical Workflows

```bash
# 🎬 Just make the subtitles
Centurion spawn video.mp4 --language en --karaoke

# 📜 Script → timed subtitles
Centurion correct podcast.wav transcript.txt --language en

# 🛠️ Fix the timing of an existing subtitle file
Centurion from-script subs.srt --audio episode.mp4 --strategy timeline-only

# 🎤 Music video with vocal separation + speakers
Centurion spawn concert.mp4 --vocal-separation --num-speakers 2

# 🖥️ Big GPU? Go fast
Centurion spawn long_lecture.wav --device cuda
```

---

## ⚠️ Notes & Limitations

- 🚧 Active development: flags may change between versions — `Centurion <command> --help` is your friend 🤝
- 🐢 First runs download tools/models (whisper.cpp, CrispASR, Demucs, GGUF files...) — grab a coffee ☕
- 🎵 Vocal separation & diarization models live on HuggingFace — slower downloads in some regions
- ⏱️ Long audio + vocal separation on CPU = patience required (deep learning is worth it, we promise)

---

## 🤝 Contributing & Feedback

Issues, PRs, and spicy feedback are all welcome! 🔥

Before contributing, get familiar with the pipeline/operator structure and keep CLI options & output format backward-compatible where possible.

---

## 📜 License

MIT License — see [LICENSE](LICENSE) for details.
