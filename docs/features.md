---
title: Features
---

#  Features

## Subtitle Styles (Batteries Included)

The default ASS template ships with **two ready-made styles**, tuned for dual-language subtitles:

| Style | Role | Font | Size | Position |
|---|---|---|---|---|
| `Default` |  Main line (source language) | **Arial** (bold) | 84 | Top-ish, `MarginV 100` |
| `Sub` |  Secondary line (translation) | **Microsoft YaHei** | 81 | Bottom, `MarginV 28` |

- Both share the classic look: white text, semi-transparent outline (3.3px) + shadow (2.5px), bottom-center aligned
-  **Zero-install fonts**: every font is a system default — Arial ships with Windows/macOS (Linux auto-substitutes the metric-compatible Liberation Sans); Microsoft YaHei ships with Windows Chinese (macOS falls back to PingFang SC, Linux to Noto Sans CJK SC — all modern sans-serif CJK, visually near-identical)

---

##  Speaker Diarization (Who's Talking?)

Every pipeline path can label **who said what** — each word gets a `Speaker` attribute.

**Two backends**, both native executables (**no Python required**):

| Backend | `DiarizationBackend` | Engine | Notes |
|---|---|---|---|
| polyvoice | `polyvoice` | polyvoice (Rust CPU): powerset segmentation + WeSpeaker ResNet34 + AHC | ~8 MB model pair, fast CPU-only, auto speaker count |
| WeSpeaker | `wespeaker` | sherpa-onnx offline diarization (pyannote segmentation + WeSpeaker ResNet34 ONNX) | Pyannote-class accuracy, models auto-downloaded |

Configure via `WorkflowConfig`:

```csharp
DiarizationBackend = "polyvoice",   // "wespeaker", or "none" to disable
NumSpeakers        = 0,             // 0 = auto (wespeaker honors --clustering.num-clusters)
```

>  Diarization runs **before sentence splitting** and is **non-fatal**: if it fails, subtitles still get generated (just without speaker labels). No drama.

---

##  Vocal Separation (BGM, Step Aside!)

Background music drowning out the speech? Separate the vocals first, then transcribe — clean input, better subtitles.

- Powered by **htdemucs via ONNX Runtime** (native .NET inference, no Python, no external CLI); the MIT-licensed StemSplitio ONNX model auto-downloads on first run (models/htdemucs)
- **GPU accelerated when available**: DirectML execution provider is tried first and falls back to CPU automatically (DirectML out-of-memory on low-memory machines is remembered per process)
- **Mirror-safe model downloads**: models are fetched through Centurion's downloader, which automatically falls back to the **hf-mirror.com** mirror when the official source times out. Zero manual steps, one working vocal track.
- **Off by default** — it's slow (deep learning is patient work) and pointless for clean speech
- Turn it on only for music / MV / BGM-heavy media:

```bash
Centurion asr song.mp4 --vocal-separation
Centurion asr song.mp4 --vocal-separation --vocal-separation-model htdemucs_ft
```

---

##  GPU Detection & Auto-Download

Centurion **detects your hardware** at startup and downloads the right tool builds automatically. No more "please manually install the CUDA version" emails.

-  Detects NVIDIA (via `nvidia-smi` / `CUDA_PATH`), other GPUs (AMD/Intel  Vulkan), system RAM, and platform
-  Tools can declare **per-device variants** (e.g. whisper.cpp CPU vs CUDA builds); the matching one is auto-selected & downloaded
-  Override detection anytime with `--device`:

```bash
Centurion asr audio.mp3 --device cuda     # force CUDA builds
Centurion asr audio.mp3 --device cpu      # stay cozy on CPU
```

Startup banner example:

```
 Device: Platform: win-x64; GPU: Intel(R) Arc(TM) 130T GPU (16GB) (non-NVIDIA); RAM: 15.7 GB; Recommended: Vulkan
```

---

##  Metadata Registry

Tools & models live in an **external JSON registry**, loaded at startup — edit it without touching code.

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

