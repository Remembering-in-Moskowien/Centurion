using Centurion.Models;

namespace Centurion.Abstractions.Strategy;

/// <summary>
/// 分句策略：将词级时间戳序列切分为适合展示的句子列表。
/// </summary>
public interface ISentenceSplitStrategy
{
    /// <summary>
    /// 按指定配置将单词列表切分为句子。
    /// </summary>
    /// <param name="words">带起止时间戳的单词列表。</param>
    /// <param name="options">分句配置参数。</param>
    /// <returns>切分后的句子列表。</returns>
    Task<List<Sentence>> Split(List<Word> words, SplitOptions options);
}
