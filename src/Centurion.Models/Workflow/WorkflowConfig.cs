namespace Centurion.Models.Workflow;

/// <summary>
/// Workflow configuration; immutable and merging MediaGenerationRequest, SplitOptions, and the various Payload parameters.
/// </summary>
public class WorkflowConfig
{
    // ---------- Input / Output ----------
    /// <summary>Full path of the audio or video file to process.</summary>
    public string InputFilePath { get; init; } = string.Empty;

    /// <summary>Name of the subcommand that triggered this run, such as "spawn" or "dub"; used in metadata such as the quality report.</summary>
    public string CommandName { get; init; } = string.Empty;
    /// <summary>Path of an existing subtitle file used as the correction baseline, such as SRT/ASS; may be null.</summary>
    public string? SubtitleFilePath { get; init; }
    /// <summary>Path of the final output subtitle file; may be null and is updated to the current command's output in standalone operator chains.</summary>
    public string? OutputFilePath { get; set; }
    /// <summary>Path of the script or transcript file used for correction; may be null.</summary>
    public string? ScriptFilePath { get; init; }
    /// <summary>
    /// Inference device preference: auto/cpu/cuda/vulkan/directml.
    /// With Auto the system detects automatically, and CUDA builds auto-download an NVIDIA GPU tool.
    /// </summary>
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;
    /// <summary>The subtitle correction strategy: timeline-only / text-only / both.</summary>
    public CorrectionStrategy CorrectStrategy { get; init; } = CorrectionStrategy.Both;
    /// <summary>Maximum allowed timeline drift in milliseconds, beyond which alignment is considered abnormal.</summary>
    public int MaxDriftMs { get; init; } = 1500;
    /// <summary>Similarity threshold for fuzzy matching, from 0 to 1; below it a match is not accepted.</summary>
    public double FuzzyThreshold { get; init; } = 0.72;
    /// <summary>Hunspell spell-check dictionary prefix, such as en_US; defaults to en_US and is downloaded automatically when missing; only the default prefix is supported.</summary>
    public string HunspellDictionary { get; init; } = "en_US";
    /// <summary>Identifier of the mapping strategy used for word-level alignment between script and audio, such as "rule".</summary>
    public string MapperStrategy { get; init; } = "rule";
    /// <summary>Text coverage threshold from 0 to 1; below it the alignment is judged insufficient.</summary>
    public double CoverageThreshold { get; init; } = 0.92;
    /// <summary>Maximum characters per second (CPS) for subtitles, used for readability constraints.</summary>
    public double MaxCps { get; init; } = 5.0;
    /// <summary>Maximum number of characters per subtitle line.</summary>
    public int MaxCharsPerLine { get; init; } = 18;
    /// <summary>Whether gaps for words missing from the script are filled with the ellipsis "[...]".</summary>
    public bool FillGapWithEllipsis { get; init; } = true;

    // ---------- Audio Preprocessing ----------
    /// <summary>Toggles and thresholds for the audio preprocessing pipeline.</summary>
    public AudioPreprocessConfig AudioPreprocess { get; init; } = new();

    // ---------- Transcription ----------
    /// <summary>Transcription engine selection, such as "crispasr", "whisper", "qwen", or "api".</summary>
    public string TranscriberEngine { get; init; } = "crispasr";   // whisper, qwen, api
    /// <summary>Model spec name used by the selected engine, such as qwen3-asr-1.7b or large-v3.</summary>
    public string? TranscriberModel { get; init; } = "qwen3-asr-1.7b";     // e.g., base, large
    /// <summary>Cloud ASR provider name: openai/groq/dashscope/deepgram; ignored by local engines.</summary>
    public string AsrProvider { get; set; } = "crispasr";
    /// <summary>Cloud ASR API key.</summary>
    public string? AsrApiKey { get; init; }
    /// <summary>Cloud ASR endpoint; when empty the provider default is used.</summary>
    public string? AsrBaseUrl { get; init; }
    /// <summary>Recognition language code, such as "en" or "zh".</summary>
    public string Language { get; init; } = "en";
    /// <summary>Initial prompt fed to the model to guide style and terminology; may be null.</summary>
    public string? InitialPrompt { get; init; }

