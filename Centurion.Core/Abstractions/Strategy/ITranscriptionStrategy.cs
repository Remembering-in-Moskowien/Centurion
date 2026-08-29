using Centurion.Core.Models;

namespace Centurion.Core.Abstractions.Strategy;

/// <summary>
/// Whisper 转录策略接口，支持不同的转录引擎（本地、API、CLI 等）
/// </summary>
public interface ITranscriptionStrategy
{
    /// <summary>
    /// 执行转录，返回词级时间戳列表
    /// </summary>
    /// <param name="audioPath">输入音频文件路径（已转换为 WAV 16kHz 单声道）</param>
    /// <param name="language">语言代码，如 "en", "zh"</param>
    /// <param name="modelName">模型名称（如 tiny/base/small）</param>
    /// <param name="initialPrompt">可选的初始提示词</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>词列表，包含文本、起止时间（毫秒）</returns>
    Task<List<Word>> TranscribeAsync(
        string audioPath,
        string language,
        string modelName,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 策略名称，用于日志
    /// </summary>
    string StrategyName { get; }
}