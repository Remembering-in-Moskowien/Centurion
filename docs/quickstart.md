---
title: Quick Start
---

# ⚡ Quick Start

## ⚙️ Prerequisites (Just Two Steps)

### 1️⃣ Install the .NET 10 SDK
👉 https://dotnet.microsoft.com/download

### 2️⃣ Install FFmpeg
👉 https://ffmpeg.org/download.html

Windows users: add the FFmpeg `bin` directory to your `PATH` (or drop `ffmpeg.exe` / `ffprobe.exe` into the project's `tools/ffmpeg` folder). 🔧

> 🎁 Everything else (whisper.cpp, CrispASR, Demucs-rs, models…) is **auto-downloaded on first use** — zero manual installs. On the first run, go grab a coffee ☕

## 🔨 Build the Project

```bash
dotnet build -c Release
```

Output lands in `Centurion.Cli/bin/Release/net10.0/` — run it directly:

```bash
./Centurion.Cli/bin/Release/net10.0/Centurion.Cli
```

## 🎬 Your First Subtitles

```bash
# Transcribe → build ASS in two steps
Centurion spawn demo.mp4 --language en
Centurion build demo.centurion.json
```

That's it. Whisper/CrispASR, models and tools download themselves on first use. 🎉

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

## 🧾 The Intermediate File

Every command reads & writes **`*.centurion.json`** — a rich, lossless middle format that carries config, per-stage sentences, word-level timestamps, speakers, translations and dub segments. Chain commands without losing information:

```bash
convert → correct → translate → dub → build
```
