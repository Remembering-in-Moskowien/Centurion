// Centurion.Core/Models/ModelRegistry.cs
using System.Collections.Generic;

namespace Centurion.Core.Metadata;

public static class ModelRegistry
{
    // ---------- Whisper 模型（单文件，用于 whisper-cli，保留） ----------
    public static IReadOnlyDictionary<string, ModelMeta> WhisperModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "tiny",
                new ModelMeta(
                    "ggml-tiny.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin"
                )
            },
            {
                "base",
                new ModelMeta(
                    "ggml-base.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"
                )
            },
            {
                "small",
                new ModelMeta(
                    "ggml-small.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"
                )
            },
            {
                "medium",
                new ModelMeta(
                    "ggml-medium.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin"
                )
            },
            {
                "large",
                new ModelMeta(
                    "ggml-large-v3.bin",
                    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin"
                )
            }
        };

    // ---------- FasterWhisper 模型（目录模型，用于 FasterWhisper.NET） ----------
    public static IReadOnlyDictionary<string, ModelMeta> FasterWhisperModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "tiny",
                new ModelMeta(
                    "https://hf-mirror.com/Systran/faster-whisper-tiny/resolve/main",
                    new List<string> { "config.json", "model.bin", "vocabulary.txt" }
                )
            },
            {
                "base",
                new ModelMeta(
                    "https://hf-mirror.com/Systran/faster-whisper-base/resolve/main",
                    new List<string> { "config.json", "model.bin", "vocabulary.txt" }
                )
            },
            {
                "small",
                new ModelMeta(
                    "https://hf-mirror.com/Systran/faster-whisper-small/resolve/main",
                    new List<string> { "config.json", "model.bin", "vocabulary.txt" }
                )
            },
            {
                "medium",
                new ModelMeta(
                    "https://hf-mirror.com/Systran/faster-whisper-medium/resolve/main",
                    new List<string> { "config.json", "model.bin", "vocabulary.txt" }
                )
            },
            {
                "large-v3",
                new ModelMeta(
                    "https://hf-mirror.com/Systran/faster-whisper-large-v3/resolve/main",
                    new List<string> { "config.json", "model.bin", "vocabulary.txt" }
                )
            }
        };

    // ---------- 说话人分割 (sherpa-onnx) 模型 ----------
    public static IReadOnlyDictionary<string, ModelMeta> DiarizationModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "voxceleb_resnet293_LM",
                new ModelMeta(
                    "voxceleb_resnet293_LM.onnx",
                    "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_resnet293_LM.onnx"
                )
            }
        };
}