# Centurion

Centurion is a .NET 10 CLI tool for generating ASS subtitles from audio/video files. It supports a complete workflow covering speech recognition, sentence splitting, script alignment, and subtitle export.

The project uses an operator pipeline architecture, where transcription, sentence splitting, text cleanup, and alignment are separated into independent modules to make the system extensible and easier to replace with different strategies.

---

## Project Status

- This project is still in an early development / pre-release stage, and commands and parameters may change as the project evolves.
- Primary output target: ASS subtitle files.
- Runtime environment: CPU-based inference; GPU acceleration is currently not supported.
- Dependencies: FFmpeg, .NET 10 SDK.

---

## Feature Overview

- Generate ASS subtitles from audio/video files
- Support transcription engines such as Whisper and Crisp
- Support rule-based sentence splitting, Catalyst/NLP splitting, and LLM-based splitting strategies
- Support text cleaning and script-based alignment
- Support forced alignment
- Support karaoke mode to generate timing with `\K` markers
- Support subtitle generation from plain-text script files
- Support subtitle conversion across different formats

---

## Requirements

### 1. Install .NET 10 SDK

Make sure the .NET 10 SDK is installed:

- https://dotnet.microsoft.com/download

### 2. Install FFmpeg

Centurion depends on FFmpeg for audio/video processing.

- https://ffmpeg.org/download.html
- On Windows, it is recommended to add the FFmpeg `bin` directory to `PATH`

---

## Build the Project

Run this at the repository root:

```bash
dotnet build -c Release
```

The build output will be generated under:

```text
Centurion.Cli/bin/Release/net10.0/
```

If you want to run the executable directly, use:

```bash
./Centurion.Cli/bin/Release/net10.0/Centurion.Cli
```

---

## Command List

The current CLI includes three main commands:

- `spawn`: core subtitle generation command
- `from-script`: generate subtitles from a script file plus media input
- `convert`: subtitle format conversion helper command

---

## 1. `spawn` Command

Used to generate ASS subtitles directly from an input media file.

```bash
Centurion.Cli spawn <INPUT_FILE> [options]
```

### Common Options

- `<INPUT_FILE>`: input audio/video file, required
- `-o|--output <OUTPUT_FILE>`: output ASS file path; if omitted, it defaults to the input filename with a `.ass` extension
- `-l|--language <LANG>`: audio language, default `en`
- `--num-speakers <NUM>`: number of speakers, default `0` (auto-detect)
- `-k|--karaoke`: enable karaoke mode and generate `\K` markers
- `-t|--transcriber <ENGINE>`: transcription engine, default `whisper`
- `--tm|--transcriber-model <MODEL>`: transcriber model name
- `--tp|--transcriber-prompt <PROMPT>`: initial transcription prompt
- `-s|--splitter <STRATEGY>`: sentence splitting strategy, supports `rule`, `nlp`, and `llm`
- `--splitter-chunk-granularity <LEVEL>`: Catalyst/NLP chunk granularity, default `0.5`
- `--splitter-target-length <CHARS>`: target characters per line, default `50`
- `--splitter-max-length <CHARS>`: maximum characters per line, default `80`
- `--splitter-spread <RANGE>`: line-length spread range, default `10`
- `--splitter-model <MODEL>`: LLM-based splitting model
- `--splitter-api-key <KEY>`: API key for LLM splitting
- `-a|--enable-alignment`: enable forced alignment, enabled by default
- `--am|--alignment-model <MODEL>`: alignment model

### Examples

#### Basic generation

```bash
Centurion.Cli spawn demo.mp4 --language en
```

#### Specify output file

```bash
Centurion.Cli spawn lecture.wav -o lecture.ass --language zh
```

#### Enable karaoke

```bash
Centurion.Cli spawn meeting.mp4 -o meeting.ass --language en --karaoke
```

---

## 2. `from-script` Command

This command is suitable for a scenario where you already have a script text file and a media file. It aligns the script with the audio and generates subtitles.

```bash
Centurion.Cli from-script <INPUT_FILE> <SCRIPT_FILE> [options]
```

### Common Options

- `<INPUT_FILE>`: input media file
- `<SCRIPT_FILE>`: script text file
- `-o|--output <OUTPUT_FILE>`: output ASS file
- `-l|--language <LANG>`: audio language, default `en`
- `-t|--transcriber <ENGINE>`: transcription engine
- `--tm|--transcriber-model <MODEL>`: transcriber model
- `-a|--enable-alignment`: enable forced alignment, enabled by default
- `--am|--alignment-model <MODEL>`: alignment model
- `--max-cps <CPS>`: maximum displayed characters per second, default `5.0`
- `--max-chars-per-line <CHARS>`: maximum characters per subtitle line, default `18`
- `--coverage-threshold <RATIO>`: script coverage warning threshold, default `0.92`
- `--fill-gap`: insert ellipses for missing script words

### Examples

```bash
Centurion.Cli from-script input.mp3 script.txt -o output.ass --language en
```

```bash
Centurion.Cli from-script input.wav script.txt --enable-alignment --max-chars-per-line 20
```

---

## 3. `convert` Command

Used to convert an existing subtitle file into ASS output, primarily for intermediate format conversion or format normalization.

```bash
Centurion.Cli convert <INPUT_FILE> [options]
```

### Options

- `<INPUT_FILE>`: input subtitle file
- `-o|--output <OUTPUT_FILE>`: output ASS file path; if omitted, it defaults to the same name with a `.ass` extension

### Example

```bash
Centurion.Cli convert subtitles.srt -o subtitles.ass
```

---

## Typical Workflows

### Generate subtitles directly

```bash
Centurion.Cli spawn video.mp4 --language en --karaoke
```

### Use a script to align the output

```bash
Centurion.Cli from-script podcast.wav transcript.txt --language en
```

### Convert to ASS output

```bash
Centurion.Cli convert input.vtt -o output.ass
```

---

## Notes and Limitations

- The project is still under active development, and command parameters and behavior may change between versions.
- All inference currently runs on CPU, so processing long audio files may be relatively slow.
- Forced alignment and various model strategies depend on the selected model and environment configuration. It is recommended to check the live help text with `--help`.
- It is recommended to run `Centurion.Cli <command> --help` before each upgrade to confirm the current command-line parameters.

---

## Contributing and Feedback

Issues, pull requests, and feedback are welcome.

If you are contributing to the project, please first understand the current pipeline structure and prioritize compatibility for command-line options and output format.

---

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