    // ---------- Sentence Splitting ----------
    /// <summary>Sentence splitting strategy, such as "rule", "llm", "sat" (local SaT model), or "nlp/catalyst".</summary>
    public string SplitStrategy { get; init; } = "rule";    // llm, rule, nlp/catalyst, sat
    /// <summary>Maximum character length of a single sentence; longer ones are split.</summary>
    public int MaxSentenceLength { get; init; } = 80;
    /// <summary>Desired target sentence length in characters, which splitting tries to approach.</summary>
    public int TargetSentenceLength { get; init; } = 50;
    /// <summary>Allowable spread of sentence length around the target.</summary>
    public int SpreadRange { get; init; } = 10;
    /// <summary>Time granularity in seconds when merging short adjacent sentences along the timeline.</summary>
    public float ChunkGranularity { get; init; } = 0.5f;
    /// <summary>Adjacent short sentences closer than this many seconds apart are merged into one.</summary>
    public double MergeGapSeconds { get; init; } = 1.5;
    /// <summary>Whether to rewrite and normalize punctuation during sentence splitting.</summary>
    public bool EnablePunctuationRewrite { get; init; } = true;
    /// <summary>Model name used for LLM-based splitting; may be null.</summary>
    public string? SplitterModel { get; init; }                 // for LLM
    /// <summary>API key required for LLM-based splitting; may be null.</summary>
    public string? SplitterApiKey { get; init; }                // for LLM
    /// <summary>LLM splitting service provider name, such as deepseek, moonshot, or openrouter; inferred automatically when empty.</summary>
    public string? SplitterProvider { get; init; }
    /// <summary>Custom endpoint for LLM-based splitting; when empty the selected provider's default endpoint is used.</summary>
    public string? SplitterBaseUrl { get; init; }
    /// <summary>Boundary probability threshold (0.0–1.0) for the SaT split strategy; default 0.5.</summary>
    public double SplitterThreshold { get; init; } = 0.5;

    // ---------- Vocal Separation (optional enhancement, off by default) ----------
    /// <summary>
    /// Whether to enable Demucs vocal separation, separating vocals from accompaniment/music before transcription.
    /// Valuable only for material with noticeable music or BGM; enabling it on pure speech significantly increases runtime.
    /// </summary>
    public bool VocalSeparation { get; set; } = false;
    /// <summary>
    /// Demucs separation model name, such as htdemucs; downloaded and cached automatically from HuggingFace by demucs-rs on first run.
    /// </summary>
    public string VocalSeparationModel { get; set; } = "htdemucs";

    // ---------- OCR Command Settings ----------
    /// <summary>OCR command: frame extraction interval in seconds; defaults to 2 seconds.</summary>
    public double OcrIntervalSeconds { get; init; } = 2.0;
    /// <summary>OCR command: optional path to the VideoSubFinder CLI executable.</summary>
    public string? OcrVideoSubFinderPath { get; init; }
    /// <summary>OCR command: OCR backend: "zhipu" (cloud GLM-OCR, default) | "ollama" (local Ollama vision model) | "llamacpp" (local llama-server).</summary>
    public string OcrBackend { get; init; } = "zhipu";
    /// <summary>OCR command: OCR model name; follows the backend by default: zhipu→glm-ocr, ollama→qwen2.5vl:7b, llamacpp→local-model.</summary>
    public string? OcrModel { get; init; }
    /// <summary>OCR command: GLM-OCR API key.</summary>
    public string? OcrApiKey { get; init; }
    /// <summary>OCR command: GLM-OCR endpoint address; defaults to the Zhipu v4 chat/completions endpoint.</summary>
    public string? OcrBaseUrl { get; init; }
    /// <summary>OCR command: VideoSubFinder subtitle detection region, top edge as a fraction of video height from 0 to 1; defaults to 0.2102, the top of the subtitle area (VSF -te).</summary>
    public double? OcrRoiTop { get; init; }
    /// <summary>OCR command: VideoSubFinder subtitle detection region, bottom edge as a fraction of video height from 0 to 1; defaults to 0 (VSF -be).</summary>
    public double? OcrRoiBottom { get; init; }
    /// <summary>OCR command: VideoSubFinder subtitle detection region, left edge as a fraction of video width from 0 to 1; defaults to 0 (VSF -le).</summary>
    public double? OcrRoiLeft { get; init; }
    /// <summary>OCR command: VideoSubFinder subtitle detection region, right edge as a fraction of video width from 0 to 1; defaults to 1 (VSF -re).</summary>
    public double? OcrRoiRight { get; init; }