##  Content-Addressed Model Storage

Downloaded models are stored by their **content SHA-256**, so identical files are deduplicated
and every cached model is self-verifying (`models/<category>/<sha256>.<ext>`).

- **Single-file models** (whisper.cpp gguf/bin, Qwen3-ASR/Aligner, htdemucs ONNX, sherpa embedders,
  OCR v5/v6): the file is downloaded to a temp path, hashed locally, then stored as
  `models/<category>/<sha256>.<ext>`.
- **Directory models** (OPUS-MT, SaT, Qwen3-TTS, IndexTTS, PolyVoice, sherpa-diarization packages):
  members keep their original file names inside a directory named after the **aggregate hash**
  (each member's SHA-256 + relative path, sorted, hashed again) — `models/<category>/<aggregate>/`.
- **Manifest**: every category has `.manifest.json` mapping the logical model name to the hash
  (`{Kind: file|dir, Hash, Ext, FileName/Files}`). `models list` reads these manifests to report
  readiness; a missing entry shows the install hint instead of a guessed path.
- **Verification on download**: when the registry declares a published SHA-256, the download is
  verified against it **before** the content hash is computed; mismatches are deleted and re-fetched.
- **Migration**: existing models were renamed in place by `scripts/migrate-models-content-hash.ps1`
  (idempotent; PowerShell 5.1 compatible). Run it again after adding models by hand to re-hash them.

```text
models/
├── whispercpp/.manifest.json
├── whispercpp/be07e048…e1b21.bin          # ggml-tiny.bin (content hash)
├── qwen3asr/.manifest.json
├── qwen3asr/ec197cef…dad8d4.gguf          # qwen3-asr-1.7b-q4_k
├── opusmt/.manifest.json
├── opusmt/137a8972…7d043/                 # zh-en package (aggregate hash dir)
│   ├── config.json
│   └── onnx/encoder_model.onnx
├── sat/09ca073b…f4d7f6/                   # sat-3l-sm
└── htdemucs/d05c269d…db70a.onnx
```

---

##  Non-Latin Language Support

Centurion no longer assumes your audio speaks English with spaces.

- **CJK & spacing-aware text**: Chinese & Japanese are joined without spaces (correct for 无空格语系), Korean keeps its word spaces — the old `string.Join(" ")` nightmare is dead
- **Language-aware punctuation**: sentence splitting recognizes `。！？，；：、…` plus Devanagari `।॥` and Arabic `؟`
- **Per-language transcription**: pass `-l zh` / `-l ja` / `-l ko` — Whisper & CrispASR obey; Whisper auto-detects when no language is given
- **Cross-lingual alignment**: the default aligner (`qwen3-forced-aligner-0.6b`) works for Chinese, Japanese, English & more
- **LLM splitting**: Chinese/Japanese use a CJK prompt branch; Korean rides the English branch (its punctuation matches anyway)
- **Model-agnostic stages**: diarization & vocal separation don't care about language at all

```bash
Centurion asr 讲座.wav -l zh --transcriber qwen3-asr-1.7b
Centurion asr anime.mkv -l ja
```

---

##  Architecture

**Operator-pipeline architecture**: every stage is an independent module — swappable, extendable, reorderable. No monoliths, no tears.

```
 input ──► FFmpegConvert ──► AudioPreprocess ──► [ VocalSeparation] ──► Transcribe
              ──► [ Diarization] ──► SentenceSplit ──► TextCleaning ──► Alignment ──► ASS
```

**Project Layout** (clean layering, zero circular deps):

| Project | Role | Depends on |
|---|---|---|
| `Centurion.Models` | Pure data models (`Sentence`/`Word`/`WorkflowConfig`/ASS…), metadata JSON registry, console facade, `InferenceDevice` | nothing  |
| `Centurion.Abstractions` | Interfaces & abstract bases (operators, strategies, factories), exceptions, request DTOs | `Centurion.Models` |
| `Centurion.Core` | The engine: managers, operators, pipeline executor, strategies, DI wiring | `Centurion.Models` + `Centurion.Abstractions` |
| `Centurion.Cli` | Spectre.Console command-line front-end | Core + Models + Abstractions |
| `Centurion.Server` | ASP.NET Core REST API over the packaged commands | Cli + Core + Abstractions |
| `Centurion.Tests` | xUnit test suite | Core + Models + Abstractions |

- Each step is a `PipelineOperator` running on a shared `SubtitleWorkflowContext` (immutable `WorkflowConfig` + mutable `WorkflowState`)
- Pipelines are **assembled dynamically per command** and executed by `PipelineExecutor`
- Fully async & cancellation-aware; **non-fatal errors just log a warning and keep going** — no half-baked bailouts

**Strategy/operator fusion & assembly-time personalization**: every strategy
(`ITranscriptionStrategy` / `IAlignmentStrategy` / `IDiarizationStrategy` / `ISentenceSplitStrategy` /
`ITranslationStrategy`) inherits `IPipelineStrategy` and declares `StrategyCapabilities`
(`None` / `AlignedTimestamps` / `SpeakerLabels`). The DAG assembler reads those capabilities when
building the pipeline and prunes redundant stages:

- **CrispASR-Qwen3** declares `AlignedTimestamps` (the Qwen3 forced aligner runs during
  transcription), so the standalone **Force Alignment** node is removed at assembly time — even
  with `--align`. Whisper.cpp / cloud ASR (no such capability) keep the node.
- The **TTS engine** for `dub` is resolved once at assembly time (`--tts-engine
  llama|indextts|qora`) and injected into the synthesis operator's constructor; the operator
  holds no runtime engine switch.

**Time-stretch engine** (dub time alignment): synthesized speech is stretched/compressed to the
subtitle window with **ffmpeg rubberband** by default (`--stretch-engine rubberband`) — high-quality
time-stretching with pitch and formants preserved, 0.25x–4.0x range, which handles e.g. a 3 s
synthesis into a 6 s window without the legacy atempo 0.5x boundary clamp. A one-pass duration
correction (re-probe + tempo fix, max two passes) lands each segment within ~50 ms of its target
window. Builds without librubberband fall back to `atempo` (`--stretch-engine atempo`, 0.5x–2.0x).

**TTS voice matching**: the recommended engines for voice cloning are `qora` (self-contained,
auto-downloads, voice cloning from a 3–10 s reference) and `llama` (Qwen3-TTS, when llama.cpp is
registered). `indextts` (IndexTTS-Rust) is wired end-to-end but upstream is not production-ready:
no prebuilt release (needs a local `cargo build`), the GPT inference path is a placeholder with a
fallback vocoder, and the ONNX parts must be converted by hand — it currently cannot deliver real
voice-cloned audio.

**llama-tts engine** (Qwen3-TTS 12Hz 1.7B): the llama.cpp tool is auto-registered in
`config/metadata.json` (b11260 win-cpu build containing `llama-tts.exe`) and auto-downloaded to
`tools/llama/`; the Qwen3-TTS GGUF backbone (`1.7b-base-q4`, talker + mmproj) auto-downloads to
`models/qwen3tts/` from hf-mirror. Verified end-to-end: synthesis, rubberband time alignment and
mixing land within the target window (3 s → 3.000 s). **Known limitation**: the 12Hz *Base*
checkpoint does not accept `--tts-speaker-file` reference cloning (llama-tts fails audio
preprocessing), so `dub --tts-engine llama` speaks in a default voice; use `qora` for cloning.

---

##  Notes & Limitations

-  Active development: flags may change between versions — `Centurion <command> --help` is your lifesaver
-  First runs download tools/models (whisper.cpp, CrispASR, Demucs, GGUF files…) — coffee recommended
-  Diarization & vocal-separation models live on HuggingFace — Centurion auto-falls back to the hf-mirror.com mirror when the official source is unreachable, so the first run just works
- ⏱ Long audio + vocal separation on CPU = patience required (deep learning is worth it, we promise)
