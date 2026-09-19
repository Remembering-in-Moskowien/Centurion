using Centurion.Models;

namespace Centurion.Abstractions.Strategy;

/// <summary>
/// 翻译策略契约：把给定句子的文本翻译到目标语言并填充 <see cref="Sentence.TranslatedText"/>，
/// 保持每句的时间轴与词级明细不变（只做文本层翻译对齐）。
/// </summary>
public interface ITranslationStrategy
{
    /// <summary>策略的显示名称。</summary>
    string StrategyName { get; }

    /// <summary>
    /// 执行翻译：逐句（或分批）把源文本翻译到目标语言，写回 <see cref="Sentence.TranslatedText"/>。
    /// </summary>
    /// <param name="sentences">待翻译的句子列表（时间轴保持不变，就地填充译文）。</param>
    /// <param name="options">翻译选项：目标语言、术语表、目标语言台本等。</param>
    /// <param name="cancellationToken">用于取消翻译过程的取消标记。</param>
    /// <returns>翻译完成后的句子列表。</returns>
    Task<List<Sentence>> TranslateAsync(
        List<Sentence> sentences,
        TranslationOptions options,
        CancellationToken cancellationToken = default);
}