    // ---------- Diarization ----------
    /// <summary>
    /// Diarization backend: "none" (off, default) | "polyvoice" (Rust CPU diarization CLI) |
    /// "wespeaker" (sherpa-onnx offline diarization with WeSpeaker embeddings).
    /// </summary>
    public string DiarizationBackend { get; init; } = "none";
    /// <summary>
    /// Reserved for backend-specific segmentation method tuning (not used by polyvoice/wespeaker; kept for config compatibility).
    /// </summary>
    public string DiarizationMethod { get; init; } = "foxnose";
    /// <summary>
    /// Reserved for future backend-specific segmentation model selection (auto-downloaded by each strategy; kept for config compatibility).
    /// </summary>
    public string DiarizationModel { get; init; } = "pyannote-seg-3.0";
    /// <summary>Expected number of speakers; 0 means automatic estimation.</summary>
    public int NumSpeakers { get; init; } = 0;

    /// <summary>
    /// Diarization post-processing: segments shorter than this many seconds are treated as fragments and smoothed together,
    /// eliminating per-segment alternating jitter and boundary-word mislabeling. Defaults to 0.5 seconds.
    /// </summary>
    public double DiarizationMinSegmentSeconds { get; init; } = 0.5;

    // ---------- Output Style ----------
    /// <summary>Whether to output karaoke mode with per-word \k timing tags.</summary>
    public bool KaraokeMode { get; init; } = false;
    /// <summary>
    /// Whether to show a speaker label before the subtitle text, such as "[SPEAKER_01] text".
    /// Speaker information is always written to the ASS Name field; this switch only controls the visibility of the text prefix and is on by default.
    /// </summary>
    public bool ShowSpeakerLabels { get; init; } = true;

    // ---------- Miscellaneous ----------
    /// <summary>Cache directory for model and temporary files.</summary>
    public string CacheDirectory { get; init; } = "./cache";
    /// <summary>Whether to enable the word-level forced alignment stage.</summary>
    public bool EnableAlignment { get; init; } = true;
    /// <summary>Model name used for forced alignment; may be null.</summary>
    public string? AlignmentModel { get; init; } = "qwen3-forced-aligner-0.6b-f16";
    /// <summary>
    /// Forced alignment chunking: when the time gap between adjacent sentences exceeds this many seconds, the sentences are split into independent chunk blocks.
    /// Each chunk launches the alignment process only once, greatly reducing process and model loading overhead for long audio. Defaults to 2.0 seconds.
    /// </summary>
    public double AlignmentChunkGapSeconds { get; init; } = 2.0;
    /// <summary>
    /// Forced alignment chunking: maximum audio duration in seconds per chunk block.
    /// Beyond it a new block is forced, keeping a single alignment within the model's context window. Defaults to 120 seconds.
    /// </summary>
    public double AlignmentMaxChunkSeconds { get; init; } = 120.0;

    // ---------- Pre-alignment Text Cleaning ----------
    /// <summary>Whether to enable text cleaning before alignment.</summary>
    public bool EnableTextCleaning { get; init; } = true;
    /// <summary>Whether to remove punctuation during cleaning.</summary>
    public bool RemovePunctuation { get; init; } = false;
    /// <summary>Whether to expand numbers into words during cleaning.</summary>
    public bool ExpandNumbers { get; init; } = true;
    /// <summary>Whether to expand abbreviations into full forms during cleaning.</summary>
    public bool ExpandAbbreviations { get; init; } = false;
    /// <summary>Path of a custom cleaning dictionary file; may be null.</summary>
    public string? CustomDictPath { get; init; }

