using System.Threading;
using System.Threading.Tasks;

namespace Centurion.Core.Abstractions;

/// <summary>
/// 模型路径解析器。
/// 负责根据模型名称返回本地磁盘上的模型路径（目录或文件），
/// 若模型不存在则自动触发下载。
/// </summary>
public interface IModelPathResolver
{
    /// <summary>
    /// 获取 Whisper 转录模型的目录路径。
    /// </summary>
    /// <param name="modelName">模型名称（tiny/base/small/medium/large-v3）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>模型目录路径</returns>
    Task<string> GetWhisperModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取说话人分割模型的文件路径。
    /// </summary>
    /// <param name="modelName">模型名称（如 voxceleb_resnet293_LM）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>ONNX 模型文件路径</returns>
    Task<string> GetDiarizationModelPathAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取强制对齐模型的目录路径。
    /// </summary>
    /// <param name="modelName">模型名称（如 wav2vec2-base-960h）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>模型目录路径</returns>
    Task<string> GetAlignmentModelPathAsync(string modelName, CancellationToken cancellationToken = default);
}