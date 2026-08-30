// Centurion.Core/Models/Metadata/ModelRegistry.cs

namespace Centurion.Core.Models.Metadata;

public enum ModelDownloadType
{
    SingleFile,
    Directory
}

public record ModelMeta
{
    public string? FileName { get; init; }
    public string? DownloadUrl { get; init; }
    public ModelDownloadType DownloadType { get; init; } = ModelDownloadType.SingleFile;
    public List<string>? Files { get; init; }
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
}

public static class ModelRegistry
{
    // ---------- Whisper.cpp 模型（单文件 .bin） ----------
    public static IReadOnlyDictionary<string, ModelMeta> WhisperModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "tiny",
                new ModelMeta("ggml-tiny.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin")
            },
            {
                "base",
                new ModelMeta("ggml-base.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin")
            },
            {
                "small",
                new ModelMeta("ggml-small.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin")
            },
            {
                "medium",
                new ModelMeta("ggml-medium.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin")
            },
            {
                "large",
                new ModelMeta("ggml-large-v3.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin")
            }
        };

    // ---------- Faster‑Whisper 模型（目录） ----------
    public static IReadOnlyDictionary<string, ModelMeta> FasterWhisperModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
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
    
    // ---------- Qwen3-ASR 模型（用于 CrispASR） ----------
    public static IReadOnlyDictionary<string, ModelMeta> Qwen3AsrModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
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
    
    // ---------- Qwen3 强制对齐模型（单文件 .gguf） ----------
    public static IReadOnlyDictionary<string, ModelMeta> Qwen3ForcedAlignerModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
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

    // ---------- 说话人分割 (sherpa-onnx) ----------
    public static IReadOnlyDictionary<string, ModelMeta> DiarizationModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "voxceleb_resnet293_LM", new ModelMeta("voxceleb_resnet293_LM.onnx",
                    "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_resnet293_LM.onnx")
            }
        };

    // ---------- Wav2Vec2 CTC 对齐模型（目录） ----------
    public static IReadOnlyDictionary<string, ModelMeta> Wav2Vec2Models { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "wav2vec2-base-960h", new ModelMeta(
                    "https://hf-mirror.com/onnx-community/wav2vec2-base-960h-ONNX/resolve/main/onnx",
                    ["model.onnx"])
            }
        };
}