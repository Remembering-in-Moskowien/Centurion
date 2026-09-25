---
title: Commands
---

# 🎮 Commands

## Command Family

| Command | What it does | Usage |
|---|---|---|
| `convert` | 🔄 **Entry point**: any subtitle file (SRT/VTT/ASS…) → intermediate file | `convert <INPUT_FILE>` |
| `spawn` | 🎬 Standard transcription: media → intermediate file | `spawn <INPUT_FILE>` |
| `from-script` | 📜 Script timing: media + script → intermediate file | `from-script <INPUT_FILE> <SCRIPT_FILE>` |
| `correct` | 🛠️ Calibrate an intermediate file's timeline & text | `correct <CENTURION_FILE>` |
| `translate` | 🌐 Translate an intermediate file (LLM, glossary, target-script) | `translate <CENTURION_FILE> -t <LANG>` |
| `dub` | 🎙️ Media dubbing: intermediate file → translated WAV audio (Qwen3-TTS) | `dub <CENTURION_FILE> -t <LANG>` |
| `build` | 🏗️ **Exit point**: intermediate file → polished ASS subtitles | `build <CENTURION_FILE>` |
| `update` | 🔄 Self-update from GitHub releases | `update [options]` |
| `transcribe` … `quality` | 🔬 Operator micro-commands (see [Advanced](advanced.md)) | `transcribe <INPUT_FILE>` … |

---

## 1️⃣ `convert` — The Entry Point 🔄

Turn **any** supported subtitle file (SRT, VTT, ASS…) into a Centurion intermediate file. This is the universal gateway: everything else in the command family reads `.centurion.json`.

```bash
Centurion convert <INPUT_FILE> [options]
```

**Options**

- `<INPUT_FILE>` — input subtitle file (SRT/VTT/ASS…)
- `-o, --output <OUTPUT_FILE>` — output intermediate path (defaults to `<input>.centurion.json`)

**Examples** 🧪

```bash
Centurion convert subtitles.srt
Centurion convert episode.ass -o episode.centurion.json
```

---

## 2️⃣ `spawn` — Transcription & OCR 🎬🔤

The bread and butter: media file in, intermediate file out (then `build` renders the ASS).

```bash
Centurion spawn <INPUT_FILE> [options]
```

`spawn` has **two modes**:

| Mode | Flag | What it does |
|---|---|---|
| `asr` (default) | `-m asr` | Speech recognition — audio conversion → preprocessing → vocal separation (optional) → transcription → diarization → splitting → cleaning → alignment 📣 |
| `ocr` | `-m ocr` | **GLM-OCR** — extracts subtitle text from video frames or images (burned-in subtitles / video-game dialogue / sign text). Skips audio steps entirely 🔤 |

OCR runs on **cloud or local** inference — pick with `--ocr-backend`:

