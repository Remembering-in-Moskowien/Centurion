---
title: Dub
---

# 🎙️ `dub` — Media Dubbing with Qwen3-TTS

Take a Centurion intermediate file (already carrying sentences, translations and speakers — run `translate` first if needed) and turn it into **dubbed audio**. Fully local, no cloud, no Python. 🐍❌

```bash
Centurion dub <CENTURION_FILE> -t <LANG> [options]
```

## The pipeline 🧠

```
intermediate file (sentences + translations + speakers)
              ──► SpeakerProfiling (SNR-scored selection of each speaker's cleanest sentence)
              ──► TTS Synthesis (llama-tts / Qwen3-TTS 1.7B, parallel by speaker, long lines chunked)
              ──► TimeAlignment (ffprobe actual length → atempo; overlap detection & compression)
              ──► AudioMix (timeline placement + optional background ducking + loudnorm)
              ──► Quality Report 📊 (translation coverage, alignment deviation, tempo stats)
```

## Options

- `<CENTURION_FILE>` — input Centurion intermediate file (translate first if the target text isn't in it yet) 📄
- `-t, --target-language <LANG>` — TTS voice language: `en`, `zh`, `ja`, `ko`, `de`, `fr`, `es`, `it`, `pt`, `ru` (ISO 639-1)
- `-o, --output <OUTPUT_FILE>` — output intermediate path (defaults to `<input>.dub.centurion.json`; the WAV goes to `<input>.dub.wav`)
- `--speaker-reference <DIR>` — directory of reference clips (`SPEAKER.wav` per speaker) for **voice cloning**; omit to auto-profile from the media 🎤
- `--background <FILE>` — background / accompaniment audio; enables **sidechain ducking** (the TTS voice ducks the music) 🎼
- `--no-ducking` — disable ducking even when `--background` is given
- `--loudness-target <LUFS>` — output loudness target, default `-16` (loudnorm) 🔊
- `--tts-parallelism <N>` — TTS synthesis parallelism, default `2` (speaker-bucketed, global throttle)
- `--max-chunk-seconds <S>` — long-sentence chunking threshold, default `15` (lines exceeding it are split proportionally and re-joined on the timeline)
- `--tts-engine <ENGINE>` — TTS engine, `llama` (default)
- `--tts-model <MODEL>` — TTS model, `1.7b-base-q4` (default; ~1.4 GB, auto-downloaded)
- `--strict-timing` — clamp out-of-range tempo to 0.5×–2.0× (default on; off keeps natural length)

## How it works 🧠

- **Bilingual matching**: the parser pairs source and translation lines by a center-time window (1500 ms); without a translation track, the whole file is treated as already-translated
- **SNR-scored voice references**: with no manual clips, each speaker's candidate sentences (2–8 s) are scored by **ffmpeg astats** — speech RMS vs. media noise floor gives an SNR estimate; the cleanest, best-timed line is cropped as the TTS voice reference 🎚️
- **Parallel synthesis**: sentences are bucketed by speaker (same speaker stays ordered), buckets synthesize in parallel under a global throttle — no more waiting for one slow line
- **Long-line chunking**: lines whose target window exceeds the threshold are split proportionally by character and re-joined seamlessly on the timeline
- **Time alignment**: every synthesized segment is measured (ffprobe) and sped up/slowed down (atempo, 0.5×–2.0×) to fit its subtitle window; out-of-range segments are clamped to the boundary and warned
- **Overlap handling**: overlapping subtitle windows are detected and the later segment is compressed forward (`MixOffsetMs`) instead of stacking noisily
- **Ducking & loudness**: with `--background`, the accompaniment is sidechain-compressed by the TTS voice (`asplit`-based graph for ffmpeg 7 compat), then everything is loudnorm'd to your target LUFS
- **Auto-everything**: llama-tts + the GGUF models are downloaded on first use (GitHub mirrors → direct, hf-mirror fallback 🌏)

## Examples 🧪

```bash
# Convert → translate → dub in one chain
Centurion convert episode.ass
Centurion translate episode.centurion.json -t zh
Centurion dub episode.translated.centurion.json -t zh

# Dub with per-character voice references
Centurion dub movie.centurion.json -t en --speaker-reference voices/

# Dub over the original soundtrack with ducking, louder output
Centurion dub subs.translated.centurion.json -t ja --background ost.wav --loudness-target -14

# Strict timing + aggressive parallelism for fast dialogue
Centurion dub subs.centurion.json -t en --strict-timing --tts-parallelism 4 --max-chunk-seconds 10
```

> 🧾 Outputs: `<input>.dub.wav` (the dub) + `<input>.dub.centurion.json` (intermediate, with all dub segments) + `<input>.dub.centurion.quality.json` (per-segment report).
