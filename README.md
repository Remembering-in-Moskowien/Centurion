# 🛡️⚡ Centurion — Speech In, Subtitles Out, One Pipeline

> **Speech → Subtitles, done properly.** 🎬
> Centurion is a **.NET 10** command-line powerhouse that turns audio/video into **polished ASS subtitles**: transcribe → diarize → split → clean → force-align, all in one automatic pipeline. Sit back, relax, let it cook. ✨

![Pipeline](https://img.shields.io/badge/architecture-operator%2Dpipeline-8A2BE2) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-2ea44f) ![Tests](https://img.shields.io/badge/tests-150%20passing-2ea44f) ![Status](https://img.shields.io/badge/status-early%20dev%20%F0%9F%9A%A7-yellow)

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
| 🛠️ Subtitle calibration | Existing subs slightly off? `correct` straightens them out — no subtitle file? It extracts the subtitle track from the media (mkvtoolnix) | ✅ |
| ✍️ Spell check | `correct --spellcheck` hunts typos with **Hunspell** (en_US auto-downloaded) and writes a `.spellcheck.json` report | ✅ Opt-in |
| 🔄 Format conversion | SRT/VTT/… → ASS, no fuss | ✅ |
| 🌐 Machine translation | Translate existing subtitles with **LLM (OpenAI / Ollama)**, glossary & target-script alignment | ✅ |
| 📝 Translated karaoke | Word-level `\K` timestamps for translations — time interpolation, long syllables get more time | ✅ |
| 🈶 Non-Latin script support | Chinese, Japanese, Korean, Cyrillic, Arabic & more — no more space-joined gibberish | ✅ |
| 🧾 Rich context JSON | Every run dumps a `.context.json` with config, results & diagnostics | ✅ |
| 🎞️ Subtitle track pre-check | Before spawn/correct/from-script, mkvtoolnix scans the media for existing subtitle tracks and warns you (report: `.tracks.json`) | ✅ Auto-installed |
| 🔄 Self-update | One command pulls the latest release from GitHub | ✅ |
| ⚡ GitHub 520 auto-mirror | All GitHub downloads (updates, Hunspell dictionaries, tool binaries) automatically try multiple 520-style mirrors, then fall back to direct | ✅ Auto |
| 🎙️ Media dubbing (Qwen3-TTS) | `dub` re-voices translated subtitles into WAV audio — voice cloning from speaker profiles, time-aligned, mixed in | ✅ Phase 1 MVP |
| 📊 Quality reports | Every path writes a `<output>.quality.json` — coverage, alignment drift, CPS, speaker stats & warnings | ✅ Every command |

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

## 🌍 Localization (JSON, on by default in English)

- All user-facing console/log messages go through `ConsoleServices.T()`; the **key is the English default text**
- Translations live in `Localization/{culture}.json` next to the executable — drop a file in, no recompile needed
- Built-in `zh-CN.json` ships with every release (Simplified Chinese 🇨🇳)

```bash
# run in Chinese
Centurion.Cli.exe --lang zh-CN spawn video.mp4 -o out.ass
# omit --lang → English (default)

# GitHub downloads (update / dictionaries / tools) auto-try 520 mirrors then direct
Centurion.Cli.exe --github-proxy https://my-mirror.example/ update      # use a custom mirror
Centurion.Cli.exe --no-github-proxy update                              # disable mirrors entirely
```

- Missing key / missing language file → falls back to English gracefully ✅
- Powered by the official `Microsoft.Extensions.Localization` NuGet package + a custom JSON resource provider

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
| `translate` | 🌐 Translate existing subtitles (LLM, glossary, target-script) | `translate <SUBTITLE_FILE> -t <LANG>` |
| `dub` | 🎙️ Media dubbing: bilingual subtitles → translated WAV audio (Qwen3-TTS) | `dub <SUBTITLE_FILE> -t <LANG>` |
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
- `-s, --splitter <STRATEGY>` — `rule` / `rule-aggressive` (default, fast-paced dialogue) / `rule-passive` (monologue, uniform speech) / `llm`
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
- `-l, --language <LANG>` — audio language, default `en` (pass `zh` / `ja` for CJK media)
- `--audio-noise-reduction` / `--audio-snr-threshold <DB>` — conditional noise reduction
- `--disable-audio-resampling` / `--disable-audio-highpass` / `--disable-audio-loudness` — preprocess toggles
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
- `--audio-noise-reduction` / `--audio-snr-threshold <DB>` — conditional noise reduction
- `--disable-audio-resampling` / `--disable-audio-highpass` / `--disable-audio-loudness` — preprocess toggles
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

## 5️⃣ `dub` — Media Dubbing with Qwen3-TTS 🎙️

Take a bilingual (or already-translated) subtitle file and turn it into **dubbed audio** — the original soundtrack's voice acting, re-spoken in your target language. Fully local, no cloud, no Python. 🐍❌

```bash
Centurion dub <SUBTITLE_FILE> -t <LANG> [options]
```

### The pipeline 🧠

```
bilingual subs ──► BilingualSubtitleParser (match source↔translation)
              ──► SpeakerProfiling (SNR-scored selection of each speaker's cleanest sentence)
              ──► TTS Synthesis (llama-tts / Qwen3-TTS 1.7B, parallel by speaker, long lines chunked)
              ──► TimeAlignment (ffprobe actual length → atempo; overlap detection & compression)
              ──► AudioMix (timeline placement + optional background ducking + loudnorm)
              ──► Quality Report 📊 (translation coverage, alignment deviation, tempo stats)
```

### Options

- `<SUBTITLE_FILE>` — input subtitle file; either **bilingual** (two tracks: source + translation) or a single already-translated track 📄
- `-t, --target-language <LANG>` — TTS voice language: `en`, `zh`, `ja`, `ko`, `de`, `fr`, `es`, `it`, `pt`, `ru` (ISO 639-1)
- `-o, --output <OUTPUT_FILE>` — output WAV path (defaults to `<input>.dub.wav`)
- `--speaker-reference <DIR>` — directory of reference clips (`SPEAKER.wav` per speaker) for **voice cloning**; omit to auto-profile from the media 🎤
- `--background <FILE>` — background / accompaniment audio; enables **sidechain ducking** (the TTS voice ducks the music) 🎼
- `--no-ducking` — disable ducking even when `--background` is given
- `--loudness-target <LUFS>` — output loudness target, default `-16` (loudnorm) 🔊
- `--tts-parallelism <N>` — TTS synthesis parallelism, default `2` (speaker-bucketed, global throttle)
- `--max-chunk-seconds <S>` — long-sentence chunking threshold, default `15` (lines exceeding it are split proportionally and re-joined on the timeline)
- `--tts-engine <ENGINE>` — TTS engine, `llama` (default)
- `--tts-model <MODEL>` — TTS model, `1.7b-base-q4` (default; ~1.4 GB, auto-downloaded)
- `--strict-timing` — clamp out-of-range tempo to 0.5×–2.0× (default on; off keeps natural length)

### How it works 🧠

- **Bilingual matching**: the parser pairs source and translation lines by a center-time window (1500 ms); without a translation track, the whole file is treated as already-translated
- **SNR-scored voice references**: with no manual clips, each speaker's candidate sentences (2–8 s) are scored by **ffmpeg astats** — speech RMS vs. media noise floor gives an SNR estimate; the cleanest, best-timed line is cropped as the TTS voice reference 🎚️
- **Parallel synthesis**: sentences are bucketed by speaker (same speaker stays ordered), buckets synthesize in parallel under a global throttle — no more waiting for one slow line
- **Long-line chunking**: lines whose target window exceeds the threshold are split proportionally by character and re-joined seamlessly on the timeline
- **Time alignment**: every synthesized segment is measured (ffprobe) and sped up/slowed down (atempo, 0.5×–2.0×) to fit its subtitle window; out-of-range segments are clamped to the boundary and warned
- **Overlap handling**: overlapping subtitle windows are detected and the later segment is compressed forward (`MixOffsetMs`) instead of stacking noisily
- **Ducking & loudness**: with `--background`, the accompaniment is sidechain-compressed by the TTS voice (`asplit`-based graph for ffmpeg 7 compat), then everything is loudnorm'd to your target LUFS
- **Auto-everything**: llama-tts + the GGUF models are downloaded on first use (GitHub mirrors → direct, hf-mirror fallback 🌏)

### Examples 🧪

```bash
# Dub an anime with bilingual subs (eng voice track, chi translation) into Chinese
Centurion dub episode.ass -t zh

# Dub with per-character voice references
Centurion dub movie.srt -t en --speaker-reference voices/

# Dub over the original soundtrack with ducking, louder output
Centurion dub subs.ass -t ja --background ost.wav --loudness-target -14

# Strict timing + aggressive parallelism for fast dialogue
Centurion dub subs.ass -t en --strict-timing --tts-parallelism 4 --max-chunk-seconds 10
```

> 🧾 Outputs: `<output>.wav` (the dub) + `<output>.quality.json` (per-segment report) + `<output>.context.json` (full run context).

---

## 📊 Quality Reports (Every Command)

Every pipeline path — `spawn`, `from-script`, `correct`, `convert`, `translate`, `dub` — finishes by writing a **`<output>.quality.json`** next to its output. No flags, no opt-in. 📈

```json
{
  "meta": { "command": "spawn", "input": "demo.mp4", "output": "demo.ass", "generatedAt": "…" },
  "counts": { "sentences": 42, "words": 318, "characters": 2205, "speakers": 2, "durationSeconds": 124.6 },
  "coverage": { "charactersPerSecond": 17.7, "coveredRatio": 0.93 },
  "alignment": { "meanDriftMs": 180, "maxDriftMs": 940 },
  "warnings": [ "…" ],
  "errors": []
}
```

- **Counts**: sentences / words / characters / distinct speakers / total duration
- **Coverage**: characters-per-second vs. your reading-speed target, transcription coverage ratio
- **Alignment**: mean & max drift against the reference timeline (drift > threshold shows up as a warning)
- **Dub** (dub runs only): segment totals & skips, **translation coverage**, mean/max **alignment deviation (ms)**, applied tempo stats, and a clone-consistency heuristic
- **Warnings & errors**: every non-fatal issue the pipeline shrugged off, visible at a glance

It's your canary in the coal mine — script too dense? CPS way up. Alignment slipping? Drift way up. No more guessing why a subtitle looks off. 🐤

---

## 6️⃣ `update` — Self-Update 🔄

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

## 7️⃣ `translate` — Translation, Without the Timeline Drama 🌐

Got a subtitle file in a language you don't want? `translate` rewrites the **text only** — the timeline, the word details, everything spatial stays untouched. It's text alignment, not a remix. 🎯

```bash
Centurion translate <SUBTITLE_FILE> -t <LANG> [options]
```

### Options

- `<SUBTITLE_FILE>` — input subtitle file (SRT/VTT/ASS…) 📄
- `-t, --target-language <LANG>` — target language code, e.g. `zh`, `en`, `ja` (required)
- `-o, --output <OUTPUT_FILE>` — output ASS path (defaults to `<input>.translated.ass`)
- `--source-language <LANG>` — source language (auto-detected when omitted)
- `-s, --strategy <STRATEGY>` — translation strategy, `llm` (default)
- `--model <MODEL>` — LLM model (OpenAI default `gpt-4o-mini`, Ollama default `llama3.1`)
- `--api-key <KEY>` — OpenAI API key; **omit it to fall back to local Ollama**
- `--glossary <FILE>` — glossary JSON: `{ "source": "target" }` or `[{ "source":…, "target":… }]` 📚
- `--target-script <FILE>` — target-language script (one line per subtitle) 📝
- `-b, --bilingual` — output bilingual subtitles (source on top, translation below) 🈳
- `-k, --karaoke` — word-level `\K` timestamps for translations 🎵

### The magic of target-script alignment ✨

Hand it an **official target-language script** (subtitles count matching) and `translate` performs **pure 1:1 text alignment** — line N of the script becomes line N of the subtitles, no LLM involved. Official wording, guaranteed. If the counts don't match, it warns you and translates with the script as a wording reference instead.

### Translated karaoke timestamps ⏱️

No word-level timing exists for a translation — so Centurion **builds one**:
- **Time interpolation**: the sentence's `[start, end]` is redistributed across the translated words
- **Long-syllable bias**: longer words (more syllables / more characters) get proportionally more time — no more "a" and "extraordinary" fighting over the same millisecond 😄
- A lead-in pause (`\K`) opens each line, matching the classic karaoke rhythm

### Examples 🧪

```bash
# OpenAI backend
Centurion translate subtitles.srt -t zh --api-key sk-xxx --model gpt-4o-mini

# Local Ollama (no key needed)
Centurion translate subtitles.srt -t ja

# Glossary + official script + bilingual + karaoke
Centurion translate subs.srt -t zh --glossary terms.json --target-script official.txt -b -k
```

> 💡 **Bilingual layout**: source line rides on top (`Default` style), translation hugs the bottom (`Sub` style) — a layout borrowed from classic dual-language fansubs.

> 🧾 Every run also drops a `<output>.context.json` next to the subtitles — full config, per-sentence translations, glossary & script load state, and any warnings. Perfect for debugging "why did *that* line come out like that".

---

## 🎨 Subtitle Styles (Batteries Included)

The default ASS template ships with **two ready-made styles**, tuned for dual-language subtitles:

| Style | Role | Font | Size | Position |
|---|---|---|---|---|
| `Default` | 🎬 Main line (source language) | **Arial** (bold) | 84 | Top-ish, `MarginV 100` |
| `Sub` | 🈳 Secondary line (translation) | **Microsoft YaHei** | 81 | Bottom, `MarginV 28` |

- Both share the classic look: white text, semi-transparent outline (3.3px) + shadow (2.5px), bottom-center aligned
- 🖥️ **Zero-install fonts**: every font is a system default — Arial ships with Windows/macOS (Linux auto-substitutes the metric-compatible Liberation Sans); Microsoft YaHei ships with Windows Chinese (macOS falls back to PingFang SC, Linux to Noto Sans CJK SC — all modern sans-serif CJK, visually near-identical)

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

## 🈶 Non-Latin Language Support

Centurion no longer assumes your audio speaks English with spaces. 🎉

- **CJK & spacing-aware text**: Chinese & Japanese are joined without spaces (correct for 无空格语系), Korean keeps its word spaces — the old `string.Join(" ")` nightmare is dead 💀
- **Language-aware punctuation**: sentence splitting recognizes `。！？，；：、…` plus Devanagari `।॥` and Arabic `؟`
- **Per-language transcription**: pass `-l zh` / `-l ja` / `-l ko` — Whisper & CrispASR obey; Whisper auto-detects when no language is given
- **Cross-lingual alignment**: the default aligner (`qwen3-forced-aligner-0.6b`) works for Chinese, Japanese, English & more
- **LLM splitting**: Chinese/Japanese use a CJK prompt branch; Korean rides the English branch (its punctuation matches anyway)
- **Model-agnostic stages**: diarization & vocal separation don't care about language at all

```bash
Centurion spawn 讲座.wav -l zh --transcriber qwen3-asr-1.7b
Centurion spawn anime.mkv -l ja
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

# 🎙️ Dub a translated subtitle track into speech
Centurion dub subtitles.ass -t zh --speaker-reference voices/
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
