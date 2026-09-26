---
title: Advanced
---

# 🔬 Advanced

## Operator Micro-Commands

`asr`/`ocr`/`from-script`/`correct`/`translate`/`dub` are **packaged** pipelines — they run several operators back to back. Sometimes you only want **one stage**. Every major operator is also exposed as its own micro-command. Input/output stay on the intermediate file (source commands also accept media), so you can drive the pipeline stage by stage, inspect between steps, and re-run a single stage without touching the rest.

```bash
# Stage-by-stage chain: convert → transcribe → split → clean → align
Centurion convert subs.srt
Centurion transcribe subs.centurion.json        # needs the audio file (Config.InputFilePath)
Centurion split subs.transcribe.centurion.json
Centurion clean subs.transcribe.split.centurion.json
Centurion align subs.transcribe.split.clean.centurion.json
Centurion build subs.transcribe.split.clean.align.centurion.json
```

| Command | Accepts | Runs | Output |
|---|---|---|---|
| `transcribe <INPUT>` | media or IR | FFmpegConvert → AudioPreprocess → Transcribe | `<input>.transcribe.centurion.json` |
| `vocalsep <INPUT>` | media or IR | FFmpegConvert → VocalSeparation | `<input>.vocalsep.centurion.json` + `<input>.vocals.wav` |
| `diarize <IR>` | IR only | Diarization | `<input>.diarize.centurion.json` |
| `split <IR>` | IR only | SentenceSplit | `<input>.split.centurion.json` |
| `clean <IR>` | IR only | TextPreprocessing | `<input>.clean.centurion.json` |
| `align <IR>` | IR only | Alignment | `<input>.align.centurion.json` |
| `spellcheck <IR>` | IR only | SpellCheck (Hunspell) | `<input>.spellcheck.centurion.json` + `.spellcheck.json` |
| `quality <IR>` | IR only | QualityReport | `<input>.quality.centurion.json` + `.quality.json` |

Each stage is checkpointed — re-running `split` on an already-split file skips cleanly instead of double-processing.

> 💡 Every micro-command prints what to run next (e.g. `split` suggests `align`). The packaged commands remain exactly as they were — micro-commands are additive.

---

## 📊 Quality Reports (Every Command)

Every pipeline path — `asr`, `ocr`, `from-script`, `correct`, `convert`, `translate`, `dub` — finishes by writing a **`<output>.quality.json`** next to its output (e.g. `demo.centurion.quality.json`). No flags, no opt-in. 📈

```json
{
  "meta": { "command": "asr", "input": "demo.mp4", "output": "demo.centurion.json", "generatedAt": "…" },
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

## 🔄 `update` — Self-Update

Never touch GitHub by hand again. `update` checks `Remembering-in-Moskowien/Centurion` releases, downloads the matching build, and swaps itself out.

```bash
Centurion update [options]
```

**Options**

- `--check` — only check for a new version; download nothing
- `--apply` — download **and** apply right away (closes & restarts the program)
- `--asset <NAME>` — manually pick a release asset name (default: auto-match by platform, e.g. `Centurion-win-x64.zip`)

**How it works** 🧠

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

## 🌍 Localization (JSON, on by default in English)

- All user-facing console/log messages go through `ConsoleServices.T()`; the **key is the English default text**
- Translations live in `Localization/{culture}.json` next to the executable — drop a file in, no recompile needed
- Built-in `zh-CN.json` ships with every release (Simplified Chinese 🇨🇳)

```bash
# run in Chinese
Centurion.Cli.exe --lang zh-CN asr video.mp4
# omit --lang → English (default)

# GitHub downloads (update / dictionaries / tools) auto-try 520 mirrors then direct
Centurion.Cli.exe --github-proxy https://my-mirror.example/ update      # use a custom mirror
Centurion.Cli.exe --no-github-proxy update                              # disable mirrors entirely
```

- Missing key / missing language file → falls back to English gracefully ✅
- Powered by the official `Microsoft.Extensions.Localization` NuGet package + a custom JSON resource provider
