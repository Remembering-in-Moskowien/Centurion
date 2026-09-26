using System.Text;
using Centurion.Core.Processing.Text;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using WeCantSpell.Hunspell;
using Centurion.Core.Utils.Infrastructure;
namespace Centurion.Core.Processing.SpellCheck;

/// <summary>
/// Hunspell 拼写检查器：按需加载/下载词典（默认 en_US，来自 LibreOffice 词典仓库），
/// 对字幕句文本分词后逐词检查，过滤 CJK、数字与纯符号词，返回可疑词与候选建议。
/// </summary>
public sealed class HunspellSpellChecker(ILogger<HunspellSpellChecker> logger)
{
    private const string DictionaryUrlBase = "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/";

    private static readonly char[] Ignored = ['\'', '-'];

    /// <summary>
    /// 检查一句文本的拼写。
    /// </summary>
    /// <param name="sentenceIndex">句子序号（用于结果定位）。</param>
    /// <param name="text">句子文本。</param>
    /// <param name="wordList">已加载的 Hunspell 词表。</param>
    /// <returns>拼写疑点列表；无嫌疑词时为空列表。</returns>
    public static IReadOnlyList<SpellCheckIssue> CheckSentence(int sentenceIndex, string text, WordList wordList)
    {
        var issues = new List<SpellCheckIssue>();
        foreach (var token in Tokenizer.Tokenize(text))
        {
            if (!IsCheckable(token))
                continue;

            if (wordList.Check(token))
                continue;

            issues.Add(new SpellCheckIssue
            {
                SentenceIndex = sentenceIndex,
                Word = token,
                Context = text,
                Suggestions = wordList.Suggest(token).Take(5).ToList()
            });
        }
        return issues;
    }

    /// <summary>
    /// 确保指定前缀的词典可用：本地 tools/hunspell/{prefix}.dic|.aff 已存在则加载；
    /// 缺失且前缀为 en_US 时自动下载；其余前缀缺失时记录警告并返回 null。
    /// </summary>
    /// <param name="prefix">词典前缀（如 en_US）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加载的 Hunspell 实例；不可用时为 null。</returns>
    public async Task<WordList?> EnsureDictionaryAsync(string prefix, CancellationToken cancellationToken)
    {
        var baseDir = Path.Combine(AppContext.BaseDirectory, "tools", "hunspell");
        var dicPath = Path.Combine(baseDir, $"{prefix}.dic");
        var affPath = Path.Combine(baseDir, $"{prefix}.aff");

        if (!File.Exists(dicPath) || !File.Exists(affPath))
        {
            if (!prefix.Equals("en_US", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Hunspell dictionary '{Prefix}' not found in {Directory}; spell check skipped.", prefix, baseDir);
                return null;
            }

            logger.LogInformation("Hunspell dictionary not found; downloading en_US from LibreOffice dictionaries...");
            Directory.CreateDirectory(baseDir);
            try
            {
                await DownloadAsync($"{DictionaryUrlBase}en_US.dic", dicPath, logger, cancellationToken);
                await DownloadAsync($"{DictionaryUrlBase}en_US.aff", affPath, logger, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Hunspell dictionary download failed: {Reason}", ex.Message);
                return null;
            }
        }

        try
        {
            return WordList.CreateFromFiles(dicPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to load Hunspell dictionary '{Prefix}': {Reason}", prefix, ex.Message);
            return null;
        }
    }

    private static bool IsCheckable(string token)
    {
        if (token.Length == 0)
            return false;
        // 纯 CJK/非拉丁词（如中文单字）：跳过
        if (token.Any(ch => ch is > '\u2E80' and < '\uA000'))
            return false;
        // 数字/含数字/纯符号：跳过
        if (token.Any(char.IsDigit))
            return false;
        if (token.All(ch => !char.IsLetter(ch)))
            return false;
        return true;
    }

    private static async Task DownloadAsync(
        string url, string destination, ILogger logger, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/1.0");
        await GitHubDownloadProxy.DownloadWithFallbackAsync(
            url,
            async candidate =>
            {
                var bytes = await client.GetByteArrayAsync(candidate, cancellationToken);
                await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
            },
            IsNetworkFailure,
            reason => logger.LogWarning("Hunspell dictionary mirror failed ({Reason}); trying next candidate...", reason),
            cancellationToken);
    }

    private static bool IsNetworkFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

}
