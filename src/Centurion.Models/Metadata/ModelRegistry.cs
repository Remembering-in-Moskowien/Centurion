namespace Centurion.Models.Metadata;

/// <summary>Describes how a model resource is downloaded and organized.</summary>
public enum ModelDownloadType
{
    /// <summary>A single model file, such as .bin or .gguf, saved directly under its filename.</summary>
    SingleFile,
    /// <summary>A directory package, such as a Faster-Whisper model, downloaded as a list of files.</summary>
    Directory,
    /// <summary>An ONNX model directory package with an ONNX task type, such as embedding or token_classification.</summary>
    OnnxModelDirectory
}

/// <summary>Download metadata for one model; fields vary by download type.</summary>
public record ModelMeta
{
    /// <summary>Local filename for single-file models.</summary>
    public string? FileName { get; init; }
    /// <summary>Model download URL.</summary>
    public string? DownloadUrl { get; init; }
    /// <summary>Download and organization type; defaults to <see cref="ModelDownloadType.SingleFile"/>.</summary>
    public ModelDownloadType DownloadType { get; init; } = ModelDownloadType.SingleFile;
    /// <summary>Relative paths of files to download for directory and ONNX types.</summary>
    public List<string>? Files { get; init; }
    /// <summary>ONNX task type, such as token_classification or embedding; set only for ONNX directory types.</summary>
    public string? OnnxModelType { get; init; }
    /// <summary>Optional subdirectory for the model within the download root.</summary>
    public string? Subdirectory { get; init; }
    /// <summary>
    /// Optional SHA-256 hash for the downloaded file or package, in lowercase hexadecimal.
    /// Null disables verification; when provided, the download is verified and deleted on mismatch.
    /// </summary>
    public string? FileHash { get; init; }

    /// <summary>Creates metadata for a single-file model.</summary>
    /// <param name="fileName">Local filename.</param>
    /// <param name="downloadUrl">Download URL.</param>
    public ModelMeta(string fileName, string downloadUrl)
    {
        FileName = fileName;
        DownloadUrl = downloadUrl;
        DownloadType = ModelDownloadType.SingleFile;
    }

    /// <summary>Creates metadata for a directory-based model.</summary>
    /// <param name="downloadUrl">Download URL.</param>
    /// <param name="files">Relative paths of files to download.</param>
    /// <param name="subdirectory">Optional model subdirectory.</param>
    public ModelMeta(string downloadUrl, List<string> files, string? subdirectory = null)
    {
        DownloadUrl = downloadUrl;
        Files = files;
        Subdirectory = subdirectory;
        DownloadType = ModelDownloadType.Directory;
    }

    /// <summary>Creates metadata for an ONNX directory-based model.</summary>
    /// <param name="downloadUrl">Download URL.</param>
    /// <param name="files">Relative paths of files to download.</param>
    /// <param name="onnxModelType">ONNX task type.</param>
    /// <param name="subdirectory">Optional model subdirectory.</param>
    public ModelMeta(string downloadUrl, List<string> files, string onnxModelType, string? subdirectory = null)
    {
        DownloadUrl = downloadUrl;
        Files = files;
        OnnxModelType = onnxModelType;
        Subdirectory = subdirectory;
        DownloadType = ModelDownloadType.OnnxModelDirectory;
    }
}

/// <summary>
/// Registry of model metadata.
/// Loaded from external JSON by <see cref="MetadataJsonLoader"/> at startup;
/// falls back to <see cref="Default"/> when no external configuration is provided.
/// </summary>
public sealed class ModelRegistry
{
    /// <summary>
    /// Built-in default registry, used when external JSON is missing and as the seed file source.
    /// </summary>
    public static ModelRegistry Default { get; } = new(
        BuildDefaultDict(BuildDefaultWhisperModels()),
        BuildDefaultDict(BuildDefaultFasterWhisperModels()),
        BuildDefaultDict(BuildDefaultQwen3AsrModels()),
        BuildDefaultDict(BuildDefaultQwen3ForcedAlignerModels()),
        BuildDefaultDict(BuildDefaultDiarizationModels()),
        BuildDefaultDict(BuildDefaultBertOnnxModels()),
        BuildDefaultDict(BuildDefaultQwen3TtsModels()),
        BuildDefaultDict(BuildDefaultIndexTtsModels()));

