// Centurion.Core/Abstractions/IModelPathResolver.cs

namespace Centurion.Abstractions;

/// <summary>
/// 模型路径解析器。
/// 负责根据模型名称返回本地磁盘上的模型路径（目录或文件），
/// 若模型不存在则自动触发下载。
/// </summary>
public interface IModelPathResolver
{
    /// <summary>
    /// 获取 Whisper.cpp 模型的 .bin 文件路径。
    /// </summary>
    /// <param name="modelName">模型名称（tiny/base/small/medium/large）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>.bin 文件完整路径</returns>
    Task<string> GetWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 Faster‑Whisper 模型的目录路径。
    /// </summary>
    /// <param name="modelName">模型名称（tiny/base/small/medium/large-v3）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>模型目录路径</returns>
    Task<string> GetFasterWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 Qwen3‑ASR.cpp 模型的 .gguf 文件路径。
    /// </summary>
    /// <param name="modelName">模型名称（如 qwen3-asr-0.6b）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>.gguf 文件完整路径</returns>
    Task<string> GetQwen3AsrModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取说话人分割模型的文件路径。
    /// </summary>
    Task<string> GetDiarizationModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取强制对齐模型的目录路径。
    /// </summary>
    Task<string> GetQwen3ForcedAlignerPathAsync(string modelName, CancellationToken cancellationToken = default);
}