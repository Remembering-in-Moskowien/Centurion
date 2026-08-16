// Centurion.Core/Models/ModelRegistry.cs

namespace Centurion.Core.Models;

public static class ModelRegistry
{
    // ---------- Whisper 模型 ----------
    public static IReadOnlyDictionary<string, ModelMeta> WhisperModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "tiny",
                new ModelMeta("ggml-tiny.bin",
                    "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
                    "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21")
            },
            {
                "base",
                new ModelMeta("ggml-base.bin",
                    "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
                    "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe")
            },
            {
                "small",
                new ModelMeta("ggml-small.bin",
                    "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
                    "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b")
            },
            {
                "medium",
                new ModelMeta("ggml-medium.bin",
                    "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
                    "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208")
            },
            {
                "large",
                new ModelMeta("ggml-large-v3.bin",
                    "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
                    "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2")
            }
        };

    // ---------- Silero VAD 模型 ----------
    public static IReadOnlyDictionary<string, ModelMeta> VadModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "silero_vad.onnx",
                new ModelMeta("silero_vad.onnx",
                    "https://hf-mirror.com/runanywhere/silero-vad-v5/resolve/main/silero_vad.onnx",
                    "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3")
            }
        };

    // ---------- WeSpeaker 模型 ----------
    public static IReadOnlyDictionary<string, ModelMeta> WespeakerModels { get; } =
        new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "wespeaker-resnet34-lm",
                new ModelMeta("wespeaker_resnet34_LM.onnx",
                    "https://wespeaker-1256283475.cos.ap-shanghai.myqcloud.com/models/voxceleb/voxceleb_resnet34_LM.onnx",
                    "7bb2f06e9df17cdf1ef14ee8a15ab08ed28e8d0ef5054ee135741560df2ec068")
            }
        };
}