    /// <summary>Whisper.cpp single-file models, keyed by model size (tiny/base/.../large).</summary>
    public IReadOnlyDictionary<string, ModelMeta> WhisperModels { get; }
    /// <summary>Faster-Whisper directory models, keyed by model size.</summary>
    public IReadOnlyDictionary<string, ModelMeta> FasterWhisperModels { get; }
    /// <summary>Qwen3-ASR models for CrispASR, keyed by model size.</summary>
    public IReadOnlyDictionary<string, ModelMeta> Qwen3AsrModels { get; }
    /// <summary>Qwen3 forced-alignment models, keyed by model size.</summary>
    public IReadOnlyDictionary<string, ModelMeta> Qwen3ForcedAlignerModels { get; }
    /// <summary>Diarization models, keyed by model name.</summary>
    public IReadOnlyDictionary<string, ModelMeta> DiarizationModels { get; }
    /// <summary>BERT ONNX models, such as NER and sentence embeddings, keyed by model name.</summary>
    public IReadOnlyDictionary<string, ModelMeta> BertOnnxModels { get; }
    /// <summary>Qwen3-TTS models used by the dub command's llama-tts engine, keyed by model size.</summary>
    public IReadOnlyDictionary<string, ModelMeta> Qwen3TtsModels { get; }
    /// <summary>IndexTTS-Rust models used by dub --tts-engine indextts, keyed by model name.</summary>
    public IReadOnlyDictionary<string, ModelMeta> IndexTtsModels { get; }

    /// <summary>Creates a registry from model dictionaries.</summary>
    /// <param name="whisperModels">Whisper.cpp models.</param>
    /// <param name="fasterWhisperModels">Faster-Whisper models.</param>
    /// <param name="qwen3AsrModels">Qwen3-ASR models.</param>
    /// <param name="qwen3ForcedAlignerModels">Qwen3 forced-alignment models.</param>
    /// <param name="diarizationModels">Diarization models.</param>
    /// <param name="bertOnnxModels">BERT ONNX models.</param>
    /// <param name="qwen3TtsModels">Qwen3-TTS models.</param>
    /// <param name="indexttsModels">IndexTTS-Rust models.</param>
    public ModelRegistry(
        IReadOnlyDictionary<string, ModelMeta> whisperModels,
        IReadOnlyDictionary<string, ModelMeta> fasterWhisperModels,
        IReadOnlyDictionary<string, ModelMeta> qwen3AsrModels,
        IReadOnlyDictionary<string, ModelMeta> qwen3ForcedAlignerModels,
        IReadOnlyDictionary<string, ModelMeta> diarizationModels,
        IReadOnlyDictionary<string, ModelMeta> bertOnnxModels,
        IReadOnlyDictionary<string, ModelMeta> qwen3TtsModels,
        IReadOnlyDictionary<string, ModelMeta> indexttsModels)
    {
        WhisperModels = whisperModels ?? throw new ArgumentNullException(nameof(whisperModels));
        FasterWhisperModels = fasterWhisperModels ?? throw new ArgumentNullException(nameof(fasterWhisperModels));
        Qwen3AsrModels = qwen3AsrModels ?? throw new ArgumentNullException(nameof(qwen3AsrModels));
        Qwen3ForcedAlignerModels = qwen3ForcedAlignerModels ?? throw new ArgumentNullException(nameof(qwen3ForcedAlignerModels));
        DiarizationModels = diarizationModels ?? throw new ArgumentNullException(nameof(diarizationModels));
        BertOnnxModels = bertOnnxModels ?? throw new ArgumentNullException(nameof(bertOnnxModels));
        Qwen3TtsModels = qwen3TtsModels ?? throw new ArgumentNullException(nameof(qwen3TtsModels));
        IndexTtsModels = indexttsModels ?? throw new ArgumentNullException(nameof(indexttsModels));
    }

    // ---------- Built-in defaults (formerly hard-coded registry data) ----------

    // Whisper.cpp models (single-file .bin).
    private static Dictionary<string, ModelMeta> BuildDefaultWhisperModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "tiny",
            new ModelMeta("ggml-tiny.bin",
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin")
        },
        {
            "base",
            new ModelMeta("ggml-base.bin",
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-base.bin")
        },
        {
            "small",
            new ModelMeta("ggml-small.bin",
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-small.bin")
        },
        {
            "medium",
            new ModelMeta("ggml-medium.bin",
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin")
        },
        {
            "large",
            new ModelMeta("ggml-large-v3.bin",
                "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin")
        }
    };

    // Faster-Whisper models (directory packages).
    private static Dictionary<string, ModelMeta> BuildDefaultFasterWhisperModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "tiny",
            new ModelMeta("https://hf-mirror.com/Systran/faster-whisper-tiny/resolve/main",
                ["config.json", "model.bin", "vocabulary.txt"])
        },
        {
            "base",
            new ModelMeta("https://hf-mirror.com/Systran/faster-whisper-base/resolve/main",
                ["config.json", "model.bin", "vocabulary.txt"])
        },
        {
            "small",
            new ModelMeta("https://hf-mirror.com/Systran/faster-whisper-small/resolve/main",
                ["config.json", "model.bin", "vocabulary.txt"])
        },
        {
            "medium",
            new ModelMeta("https://hf-mirror.com/Systran/faster-whisper-medium/resolve/main",
                ["config.json", "model.bin", "vocabulary.txt"])
        },
        {
            "large-v3",
            new ModelMeta("https://hf-mirror.com/Systran/faster-whisper-large-v3/resolve/main",
                ["config.json", "model.bin", "vocabulary.json"])
        }
    };

