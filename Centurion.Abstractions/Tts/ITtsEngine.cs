using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Abstractions.Tts;

/// <summary>
/// 文本转语音引擎抽象：给定目标文本、参考音频与语言，合成一段 wav。
/// 具体实现可基于本地 CLI（llama-tts）或远程 API，管道算子不感知后端差异。
/// </summary>
public interface ITtsEngine
{
    /// <summary>引擎名称（如 "llama"）。</summary>
    string EngineName { get; }

    /// <summary>
    /// 合成单句语音。
    /// </summary>
    /// <param name="text">目标语言文本（去除多余空白）。</param>
    /// <param name="referenceAudioPath">说话人参考音频路径；为空时使用引擎默认音色。</param>
    /// <param name="language">目标语言（ISO 639-1，如 zh）。</param>
    /// <param name="outputWavPath">输出 wav 文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>合成音频时长（秒）；失败时抛出 TtsSynthesisException。</returns>
    Task<double> SynthesizeAsync(string text, string? referenceAudioPath, string language, string outputWavPath, CancellationToken cancellationToken);
}

/// <summary>TTS 合成失败时抛出的异常（携带原因，供上层记录 Warning 后跳过该句）。</summary>
public sealed class TtsSynthesisException(string message, Exception? inner = null) : Exception(message, inner);