    // ---------- Translation Module (translate subcommand) ----------
    /// <summary>Target language code for translation, such as zh, en, or ja; required by the translate command.</summary>
    public string TargetLanguage { get; init; } = string.Empty;
    /// <summary>Translation strategy name, such as llm; defaults to llm.</summary>
    public string TranslationStrategy { get; init; } = "llm";
    /// <summary>Model name used for LLM translation; may be null (OpenAI defaults to gpt-4o-mini, Ollama defaults to llama3.1).</summary>
    public string? TranslationModel { get; init; }
    /// <summary>API key required for LLM translation; falls back to local Ollama when empty.</summary>
    public string? TranslationApiKey { get; init; }
    /// <summary>LLM translation service provider name, such as deepseek, moonshot, or openrouter; inferred automatically when empty.</summary>
    public string? TranslationProvider { get; init; }
    /// <summary>Custom endpoint for LLM translation; when empty the selected provider's default endpoint is used.</summary>
    public string? TranslationBaseUrl { get; init; }
    /// <summary>Glossary file path, a JSON mapping source-language terms to target-language terms; may be null.</summary>
    public string? GlossaryPath { get; init; }
    /// <summary>Target-language script file path, one target-language translation per line; may be null.</summary>
    public string? TargetScriptPath { get; init; }
    /// <summary>Whether to output bilingual subtitles, source \\N translation; defaults to target language only.</summary>
    public bool Bilingual { get; init; } = false;

    // ── Dub (media localization) ──

    /// <summary>TTS engine; currently only "llama", i.e. llama.cpp llama-tts.</summary>
    public string TtsEngine { get; init; } = "llama";

    /// <summary>TTS model; the name registered in metadata.json Models, such as 1.7b-base-q4.</summary>
    public string TtsModel { get; init; } = "1.7b-base-q4";

    /// <summary>Dubbing target language in ISO 639-1, such as zh/en/ja, used for llama-tts --tts-lang.</summary>
    public string TtsLanguage { get; init; } = "zh";

    /// <summary>Optional: manually specify the speaker reference audio directory, one wav per speaker, named SPEAKER_xx.wav.</summary>
    public string? SpeakerReferenceDir { get; init; }

    /// <summary>Optional: path of the translation subtitle track file in bilingual mode, matched to the main subtitle by time window.</summary>
    public string? TranslationSubtitlePath { get; init; }

    /// <summary>Strategy when TTS speech rate falls outside the allowed range: true clamps to the engine boundaries, false keeps the original synthesis duration.</summary>
    public bool DubStrictTiming { get; init; } = true;

    /// <summary>Time-stretch engine for aligning synthesized speech to the subtitle timeline:
    /// "rubberband" (default; ffmpeg librubberband filter, high quality, 0.25x~4.0x) | "atempo"
    /// (legacy ffmpeg atempo, 0.5x~2.0x). Falls back to atempo when the ffmpeg build lacks rubberband.</summary>
    public string DubStretchEngine { get; init; } = "rubberband";

    /// <summary>Optional: path of accompaniment/background audio; providing it enables ducking mixing.</summary>
    public string? DubBackgroundPath { get; init; }

    /// <summary>Output loudness target in LUFS; defaults to -16.</summary>
    public double DubLoudnessTarget { get; init; } = -16;

    /// <summary>TTS synthesis parallelism; defaults to 2, lower to 1 when memory is tight.</summary>
    public int TtsParallelism { get; init; } = 2;

    /// <summary>Long-sentence chunking threshold in seconds; defaults to 15, and longer target durations are split into sub-segments proportionally.</summary>
    public double DubMaxChunkSeconds { get; init; } = 15;

    /// <summary>Whether to enable ducking; on by default when accompaniment is present.</summary>
    public bool DubDucking { get; init; } = true;
}