    // Qwen3-ASR models for CrispASR.
    private static Dictionary<string, ModelMeta> BuildDefaultQwen3AsrModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "qwen3-asr-0.6b",
            new ModelMeta(
                fileName: "qwen3-asr-0.6b-q4_k.gguf",
                downloadUrl: "https://hf-mirror.com/cstr/qwen3-asr-0.6b-GGUF/resolve/main/qwen3-asr-0.6b-q4_k.gguf"
            )
        },
        {
            "qwen3-asr-1.7b",
            new ModelMeta(
                fileName: "qwen3-asr-1.7b-q4_k.gguf",
                downloadUrl: "https://hf-mirror.com/cstr/qwen3-asr-1.7b-GGUF/resolve/main/qwen3-asr-1.7b-q4_k.gguf"
            )
        }
    };

    // Qwen3 forced-alignment models (single-file .gguf).
    private static Dictionary<string, ModelMeta> BuildDefaultQwen3ForcedAlignerModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "qwen3-forced-aligner-0.6b",
            new ModelMeta(
                fileName: "qwen3-forced-aligner-0.6b-q4_k.gguf",
                downloadUrl: "https://hf-mirror.com/cstr/qwen3-forced-aligner-0.6b-GGUF/resolve/main/qwen3-forced-aligner-0.6b-q4_k.gguf"
            )
        },
        // Optional Q8_0 variant for higher precision.
        {
            "qwen3-forced-aligner-0.6b-q8_0",
            new ModelMeta(
                fileName: "qwen3-forced-aligner-0.6b-q8_0.gguf",
                downloadUrl: "https://hf-mirror.com/cstr/qwen3-forced-aligner-0.6b-GGUF/resolve/main/qwen3-forced-aligner-0.6b-q8_0.gguf"
            )
        },
        // Optional F16 variant for maximum precision.
        {
            "qwen3-forced-aligner-0.6b-f16",
            new ModelMeta(
                fileName: "qwen3-forced-aligner-0.6b-f16.gguf",
                downloadUrl: "https://hf-mirror.com/cstr/qwen3-forced-aligner-0.6b-GGUF/resolve/main/qwen3-forced-aligner-0.6b-f16.gguf"
            )
        }
    };

    // Diarization models (sherpa-onnx).
    private static Dictionary<string, ModelMeta> BuildDefaultDiarizationModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "voxceleb_resnet293_LM", new ModelMeta("voxceleb_resnet293_LM.onnx",
                "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_resnet293_LM.onnx")
        }
    };

    private static Dictionary<string, ModelMeta> BuildDefaultBertOnnxModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["bert-base-ner"] = new ModelMeta(
            "https://hf-mirror.com/optimum/bert-base-NER/resolve/main",
            ["model.onnx", "config.json", "tokenizer.json", "vocab.txt"],
            onnxModelType: "token_classification"),
        ["all-minilm-l6-v2"] = new ModelMeta(
            "https://hf-mirror.com/Xenova/all-MiniLM-L6-v2/resolve/main",
            ["onnx/model.onnx", "config.json", "tokenizer.json", "vocab.txt"],
            onnxModelType: "embedding")
    };

    // Qwen3-TTS model for llama-tts: 1.7B Base GGUF (talker + mmproj, directory package).
    private static Dictionary<string, ModelMeta> BuildDefaultQwen3TtsModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "1.7b-base-q4",
            new ModelMeta(
                "https://hf-mirror.com/ggml-org/Qwen3-TTS-12Hz-1.7B-Base-GGUF/resolve/main",
                ["Qwen3-TTS-12Hz-1.7B-Base-Q4_K_M.gguf", "mmproj-Qwen3-TTS-12Hz-1.7B-Base-Q8_0.gguf"])
        }
    };

    // IndexTTS-Rust directory model: official pre-converted ONNX with external .data weights; GPT/S2Mel ONNX files are unpublished and must be converted manually.
    private static Dictionary<string, ModelMeta> BuildDefaultIndexTtsModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "indextts2",
            new ModelMeta(
                "https://hf-mirror.com/ThreadAbort/IndexTTS-Rust/resolve/models/models",
                ["bigvgan.onnx", "bigvgan.onnx.data", "speaker_encoder.onnx", "speaker_encoder.onnx.data"])
        }
    };

    private static IReadOnlyDictionary<string, ModelMeta> BuildDefaultDict(Dictionary<string, ModelMeta> source) =>
        new Dictionary<string, ModelMeta>(source, StringComparer.OrdinalIgnoreCase);
}
