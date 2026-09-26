---
title: Features
---

# 🎨 Features

## Subtitle Styles (Batteries Included)

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
Centurion asr song.mp4 --vocal-separation
Centurion asr song.mp4 --vocal-separation --vocal-separation-model htdemucs_ft
```

---

## 🖥️ GPU Detection & Auto-Download

Centurion **detects your hardware** at startup and downloads the right tool builds automatically. No more "please manually install the CUDA version" emails. 📬➡️🗑️

- 🔍 Detects NVIDIA (via `nvidia-smi` / `CUDA_PATH`), other GPUs (AMD/Intel → Vulkan), system RAM, and platform
- 📦 Tools can declare **per-device variants** (e.g. whisper.cpp CPU vs CUDA builds); the matching one is auto-selected & downloaded
- 🎯 Override detection anytime with `--device`:

```bash
Centurion asr audio.mp3 --device cuda     # force CUDA builds
Centurion asr audio.mp3 --device cpu      # stay cozy on CPU
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
Centurion asr 讲座.wav -l zh --transcriber qwen3-asr-1.7b
Centurion asr anime.mkv -l ja
```

---

## 🧠 Architecture

**Operator-pipeline architecture**: every stage is an independent module — swappable, extendable, reorderable. No monoliths, no tears. 🧩

```
 input ──► FFmpegConvert ──► AudioPreprocess ──► [🎤 VocalSeparation] ──► Transcribe
              ──► [👥 Diarization] ──► SentenceSplit ──► TextCleaning ──► Alignment ──► ASS 📦
```

**Project Layout** (clean layering, zero circular deps):

| Project | Role | Depends on |
|---|---|---|
| `Centurion.Models` | Pure data models (`Sentence`/`Word`/`WorkflowConfig`/ASS…), metadata JSON registry, console facade, `InferenceDevice` | nothing 🧱 |
| `Centurion.Abstractions` | Interfaces & abstract bases (operators, strategies, factories), exceptions, request DTOs | `Centurion.Models` |
| `Centurion.Core` | The engine: managers, operators, pipeline executor, strategies, DI wiring | `Centurion.Models` + `Centurion.Abstractions` |
| `Centurion.Cli` | Spectre.Console command-line front-end | Core + Models + Abstractions |
| `Centurion.Server` | ASP.NET Core REST API over the packaged commands | Cli + Core + Abstractions |
| `Centurion.Tests` | xUnit test suite | Core + Models + Abstractions |

- Each step is a `PipelineOperator` running on a shared `SubtitleWorkflowContext` (immutable `WorkflowConfig` + mutable `WorkflowState`)
- Pipelines are **assembled dynamically per command** and executed by `PipelineExecutor`
- Fully async & cancellation-aware; **non-fatal errors just log a warning and keep going** — no half-baked bailouts 💪

---

## ⚠️ Notes & Limitations

- 🚧 Active development: flags may change between versions — `Centurion <command> --help` is your lifesaver 🤝
- 🐢 First runs download tools/models (whisper.cpp, CrispASR, Demucs, GGUF files…) — coffee recommended ☕
- 🎵 Diarization & vocal-separation models live on HuggingFace — Centurion auto-falls back to the hf-mirror.com mirror when the official source is unreachable, so the first run just works 🌏
- ⏱️ Long audio + vocal separation on CPU = patience required (deep learning is worth it, we promise)
