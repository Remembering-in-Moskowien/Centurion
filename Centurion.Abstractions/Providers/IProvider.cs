using Centurion.Models.Providers;
namespace Centurion.Abstractions.Providers;

/// <summary>
/// 全部 Provider 的公共契约：名称、能力声明与可用性探测。
/// 每个推理域（ASR/OCR/LLM/TTS/Diarization/VocalSeparation）的 Provider
/// 继承本接口并按域扩展执行方法。
/// </summary>
public interface IProvider
{
    /// <summary>稳定标识（如 "whispercpp"、"openai"、"zhipu"），用于配置与命令。</summary>
    string Name { get; }

    /// <summary>人类可读显示名（用于 providers list 等）。</summary>
    string DisplayName { get; }

    /// <summary>能力声明：本地/云、语言、GPU、成本、延迟、质量档位。</summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// 探测当前是否可用：云端 Provider 检查 API 密钥与端点；本地 Provider 检查工具/模型是否就位。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可用返回 true；不可用返回 false（不抛异常）。</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 语音识别（ASR）Provider：给定音频返回词级时间戳。
/// </summary>
public interface IAsrProvider : IProvider
{
    /// <summary>
    /// 转录音频。
    /// </summary>
    /// <param name="audioPath">输入音频文件路径（WAV 16kHz 单声道）。</param>
    /// <param name="language">语言代码（en/zh 等）。</param>
    /// <param name="model">模型名；为空时使用 Provider 默认。</param>
    /// <param name="initialPrompt">可选的初始提示词。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>词级结果与用量统计。</returns>
    Task<ProviderResult<IReadOnlyList<Centurion.Models.Word>>> TranscribeAsync(
        string audioPath,
        string language,
        string? model,
        string? initialPrompt,
        CancellationToken cancellationToken);
}

/// <summary>
/// 光学字符识别（OCR）Provider：对图片提取字幕文本。
/// </summary>
public interface IOcrProvider : IProvider
{
    /// <summary>
    /// 对单张图片执行 OCR。
    /// </summary>
    /// <param name="imagePath">图片文件路径。</param>
    /// <param name="model">模型名；为空时使用 Provider 默认。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提取的字幕文本（无字幕时可能返回占位文本）。</returns>
    Task<ProviderResult<string>> OcrImageAsync(
        string imagePath,
        string? model,
        CancellationToken cancellationToken);
}

/// <summary>LLM 对话消息。</summary>
/// <param name="Role">角色（system/user/assistant）。</param>
/// <param name="Content">消息内容。</param>
public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// 大语言模型（LLM）Provider：统一 chat/completions 抽象。
/// </summary>
public interface ILlmProvider : IProvider
{
    /// <summary>
    /// 发送一轮对话并返回助手回复文本。
    /// </summary>
    /// <param name="messages">对话消息列表。</param>
    /// <param name="model">模型名；为空时使用 Provider 默认。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回复文本与用量统计（token/成本）。</returns>
    Task<ProviderResult<string>> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string? model,
        CancellationToken cancellationToken);
}

/// <summary>
/// 文本转语音（TTS）Provider：合成单句语音。
/// </summary>
public interface ITtsProvider : IProvider
{
    /// <summary>
    /// 合成单句语音。
    /// </summary>
    /// <param name="text">目标语言文本。</param>
    /// <param name="referenceAudioPath">说话人参考音频路径；为空时使用默认音色。</param>
    /// <param name="language">目标语言（ISO 639-1）。</param>
    /// <param name="outputWavPath">输出 wav 路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>合成音频时长（秒）与用量统计。</returns>
    Task<ProviderResult<double>> SynthesizeAsync(
        string text,
        string? referenceAudioPath,
        string language,
        string outputWavPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// 说话人分割（Diarization）Provider：给定音频返回说话人时间片段。
/// </summary>
public interface IDiarizationProvider : IProvider
{
    /// <summary>
    /// 对音频执行说话人分割。
    /// </summary>
    /// <param name="audioPath">预处理后的音频路径。</param>
    /// <param name="numSpeakers">预期说话人数（0 = 自动）。</param>
    /// <param name="segmentModel">分割模型名；为空使用后端默认。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>说话人片段列表与用量统计。</returns>
    Task<ProviderResult<IReadOnlyList<Centurion.Abstractions.Strategy.SpeakerSegment>>> DiarizeAsync(
        string audioPath,
        int numSpeakers,
        string? segmentModel,
        CancellationToken cancellationToken);
}

/// <summary>
/// 人声分离（Vocal Separation）Provider：从音频中分离人声轨。
/// </summary>
public interface IVocalSeparationProvider : IProvider
{
    /// <summary>
    /// 分离人声轨。
    /// </summary>
    /// <param name="audioPath">输入音频路径。</param>
    /// <param name="outputWavPath">输出人声 wav 路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际人声轨路径与用量统计。</returns>
    Task<ProviderResult<string>> SeparateVocalsAsync(
        string audioPath,
        string outputWavPath,
        CancellationToken cancellationToken);
}
