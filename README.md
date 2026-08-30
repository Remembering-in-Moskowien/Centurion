# Centurion

**Automated Subtitle Generation Tool**

Centurion is a .NET 10 command-line tool designed to generate ASS subtitles with precise word-level timestamps from media files. Built with a modular **"everything is an operator"** pipeline architecture, it integrates transcription, intelligent sentence splitting, speaker diarization, and forced alignment into a flexible, extensible workflow.

---

## 🚧 Current Status

- **Version**: Pre‑release (unstable). Pre‑release packages are available on [GitHub Releases](https://github.com/2128611819qqcom/Centurion/releases).
- **GPU Support**: Not yet supported — all inference runs on CPU.
- **Primary Command**: `spawn` — the core subtitle generation workflow.
- **Helper Command**: `convert` — format conversion utilities.
- **Forced Alignment**: Previously planned `--align` flag is currently **disabled** and will be ignored. This feature will be re‑evaluated and restored in a future release.

---

## ✨ Features

- **End‑to‑End Subtitle Generation** – Input any audio or video file, output ASS subtitles with a single command.
- **High‑Performance CPU Transcription** – Powered by FasterWhisper.NET, delivering acceptable speed without GPU acceleration.
- **Intelligent Sentence Splitting** – Semantic segmentation via NLP (Catalyst / rule‑based / LLM‑pluggable) for improved readability.
- **Speaker Diarization** – Identifies and labels speakers per subtitle line using sherpa‑onnx.
- **Karaoke Mode** – Generates ASS subtitles with `\K` tags for word‑by‑word highlighting in compatible players.
- **Pluggable Architecture** – Transcription, splitting, and alignment engines are fully swappable via strategy factories — extendable with new backends (Qwen‑ASR, LLM‑based splitters, Gentle aligner, etc.) without modifying core operators.

---

## 📦 Installation

### Download Pre‑release Package

- Ensure that [FFMpeg](https://ffmpeg.org/download.html) is installed on your machine.
- Visit the [Releases](https://github.com/2128611819qqcom/Centurion/releases) page and download the archive for your platform:
  - `win-x64.zip`
  - (Other platforms as available)
- Extract the archive to any directory.
- Add the executable path to your `PATH` environment variable, or run it directly using the full path.

> Pre‑release builds include bleeding‑edge features and may contain rough edges. Feedback is welcome!

### Build from Source (Development Only)

- Ensure [.NET 10 SDK](https://dotnet.microsoft.com/download) is installed, then:

  ```bash
  git clone https://github.com/2128611819qqcom/Centurion.git
  cd Centurion
  dotnet build -c Release
  ```

- Build outputs are located under `Centurion.Cli/bin/Release/net10.0/`.

---

## 🚀 Usage

### `spawn` — Main Subtitle Generation Command

```bash
Centurion.Cli spawn <INPUT_FILE> [options]
```

#### Options

| Option | Description |
| :----- | :---------- |
| `<INPUT_FILE>` | Input media file path (**required**) |
| `-o, --output <OUTPUT_FILE>` | Output ASS subtitle file path (default: input filename + `.ass`) |
| `--language <LANG>` | Audio language code (e.g., `en`, `zh`, `ja`). Default: `en` |
| `--transcriber <ENGINE>` | Transcription engine: `whisper`, `qwen`, `api`. Default: `whisper` |
| `--transcriber-model <MODEL>` | Model name (e.g., `base`, `large`, `qwen-asr-1.0`) |
| `--transcriber-prompt <PROMPT>` | Initial prompt for transcription |
| `--splitter <STRATEGY>` | Split strategy: `heuristic`, `rule`, `llm`. Default: `heuristic` |
| `--splitter-target-length <CHARS>` | Target characters per line. Default: `50` |
| `--splitter-max-length <CHARS>` | Maximum characters per line. Default: `80` |
| `--splitter-spread <RANGE>` | Spread range for line length distribution. Default: `10` |
| `--splitter-model <MODEL>` | Model for LLM‑based splitting (e.g., `gpt-4`) |
| `--splitter-api-key <KEY>` | API key for LLM splitter |
| `--aligner <ENGINE>` | Alignment engine: `qwen`, `gentle` (omit to disable) |
| `--aligner-model <MODEL>` | Model for alignment (e.g., `qwen-align-1.0`) |
| `--num-speakers <NUM>` | Number of speakers (`0` for auto‑detection). Default: `0` |
| `--karaoke` | Enable karaoke mode (generates `\K` tags) |

> **Note**: The legacy `--align` flag is **currently disabled** and will be ignored. Forced alignment will be reintroduced via the `--aligner` family of options in a future release.

#### Examples

- **Basic English subtitle generation**:
  ```bash
  Centurion.Cli spawn video.mp4 --language en
  ```

- **Chinese subtitles with karaoke mode**:
  ```bash
  Centurion.Cli spawn lecture.mp4 -o subs.ass --language zh --karaoke
  ```

- **Use Qwen‑ASR + LLM splitting + Qwen alignment**:
  ```bash
  Centurion.Cli spawn audio.wav --transcriber qwen --transcriber-model qwen-asr-1.0 --splitter llm --splitter-model gpt-4 --splitter-api-key sk-xxx --aligner qwen
  ```

### `convert` — Auxiliary Conversion Command

A helper command for subtitle format conversion or content adjustment. Currently limited in scope — refer to:

```bash
Centurion.Cli convert --help
```

> `convert` is a secondary utility; the primary functionality is provided by `spawn`.

---

## ⚠️ Important Notes

- **Development Stage**: Commands and options are subject to change. Refer to the actual runtime help output for the most up‑to‑date behavior.
- **CPU‑Only**: All inference runs on CPU. Processing long audio files may be CPU‑intensive — a capable machine is recommended.
- **Forced Alignment Disabled**: The `--align` flag is currently non‑functional and ignored. This feature will be re‑evaluated in future versions.

---

## 🤝 Contributing & Feedback

Issues and Pull Requests are welcome! As the project is still in its early stages, please open an Issue first to discuss major feature changes before investing significant effort.

---

## 📄 License

MIT License — see the [LICENSE](LICENSE) file for details.

---

**Built with .NET 10** — modular, extensible, and evolving.