| Backend | What it is | Key? |
|---|---|---|
| `zhipu` (default) | [Zhipu AI](https://open.bigmodel.cn) GLM-OCR in the cloud | `--ocr-api-key` required |
| `ollama` | **Local** Ollama vision model (`qwen2.5vl`, `llava`…) | none 🏠 |
| `llamacpp` | **Local** llama-server with a vision GGUF | none 🏠 |

It extracts frames at a fixed interval, OCRs each one, and merges consecutive identical lines into timed sentences — then the usual `split → clean → quality` stages take over. You can point it at a single image too. 🖼️

```bash
# Cloud GLM-OCR
Centurion spawn episode.mkv -m ocr --ocr-api-key <KEY> --language zh

# Local Ollama — no key, just a running `ollama serve`
Centurion spawn episode.mkv -m ocr --ocr-backend ollama --ocr-model qwen2.5vl:7b

# Local llama-server — bring your own vision GGUF
Centurion spawn movie.mp4 -m ocr --ocr-backend llamacpp --ocr-base-url http://127.0.0.1:8080/v1

# A screenshot / subtitle image
Centurion spawn frame.png -m ocr --ocr-backend ollama

# Tune the frame interval or swap the model
Centurion spawn movie.mp4 -m ocr --ocr-api-key <KEY> --ocr-interval 1.5 --ocr-model glm-4v-plus
```

**Common Options**

- `<INPUT_FILE>` — input audio/video file (required) 🎞️
- `-o, --output <OUTPUT_FILE>` — output intermediate path (defaults to `<input>.centurion.json`)
- `-l, --language <LANG>` — audio language, default `en`
- `--num-speakers <NUM>` — speaker count for diarization, `0` = auto-detect (default)
- `-k, --karaoke` — karaoke mode with `\K` tags 🎵
- `-t, --transcriber <ENGINE>` — engine: `crispasr` (default) / `whisper` / **`openai` / `groq` / `dashscope` / `deepgram`** (cloud ASR ☁️)
- `--tm, --transcriber-model <MODEL>` — model, e.g. `qwen3-asr-1.7b`, `base`, `large` (cloud defaults: `whisper-1`, `whisper-large-v3`, `paraformer-realtime-v2`, `nova-2`)
- `--tp, --transcriber-prompt <PROMPT>` — initial prompt 🧠
- `--asr-provider <PROVIDER>` — cloud ASR provider name (`openai` / `groq` / `dashscope` / `deepgram`), default `crispasr`
- `--asr-api-key <KEY>` — cloud ASR API key (**required** for cloud engines)
- `--asr-base-url <URL>` — custom cloud ASR endpoint
- `--vocal-separation` — separate vocals with Demucs first (great for BGM-heavy media) 🎤
- `--vocal-separation-model <MODEL>` — Demucs model, default `htdemucs`
- `--device <DEVICE>` — inference device: `auto` (default) / `cpu` / `cuda` / `vulkan` / `directml` 🖥️
- `--audio-noise-reduction` — conditional noise reduction
- `--audio-snr-threshold <DB>` — SNR threshold for noise reduction, default `15`
- `--disable-audio-resampling` / `--disable-audio-highpass` / `--disable-audio-loudness` — preprocess toggles
- `-s, --splitter <STRATEGY>` — `rule` / `rule-aggressive` (default, fast-paced dialogue) / `rule-passive` (monologue, uniform speech) / `llm`
- `--splitter-*` — sentence-splitting knobs (length, granularity, spread…)
- `-a, --align` — forced alignment, **on by default** 📏 (ASR mode only)
- `--am, --alignment-model <MODEL>` — aligner model, default `qwen3-forced-aligner-0.6b-f16`
- `-m, --mode <MODE>` — `asr` (default, speech recognition) or `ocr` (GLM-OCR from frames/images) 🔤
- `--ocr-interval <SECONDS>` — frame interval for OCR mode, default `2`
- `--ocr-backend <BACKEND>` — `zhipu` (default, cloud) / `ollama` (local) / `llamacpp` (local)
- `--ocr-model <MODEL>` — OCR model (per backend: `glm-ocr` / `qwen2.5vl:7b` / server-loaded)
- `--ocr-api-key <KEY>` — GLM-OCR API key — required for `-m ocr` **only with zhipu backend**
- `--ocr-base-url <URL>` — custom endpoint (per backend default)

**Examples** 🧪

```bash
# The classic: transcribe → build ASS in two steps
Centurion spawn demo.mp4 --language en
Centurion build demo.centurion.json

# Cloud ASR: OpenAI Whisper
Centurion spawn demo.mp4 -t openai --asr-api-key <KEY> --language en

# Cloud ASR: Groq (fast & cheap)
Centurion spawn demo.mp4 -t groq --asr-api-key <KEY> --language en

# Cloud ASR: Alibaba DashScope (great for Chinese)
Centurion spawn lecture.wav -t dashscope --asr-api-key <KEY> --language zh

# Chinese lecture, custom output
Centurion spawn lecture.wav -o lecture.centurion.json --language zh
Centurion build lecture.centurion.json -o lecture.ass

# Meeting + karaoke + speaker labels
Centurion spawn meeting.mp4 --karaoke --num-speakers 2
Centurion build meeting.centurion.json
```

---

## 3️⃣ `correct` — Calibrate an Intermediate File 🛠️

Subtitles that are close-but-not-quite? `convert` the subtitle first, then correct the intermediate file against the source audio and/or a reference script. Output stays an intermediate file — chain it into `translate`, `dub`, or straight to `build`.

```bash
Centurion correct <CENTURION_FILE> [options]
```

**Common Options**

- `<CENTURION_FILE>` — input Centurion intermediate file (.centurion.json) 📄
- `-o, --output <OUTPUT_FILE>` — output intermediate path (defaults to `<input>.corrected.centurion.json`)
- `--audio <AUDIO_FILE>` — audio for **timeline correction** ⏱️
- `--script <SCRIPT_FILE>` — script for **text correction** ✏️
- `-s, --strategy <MODE>` — `both` (default) / `timeline-only` / `text-only`
- `--max-drift <MS>` — max drift for reporting, default `1500`
- `--fuzzy-threshold <T>` — minimum text similarity 0..1, default `0.72`
- `--vocal-separation` / `--vocal-separation-model <MODEL>` — Demucs before alignment 🎤
- `--device <DEVICE>` — inference device 🖥️
- `-l, --language <LANG>` — audio language, default `en` (pass `zh` / `ja` for CJK media)
- `--audio-noise-reduction` / `--audio-snr-threshold <DB>` — conditional noise reduction
- `--disable-audio-resampling` / `--disable-audio-highpass` / `--disable-audio-loudness` — preprocess toggles
- `-k, --karaoke` — karaoke mode 🎵

**Example** 🧪

```bash
# Convert first, then correct against audio + script
Centurion convert subtitles.srt
Centurion correct subtitles.centurion.json --audio podcast.mp3 --script transcript.txt

# Timeline-only pass
Centurion correct subtitles.centurion.json --audio podcast.mp3 -s timeline-only

# Render the corrected result
Centurion build subtitles.corrected.centurion.json
```

---

## 4️⃣ `from-script` — Script Timing 📜

Got a transcript/script and the matching media? This command aligns the script to the audio and produces an intermediate file — perfect for podcasts, interviews, and any "we already know what was said" scenario.

```bash
Centurion from-script <INPUT_FILE> <SCRIPT_FILE> [options]
```

**Common Options**

- `<INPUT_FILE>` — input media file 🎞️
- `<SCRIPT_FILE>` — plain-text script file 📄
- `-o, --output <OUTPUT_FILE>` — output intermediate path (defaults to `<input>.centurion.json`)
- `-l, --language <LANG>` — audio language, default `en`
- `-t, --transcriber <ENGINE>` — transcription engine, default `whisper`
- `--tm, --transcriber-model <MODEL>` — model, default `base`
- `--vocal-separation` / `--vocal-separation-model <MODEL>` — Demucs vocal separation 🎤
- `--device <DEVICE>` — inference device 🖥️
- `--audio-noise-reduction` / `--audio-snr-threshold <DB>` — conditional noise reduction
- `--disable-audio-resampling` / `--disable-audio-highpass` / `--disable-audio-loudness` — preprocess toggles
- `-a, --align` — forced alignment (default on) 📏
- `--am, --alignment-model <MODEL>` — aligner model
- `--max-cps <CPS>` — max characters per second, default `5.0`
- `--max-chars-per-line <CHARS>` — max characters per line, default `18`
- `--coverage-threshold <RATIO>` — script-coverage warning threshold, default `0.92`
- `--fill-gap` — render missing script words as ellipsis

**Example** 🧪

```bash
Centurion from-script podcast.mp3 transcript.txt --language en --max-chars-per-line 20
Centurion build podcast.centurion.json
```

---

## 5️⃣ `build` — The Exit Point 🏗️

Render any intermediate file into a polished ASS subtitle file. This is the **only** command that produces subtitles — everything upstream speaks `.centurion.json`.

```bash
Centurion build <CENTURION_FILE> [options]
```

**Options**

- `<CENTURION_FILE>` — input Centurion intermediate file (required)
- `-o, --output <OUTPUT_FILE>` — output path (defaults to `<input name>.ass`, i.e. `movie.centurion.json → movie.ass`)
- `-f, --format <FORMAT>` — output format: `ass` (default) · `srt` · `txt`; omitted, it is inferred from the `-o` extension

**What it renders** 🎨

- **ASS**: full styling — per-sentence timing, speaker labels, bilingual layout (source on `Default`, translation on `Sub`), karaoke `\K` tags
- **SRT**: standard time-axis text — numbering, `HH:MM:SS,mmm -->` ranges, bilingual lines joined source-then-translation
- **TXT**: one line per sentence (bilingual = two lines) — perfect for reading, editing or feeding another tool

**Examples** 🧪

```bash
Centurion build movie.centurion.json
Centurion build movie.corrected.centurion.json -o movie_final.ass
```
