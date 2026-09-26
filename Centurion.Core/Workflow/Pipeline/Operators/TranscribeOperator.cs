using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Providers;
using Centurion.Core.Providers;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 转录算子：沿已解析的 ASR fallback 链执行语音识别。
/// 链由 <see cref="Providers.ProviderFactory"/> 在组装时解析（本地/云互备；
/// 无 API 密钥或云端失败时自动回退本地）；用量（token/音频秒/估算成本）聚合进
/// <see cref="WorkflowState.ProviderUsages"/>。
/// </summary>
public class TranscribeOperator(
    IReadOnlyList<IAsrProvider> chain,
    IProviderFactory providerFactory,
    ILogger<TranscribeOperator> logger) : PipelineOperatorBase<TranscribeOperator>(logger)
{
    private readonly IReadOnlyList<IAsrProvider> _chain = chain;
    private readonly IProviderFactory _providerFactory = providerFactory;
    private readonly ProviderPolicies _policies = new(providerFactory.Policies);

    /// <inheritdoc />
    public override string Name => "Transcribe";

    /// <summary>
    /// 转录：沿链执行，自动跳过不可用/失败的 Provider 并切换备用。
    /// </summary>
    /// <param name="context">字幕工作流上下文（输入音频、配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="NotSupportedException">转录引擎未被任何 Provider 支持（组装期已拦截）。</exception>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;
        var inputPath = context.State.VocalsPath
            ?? context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;

        if (_chain.Count == 0)
            throw new NotSupportedException($"No ASR provider available for engine '{config.TranscriberEngine}'.");

        var providerResult = await ProviderChain.ExecuteAsync(
            _chain,
            (provider, ct) => ((IAsrProvider)provider).TranscribeAsync(
                inputPath, config.Language, config.TranscriberModel, config.InitialPrompt, ct),
            _policies,
            _providerFactory.Policies.BudgetUsdPerRun,
            Logger,
            cancellationToken);

        var usage = providerResult.Usage with
        {
            AudioSeconds = await ProbeAudioSecondsAsync(inputPath, cancellationToken)
        };
        context.State.ProviderUsages.Add(usage);

        var words = providerResult.Value;
        // VAD 聚合轴 → 源时间轴还原（词在聚合语音轴上，按段映射回原曲时间）
        if (context.State.VoiceSegments is { Count: > 0 } segMap)
            words = MapWordsToSource(words, segMap);

        if (context.State.TranscribeSentences.Count == 0)
            context.State.TranscribeSentences = GroupIntoSentences(DeduplicateWordRepeats(words));
        context.State.CurrentSentences = [.. context.State.TranscribeSentences];
        context.State.IsTranscribed = true;

        OnProgress(100, "Transcription completed.");
        LogInfo($"Transcribed {context.State.TranscribeSentences.Count} sentences " +
                $"via {usage.ProviderName} (est. ${usage.EstimatedCostUsd:F4}).");
    }

    /// <summary>
    /// 把聚合轴上的词时间戳还原回源音频时间轴。
    /// VAD 聚合把语音段拼接（段间含静音缓冲），词在聚合轴上的位置需按段映射：
    /// 源时间 = 段源起点 + (聚合时间 - 段聚合起点)。词按聚合轴升序，用游标线性定位。
    /// </summary>
    /// <param name="words">聚合轴上的转录词流（provider 原始输出）。</param>
    /// <param name="map">VAD 语音段映射（含 <see cref="VoiceSegment.AggStartMs"/>）。</param>
    /// <returns>还原到源时间轴的词流。</returns>
    internal static List<Word> MapWordsToSource(IReadOnlyList<Word> words, IReadOnlyList<VoiceSegment> map)
    {
        if (map.Count == 0)
            return [.. words];

        var result = new List<Word>(words.Count);
        var segIndex = 0;
        foreach (var w in words)
        {
            while (segIndex < map.Count - 1 && w.Start >= map[segIndex + 1].AggStartMs)
                segIndex++;
            if (segIndex >= map.Count)
                segIndex = map.Count - 1;

            var seg = map[segIndex];
            var offset = Math.Max(0, w.Start - seg.AggStartMs);
            result.Add(new Word
            {
                Text = w.Text,
                Start = seg.StartMs + offset,
                End = seg.StartMs + offset + (w.End - w.Start),
                Speaker = w.Speaker,
                PosTag = w.PosTag,
                Confidence = w.Confidence,
                Status = w.Status
            });
        }
        return result;
    }

    /// <summary>
    /// 去除词流中相邻完全重复的词（文本相同且起止时间戳完全一致）。
    /// CrispASR 的 qwen3 后端在分段解码时会把句首 token 重复发射一次
    /// （同文本同时间戳，实测几乎每句首词都重复，导致字幕每句前多出一个词）；
    /// 真实语音中不存在两个时间戳完全一致的词，故此规则安全，不会误删叠词/叠句。
    /// </summary>
    /// <param name="words">转录词流（provider 原始输出）。</param>
    /// <returns>去重后的词流。</returns>
    internal static List<Word> DeduplicateWordRepeats(IReadOnlyList<Word> words)
    {
        var result = new List<Word>(words.Count);
        Word? prev = null;
        foreach (var word in words)
        {
            if (prev is not null
                && prev.Start == word.Start
                && prev.End == word.End
                && string.Equals(prev.Text, word.Text, StringComparison.Ordinal))
            {
                continue;
            }
            result.Add(word);
            prev = word;
        }
        return result;
    }

    /// <summary>把词级结果按句分组（按标点启发式切句；无标点时整段为一句），句级置信度为词级均值。</summary>
    internal static List<Sentence> GroupIntoSentences(IReadOnlyList<Word> words)
    {
        var sentences = new List<Sentence>();
        if (words.Count == 0)
            return sentences;

        Sentence? current = null;
        foreach (var word in words)
        {
            if (current is null)
            {
                // 首词直接建立句子（Words 初始即含该词）；此前在 ??= 后又 Add 一次，
                // 导致每句首词双加（字幕每句前多出一个词），已修复。
                current = new Sentence
                {
                    Start = word.Start,
                    End = word.End,
                    Text = word.Text,
                    Words = [word]
                };
            }
            else
            {
                current.Words.Add(word);
                current.Text = string.Concat(current.Words.Select(w => w.Text));
                current.End = word.End;
            }
            if (IsSentenceBoundary(word.Text))
            {
                sentences.Add(current);
                current = null;
            }
        }
        if (current is not null)
            sentences.Add(current);

        foreach (var sentence in sentences)
            sentence.Confidence = AggregateConfidence(sentence.Words);
        return sentences;
    }

    /// <summary>词级置信度聚合为句级（平均；全部为 null 时返回 null）。</summary>
    internal static double? AggregateConfidence(IReadOnlyList<Word> words)
    {
        var values = words.Where(w => w.Confidence is not null).Select(w => w.Confidence!.Value).ToList();
        return values.Count == 0 ? null : Math.Round(values.Average(), 4);
    }

    private static bool IsSentenceBoundary(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && (trimmed.EndsWith('。') || trimmed.EndsWith('！')
            || trimmed.EndsWith('？') || trimmed.EndsWith('.') || trimmed.EndsWith('!')
            || trimmed.EndsWith('?'));
    }

    /// <summary>探测音频时长（秒），失败返回 0（不影响转录结果）。</summary>
    private static async Task<double> ProbeAudioSecondsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = await FFMpegCore.FFProbe.AnalyseAsync(path, cancellationToken: cancellationToken);
            return info.Duration.TotalSeconds;
        }
        catch
        {
            return 0;
        }
    }
}
