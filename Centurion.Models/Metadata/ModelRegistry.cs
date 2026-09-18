// Centurion.Core/Models/Metadata/ModelRegistry.cs

namespace Centurion.Models.Metadata;

public enum ModelDownloadType
{
    SingleFile,
    Directory,
    OnnxModelDirectory
}

public record ModelMeta
{
    public string? FileName { get; init; }
    public string? DownloadUrl { get; init; }
    public ModelDownloadType DownloadType { get; init; } = ModelDownloadType.SingleFile;
    public List<string>? Files { get; init; }
    public string? OnnxModelType { get; init; }
    public string? Subdirectory { get; init; }

    public ModelMeta(string fileName, string downloadUrl)
    {
        FileName = fileName;
        DownloadUrl = downloadUrl;
        DownloadType = ModelDownloadType.SingleFile;
    }

    public ModelMeta(string downloadUrl, List<string> files, string? subdirectory = null)
    {
        DownloadUrl = downloadUrl;
        Files = files;
        Subdirectory = subdirectory;
        DownloadType = ModelDownloadType.Directory;
    }

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
/// 模型元数据注册表。
/// 实例化对象，由 <see cref="MetadataJsonLoader"/> 在程序启动时从外部 JSON 加载；
/// 未提供外部配置时回退到 <see cref="Default"/>（内置默认条目）。
/// </summary>
public sealed class ModelRegistry
{
    /// <summary>
    /// 内置默认注册表（外部 JSON 缺失时的回退值，也是种子文件的内容来源）。
    /// </summary>
    public static ModelRegistry Default { get; } = new(
        BuildDefaultDict(BuildDefaultWhisperModels()),
        BuildDefaultDict(BuildDefaultFasterWhisperModels()),
        BuildDefaultDict(BuildDefaultQwen3AsrModels()),
        BuildDefaultDict(BuildDefaultQwen3ForcedAlignerModels()),
        BuildDefaultDict(BuildDefaultDiarizationModels()),
        BuildDefaultDict(BuildDefaultBertOnnxModels()));

    public IReadOnlyDictionary<string, ModelMeta> WhisperModels { get; }
    public IReadOnlyDictionary<string, ModelMeta> FasterWhisperModels { get; }
    public IReadOnlyDictionary<string, ModelMeta> Qwen3AsrModels { get; }
    public IReadOnlyDictionary<string, ModelMeta> Qwen3ForcedAlignerModels { get; }
    public IReadOnlyDictionary<string, ModelMeta> DiarizationModels { get; }
    public IReadOnlyDictionary<string, ModelMeta> BertOnnxModels { get; }

    public ModelRegistry(
        IReadOnlyDictionary<string, ModelMeta> whisperModels,
        IReadOnlyDictionary<string, ModelMeta> fasterWhisperModels,
        IReadOnlyDictionary<string, ModelMeta> qwen3AsrModels,
        IReadOnlyDictionary<string, ModelMeta> qwen3ForcedAlignerModels,
        IReadOnlyDictionary<string, ModelMeta> diarizationModels,
        IReadOnlyDictionary<string, ModelMeta> bertOnnxModels)
    {
        WhisperModels = whisperModels ?? throw new ArgumentNullException(nameof(whisperModels));
        FasterWhisperModels = fasterWhisperModels ?? throw new ArgumentNullException(nameof(fasterWhisperModels));
        Qwen3AsrModels = qwen3AsrModels ?? throw new ArgumentNullException(nameof(qwen3AsrModels));
        Qwen3ForcedAlignerModels = qwen3ForcedAlignerModels ?? throw new ArgumentNullException(nameof(qwen3ForcedAlignerModels));
        DiarizationModels = diarizationModels ?? throw new ArgumentNullException(nameof(diarizationModels));
        BertOnnxModels = bertOnnxModels ?? throw new ArgumentNullException(nameof(bertOnnxModels));
    }

    // ---------- 内置默认条目（原硬编码注册数据） ----------

    // Whisper.cpp 模型（单文件 .bin）
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

    // Faster‑Whisper 模型（目录）
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

    // Qwen3-ASR 模型（用于 CrispASR）
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

    // Qwen3 强制对齐模型（单文件 .gguf）
    private static Dictionary<string, ModelMeta> BuildDefaultQwen3ForcedAlignerModels() => new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "qwen3-forced-aligner-0.6b",
            new ModelMeta(
                fileName: "qwen3-forced-aligner-0.6b-q4_k.gguf",
                downloadUrl: "https://hf-mirror.com/cstr/qwen3-forced-aligner-0.6b-GGUF/resolve/main/qwen3-forced-aligner-0.6b-q4_k.gguf"
            )
        },
        // 可选的 Q8_0 版本（更高精度）
        {
            "qwen3-forced-aligner-0.6b-q8_0",
            new ModelMeta(
                fileName: "qwen3-forced-aligner-0.6b-q8_0.gguf",
                downloadUrl: "https://hf-mirror.com/cstr/qwen3-forced-aligner-0.6b-GGUF/resolve/main/qwen3-forced-aligner-0.6b-q8_0.gguf"
            )
        },
        // 可选的 F16 版本（最高精度）
        {
            "qwen3-forced-aligner-0.6b-f16",
            new ModelMeta(
                fileName: "qwen3-forced-aligner-0.6b-f16.gguf",
                downloadUrl: "https://hf-mirror.com/cstr/qwen3-forced-aligner-0.6b-GGUF/resolve/main/qwen3-forced-aligner-0.6b-f16.gguf"
            )
        }
    };

    // 说话人分割 (sherpa-onnx)
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

    private static IReadOnlyDictionary<string, ModelMeta> BuildDefaultDict(Dictionary<string, ModelMeta> source) =>
        new Dictionary<string, ModelMeta>(source, StringComparer.OrdinalIgnoreCase);
}
