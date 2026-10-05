using System.Text;
using Centurion.Core.Processing.Text;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using WeCantSpell.Hunspell;
using Centurion.Core.Utils.Infrastructure;
namespace Centurion.Core.Processing.SpellCheck;

/// <summary>
/// Hunspell spell checker: loads/downloads dictionaries on demand (default en_US, from the LibreOffice
/// dictionaries repository), tokenizes subtitle sentence text and checks it word by word, filtering out
/// CJK, digits and pure-symbol tokens, and returns suspect words with candidate suggestions.
/// </summary>
public sealed class HunspellSpellChecker(ILogger<HunspellSpellChecker> logger)
{
    private const string DictionaryUrlBase = "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/";

    private static readonly char[] Ignored = ['\'', '-'];

    /// <summary>
    /// Checks the spelling of one sentence's text.
    /// </summary>
    /// <param name="sentenceIndex">The sentence index (used to locate results).</param>
    /// <param name="text">The sentence text.</param>
    /// <param name="wordList">The loaded Hunspell word list.</param>
    /// <returns>The list of spelling issues; an empty list when no words are suspect.</returns>
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
    /// Ensures the dictionary for the given prefix is available: loads the local tools/hunspell/{prefix}.dic|.aff
    /// if present; auto-downloads when missing and the prefix is en_US; logs a warning and returns null for any other missing prefix.
    /// </summary>
    /// <param name="prefix">The dictionary prefix (e.g. en_US).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The loaded Hunspell instance; null when unavailable.</returns>
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
        // Pure CJK / non-Latin tokens (e.g. single Chinese characters): skip
        if (token.Any(ch => ch is > '\u2E80' and < '\uA000'))
            return false;
        // Digits / digit-containing / pure-symbol tokens: skip
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
