---
title: Commands
---

# 🎮 Commands

## Command Family

| Command | What it does | Usage |
|---|---|---|
| `convert` | 🔄 **Entry point**: any subtitle file (SRT/VTT/ASS…) → intermediate file | `convert <INPUT_FILE>` |
| `asr` | 🎬 Speech recognition: media → intermediate file | `asr <INPUT_FILE>` |
| `ocr` | 👁️ Subtitle text recognition from video or images | `ocr <INPUT_FILE>` |
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

## 2️⃣ `asr` — Speech Transcription 🎬

The bread and butter: media file in, intermediate file out (then `build` renders the ASS).

```bash
Centurion asr <INPUT_FILE> [options]
```

The ASR pipeline runs audio conversion → preprocessing → optional vocal separation → transcription → optional diarization → splitting → cleaning → optional forced alignment.

**Options**

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
- `-a, --align` — forced alignment, **on by default** 📏
- `--am, --alignment-model <MODEL>` — aligner model, default `qwen3-forced-aligner-0.6b-f16`

**Examples** 🧪

```bash
# The classic: transcribe → build ASS in two steps
Centurion asr demo.mp4 --language en
Centurion build demo.centurion.json

# Cloud ASR: OpenAI Whisper
Centurion asr demo.mp4 -t openai --asr-api-key <KEY> --language en

# Cloud ASR: Groq (fast & cheap)
Centurion asr demo.mp4 -t groq --asr-api-key <KEY> --language en

# Cloud ASR: Alibaba DashScope (great for Chinese)
Centurion asr lecture.wav -t dashscope --asr-api-key <KEY> --language zh

# Chinese lecture, custom output
Centurion asr lecture.wav -o lecture.centurion.json --language zh
Centurion build lecture.centurion.json -o lecture.ass

# Meeting + karaoke + speaker labels
Centurion asr meeting.mp4 --karaoke --num-speakers 2
Centurion build meeting.centurion.json
```

---

## OCR — Subtitle Text Recognition 👁️

Extract burned-in subtitles, game dialogue, or other visible text from video frames or a single image.

```bash
Centurion ocr <INPUT_FILE> [options]
```

OCR inference can run in the cloud or locally:

| Backend | What it is | Key? |
|---|---|---|
| `zhipu` (default) | [Zhipu AI](https://open.bigmodel.cn) GLM-OCR in the cloud | `--ocr-api-key` required |
| `ollama` | Local Ollama vision model (`qwen2.5vl`, `llava`...) | none |
| `llamacpp` | Local llama-server with a vision GGUF | none |

For video, `ocr` uses `VideoSubFinderCli` from `PATH` or downloads and caches it on supported x64 systems. If unavailable or no frames are found, it falls back to fixed-interval FFmpeg extraction. Images are passed directly.

```bash
# Cloud GLM-OCR
Centurion ocr episode.mkv --ocr-api-key <KEY> --language zh

# Local Ollama
Centurion ocr episode.mkv --ocr-backend ollama --ocr-model qwen2.5vl:7b

# Local llama-server
Centurion ocr movie.mp4 --ocr-backend llamacpp --ocr-base-url http://127.0.0.1:8080/v1

# A screenshot / subtitle image
Centurion ocr frame.png --ocr-backend ollama

# Override VideoSubFinder and tune fallback interval
Centurion ocr movie.mp4 --ocr-api-key <KEY> --ocr-interval 1.5 --ocr-videosubfinder-path "C:\\VideoSubFinder\\VideoSubFinderCli.exe"
```

**OCR Options**

- `<INPUT_FILE>` — supported video, audio, or image file
- `-o, --output <OUTPUT_FILE>` — output intermediate file
- `-l, --language <LANG>` — text language, default `en`
- `--ocr-interval <SECONDS>` — fallback frame interval, default `2`
- `--ocr-videosubfinder-path <PATH>` — VideoSubFinder CLI path override
- `--ocr-backend <BACKEND>` — `zhipu` (default) / `ollama` / `llamacpp`
- `--ocr-model <MODEL>` — OCR model name
- `--ocr-api-key <KEY>` — GLM-OCR API key, required for `zhipu`
- `--ocr-base-url <URL>` — custom OCR endpoint
- `-s, --splitter <STRATEGY>` and `--splitter-*` — configure text splitting
- `--llm-provider <PROVIDER>` / `--llm-base-url <URL>` — LLM splitter service

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
